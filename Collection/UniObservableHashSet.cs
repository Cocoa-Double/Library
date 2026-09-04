using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using UnityEngine;

namespace Cocoa.Lib.Collection
{
    /// <summary>
    /// 변경 통지와 Unity Inspector 시각화를 모두 지원하는 HashSet.
    /// 단일 변경(Add/Remove)은 개별 이벤트로 통지되며,
    /// 집합 연산(UnionWith/IntersectWith 등)은 Reset 이벤트로 일괄 통지됩니다.
    /// </summary>
    [Serializable]
    public class UniObservableHashSet<T>
        : ISet<T>,
          IReadOnlyCollection<T>,
          IUniObservableCollection<T>,
          ISerializationCallbackReceiver
    {
        #region Fields

        private HashSet<T> _entries;

        //== 직렬화 백킹 (모든 빌드 컴파일, JsonUtility/Newtonsoft round-trip).
        [SerializeField] private List<T> _serialized = new List<T>();

        #endregion

        #region Events

        //== IUniObservableCollection 계약
        public event Action<T> ItemAdded;
        public event Action<T> ItemRemoved;

        /// <summary>HashSet은 요소 교체 개념이 없어 이 이벤트는 발화되지 않습니다 (인터페이스 호환용).</summary>
#pragma warning disable CS0067
        public event Action<int, T, T> ItemReplaced;
#pragma warning restore CS0067

        public event NotifyCollectionChangedEventHandler CollectionChanged;

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

        public UniObservableHashSet()
        {
            _entries = new HashSet<T>();
        }

        public UniObservableHashSet(int capacity)
        {
            _entries = new HashSet<T>(capacity);
        }

        public UniObservableHashSet(IEqualityComparer<T> comparer)
        {
            _entries = new HashSet<T>(comparer);
        }

        public UniObservableHashSet(int capacity, IEqualityComparer<T> comparer)
        {
            _entries = new HashSet<T>(capacity, comparer);
        }

        public UniObservableHashSet(IEnumerable<T> source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            _entries = new HashSet<T>(source);
        }

        public UniObservableHashSet(IEnumerable<T> source, IEqualityComparer<T> comparer)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            _entries = new HashSet<T>(source, comparer);
        }

        #endregion

        #region Accessors

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
        /// 항목을 추가합니다. 이미 존재하면 false를 반환하고 이벤트는 발화되지 않습니다.
        /// </summary>
        public bool Add(T item)
        {
            if (_entries.Add(item))
            {
                RaiseAdded(item);
                return true;
            }
            return false;
        }

        /// <summary>항목을 제거합니다. 항목이 없으면 false를 반환하고 이벤트는 발화되지 않습니다.</summary>
        public bool Remove(T item)
        {
            if (_entries.Remove(item))
            {
                RaiseRemoved(item);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 조건에 맞는 항목들을 제거합니다. 제거된 개수를 반환합니다.
        /// 일괄 변경이므로 Reset 이벤트로 통지됩니다.
        /// </summary>
        public int RemoveWhere(Predicate<T> match)
        {
            if (match == null)
            {
                throw new ArgumentNullException(nameof(match));
            }
            int removed = _entries.RemoveWhere(match);
            if (removed > 0)
            {
                RaiseReset();
            }
            return removed;
        }

        public void Clear()
        {
            if (_entries.Count == 0)
            {
                return;
            }
            _entries.Clear();
            RaiseReset();
        }

        /// <summary>내부 용량을 현재 항목 수에 맞춰 축소합니다. 컬렉션 변경이 아니므로 이벤트는 발화되지 않습니다.</summary>
        public void TrimExcess()
        {
            _entries.TrimExcess();
        }

        //== ICollection<T> 명시적 구현 — bool 반환 Add와 충돌 회피
        void ICollection<T>.Add(T item)
        {
            Add(item);
        }

        #endregion

        #region Set Operations

        //== 집합 연산은 일괄 변경으로 간주하여 Reset 이벤트로 통지

        /// <summary>현재 집합에 other의 모든 항목을 합칩니다 (합집합). 변경 시 Reset 이벤트가 발화됩니다.</summary>
        public void UnionWith(IEnumerable<T> other)
        {
            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }
            int beforeCount = _entries.Count;
            _entries.UnionWith(other);
            if (_entries.Count != beforeCount)
            {
                RaiseReset();
            }
        }

        /// <summary>현재 집합을 other에도 있는 항목만 남깁니다 (교집합). 변경 시 Reset 이벤트가 발화됩니다.</summary>
        public void IntersectWith(IEnumerable<T> other)
        {
            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }
            int beforeCount = _entries.Count;
            _entries.IntersectWith(other);
            if (_entries.Count != beforeCount)
            {
                RaiseReset();
            }
        }

        /// <summary>현재 집합에서 other에 있는 항목들을 제거합니다 (차집합). 변경 시 Reset 이벤트가 발화됩니다.</summary>
        public void ExceptWith(IEnumerable<T> other)
        {
            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }
            int beforeCount = _entries.Count;
            _entries.ExceptWith(other);
            if (_entries.Count != beforeCount)
            {
                RaiseReset();
            }
        }

        /// <summary>
        /// 현재 집합과 other의 대칭 차집합으로 만듭니다 (양쪽 중 하나에만 있는 항목).
        /// 변경 시 Reset 이벤트가 발화됩니다.
        /// </summary>
        public void SymmetricExceptWith(IEnumerable<T> other)
        {
            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }
            //== SymmetricExcept는 Count만으로 변경 여부 판단 불가
            //== (양쪽에서 동일 개수가 빠지고 추가될 수도 있음)
            //== 따라서 항상 Reset 발화 (단, 입력이 비어있으면 변경 없음)
            bool hasAny = false;
            using (var enumerator = other.GetEnumerator())
            {
                hasAny = enumerator.MoveNext();
            }
            _entries.SymmetricExceptWith(other);
            if (hasAny)
            {
                RaiseReset();
            }
        }

        #endregion

        #region Set Comparison

        public bool IsSubsetOf(IEnumerable<T> other)
        {
            return _entries.IsSubsetOf(other);
        }

        public bool IsSupersetOf(IEnumerable<T> other)
        {
            return _entries.IsSupersetOf(other);
        }

        public bool IsProperSubsetOf(IEnumerable<T> other)
        {
            return _entries.IsProperSubsetOf(other);
        }

        public bool IsProperSupersetOf(IEnumerable<T> other)
        {
            return _entries.IsProperSupersetOf(other);
        }

        public bool Overlaps(IEnumerable<T> other)
        {
            return _entries.Overlaps(other);
        }

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
        /// 내부 컬렉션을 직접 채우므로 로드 시 변경 이벤트는 발화되지 않습니다.
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
            if (_serialized != null)
            {
                for (int i = 0; i < _serialized.Count; i++)
                {
                    _entries.Add(_serialized[i]);
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