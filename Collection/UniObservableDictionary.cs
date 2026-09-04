using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using UnityEngine;

namespace Cocoa.Lib.Collection
{
    /// <summary>
    /// 변경 통지와 Unity Inspector 시각화를 모두 지원하는 사전(Dictionary).
    /// 이벤트는 UniPair 단위로 통지되어 UniDictionary 계열과 일관성을 갖습니다.
    /// 키-값 추가, 제거, 교체 시점에 이벤트가 발화되며,
    /// Inspector 미러는 지연 동기화 방식으로 런타임 오버헤드를 최소화합니다.
    /// </summary>
    [Serializable]
    public class UniObservableDictionary<TKey, TValue>
        : IDictionary<TKey, TValue>,
          IReadOnlyDictionary<TKey, TValue>,
          IUniObservableCollection<UniPair<TKey, TValue>>,
          ISerializationCallbackReceiver
    {
        #region Fields

        private Dictionary<TKey, TValue> _entries;
        private IEqualityComparer<TValue> _valueComparer = EqualityComparer<TValue>.Default;

        //== 직렬화 백킹 (모든 빌드 컴파일, JsonUtility/Newtonsoft round-trip).
        [SerializeField]
        private List<UniPair<TKey, TValue>> _serialized
            = new List<UniPair<TKey, TValue>>();

        #endregion

        #region Events

        //== IUniObservableCollection<UniPair> 계약
        public event Action<UniPair<TKey, TValue>> ItemAdded;
        public event Action<UniPair<TKey, TValue>> ItemRemoved;
        public event Action<int, UniPair<TKey, TValue>, UniPair<TKey, TValue>> ItemReplaced;
        public event NotifyCollectionChangedEventHandler CollectionChanged;

        //== 사전 도메인 별칭 이벤트 — 키와 값을 분리해서 사용하기 편리

        /// <summary>새 항목이 추가될 때 발화됩니다. (key, value)</summary>
        public event Action<TKey, TValue> EntryAdded;

        /// <summary>항목이 제거될 때 발화됩니다. (key, value)</summary>
        public event Action<TKey, TValue> EntryRemoved;

        /// <summary>기존 키의 값이 다른 값으로 교체될 때 발화됩니다. (key, oldValue, newValue)</summary>
        public event Action<TKey, TValue, TValue> EntryReplaced;

        #endregion

        #region Properties

        /// <summary>현재 사전에 있는 항목 수.</summary>
        public int Count
        {
            get { return _entries.Count; }
        }

        /// <summary>사전이 비어 있는지 여부.</summary>
        public bool IsEmpty
        {
            get { return _entries.Count == 0; }
        }

        /// <summary>읽기 전용 여부 (이 사전은 항상 false).</summary>
        public bool IsReadOnly
        {
            get { return false; }
        }

        /// <summary>모든 키 컬렉션.</summary>
        public ICollection<TKey> Keys
        {
            get { return _entries.Keys; }
        }

        /// <summary>모든 값 컬렉션.</summary>
        public ICollection<TValue> Values
        {
            get { return _entries.Values; }
        }

        /// <summary>키 비교에 사용되는 IEqualityComparer.</summary>
        public IEqualityComparer<TKey> Comparer
        {
            get { return _entries.Comparer; }
        }

        /// <summary>
        /// Set 시 동일 값 검사에 사용할 비교자입니다.
        /// 기본값은 EqualityComparer&lt;TValue&gt;.Default.
        /// 파생 클래스에서 변경할 수 있습니다 (예: UniReactiveDictionary는 참조 비교 사용).
        /// </summary>
        protected IEqualityComparer<TValue> ValueComparer
        {
            get { return _valueComparer; }
            set { _valueComparer = value ?? EqualityComparer<TValue>.Default; }
        }

        //== IReadOnlyDictionary 명시적 구현
        IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys
        {
            get { return _entries.Keys; }
        }

        IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values
        {
            get { return _entries.Values; }
        }

        #endregion

        #region Constructors

        public UniObservableDictionary()
        {
            _entries = new Dictionary<TKey, TValue>();
        }

        public UniObservableDictionary(int capacity)
        {
            _entries = new Dictionary<TKey, TValue>(capacity);
        }

        public UniObservableDictionary(IEqualityComparer<TKey> comparer)
        {
            _entries = new Dictionary<TKey, TValue>(comparer);
        }

        public UniObservableDictionary(int capacity, IEqualityComparer<TKey> comparer)
        {
            _entries = new Dictionary<TKey, TValue>(capacity, comparer);
        }

        public UniObservableDictionary(IDictionary<TKey, TValue> source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            _entries = new Dictionary<TKey, TValue>(source);
        }

        public UniObservableDictionary(IDictionary<TKey, TValue> source, IEqualityComparer<TKey> comparer)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            _entries = new Dictionary<TKey, TValue>(source, comparer);
        }

