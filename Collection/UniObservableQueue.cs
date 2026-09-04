using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using UnityEngine;

namespace Cocoa.Lib.Collection
{
    /// <summary>
    /// Unity Inspector에서 내용을 확인할 수 있고, 변경을 통지하는 큐.
    /// Enqueue/Dequeue 등 모든 변경 시점에 이벤트가 발화되며,
    /// Inspector 미러는 지연 동기화 방식으로 런타임 오버헤드를 최소화합니다.
    /// </summary>
    [Serializable]
    public class UniObservableQueue<T>
        : IUniObservableCollection<T>,
          IReadOnlyCollection<T>,
          ISerializationCallbackReceiver
    {
        #region Fields

        private Queue<T> _queue;

        //== 직렬화 백킹 (모든 빌드 컴파일, JsonUtility/Newtonsoft round-trip).
        [SerializeField] private List<T> _serialized = new List<T>();

        #endregion

        #region Events

        //== IUniObservableCollection 계약
        public event Action<T> ItemAdded;
        public event Action<T> ItemRemoved;

#pragma warning disable CS0067
        public event Action<int, T, T> ItemReplaced;
#pragma warning restore CS0067
        public event NotifyCollectionChangedEventHandler CollectionChanged;

        //== 큐 도메인 별칭 이벤트

        /// <summary>Enqueue 시 발화됩니다. ItemAdded와 동일 시점.</summary>
        public event Action<T> ItemEnqueued
        {
            add { ItemAdded += value; }
            remove { ItemAdded -= value; }
        }

        /// <summary>Dequeue 시 발화됩니다. ItemRemoved와 동일 시점.</summary>
        public event Action<T> ItemDequeued
        {
            add { ItemRemoved += value; }
            remove { ItemRemoved -= value; }
        }

        #endregion

        #region Properties

        /// <summary>현재 큐에 있는 항목 수.</summary>
        public int Count
        {
            get { return _queue.Count; }
        }

        /// <summary>큐가 비어 있는지 여부.</summary>
        public bool IsEmpty
        {
            get { return _queue.Count == 0; }
        }

        #endregion

        #region Constructors

        public UniObservableQueue()
        {
            _queue = new Queue<T>();
        }

        public UniObservableQueue(int capacity)
        {
            _queue = new Queue<T>(capacity);
        }

        public UniObservableQueue(IEnumerable<T> source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            _queue = new Queue<T>(source);
        }

        #endregion

        #region Queue Operations

        /// <summary>항목을 큐의 끝에 추가합니다.</summary>
        public void Enqueue(T item)
        {
            _queue.Enqueue(item);
            RaiseAdded(item);
        }

        /// <summary>여러 항목을 한 번에 추가합니다. 각 항목마다 이벤트가 발화됩니다.</summary>
        public void EnqueueRange(IEnumerable<T> items)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }
            foreach (var item in items)
            {
                Enqueue(item);
            }
        }

        /// <summary>큐의 가장 앞 항목을 꺼냅니다. 큐가 비어 있으면 예외 발생.</summary>
        public T Dequeue()
        {
            T item = _queue.Dequeue();
            RaiseRemoved(item);
            return item;
        }

        /// <summary>큐의 가장 앞 항목을 안전하게 꺼냅니다. 비어 있으면 false 반환.</summary>
        public bool TryDequeue(out T item)
        {
            if (_queue.TryDequeue(out item))
            {
                RaiseRemoved(item);
                return true;
            }
            return false;
        }

        /// <summary>큐의 가장 앞 항목을 꺼내지 않고 확인합니다. 큐가 비어 있으면 예외 발생.</summary>
        public T Peek()
        {
            return _queue.Peek();
        }

        /// <summary>큐의 가장 앞 항목을 안전하게 확인합니다. 비어 있으면 false 반환.</summary>
        public bool TryPeek(out T item)
        {
            return _queue.TryPeek(out item);
        }

        /// <summary>큐의 모든 항목을 제거합니다.</summary>
        public void Clear()
        {
            if (_queue.Count == 0)
            {
                return;
            }
            _queue.Clear();
            RaiseReset();
        }

        #endregion

        #region Queries

        /// <summary>특정 항목이 큐에 포함되어 있는지 확인합니다.</summary>
        public bool Contains(T item)
        {
            return _queue.Contains(item);
        }

        /// <summary>큐 내용을 배열로 복사하여 반환합니다.</summary>
        public T[] ToArray()
        {
            return _queue.ToArray();
        }

        /// <summary>큐 내용을 지정한 배열에 복사합니다.</summary>
        public void CopyTo(T[] array, int arrayIndex)
        {
            _queue.CopyTo(array, arrayIndex);
        }

        #endregion

        #region Enumeration

        public IEnumerator<T> GetEnumerator()
        {
            return _queue.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        #endregion

        #region Serialization Callbacks

        /// <summary>
        /// Unity가 직렬화하기 직전에 호출됩니다.
        /// 큐 내용을 Inspector 표시용 리스트로 복사합니다 (단방향, 읽기 전용 뷰).
        /// </summary>
        /// <summary>직렬화 직전 호출. 큐 내용을 직렬화 백킹 리스트로 복사합니다 (front → back).</summary>
        public void OnBeforeSerialize()
        {
            if (_serialized == null)
            {
                _serialized = new List<T>();
            }
            _serialized.Clear();
            if (_queue == null)
            {
                return;
            }
            if (_serialized.Capacity < _queue.Count)
            {
                _serialized.Capacity = _queue.Count;
            }
            //== Queue<T> 열거는 front → back 순서
            foreach (var item in _queue)
            {
                _serialized.Add(item);
            }
        }

        /// <summary>
        /// 역직렬화 직후 호출. 백킹 리스트로부터 큐를 재구성합니다 (front → back 순서 보존).
        /// 내부 큐를 직접 채우므로 로드 시 변경 이벤트는 발화되지 않습니다.
        /// </summary>
        public void OnAfterDeserialize()
        {
            if (_queue == null)
            {
                _queue = new Queue<T>();
            }
            else
            {
                _queue.Clear();
            }
            if (_serialized != null)
            {
                for (int i = 0; i < _serialized.Count; i++)
                {
                    _queue.Enqueue(_serialized[i]);
                }
            }
        }

        #endregion

        #region Private Helpers

        private void RaiseAdded(T item)
        {
            ItemAdded?.Invoke(item);
            CollectionChanged?.Invoke(this,
                new NotifyCollectionChangedEventArgs(
                    NotifyCollectionChangedAction.Add, item));
        }

        private void RaiseRemoved(T item)
        {
            ItemRemoved?.Invoke(item);
            CollectionChanged?.Invoke(this,
                new NotifyCollectionChangedEventArgs(
                    NotifyCollectionChangedAction.Remove, item));
        }

        private void RaiseReset()
        {
            CollectionChanged?.Invoke(this,
                new NotifyCollectionChangedEventArgs(
                    NotifyCollectionChangedAction.Reset));
        }

        #endregion
    }
}