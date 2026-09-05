using System;
using System.Collections.Generic;
using UnityEngine;

using Cocoa.Lib.Util;

namespace Cocoa.Lib.Event
{
    /// <summary>
    /// 타입 기반 이벤트 버스. 메시지 타입 자체가 채널 키 역할을 한다.
    /// 인스턴스로 만들어 쓸 수 있고, 별도 분리가 필요 없으면 정적 Default를 쓴다.
    /// </summary>
    /// <remarks>
    /// 채널을 문자열이나 enum이 아니라 타입으로 잡았기 때문에 발행과 구독의 시그니처가 컴파일 타임에 검증된다.
    /// 다만 채널은 정적 타입으로 결정되므로, 파생 타입 인스턴스를 기반 타입 변수에 담아 발행하면 기반 타입 구독자만 호출된다.
    /// </remarks>
    public class EventBus
    {
        #region Nested Types

        //== 구독 한 건. 발행 경로에서 배열에 인라인으로 담기도록 구조체로 둔다.
        private readonly struct Subscription
        {
            public readonly Delegate Handler;
            public readonly int Priority;

            //== 발급 순서이자 토큰 식별자. 같은 우선순위 안에서 등록 순서를 보장하는 데 쓴다.
            public readonly long Id;

            public Subscription(Delegate handler, int priority, long id)
            {
                Handler = handler;
                Priority = priority;
                Id = id;
            }
        }

        /// <summary>
        /// 구독 한 건을 가리키는 해제용 토큰. Dispose하면 구독이 풀린다.
        /// 여러 번 호출해도 안전하다.
        /// </summary>
        public sealed class SubscriptionToken : IDisposable
        {
            private EventBus _bus;
            private Type _eventType;
            private readonly long _subscriptionId;

            internal SubscriptionToken(EventBus bus, Type eventType, long subscriptionId)
            {
                _bus = bus;
                _eventType = eventType;
                _subscriptionId = subscriptionId;
            }

            /// <summary>이미 해제되었는지 여부.</summary>
            public bool IsDisposed
            {
                get { return _bus == null; }
            }

            public void Dispose()
            {
                if (_bus == null)
                {
                    return;
                }

                _bus.RemoveSubscription(_eventType, _subscriptionId);

                //== 버스 참조를 끊어 토큰이 버스를 붙잡고 있지 않게 한다.
                _bus = null;
                _eventType = null;
            }
        }

        #endregion

        #region Static

        /// <summary>
        /// 라이브러리가 제공하는 공용 인스턴스. 버스를 따로 나눌 이유가 없을 때 쓴다.
        /// </summary>
        public static EventBus Default { get; } = new EventBus();

        #endregion

        #region Fields

        //== 이벤트 타입 -> 우선순위 순으로 정렬된 구독 배열.
        //== 배열은 변경하지 않고 매번 새로 만든다(copy-on-write). 발행 경로에서 복사본을 뜰 필요가 없어진다.
        private readonly Dictionary<Type, Subscription[]> _subscriptionsByEventType
            = new Dictionary<Type, Subscription[]>();

        private readonly object _lock = new object();

        //== 마지막으로 발급한 구독 ID. 항상 증가한다.
        private long _lastSubscriptionId;

        #endregion

        #region Properties

        /// <summary>구독자가 하나라도 있는 이벤트 타입의 수.</summary>
        public int SubscribedTypeCount
        {
            get
            {
                lock (_lock)
                {
                    return _subscriptionsByEventType.Count;
                }
            }
        }

        #endregion

        #region Subscribe

        /// <summary>
        /// 이벤트 타입에 핸들러를 등록한다.
        /// 반환된 토큰을 Dispose하거나 Unsubscribe를 호출해 해제한다.
        /// </summary>
        /// <typeparam name="T">구독할 이벤트 타입.</typeparam>
        /// <param name="handler">이벤트가 발행되면 호출할 핸들러.</param>
        /// <param name="priority">클수록 먼저 호출된다. 같으면 등록 순서를 따른다.</param>
        /// <returns>구독을 해제할 수 있는 토큰.</returns>
        public SubscriptionToken Subscribe<T>(Action<T> handler, int priority = 0)
        {
            //== 구독이 조용히 실패하면 이벤트가 오지 않는 원인을 추적하기 어렵다. 계약 위반이므로 즉시 던진다.
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            Type eventType = typeof(T);

            lock (_lock)
            {
                long id = ++_lastSubscriptionId;
                Subscription subscription = new Subscription(handler, priority, id);

                _subscriptionsByEventType.TryGetValue(eventType, out Subscription[] current);
                _subscriptionsByEventType[eventType] = InsertSorted(current, subscription);

                return new SubscriptionToken(this, eventType, id);
            }
        }

        /// <summary>
        /// GameObject의 수명에 묶인 구독. owner가 파괴되면 자동으로 해제된다.
        /// 토큰을 따로 들고 있다가 Dispose할 필요가 없다.
        /// </summary>
        /// <param name="owner">구독 수명을 맞출 대상.</param>
        /// <param name="handler">이벤트가 발행되면 호출할 핸들러.</param>
        /// <param name="priority">클수록 먼저 호출된다. 같으면 등록 순서를 따른다.</param>
        public SubscriptionToken SubscribeFor<T>(MonoBehaviour owner, Action<T> handler, int priority = 0)
        {
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }

            SubscriptionToken token = Subscribe(handler, priority);
            GetOrCreateBinder(owner.gameObject).Bind(token);

            return token;
        }

        #endregion

        #region Unsubscribe