        public UniObservableDictionary(IEnumerable<KeyValuePair<TKey, TValue>> source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            _entries = new Dictionary<TKey, TValue>();
            foreach (var pair in source)
            {
                _entries.Add(pair.Key, pair.Value);
            }
        }

        /// <summary>UniPair 시퀀스로부터 생성합니다.</summary>
        public UniObservableDictionary(IEnumerable<UniPair<TKey, TValue>> source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            _entries = new Dictionary<TKey, TValue>();
            foreach (var pair in source)
            {
                if (pair == null)
                {
                    continue;
                }
                _entries.Add(pair.key, pair.value);
            }
        }

        #endregion

        #region Indexers

        /// <summary>
        /// 키로 값을 조회하거나 설정합니다.
        /// 설정 시 키가 없으면 EntryAdded, 있으면 EntryReplaced 이벤트가 발화됩니다.
        /// 동일한 값으로 설정하면 이벤트가 발화되지 않습니다.
        /// </summary>
        public TValue this[TKey key]
        {
            get { return _entries[key]; }
            set { Set(key, value); }
        }

        #endregion

        #region Accessors

        public bool ContainsKey(TKey key)
        {
            return _entries.ContainsKey(key);
        }

        public bool ContainsValue(TValue value)
        {
            return _entries.ContainsValue(value);
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            return _entries.TryGetValue(key, out value);
        }

        /// <summary>키가 없으면 fallback 값을 반환합니다.</summary>
        public TValue GetOrDefault(TKey key, TValue fallback = default)
        {
            if (_entries.TryGetValue(key, out var value))
            {
                return value;
            }
            return fallback;
        }

