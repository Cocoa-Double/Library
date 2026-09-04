using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Cocoa.Lib.Collection
{
    /// <summary>
    /// 사전 변경뿐 아니라 값 객체의 속성 변경(INotifyPropertyChanged)까지 통지하는 사전.
    /// 사전에 추가된 값의 PropertyChanged를 자동으로 구독하고, 제거 시 해제합니다.
    /// 키 객체의 INPC는 추적하지 않습니다 (키가 변경되면 해시가 깨지므로).
    /// 값 동등성은 참조 비교로 처리되어, 같은 값을 가진 다른 인스턴스는 교체로 인식됩니다.
    /// </summary>
    [Serializable]
    public class UniReactiveDictionary<TKey, TValue> : UniObservableDictionary<TKey, TValue>
        where TValue : class, INotifyPropertyChanged
    {
        #region Static Fields

        //== PropertyChangedEventArgs 할당 최소화를 위한 캐시
        private static readonly Dictionary<string, PropertyChangedEventArgs> ArgsCache
            = new Dictionary<string, PropertyChangedEventArgs>();
        private static readonly object CacheLock = new object();

        #endregion

        #region Fields

        //== 값 → 해당 값이 매핑된 키 집합 (역방향 lookup)
        //== 같은 값 인스턴스가 여러 키에 들어있을 수 있으므로 HashSet 사용
        //== 참조 동등성으로 비교하여 사용자 Equals 오버라이드의 영향을 받지 않음
        private Dictionary<TValue, HashSet<TKey>> _valueToKeys
            = new Dictionary<TValue, HashSet<TKey>>(ReferenceEqualityComparer<TValue>.Instance);

        #endregion

        #region Events

        /// <summary>
        /// 값 객체의 속성이 변경되면 발화됩니다.
        /// sender는 변경된 값 객체이며, 어떤 키에 매핑되어 있는지 알려면 ValuePropertyChanged를 사용하세요.
        /// </summary>
        public event PropertyChangedEventHandler ItemPropertyChanged;

        /// <summary>
        /// 값 객체의 속성이 변경되면 발화됩니다. 키 정보도 함께 제공됩니다.
        /// (key, value, propertyName)
        /// 같은 값 인스턴스가 여러 키에 매핑되어 있다면 각 키마다 발화됩니다.
        /// </summary>
        public event Action<TKey, TValue, string> ValuePropertyChanged;

        #endregion

        #region Constructors

        public UniReactiveDictionary() : base()
        {
            SetupReactive();
        }

        public UniReactiveDictionary(int capacity) : base(capacity)
        {
            SetupReactive();
        }

        public UniReactiveDictionary(IEqualityComparer<TKey> comparer) : base(comparer)
        {
            SetupReactive();
        }

        public UniReactiveDictionary(int capacity, IEqualityComparer<TKey> comparer)
            : base(capacity, comparer)
        {
            SetupReactive();
        }

        public UniReactiveDictionary(IDictionary<TKey, TValue> source) : base(source)
        {
            SetupReactive();
            SubscribeAll(source);
        }

        public UniReactiveDictionary(IDictionary<TKey, TValue> source, IEqualityComparer<TKey> comparer)
            : base(source, comparer)
        {
            SetupReactive();
            SubscribeAll(source);
        }

        public UniReactiveDictionary(IEnumerable<KeyValuePair<TKey, TValue>> source) : base(source)
        {
            SetupReactive();
            SubscribeAll(source);
        }

        public UniReactiveDictionary(IEnumerable<UniPair<TKey, TValue>> source) : base(source)
        {
            SetupReactive();
            foreach (var pair in source)
            {
                if (pair == null)
                {
                    continue;
                }
                Subscribe(pair.key, pair.value);
            }
        }

        #endregion

        #region Hooks Override

        protected override void OnEntryAdded(TKey key, TValue value)
        {
            Subscribe(key, value);
        }

        protected override void OnEntryRemoved(TKey key, TValue value)
        {
            Unsubscribe(key, value);
        }

        protected override void OnEntryReplaced(TKey key, TValue oldValue, TValue newValue)
        {
            Unsubscribe(key, oldValue);
            Subscribe(key, newValue);
        }

        protected override void OnEntriesClearing()
        {
            //== 모든 값의 구독 해제
            if (_valueToKeys == null)
            {
                return;
            }
            foreach (var value in _valueToKeys.Keys)
            {
                if (value != null)
                {
                    value.PropertyChanged -= OnValuePropertyChanged;
                }
            }
            _valueToKeys.Clear();
        }

        //== 역직렬화로 로드된 항목들에 다시 구독.
        //== 생성자가 호출되지 않는 경로(JsonUtility)도 있으므로 리액티브 상태를 여기서 복원한다.
        protected override void OnAfterDeserializeRebuilt()
        {
            //== 값 비교를 참조 동등성으로 복원 (생성자의 SetupReactive 대체)
            ValueComparer = ReferenceEqualityComparer<TValue>.Instance;

            if (_valueToKeys == null)
            {
                _valueToKeys = new Dictionary<TValue, HashSet<TKey>>(ReferenceEqualityComparer<TValue>.Instance);
            }
            else
            {
                foreach (var value in _valueToKeys.Keys)
                {
                    if (value != null)
                    {
                        value.PropertyChanged -= OnValuePropertyChanged;
                    }
                }
                _valueToKeys.Clear();
            }

            //== 로드된 모든 항목 재구독
            foreach (var kvp in this)
            {
                Subscribe(kvp.Key, kvp.Value);
            }
        }

        #endregion

        #region Private Helpers

        private void SetupReactive()
        {
            //== 값 비교를 참조 동등성으로 강제. 
            //== 사용자 Equals 오버라이드가 있어도 인스턴스가 다르면 교체로 처리되어 구독이 정확히 갱신됨
            ValueComparer = ReferenceEqualityComparer<TValue>.Instance;
        }

        private void SubscribeAll(IEnumerable<KeyValuePair<TKey, TValue>> source)
        {
            //== base 생성자는 _entries를 직접 채우므로 OnEntryAdded 훅이 호출되지 않음
            //== 여기서 명시적으로 구독을 걸어야 함
            foreach (var kvp in source)
            {
                Subscribe(kvp.Key, kvp.Value);
            }
        }

        private void Subscribe(TKey key, TValue value)
        {
            if (value == null)
            {
                return;
            }

            if (!_valueToKeys.TryGetValue(value, out var keys))
            {
                //== 이 인스턴스가 처음 등록되는 경우만 구독
                keys = new HashSet<TKey>();
                _valueToKeys[value] = keys;
                value.PropertyChanged += OnValuePropertyChanged;
            }
            keys.Add(key);
        }

        private void Unsubscribe(TKey key, TValue value)
        {
            if (value == null)
            {
                return;
            }

            if (_valueToKeys.TryGetValue(value, out var keys))
            {
                keys.Remove(key);
                if (keys.Count == 0)
                {
                    //== 더 이상 이 인스턴스를 가리키는 키가 없으면 구독 해제
                    _valueToKeys.Remove(value);
                    value.PropertyChanged -= OnValuePropertyChanged;
                }
            }
        }

        private static PropertyChangedEventArgs GetCachedArgs(string propertyName)
        {
            lock (CacheLock)
            {
                if (!ArgsCache.TryGetValue(propertyName, out var args))
                {
                    args = new PropertyChangedEventArgs(propertyName);
                    ArgsCache[propertyName] = args;
                }
                return args;
            }
        }

        private void OnValuePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            var args = GetCachedArgs(e.PropertyName);
            ItemPropertyChanged?.Invoke(sender, args);

            //== 키 정보가 필요한 이벤트도 발화
            if (sender is TValue value && _valueToKeys.TryGetValue(value, out var keys))
            {
                //== 같은 값이 여러 키에 매핑된 경우 각 키마다 발화
                foreach (var key in keys)
                {
                    ValuePropertyChanged?.Invoke(key, value, e.PropertyName);
                }
            }
        }

        #endregion
    }

    /// <summary>
    /// 참조 동일성 기반 비교자.
    /// 값 객체 인스턴스를 키로 사용하는 내부 lookup 등에 사용됩니다.
    /// </summary>
    internal sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T>
        where T : class
    {
        public static readonly ReferenceEqualityComparer<T> Instance = new ReferenceEqualityComparer<T>();

        public bool Equals(T x, T y)
        {
            return ReferenceEquals(x, y);
        }

        public int GetHashCode(T obj)
        {
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}