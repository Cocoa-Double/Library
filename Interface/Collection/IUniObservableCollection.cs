using System;
using System.Collections.Specialized;

namespace Cocoa.Lib.Collection
{
    public interface IUniObservableCollection<T> : INotifyCollectionChanged
    {
        event Action<T> ItemAdded;
        event Action<T> ItemRemoved;
        event Action<int, T, T> ItemReplaced;
    }
}
