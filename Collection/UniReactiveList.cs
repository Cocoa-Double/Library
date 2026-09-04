using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Cocoa.Lib.Collection
{
    /// <summary>
    /// 컬렉션 변경뿐 아니라 요소 내부의 속성 변경(INotifyPropertyChanged)까지 통지하는 리스트.
    /// 리스트에 추가된 요소의 PropertyChanged를 자동으로 구독하고, 제거 시 해제합니다.
    /// </summary>
    [Serializable]
    public class UniReactiveList<T> : UniObservableList<T>
        where T : class, INotifyPropertyChanged
    {
        #region Static Fields

        private static readonly Dictionary<string, PropertyChangedEventArgs> ArgsCache
            = new Dictionary<string, PropertyChangedEventArgs>();
        private static readonly object CacheLock = new object();

        #endregion

        #region Events

        /// <summary>요소의 속성이 변경되면 발화됩니다. sender는 변경된 요소입니다.</summary>
        public event PropertyChangedEventHandler ItemPropertyChanged;

        #endregion

        #region Constructors

        public UniReactiveList() : base() { }

        public UniReactiveList(IList<T> list) : base(list)
        {
            //== base 생성자는 내부 리스트에 직접 채워넣으므로 InsertItem 훅이 호출되지 않음
            foreach (var item in this)
            {
                if (item != null)
                {
                    item.PropertyChanged += OnElementPropertyChanged;
                }
            }
        }

        #endregion

        #region Protected Hooks Override

        protected override void OnItemInserted(int index, T item)
        {
            if (item != null)
            {
                item.PropertyChanged += OnElementPropertyChanged;
            }
        }

        protected override void OnItemRemoved(int index, T item)
        {
            if (item != null)
            {
                item.PropertyChanged -= OnElementPropertyChanged;
            }
        }

        protected override void OnItemReplaced(int index, T oldItem, T newItem)
        {
            if (oldItem != null)
            {
                oldItem.PropertyChanged -= OnElementPropertyChanged;
            }
            if (newItem != null)
            {
                newItem.PropertyChanged += OnElementPropertyChanged;
            }
        }

        protected override void OnItemsClearing()
        {
            foreach (var item in this)
            {
                if (item != null)
                {
                    item.PropertyChanged -= OnElementPropertyChanged;
                }
            }
        }

        //== 역직렬화로 로드된 요소들에 다시 구독 (베이스가 백킹을 직접 채워 OnItemInserted 가 호출되지 않으므로)
        protected override void OnAfterDeserializeRebuilt()
        {
            foreach (var item in this)
            {
                if (item != null)
                {
                    //== 중복 구독 방지
                    item.PropertyChanged -= OnElementPropertyChanged;
                    item.PropertyChanged += OnElementPropertyChanged;
                }
            }
        }

        #endregion

        #region Private Helpers

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

        private void OnElementPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            ItemPropertyChanged?.Invoke(sender, GetCachedArgs(e.PropertyName));
        }

        #endregion
    }
}