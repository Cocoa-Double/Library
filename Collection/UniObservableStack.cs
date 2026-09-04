using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using UnityEngine;

namespace Cocoa.Lib.Collection
{
    /// <summary>
    /// Unity Inspector에서 내용을 확인할 수 있고, 변경을 통지하는 스택.
    /// Push/Pop 등 모든 변경 시점에 이벤트가 발화되며,
    /// Inspector 미러는 지연 동기화 방식으로 런타임 오버헤드를 최소화합니다.
    /// 열거 순서는 Stack<T> 표준과 동일하게 top → bottom 입니다.
    /// </summary>
    [Serializable]
    public class UniObservableStack<T>
        : IUniObservableCollection<T>,
          IReadOnlyCollection<T>,
          ISerializationCallbackReceiver
    {
        #region Fields

        private Stack<T> _stack;

        //== 직렬화 백킹 (모든 빌드 컴파일, JsonUtility/Newtonsoft round-trip). top → bottom 순서로 저장.
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

        //== 스택 도메인 별칭 이벤트

        /// <summary>Push 시 발화됩니다. ItemAdded와 동일 시점.</summary>
        public event Action<T> ItemPushed
        {
            add { ItemAdded += value; }
            remove { ItemAdded -= value; }
        }

        /// <summary>Pop 시 발화됩니다. ItemRemoved와 동일 시점.</summary>
        public event Action<T> ItemPopped
        {
            add { ItemRemoved += value; }
            remove { ItemRemoved -= value; }
        }

        #endregion

        #region Properties

        /// <summary>현재 스택에 있는 항목 수.</summary>
        public int Count
        {
            get { return _stack.Count; }
        }

        /// <summary>스택이 비어 있는지 여부.</summary>
        public bool IsEmpty
        {
            get { return _stack.Count == 0; }
        }

        #endregion

        #region Constructors

        public UniObservableStack()
        {
            _stack = new Stack<T>();
        }

        public UniObservableStack(int capacity)
        {
            _stack = new Stack<T>(capacity);
        }

        public UniObservableStack(IEnumerable<T> source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            //== Stack<T>(IEnumerable)은 입력 순서대로 push하므로
            //== 입력의 마지막 항목이 top에 위치함
            _stack = new Stack<T>(source);
        }

        #endregion

        #region Stack Operations

        /// <summary>항목을 스택의 맨 위에 추가합니다.</summary>
        public void Push(T item)
        {
            _stack.Push(item);
            RaiseAdded(item);
        }

        /// <summary>여러 항목을 순서대로 push합니다. 마지막 항목이 top이 됩니다.</summary>
        public void PushRange(IEnumerable<T> items)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }
            foreach (var item in items)
            {
                Push(item);
            }
        }

        /// <summary>스택의 맨 위 항목을 꺼냅니다. 스택이 비어 있으면 예외 발생.</summary>
        public T Pop()
        {
            T item = _stack.Pop();
            RaiseRemoved(item);
            return item;
        }

        /// <summary>스택의 맨 위 항목을 안전하게 꺼냅니다. 비어 있으면 false 반환.</summary>
        public bool TryPop(out T item)
        {
            if (_stack.TryPop(out item))
            {
                RaiseRemoved(item);
                return true;
            }
            return false;
        }

        /// <summary>스택의 맨 위 항목을 꺼내지 않고 확인합니다. 스택이 비어 있으면 예외 발생.</summary>
        public T Peek()
        {
            return _stack.Peek();
        }

        /// <summary>스택의 맨 위 항목을 안전하게 확인합니다. 비어 있으면 false 반환.</summary>
        public bool TryPeek(out T item)
        {
            return _stack.TryPeek(out item);
        }

        /// <summary>스택의 모든 항목을 제거합니다.</summary>
        public void Clear()
        {
            if (_stack.Count == 0)
            {
                return;
            }
            _stack.Clear();
            RaiseReset();
        }

        #endregion

        #region Queries

        /// <summary>특정 항목이 스택에 포함되어 있는지 확인합니다.</summary>
        public bool Contains(T item)
        {
            return _stack.Contains(item);
        }

        /// <summary>스택 내용을 배열로 복사하여 반환합니다 (top → bottom 순서).</summary>
        public T[] ToArray()
        {
            return _stack.ToArray();
        }

        /// <summary>스택 내용을 지정한 배열에 복사합니다 (top → bottom 순서).</summary>
        public void CopyTo(T[] array, int arrayIndex)
        {
            _stack.CopyTo(array, arrayIndex);
        }

        #endregion

        #region Enumeration

        /// <summary>스택을 top → bottom 순서로 열거합니다.</summary>
        public IEnumerator<T> GetEnumerator()
        {
            return _stack.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        #endregion

        #region Serialization Callbacks

        /// <summary>
        /// Unity가 직렬화하기 직전에 호출됩니다.
        /// 스택 내용을 Inspector 표시용 리스트로 복사합니다 (top → bottom 순서, 단방향).
        /// </summary>
        public void OnBeforeSerialize()
        {
            if (_serialized == null)
            {
                _serialized = new List<T>();
            }
            _serialized.Clear();
            if (_stack == null)
            {
                return;
            }
            if (_serialized.Capacity < _stack.Count)
            {
                _serialized.Capacity = _stack.Count;
            }
            //== Stack<T>.GetEnumerator()는 top → bottom 순서
            foreach (var item in _stack)
            {
                _serialized.Add(item);
            }
        }

        /// <summary>
        /// 역직렬화 직후 호출. 백킹 리스트(top → bottom)로부터 스택을 재구성합니다.
        /// 원래 top 이 top 으로 복원되도록 역순(bottom → top)으로 push 합니다.
        /// 내부 스택을 직접 채우므로 로드 시 변경 이벤트는 발화되지 않습니다.
        /// </summary>
        public void OnAfterDeserialize()
        {
            if (_stack == null)
            {
                _stack = new Stack<T>();
            }
            else
            {
                _stack.Clear();
            }
            if (_serialized != null)
            {
                //== _serialized 는 top → bottom. bottom(마지막)부터 push 해야 top 이 top 으로 복원됨
                for (int i = _serialized.Count - 1; i >= 0; i--)
                {
                    _stack.Push(_serialized[i]);
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