        /// <summary>
        /// 핸들러 참조로 구독을 해제한다. 토큰을 들고 있다면 그쪽이 더 빠르고 정확하다.
        /// 같은 핸들러가 여러 번 등록되어 있으면 가장 먼저 등록된 하나만 제거한다.
        /// </summary>
        /// <returns>제거된 구독이 있었는지 여부.</returns>
        public bool Unsubscribe<T>(Action<T> handler)
        {
            if (handler == null)
            {
                return false;
            }

            Type eventType = typeof(T);

            lock (_lock)
            {
                if (!_subscriptionsByEventType.TryGetValue(eventType, out Subscription[] current))
                {
                    return false;
                }

                for (int i = 0; i < current.Length; i++)
                {
                    if (current[i].Handler.Equals(handler))
                    {
                        ApplyRemoval(eventType, current, i);
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>해당 이벤트 타입의 구독을 전부 해제한다.</summary>
        public void UnsubscribeAll<T>()
        {
            Type eventType = typeof(T);

            lock (_lock)
            {
                _subscriptionsByEventType.Remove(eventType);
            }
        }

        /// <summary>모든 이벤트 타입의 구독을 전부 해제한다.</summary>
        public void Clear()
        {
            lock (_lock)
            {
                _subscriptionsByEventType.Clear();
            }
        }

        #endregion

        #region Publish

        /// <summary>
        /// 이벤트를 발행한다. 등록된 핸들러가 우선순위 순으로 호출된다.
        /// 한 핸들러가 예외를 던져도 나머지 핸들러는 그대로 호출된다.
        /// 발행 도중에 추가되거나 해제된 구독은 다음 발행부터 반영된다.
        /// </summary>
        public void Publish<T>(T message)
        {
            Type eventType = typeof(T);

            //== 배열은 교체만 되고 수정되지 않으므로 참조만 꺼내면 된다. 발행마다 복사본을 뜨지 않는다.
            Subscription[] handlers;
            lock (_lock)
            {
                if (!_subscriptionsByEventType.TryGetValue(eventType, out handlers))
                {
                    return;
                }
            }

            //== 핸들러 호출은 락 밖에서. 핸들러가 다시 버스를 건드려도 교착에 빠지지 않는다.
            for (int i = 0; i < handlers.Length; i++)
            {
                try
                {
                    ((Action<T>)handlers[i].Handler).Invoke(message);
                }
                catch (Exception e)
                {
                    Log.Error($"[EventBus] {eventType.Name} 핸들러에서 예외 발생: {e}", LogColor.Red);
                }
            }
        }

        #endregion

        #region Queries

        /// <summary>해당 이벤트 타입에 등록된 핸들러 수.</summary>
        public int GetSubscriberCount<T>()
        {
            Type eventType = typeof(T);

            lock (_lock)
            {
                if (!_subscriptionsByEventType.TryGetValue(eventType, out Subscription[] handlers))
                {
                    return 0;
                }

                return handlers.Length;
            }
        }

        /// <summary>해당 이벤트 타입에 구독자가 있는지 여부.</summary>
        public bool HasSubscribers<T>()
        {
            return GetSubscriberCount<T>() > 0;
        }

        #endregion

        #region Private Helpers

        //== 정렬된 자리에 끼워 넣은 새 배열을 만든다. ID가 항상 증가하므로 같은 우선순위끼리는 등록 순서가 유지된다.
        private static Subscription[] InsertSorted(Subscription[] source, Subscription target)
        {
            int length = source != null ? source.Length : 0;
            Subscription[] result = new Subscription[length + 1];

            int index = 0;
            while (index < length && !ShouldPlaceBefore(target, source[index]))
            {
                result[index] = source[index];
                index++;
            }

            result[index] = target;

            for (int i = index; i < length; i++)
            {
                result[i + 1] = source[i];
            }

            return result;
        }

        private static bool ShouldPlaceBefore(Subscription target, Subscription current)
        {
            if (target.Priority != current.Priority)
            {
                return target.Priority > current.Priority;
            }

            return target.Id < current.Id;
        }

        private static Subscription[] RemoveAt(Subscription[] source, int index)
        {
            Subscription[] result = new Subscription[source.Length - 1];
            Array.Copy(source, 0, result, 0, index);
            Array.Copy(source, index + 1, result, index, source.Length - index - 1);

            return result;
        }

        //== 호출 시점에 이미 _lock 을 잡고 있어야 한다.
        private void ApplyRemoval(Type eventType, Subscription[] current, int index)
        {
            //== 마지막 하나가 빠지면 딕셔너리 항목 자체를 지운다. 빈 배열이 남지 않게 한다.
            if (current.Length == 1)
            {
                _subscriptionsByEventType.Remove(eventType);
                return;
            }

            _subscriptionsByEventType[eventType] = RemoveAt(current, index);
        }

        private void RemoveSubscription(Type eventType, long subscriptionId)
        {
            lock (_lock)
            {
                if (!_subscriptionsByEventType.TryGetValue(eventType, out Subscription[] current))
                {
                    return;
                }

                for (int i = 0; i < current.Length; i++)
                {
                    if (current[i].Id == subscriptionId)
                    {
                        ApplyRemoval(eventType, current, i);
                        return;
                    }
                }
            }
        }

        private static EventSubscriptionBinder GetOrCreateBinder(GameObject owner)
        {
            if (owner.TryGetComponent(out EventSubscriptionBinder binder))
            {
                return binder;
            }

            binder = owner.AddComponent<EventSubscriptionBinder>();
            binder.hideFlags = HideFlags.HideInInspector;

            return binder;
        }

        #endregion
    }
}
