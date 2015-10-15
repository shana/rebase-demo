using System;
using System.Collections.Generic;
using System.Collections.Specialized;

namespace GitHub.Collections
{
    public interface ITrackingCollection<T> : IDisposable, IList<T>  where T : ICopyable<T>
    {
        IObservable<T> Listen(IObservable<T> obs);
        IDisposable Subscribe();
        IDisposable Subscribe(Action onCompleted);
        void SetComparer(Func<T, T, int> theComparer);
        void SetFilter(Func<T, int, IList<T>, bool> filter);
        event NotifyCollectionChangedEventHandler CollectionChanged;
    }
}