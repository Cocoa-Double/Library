using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Cocoa.Lib.Collection
{
    /// <summary>
    /// Unity Inspector 표시 + JsonUtility 직렬화(round-trip)를 지원하는 HashSet.
    /// 직렬화 백킹(항목 리스트)을 통해 저장/로드되며, Inspector에서 편집한 값도 런타임 집합에 반영됩니다(양방향).
    /// 변경 통지가 필요하다면 UniObservableHashSet을 사용하세요.
    /// </summary>
    /// <remarks>
    /// 직렬화 제약: T 는 JsonUtility 직렬화 가능 타입이어야 합니다.
    /// 커스텀 <see cref="IEqualityComparer{T}"/> 는 직렬화되지 않아 로드 시 기본 비교자로 리셋됩니다.
    /// 중복 항목은 한 번만 들어갑니다(HashSet 특성).
    /// </remarks>
    [Serializable]
    public class UniHashSet<T>
        : ISet<T>,
          IReadOnlyCollection<T>,
          ISerializationCallbackReceiver
    {
        #region Fields

        private HashSet<T> _entries;

        //== 직렬화 백킹 (모든 빌드 컴파일). JsonUtility/Inspector 가 이 리스트를 읽고 쓴다.
        [SerializeField] private List<T> _serialized = new List<T>();

        #endregion

        #region Properties

        /// <summary>현재 집합에 있는 항목 수.</summary>
        public int Count
        {
            get { return _entries.Count; }
        }

        /// <summary>집합이 비어 있는지 여부.</summary>
        public bool IsEmpty
        {
            get { return _entries.Count == 0; }
        }

        /// <summary>읽기 전용 여부 (이 집합은 항상 false).</summary>
        public bool IsReadOnly
        {
            get { return false; }
        }

        /// <summary>요소 비교에 사용되는 IEqualityComparer.</summary>
        public IEqualityComparer<T> Comparer
        {
            get { return _entries.Comparer; }
        }

        #endregion

        #region Constructors

        public UniHashSet()
        {
            _entries = new HashSet<T>();
        }

        public UniHashSet(int capacity)
        {
            _entries = new HashSet<T>(capacity);
        }

        public UniHashSet(IEqualityComparer<T> comparer)
        {
            _entries = new HashSet<T>(comparer);
        }

        public UniHashSet(int capacity, IEqualityComparer<T> comparer)
        {
            _entries = new HashSet<T>(capacity, comparer);
        }

        public UniHashSet(IEnumerable<T> source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            _entries = new HashSet<T>(source);
        }

        public UniHashSet(IEnumerable<T> source, IEqualityComparer<T> comparer)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            _entries = new HashSet<T>(source, comparer);
        }

        #endregion

        #region Accessors

        /// <summary>항목이 집합에 포함되어 있는지 확인합니다.</summary>
        public bool Contains(T item)
        {
            return _entries.Contains(item);
        }

        /// <summary>
        /// 집합에서 동등한 항목을 찾아 반환합니다.
        /// 사용자 정의 Equals/GetHashCode가 있을 때, 동등하지만 다른 인스턴스를 가져올 때 유용합니다.
        /// </summary>
        public bool TryGetValue(T equalValue, out T actualValue)
        {
            return _entries.TryGetValue(equalValue, out actualValue);
        }

        /// <summary>집합 내용을 배열에 복사합니다.</summary>
        public void CopyTo(T[] array)
        {
            _entries.CopyTo(array);
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            _entries.CopyTo(array, arrayIndex);
        }

        public void CopyTo(T[] array, int arrayIndex, int count)
        {
            _entries.CopyTo(array, arrayIndex, count);
        }

        #endregion

        #region Mutators

        /// <summary>
        /// 항목을 추가합니다. 이미 존재하면 false를 반환합니다.
        /// HashSet&lt;T&gt;.Add의 표준 시그니처입니다.
        /// </summary>
        public bool Add(T item)
        {
            return _entries.Add(item);
        }

        /// <summary>항목을 제거합니다.</summary>
        public bool Remove(T item)
        {
            return _entries.Remove(item);
        }

        /// <summary>조건에 맞는 항목들을 제거합니다. 제거된 개수를 반환합니다.</summary>
        public int RemoveWhere(Predicate<T> match)
        {
            if (match == null)
            {
                throw new ArgumentNullException(nameof(match));
            }
            return _entries.RemoveWhere(match);
        }

        /// <summary>모든 항목을 제거합니다.</summary>
        public void Clear()
        {
            _entries.Clear();
        }

        /// <summary>내부 용량을 현재 항목 수에 맞춰 축소합니다.</summary>
        public void TrimExcess()
        {
            _entries.TrimExcess();
        }

        //== ICollection<T> 명시적 구현 — bool 반환 Add와 충돌 회피
        void ICollection<T>.Add(T item)
        {
            _entries.Add(item);
        }

        #endregion

        #region Set Operations

        /// <summary>현재 집합에 other의 모든 항목을 합칩니다 (합집합).</summary>
        public void UnionWith(IEnumerable<T> other)
        {
            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }
            _entries.UnionWith(other);
        }

        /// <summary>현재 집합을 other에도 있는 항목만 남깁니다 (교집합).</summary>
        public void IntersectWith(IEnumerable<T> other)
        {
            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }
            _entries.IntersectWith(other);
        }

        /// <summary>현재 집합에서 other에 있는 항목들을 제거합니다 (차집합).</summary>
        public void ExceptWith(IEnumerable<T> other)
        {
            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }
            _entries.ExceptWith(other);
        }

        /// <summary>현재 집합과 other의 대칭 차집합으로 만듭니다 (양쪽 중 하나에만 있는 항목).</summary>
        public void SymmetricExceptWith(IEnumerable<T> other)
        {
            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }
            _entries.SymmetricExceptWith(other);
        }

        #endregion

        #region Set Comparison

        /// <summary>현재 집합이 other의 부분집합인지 확인합니다.</summary>
        public bool IsSubsetOf(IEnumerable<T> other)
        {
            return _entries.IsSubsetOf(other);
        }

        /// <summary>현재 집합이 other의 상위집합인지 확인합니다.</summary>
        public bool IsSupersetOf(IEnumerable<T> other)
        {
            return _entries.IsSupersetOf(other);
        }

        /// <summary>현재 집합이 other의 진부분집합(같지 않은 부분집합)인지 확인합니다.</summary>
        public bool IsProperSubsetOf(IEnumerable<T> other)
        {
            return _entries.IsProperSubsetOf(other);
        }

        /// <summary>현재 집합이 other의 진상위집합(같지 않은 상위집합)인지 확인합니다.</summary>
        public bool IsProperSupersetOf(IEnumerable<T> other)
        {
            return _entries.IsProperSupersetOf(other);
        }

        /// <summary>두 집합이 공통 요소를 가지는지 확인합니다.</summary>
        public bool Overlaps(IEnumerable<T> other)
        {
            return _entries.Overlaps(other);
        }

        /// <summary>두 집합이 동일한 요소를 가지는지 확인합니다.</summary>
        public bool SetEquals(IEnumerable<T> other)
        {
            return _entries.SetEquals(other);
        }

        #endregion

        #region Enumeration

        public IEnumerator<T> GetEnumerator()
        {
            return _entries.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        #endregion

        #region Serialization Callbacks

        /// <summary>직렬화 직전 호출. 집합 내용을 직렬화 백킹 리스트로 복사합니다.</summary>
        public void OnBeforeSerialize()
        {
            if (_serialized == null)
            {
                _serialized = new List<T>();
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
            foreach (var item in _entries)
            {
                _serialized.Add(item);
            }
        }

        /// <summary>
        /// 역직렬화 직후 호출. 백킹 리스트로부터 집합을 재구성합니다.
        /// JsonUtility 는 생성자를 호출하지 않으므로 _entries 가 null 일 수 있어 방어적으로 생성합니다.
        /// </summary>
        public void OnAfterDeserialize()
        {
            if (_entries == null)
            {
                _entries = new HashSet<T>();
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
                _entries.Add(_serialized[i]);
            }
        }

        #endregion
    }
}