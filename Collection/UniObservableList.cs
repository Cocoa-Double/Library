using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using UnityEngine;

namespace Cocoa.Lib.Collection
{
    /// <summary>
    /// 컬렉션 변경(추가/제거/교체/초기화)을 통지하며, JsonUtility/Newtonsoft 직렬화(round-trip)를 지원하는 리스트.
    /// 요소 내부의 속성 변경은 추적하지 않습니다 — 그 기능이 필요하면 UniReactiveList를 사용하세요.
    /// </summary>
    /// <remarks>
    /// 내부적으로 <see cref="List{T}"/> 를 합성(composition)하며, 그 리스트가 곧 직렬화 백킹입니다.
    /// (기존 Collection<T> 상속은 직렬화 시 내부 저장소를 재생성할 수 없어 제거됨)
    /// 변경 동작은 InsertItem/RemoveItem/SetItem/ClearItems 템플릿 메서드를 통하며,
    /// 역직렬화 시에는 이들을 거치지 않고 백킹을 직접 사용하므로 로드 시 이벤트가 발화되지 않습니다.
    /// </remarks>
    [Serializable]
    public class UniObservableList<T>
        : IList<T>,
          IReadOnlyList<T>,
          IUniObservableCollection<T>,
          ISerializationCallbackReceiver
    {
        #region Fields

        //== 내부 저장소 겸 직렬화 백킹 (모든 빌드 컴파일).
        [SerializeField] private List<T> _items = new List<T>();

        #endregion

        #region Events

        public event Action<T> ItemAdded;
        public event Action<T> ItemRemoved;
        public event Action<int, T, T> ItemReplaced;
        public event NotifyCollectionChangedEventHandler CollectionChanged;

        #endregion

        #region Properties

        public int Count
        {
            get { return _items.Count; }
        }

        public bool IsReadOnly
        {
            get { return false; }
        }

        public int Capacity
        {
            get { return _items.Capacity; }
            set { _items.Capacity = value; }
        }

        #endregion

        #region Indexer

        public T this[int index]
        {
            get { return _items[index]; }
            set { SetItem(index, value); }
        }

        #endregion

        #region Constructors

        public UniObservableList()
        {
            _items = new List<T>();
        }

        public UniObservableList(IList<T> list)
        {
            if (list == null)
            {
                throw new ArgumentNullException(nameof(list));
            }
            //== 생성 시점에는 이벤트를 발화하지 않고 그대로 채운다
            _items = new List<T>(list);
        }

        #endregion

        #region IList<T>

        public void Add(T item)
        {
            InsertItem(_items.Count, item);
        }

        public void Insert(int index, T item)
        {
            if (index < 0 || index > _items.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            InsertItem(index, item);
        }

        public bool Remove(T item)
        {
            int index = _items.IndexOf(item);
            if (index < 0)
            {
                return false;
            }
            RemoveItem(index);
            return true;
        }

        public void RemoveAt(int index)
        {
            if (index < 0 || index >= _items.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            RemoveItem(index);
        }

        public void Clear()
        {
            ClearItems();
        }

        public bool Contains(T item)
        {
            return _items.Contains(item);
        }

        public int IndexOf(T item)
        {
            return _items.IndexOf(item);
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            _items.CopyTo(array, arrayIndex);
        }

        public IEnumerator<T> GetEnumerator()
        {
            return _items.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _items.GetEnumerator();
        }

        #endregion

        #region Template Methods (변경 진입점)

        //== Collection<T> 의 가상 메서드 구조를 보존 — 파생 클래스는 아래 On* 훅을 오버라이드한다.

        protected virtual void InsertItem(int index, T item)
        {
            _items.Insert(index, item);
            OnItemInserted(index, item);
            ItemAdded?.Invoke(item);
            CollectionChanged?.Invoke(this,
                new NotifyCollectionChangedEventArgs(
                    NotifyCollectionChangedAction.Add, item, index));
        }

        protected virtual void RemoveItem(int index)
        {
            T removed = _items[index];
            _items.RemoveAt(index);
            OnItemRemoved(index, removed);
            ItemRemoved?.Invoke(removed);
            CollectionChanged?.Invoke(this,
                new NotifyCollectionChangedEventArgs(
                    NotifyCollectionChangedAction.Remove, removed, index));
        }

        protected virtual void SetItem(int index, T item)
        {
            T oldItem = _items[index];
            if (EqualityComparer<T>.Default.Equals(oldItem, item))
            {
                return;
            }
            _items[index] = item;
            OnItemReplaced(index, oldItem, item);
            ItemReplaced?.Invoke(index, oldItem, item);
            CollectionChanged?.Invoke(this,
                new NotifyCollectionChangedEventArgs(
                    NotifyCollectionChangedAction.Replace, item, oldItem, index));
        }

        protected virtual void ClearItems()
        {
            OnItemsClearing();
            _items.Clear();
            CollectionChanged?.Invoke(this,
                new NotifyCollectionChangedEventArgs(
                    NotifyCollectionChangedAction.Reset));
        }

        #endregion

        #region Protected Hooks

        protected virtual void OnItemInserted(int index, T item) { }
        protected virtual void OnItemRemoved(int index, T item) { }
        protected virtual void OnItemReplaced(int index, T oldItem, T newItem) { }
        protected virtual void OnItemsClearing() { }

        /// <summary>
        /// 역직렬화로 리스트가 재구성된 직후 호출됩니다.
        /// 파생 클래스(UniReactiveList)가 로드된 요소에 다시 구독하는 데 사용합니다.
        /// </summary>
        protected virtual void OnAfterDeserializeRebuilt() { }

        #endregion

        #region Bulk Operations

        public void AddRange(IEnumerable<T> collection)
        {
            if (collection == null)
            {
                throw new ArgumentNullException(nameof(collection));
            }
            foreach (var item in collection)
            {
                Add(item);
            }
        }

        public void InsertRange(int index, IEnumerable<T> collection)
        {
            if (collection == null)
            {
                throw new ArgumentNullException(nameof(collection));
            }
            int currentIndex = index;
            foreach (var item in collection)
            {
                Insert(currentIndex, item);
                currentIndex++;
            }
        }

        public int RemoveAll(Predicate<T> match)
        {
            if (match == null)
            {
                throw new ArgumentNullException(nameof(match));
            }
            int removed = _items.RemoveAll(match);
            if (removed > 0)
            {
                RaiseReset();
            }
            return removed;
        }

        public void RemoveRange(int index, int count)
        {
            if (count == 0)
            {
                return;
            }
            _items.RemoveRange(index, count);
            RaiseReset();
        }

        #endregion

        #region Sort & Reverse

        //== 정렬/반전은 SetItem을 거치지 않으므로 Reset 이벤트로 일괄 통지

        public void Sort()
        {
            _items.Sort();
            RaiseReset();
        }

        public void Sort(IComparer<T> comparer)
        {
            _items.Sort(comparer);
            RaiseReset();
        }

        public void Sort(Comparison<T> comparison)
        {
            _items.Sort(comparison);
            RaiseReset();
        }

        public void Sort(int index, int count, IComparer<T> comparer)
        {
            _items.Sort(index, count, comparer);
            RaiseReset();
        }

        public void Reverse()
        {
            _items.Reverse();
            RaiseReset();
        }

        public void Reverse(int index, int count)
        {
            _items.Reverse(index, count);
            RaiseReset();
        }

        #endregion

        #region Search

        public int BinarySearch(T item)
        {
            return _items.BinarySearch(item);
        }

        public int BinarySearch(T item, IComparer<T> comparer)
        {
            return _items.BinarySearch(item, comparer);
        }

        public int BinarySearch(int index, int count, T item, IComparer<T> comparer)
        {
            return _items.BinarySearch(index, count, item, comparer);
        }

        public T Find(Predicate<T> match)
        {
            return _items.Find(match);
        }

        public List<T> FindAll(Predicate<T> match)
        {
            return _items.FindAll(match);
        }

        public int FindIndex(Predicate<T> match)
        {
            return _items.FindIndex(match);
        }

        public int FindIndex(int startIndex, Predicate<T> match)
        {
            return _items.FindIndex(startIndex, match);
        }

        public int FindLastIndex(Predicate<T> match)
        {
            return _items.FindLastIndex(match);
        }

        public T FindLast(Predicate<T> match)
        {
            return _items.FindLast(match);
        }

        public bool TrueForAll(Predicate<T> match)
        {
            return _items.TrueForAll(match);
        }

        public void ForEach(Action<T> action)
        {
            _items.ForEach(action);
        }

        #endregion

        #region Serialization Callbacks

        /// <summary>직렬화 직전 호출. 내부 리스트가 곧 백킹이므로 별도 작업이 없습니다.</summary>
        public void OnBeforeSerialize()
        {
            if (_items == null)
            {
                _items = new List<T>();
            }
        }

        /// <summary>
        /// 역직렬화 직후 호출. JsonUtility 는 생성자를 호출하지 않으므로 _items 가 null 일 수 있어 방어적으로 생성합니다.
        /// 백킹을 직접 사용하므로 로드 시 변경 이벤트는 발화되지 않습니다.
        /// </summary>
        public void OnAfterDeserialize()
        {
            if (_items == null)
            {
                _items = new List<T>();
            }
            OnAfterDeserializeRebuilt();
        }

        #endregion

        #region Private Helpers

        private void RaiseReset()
        {
            CollectionChanged?.Invoke(this,
                new NotifyCollectionChangedEventArgs(
                    NotifyCollectionChangedAction.Reset));
        }

        #endregion
    }
}