        public bool Contains(KeyValuePair<TKey, TValue> item)
        {
            return ((ICollection<KeyValuePair<TKey, TValue>>)_entries).Contains(item);
        }

        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            ((ICollection<KeyValuePair<TKey, TValue>>)_entries).CopyTo(array, arrayIndex);
        }

        #endregion

        #region Mutators

        /// <summary>
        /// 키-값을 추가합니다. 키가 이미 존재하면 예외를 발생시킵니다.
        /// 안전한 추가를 원하면 TryAdd를 사용하세요.
        /// </summary>
        public void Add(TKey key, TValue value)
        {
            _entries.Add(key, value);
            RaiseAdded(key, value);
        }

        /// <summary>
        /// 키-값을 설정합니다. 키가 없으면 추가, 있으면 교체합니다.
        /// 동일한 값으로 설정하면 이벤트는 발화되지 않습니다.
        /// 동일성 비교는 ValueComparer에 따라 달라집니다.
        /// </summary>
        public virtual void Set(TKey key, TValue value)
        {
            if (_entries.TryGetValue(key, out var oldValue))
            {
                if (_valueComparer.Equals(oldValue, value))
                {
                    //== 동일한 값이면 이벤트 발화 안 함
                    return;
                }
                _entries[key] = value;
                RaiseReplaced(key, oldValue, value);
            }
            else
            {
                _entries[key] = value;
                RaiseAdded(key, value);
            }
        }

        /// <summary>키가 없을 때만 추가합니다.</summary>
        public bool TryAdd(TKey key, TValue value)
        {
            if (_entries.TryAdd(key, value))
            {
                RaiseAdded(key, value);
                return true;
            }
            return false;
        }

        public bool Remove(TKey key)
        {
            if (_entries.TryGetValue(key, out var value) && _entries.Remove(key))
            {
                RaiseRemoved(key, value);
                return true;
            }
            return false;
        }

        public bool Remove(TKey key, out TValue value)
        {
            if (_entries.Remove(key, out value))
            {
                RaiseRemoved(key, value);
                return true;
            }
            return false;
        }

        public void Clear()
        {
            if (_entries.Count == 0)
            {
                return;
            }
            OnEntriesClearing();
            _entries.Clear();
            RaiseReset();
        }

        //== IDictionary<KVP> 계약
        public void Add(KeyValuePair<TKey, TValue> item)
        {
            Add(item.Key, item.Value);
        }

        public bool Remove(KeyValuePair<TKey, TValue> item)
        {
            if (((ICollection<KeyValuePair<TKey, TValue>>)_entries).Remove(item))
            {
                RaiseRemoved(item.Key, item.Value);
                return true;
            }
            return false;
        }

        //== UniPair 기반 변경 메서드

        /// <summary>UniPair로부터 키-값을 추가합니다.</summary>
        public void Add(UniPair<TKey, TValue> pair)
        {
            if (pair == null)
            {
                throw new ArgumentNullException(nameof(pair));
            }
            Add(pair.key, pair.value);
        }

        /// <summary>UniPair에 해당하는 항목을 제거합니다. 키-값이 모두 일치해야 합니다.</summary>
        public bool Remove(UniPair<TKey, TValue> pair)
        {
            if (pair == null)
            {
                return false;
            }
            if (_entries.TryGetValue(pair.key, out var existing)
                && EqualityComparer<TValue>.Default.Equals(existing, pair.value))
            {
                return Remove(pair.key);
            }
            return false;
        }

        #endregion

        #region Bulk Operations

        /// <summary>여러 항목을 한 번에 추가합니다. 각 항목마다 이벤트가 발화됩니다.</summary>
        public void AddRange(IEnumerable<KeyValuePair<TKey, TValue>> items)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }
            foreach (var item in items)
            {
                Add(item.Key, item.Value);
            }
        }

        /// <summary>UniPair 시퀀스를 일괄 추가합니다.</summary>
        public void AddRange(IEnumerable<UniPair<TKey, TValue>> items)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }
            foreach (var item in items)
            {
                if (item == null)
                {
                    continue;
                }
                Add(item.key, item.value);
            }
        }

        /// <summary>조건에 맞는 항목들을 제거합니다. 제거된 개수를 반환합니다.</summary>
        public int RemoveAll(Func<TKey, TValue, bool> predicate)
        {
            if (predicate == null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }
            //== 열거 중 컬렉션 수정을 피하기 위해 키를 먼저 수집
            var keysToRemove = new List<TKey>();
            foreach (var pair in _entries)
            {
                if (predicate(pair.Key, pair.Value))
                {
                    keysToRemove.Add(pair.Key);
                }
            }
            foreach (var key in keysToRemove)
            {
                Remove(key);
            }
            return keysToRemove.Count;
        }

        #endregion

        #region Enumeration

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            return _entries.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>UniPair 시퀀스로 순회합니다 (각 호출마다 새 UniPair 생성).</summary>
        public IEnumerable<UniPair<TKey, TValue>> EnumerateAsPairs()
        {
            foreach (var kvp in _entries)
            {
                yield return new UniPair<TKey, TValue>(kvp.Key, kvp.Value);
            }
        }

        #endregion

        #region Protected Hooks

        /// <summary>키-값이 추가된 후 호출됩니다. 파생 클래스에서 구독 등의 처리를 할 수 있습니다.</summary>
        protected virtual void OnEntryAdded(TKey key, TValue value) { }

        /// <summary>키-값이 제거된 후 호출됩니다. 파생 클래스에서 구독 해제 등의 처리를 할 수 있습니다.</summary>
        protected virtual void OnEntryRemoved(TKey key, TValue value) { }

        /// <summary>기존 키의 값이 교체된 후 호출됩니다.</summary>
        protected virtual void OnEntryReplaced(TKey key, TValue oldValue, TValue newValue) { }

        /// <summary>Clear가 호출되기 직전에 호출됩니다. 이 시점에는 아직 _entries에 접근 가능합니다.</summary>
        protected virtual void OnEntriesClearing() { }

        /// <summary>
        /// 역직렬화로 사전이 재구성된 직후 호출됩니다.
        /// 파생 클래스(UniReactiveDictionary)가 로드된 값에 다시 구독하는 데 사용합니다.
        /// </summary>
        protected virtual void OnAfterDeserializeRebuilt() { }

        #endregion

        #region Serialization Callbacks

        /// <summary>직렬화 직전 호출. 사전 내용을 직렬화 백킹 리스트로 복사합니다.</summary>
        public void OnBeforeSerialize()
        {
            if (_serialized == null)
            {
                _serialized = new List<UniPair<TKey, TValue>>();
            }
            _serialized.Clear();
            if (_entries == null)
            {
                return;
            }
            if (_serialized.Capacity < _entries.Count)
            {
                _serialized.Capacity = _entries.Count;
            }
            foreach (var kvp in _entries)
            {
                _serialized.Add(new UniPair<TKey, TValue>(kvp.Key, kvp.Value));
            }
        }

        /// <summary>
        /// 역직렬화 직후 호출. 백킹 리스트로부터 사전을 재구성합니다.
        /// 내부 사전을 직접 채우므로 로드 시 변경 이벤트는 발화되지 않습니다.
        /// JsonUtility 는 생성자를 호출하지 않으므로 필드가 null 일 수 있어 방어적으로 초기화합니다.
        /// </summary>
        public void OnAfterDeserialize()
        {
            if (_valueComparer == null)
            {
                _valueComparer = EqualityComparer<TValue>.Default;
            }
            if (_entries == null)
            {
                _entries = new Dictionary<TKey, TValue>();
            }
            else
            {
                _entries.Clear();
            }
            if (_serialized != null)
            {
                for (int i = 0; i < _serialized.Count; i++)
                {
                    var pair = _serialized[i];
                    if (pair == null)
                    {
                        continue;
                    }
                    _entries[pair.key] = pair.value;
                }
            }
            //== 파생 클래스(UniReactiveDictionary 등)가 로드된 항목에 재구독할 수 있도록 통지
            OnAfterDeserializeRebuilt();
        }

        #endregion

        #region Private Helpers

        private void RaiseAdded(TKey key, TValue value)
        {
            OnEntryAdded(key, value);

            var pair = new UniPair<TKey, TValue>(key, value);
            EntryAdded?.Invoke(key, value);
            ItemAdded?.Invoke(pair);
            CollectionChanged?.Invoke(this,
                new NotifyCollectionChangedEventArgs(
                    NotifyCollectionChangedAction.Add, pair));
        }

        private void RaiseRemoved(TKey key, TValue value)
        {
            OnEntryRemoved(key, value);

            var pair = new UniPair<TKey, TValue>(key, value);
            EntryRemoved?.Invoke(key, value);
            ItemRemoved?.Invoke(pair);
            CollectionChanged?.Invoke(this,
                new NotifyCollectionChangedEventArgs(
                    NotifyCollectionChangedAction.Remove, pair));
        }

        private void RaiseReplaced(TKey key, TValue oldValue, TValue newValue)
        {
            OnEntryReplaced(key, oldValue, newValue);

            var oldPair = new UniPair<TKey, TValue>(key, oldValue);
            var newPair = new UniPair<TKey, TValue>(key, newValue);
            EntryReplaced?.Invoke(key, oldValue, newValue);

            //== 사전은 인덱스 개념이 없으므로 -1로 통지
            ItemReplaced?.Invoke(-1, oldPair, newPair);
            CollectionChanged?.Invoke(this,
                new NotifyCollectionChangedEventArgs(
                    NotifyCollectionChangedAction.Replace, newPair, oldPair));
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