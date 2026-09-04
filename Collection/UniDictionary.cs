using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Cocoa.Lib.Collection
{
    /// <summary>
    /// Unity Inspector 표시 + JsonUtility 직렬화(round-trip)를 지원하는 사전(Dictionary).
    /// 직렬화 백킹(키-값 리스트)을 통해 저장/로드되며, Inspector에서 편집한 값도 런타임 사전에 반영됩니다(양방향).
    /// 변경 통지가 필요하다면 UniObservableDictionary를 사용하세요.
    /// </summary>
    /// <remarks>
    /// 직렬화 제약: TKey/TValue 는 JsonUtility 직렬화 가능 타입(기본형/문자열/enum/[Serializable])이어야 합니다.
    /// 커스텀 <see cref="IEqualityComparer{T}"/> 는 직렬화되지 않아 로드 시 기본 비교자로 리셋됩니다.
    /// 중복 키는 마지막 값이 우선합니다.
    /// </remarks>
    [Serializable]
    public class UniDictionary<TKey, TValue>
        : IDictionary<TKey, TValue>,
          IReadOnlyDictionary<TKey, TValue>,
          ISerializationCallbackReceiver
    {
        #region Fields

        private Dictionary<TKey, TValue> _entries;

        //== 직렬화 백킹 (모든 빌드 컴파일). JsonUtility/Inspector 가 이 리스트를 읽고 쓴다.
        [SerializeField]
        private List<UniPair<TKey, TValue>> _serialized = new List<UniPair<TKey, TValue>>();

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

        public UniDictionary()
        {
            _entries = new Dictionary<TKey, TValue>();
        }

        public UniDictionary(int capacity)
        {
            _entries = new Dictionary<TKey, TValue>(capacity);
        }

        public UniDictionary(IEqualityComparer<TKey> comparer)
        {
            _entries = new Dictionary<TKey, TValue>(comparer);
        }

        public UniDictionary(int capacity, IEqualityComparer<TKey> comparer)
        {
            _entries = new Dictionary<TKey, TValue>(capacity, comparer);
        }

        public UniDictionary(IDictionary<TKey, TValue> source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            _entries = new Dictionary<TKey, TValue>(source);
        }

        public UniDictionary(IDictionary<TKey, TValue> source, IEqualityComparer<TKey> comparer)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            _entries = new Dictionary<TKey, TValue>(source, comparer);
        }

        public UniDictionary(IEnumerable<KeyValuePair<TKey, TValue>> source)
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

        #endregion

        #region Indexers

        public TValue this[TKey key]
        {
            get { return _entries[key]; }
            set { _entries[key] = value; }
        }

        #endregion

        #region Accessors

        public bool ContainsKey(TKey key)
        {
            return _entries.ContainsKey(key);
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            return _entries.TryGetValue(key, out value);
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

        public void Add(TKey key, TValue value)
        {
            _entries.Add(key, value);
        }

        public bool TryAdd(TKey key, TValue value)
        {
            return _entries.TryAdd(key, value);
        }

        public bool Remove(TKey key)
        {
            return _entries.Remove(key);
        }

        public bool Remove(TKey key, out TValue value)
        {
            return _entries.Remove(key, out value);
        }

        public void Clear()
        {
            _entries.Clear();
        }

        //== IDictionary<KVP> 계약 — Add(key, value)로 위임
        public void Add(KeyValuePair<TKey, TValue> item)
        {
            Add(item.Key, item.Value);
        }

        public bool Remove(KeyValuePair<TKey, TValue> item)
        {
            return ((ICollection<KeyValuePair<TKey, TValue>>)_entries).Remove(item);
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

        #endregion

        #region Serialization Callbacks

        /// <summary>
        /// 직렬화 직전 호출. 사전 내용을 직렬화 백킹 리스트로 복사합니다.
        /// </summary>
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
        /// JsonUtility 는 생성자를 호출하지 않으므로 _entries 가 null 일 수 있어 방어적으로 생성합니다.
        /// </summary>
        public void OnAfterDeserialize()
        {
            if (_entries == null)
            {
                _entries = new Dictionary<TKey, TValue>();
            }
            else
            {
                _entries.Clear();
            }
            if (_serialized == null)
            {
                return;
            }
            for (int i = 0; i < _serialized.Count; i++)
            {
                var pair = _serialized[i];
                if (pair == null)
                {
                    continue;
                }
                //== 인덱서 사용: 중복 키는 마지막 값 우선 (Add 의 예외 회피)
                _entries[pair.key] = pair.value;
            }
        }

        #endregion
    }
}