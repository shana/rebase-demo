#define DISABLE_REACTIVEUI

#if !DISABLE_REACTIVEUI
using ReactiveUI;
#endif
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Windows.Threading;

namespace GitHub.Collections
{
    public class TrackingCollection<T> : ObservableCollection<T>, IDisposable
        where T : class, ICopyable<T>
    {
        protected enum TheAction
        {
            None,
            Move,
            Add,
            Insert
        }

        CompositeDisposable disposables = new CompositeDisposable();
        IObservable<T> source;
        IObservable<T> sourceQueue;
        Func<T, T, int> comparer;
        Func<T, int, IList<T>, bool> filter;
        IScheduler scheduler;
        int itemCount = 0;
        ConcurrentQueue<T> queue;

        List<T> original = new List<T>();
#if DEBUG
        public IList<T> DebugInternalList => original;
#endif

        TimeSpan delay;
        TimeSpan requestedDelay;
        TimeSpan fuzziness;
        public TimeSpan ProcessingDelay {
            get { return requestedDelay; }
            set
            {
                requestedDelay = value;
                delay = value;
            }
        }
        

        public TrackingCollection()
        {
            queue = new ConcurrentQueue<T>();
            ProcessingDelay = TimeSpan.FromMilliseconds(10);
            fuzziness = TimeSpan.FromMilliseconds(1);
        }

        public TrackingCollection(Func<T, T, int> comparer = null, Func<T, int, IList<T>, bool> filter = null, IScheduler scheduler = null)
            : this()
        {
#if DISABLE_REACTIVEUI
            this.scheduler = GetScheduler(scheduler);
#else
            this.scheduler = scheduler ?? RxApp.MainThreadScheduler;
#endif
            this.comparer = comparer;
            this.filter = filter;
            original = new List<T>();
        }

        public TrackingCollection(IObservable<T> source,
            Func<T, T, int> comparer = null,
            Func<T, int, IList<T>, bool> filter = null,
            IScheduler scheduler = null)
            : this(comparer, filter, scheduler)
        {
            this.source = source;
            Listen(source);
        }

        public IObservable<T> Listen(IObservable<T> obs)
        {
            sourceQueue = obs
                .Do(data => queue.Enqueue(data));

            source = Observable
                .Generate(StartQueue(),
                    i => !disposed,
                    i => i+1,
                    i => GetFromQueue(),
                    i => delay
                )
                .Where(data => data != null)
                .ObserveOn(scheduler)
                .Select(x => ProcessItem(x, original))
                .Select(SortedNone)
                .Select(SortedAdd)
                .Select(SortedInsert)
                .Select(SortedMove)
                .Select(CheckFilter)
                .Select(FilteredAdd)
                .Select(CalculateIndexes)
                .Select(FilteredNone)
                .Select(FilteredInsert)
                .Select(FilteredMove)
                .TimeInterval()
                .Select(UpdateProcessingDelay)
                .Select(data => data.Item)
                .Publish()
                .RefCount();

            return source;
        }

        public IDisposable Subscribe()
        {
            if (source == null)
                throw new InvalidOperationException("No source observable has been set. Call Listen or pass an observable to the constructor");
            if (disposed)
                throw new ObjectDisposedException("TrackingCollection");
            disposables.Add(source.Subscribe());
            return this;
        }

        public IDisposable Subscribe(Action<T> onNext, Action onCompleted)
        {
            if (source == null)
                throw new InvalidOperationException("No source observable has been set. Call Listen or pass an observable to the constructor");
            if (disposed)
                throw new ObjectDisposedException("TrackingCollection");
            disposables.Add(source.Subscribe(onNext, onCompleted));
            return this;
        }

        public void AddItem(T item)
        {
            if (disposed)
                throw new ObjectDisposedException("TrackingCollection");
            queue.Enqueue(item);
        }

        public void RemoveItem(T item)
        {
            if (disposed)
                throw new ObjectDisposedException("TrackingCollection");
            var position = GetIndexUnfiltered(original, item);
            var index = GetIndex(item);
            original.Remove(item);
            InternalRemoveItem(item);
            RecalculateFilter(original, index, position, original.Count);
        }

        void InternalAddItem(T item)
        {
            Add(item);
        }

        void InternalInsertItem(T item, int position)
        {
            Insert(position, item);
        }

        void InternalRemoveItem(T item)
        {
            Remove(item);
        }

        void InternalMoveItem(T item, int positionFrom, int positionTo)
        {
            Move(positionFrom, positionFrom < positionTo ? positionTo - 1 : positionTo);
        }

        ActionData CheckFilter(ActionData data)
        {
            data.IsIncluded = true;
            if (filter != null)
                data.IsIncluded = filter(data.Item, data.Position, this);
            return data;
        }

        int StartQueue()
        {
            disposables.Add(sourceQueue.Subscribe(_ => itemCount++));
            return 0;
        }

        T GetFromQueue()
        {
            try
            {
                T d = null;
                if (queue?.TryDequeue(out d) ?? false)
                    return d;
            }
            catch { }
            return null;
        }

        ActionData ProcessItem(T item, IList<T> list)
        {
            ActionData ret;

            var idx = list.IndexOf(item);
            if (idx >= 0)
            {
                var old = list[idx];
                var comparison = 0;
                if (comparer != null)
                    comparison = comparer(item, old);

                // no sorting to be done, just replacing the element in-place
                if (comparer == null || comparison == 0)
                    ret = new ActionData(TheAction.None, item, null, idx, idx, list);
                else
                    // element has moved, save the original object, because we want to update its contents and move it
                    // but not overwrite the instance.
                    ret = new ActionData(TheAction.Move, item, old, comparison, idx, list);
            }
            // the element doesn't exist yet
            // figure out whether we're larger than the last element or smaller than the first or
            // if we have to place the new item somewhere in the middle
            else if (comparer != null && list.Count > 0)
            {
                if (comparer(list[0], item) >= 0)
                    ret = new ActionData(TheAction.Insert, item, null, 0, -1, list);

                else if (comparer(list[list.Count - 1], item) <= 0)
                    ret = new ActionData(TheAction.Add, item, null, list.Count, -1, list);

                // this happens if the original observable is not sorted, or it's sorting order doesn't
                // match the comparer that has been set
                else
                {
                    idx = BinarySearch(list, 0, item, comparer);
                    if (idx < 0)
                        ret = new ActionData(TheAction.Add, item, null, list.Count, -1, list);

                    else
                        ret = new ActionData(TheAction.Insert, item, null, idx, -1, list);
                }
            }
            else
                ret = new ActionData(TheAction.Add, item, null, list.Count, -1, list);
            return ret;
        }

        ActionData SortedNone(ActionData data)
        {
            if (data.TheAction != TheAction.None)
                return data;
            data.List[data.OldPosition].CopyFrom(data.Item);
            return data;
        }

        ActionData SortedAdd(ActionData data)
        {
            if (data.TheAction != TheAction.Add)
                return data;
            data.List.Add(data.Item);
            return data;
        }

        ActionData SortedInsert(ActionData data)
        {
            if (data.TheAction != TheAction.Insert)
                return data;
            data.List.Insert(data.Position, data.Item);
            return data;
        }
        ActionData SortedMove(ActionData data)
        {
            if (data.TheAction != TheAction.Move)
                return data;
            data.OldItem.CopyFrom(data.Item);
            var pos = FindNewPositionForItem(data.OldPosition, data.Position < 0, data.List, comparer);
            // the old item is the one moving around
            return new ActionData(data.TheAction, data.OldItem, null, pos, data.OldPosition, data.List);
        }

        ActionData FilteredAdd(ActionData data)
        {
            if (data.TheAction != TheAction.Add)
                return data;

            if (data.IsIncluded)
                InternalAddItem(data.Item);
            return data;
        }

        ActionData CalculateIndexes(ActionData data)
        {
            data.Index = GetIndex(data.Item);
            data.IndexPivot = GetLiveListPivot(data.Position, data.List);
            return data;
        }

        ActionData FilteredNone(ActionData data)
        {
            if (data.TheAction != TheAction.None)
                return data;

            // nothing has changed as far as the live list is concerned
            if ((data.IsIncluded && data.Index >= 0) || !data.IsIncluded && data.Index < 0)
                return data;

            // wasn't on the live list, but it is now
            if (data.IsIncluded && data.Index < 0)
                InsertAndRecalculate(data.List, data.Item, data.IndexPivot, data.Position, false);

            // was on the live list, it's not anymore
            else if (!data.IsIncluded && data.Index >= 0)
                RemoveAndRecalculate(data.List, data.Item, data.Index, data.Position, false);

            return data;
        }

        ActionData FilteredInsert(ActionData data)
        {
            if (data.TheAction != TheAction.Insert)
                return data;

            if (data.IsIncluded)
                InsertAndRecalculate(data.List, data.Item, data.IndexPivot, data.Position, false);

            // need to recalculate the filter because inserting an object (even if it's not itself visible)
            // can change visibility of other items after it
            else
                RecalculateFilter(data.List, data.IndexPivot, data.Position, data.List.Count);
            return data;
        }

        /// <summary>
        /// Checks if the object being moved affects the filtered list in any way and update
        /// the list accordingly
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        ActionData FilteredMove(ActionData data)
        {
            if (data.TheAction != TheAction.Move)
                return data;

            int start, end;
            start = end = -1;

            // if there's no filter, the filtered list is equal to the unfiltered list, just move
            if (filter == null)
            {
                start = data.OldPosition < data.Position ? data.OldPosition : data.Position;
                end = data.Position > data.OldPosition ? data.Position : data.OldPosition;
                MoveAndRecalculate(data.List, data.Item, data.Index, data.IndexPivot, start, end, false);
                return data;
            }

            // check if the filtered list is affected indirectly by the move (eg., if the filter involves position of items,
            // moving an item outside the bounds of the filter can affect the items being currently shown/hidden)
            if (Count > 0)
            {
                start = GetIndexUnfiltered(data.List, this[0]);
                end = GetIndexUnfiltered(data.List, this[Count - 1]);
            }

            // true if the filtered list has been indirectly affected by this objects' move
            var filteredListChanged = Count > 0 && (!filter(this[0], start, this) || !filter(this[Count - 1], end, this));

            // the filtered list hasn't been affected, so we just need to reevaluate the filter from
            // the position of the affected item
            if (!filteredListChanged)
            {
                start = data.OldPosition < data.Position ? data.OldPosition : data.Position;
                end = data.Position > data.OldPosition ? data.Position : data.OldPosition;
            }

            // the move caused the object to not be visible in the live list anymore, so remove
            if (!data.IsIncluded && data.Index >= 0)
                RemoveAndRecalculate(data.List, data.Item, data.Index, start, filteredListChanged);

            // the move caused the object to become visible in the live list, insert it
            // and recalculate all the other things on the live list after this one
            else if (data.IsIncluded && data.Index < 0)
                InsertAndRecalculate(data.List, data.Item, data.IndexPivot, start, filteredListChanged);

            // move the object and recalculate the filter between the bounds of the move
            else if (data.IsIncluded)
                MoveAndRecalculate(data.List, data.Item, data.Index, data.IndexPivot, start, end, filteredListChanged);

            // recalculate the filter for every item, there's no way of telling what changed
            else if (filteredListChanged)
                RecalculateFilter(data.List, 0, 0, data.List.Count);

            return data;
        }

        /// <summary>
        /// Compensate time between items by time taken in processing them
        /// so that the average time between an item being processed
        /// is +- the requested processing delay.
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        ActionData UpdateProcessingDelay(TimeInterval<ActionData> data)
        {
            if (requestedDelay == TimeSpan.Zero)
                return data.Value;
            var time = data.Interval;
            if (time > requestedDelay + fuzziness)
                delay -= time - requestedDelay;
            else if (time < requestedDelay + fuzziness)
                delay += requestedDelay - time;
            delay = delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
            return data.Value;
        }

        /// <summary>
        /// Insert an object into the live list at liveListCurrentIndex and recalculate
        /// positions for all objects after that
        /// </summary>
        /// <param name="list">The unfiltered, sorted list of items</param>
        /// <param name="item"></param>
        /// <param name="index"></param>
        /// <param name="position">Index of the unfiltered, sorted list to start reevaluating the filtered list</param>
        void InsertAndRecalculate(IList<T> list, T item, int index, int position, bool rescanAll)
        {
            InternalInsertItem(item, index);
            if (rescanAll)
                index = 0; // reevaluate filter from the start of the filtered list
            else
            {
                index++;
                position++;
            }
            RecalculateFilter(list, index, position, list.Count);
        }

        /// <summary>
        /// Remove an object from the live list at index and recalculate positions
        /// for all objects after that
        /// </summary>
        /// <param name="list">The unfiltered, sorted list of items</param>
        /// <param name="item"></param>
        /// <param name="index">The index in the live list</param>
        /// <param name="position">The position in the sorted, unfiltered list</param>
        void RemoveAndRecalculate(IList<T> list, T item, int index, int position, bool rescanAll)
        {
            InternalRemoveItem(item);
            if (rescanAll)
                index = 0; // reevaluate filter from the start of the filtered list
            RecalculateFilter(list, index, position, list.Count);
        }

        /// <summary>
        /// Move an object in the live list and recalculate positions
        /// for all objects between the bounds of the affected indexes
        /// </summary>
        /// <param name="list">The unfiltered, sorted list of items</param>
        /// <param name="obj"></param>
        /// <param name="from">Index in the live list where the object is</param>
        /// <param name="to">Index in the live list where the object is going to be</param>
        /// <param name="start">Index in the unfiltered, sorted list to start reevaluating the filter</param>
        /// <param name="end">Index in the unfiltered, sorted list to end reevaluating the filter</param>
        void MoveAndRecalculate(IList<T> list, T item, int from, int to, int start, int end, bool rescanAll)
        {
            if (start > end)
                throw new ArgumentOutOfRangeException(nameof(start), "Start cannot be bigger than end, evaluation of the filter goes forward.");

            InternalMoveItem(item, from, to);
            if (rescanAll)
                to = 0; // reevaluate filter from the start of the filtered list
            else
            {
                to++;
                start++;
            }
            RecalculateFilter(list, to, start, end);
        }


        /// <summary>
        /// Go through the list of objects and adjust their "visibility" in the live list
        /// (by removing/inserting as needed). 
        /// </summary>
        /// <param name="index">Index in the live list corresponding to the start index of the object list</param>
        /// <param name="start">Start index of the object list</param>
        /// <param name="end">End index of the object list</param>
        void RecalculateFilter(IList<T> list, int index, int start, int end)
        {
            if (filter == null)
                return;
            for (int i = start; i < end; i++)
            {
                var obj = list[i];
                var idx = GetIndex(obj);
                var isIncluded = filter(obj, i, this);

                // element is still included and hasn't changed positions
                if (isIncluded && idx >= 0)
                    index++;
                // element is included and wasn't before
                else if (isIncluded && idx < 0)
                {
                    if (index == Count)
                        InternalAddItem(obj);
                    else
                        InternalInsertItem(obj, index);
                    index++;
                }
                // element is not included and was before
                else if (!isIncluded && idx >= 0)
                    InternalRemoveItem(obj);
            }
        }

        /// <summary>
        /// Get the index in the live list of an object at position.
        /// This will scan back to the beginning of the live list looking for
        /// the closest left neighbour and return the position after that.
        /// </summary>
        /// <param name="position">The index of an object in the unfiltered, sorted list that we want to map to the filtered live list</param>
        /// <param name="list">The unfiltered, sorted list of items</param>
        /// <returns></returns>
        int GetLiveListPivot(int position, IList<T> list)
        {
            var index = -1;
            if (position > 0)
            {
                for (int i = position - 1; i >= 0; i--)
                {
                    index = GetIndex(list[i]);
                    if (index >= 0)
                    {
                        // found an element to the left of what we want, so now we know the index where to start
                        // manipulating the list
                        index++;
                        break;
                    }
                }
            }

            // there was no element to the left of the one we want, start at the beginning of the live list
            if (index < 0)
                index = 0;
            return index;
        }

        int GetIndex(T item)
        {
            return IndexOf(item);
        }

        int GetIndexUnfiltered(IList<T> list, T item)
        {
            return list.IndexOf(item);
        }

        void RaiseMoveEvent(T item, int from, int to)
        {
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Move, item, to, from));
        }

        void RaiseResetEvent()
        {
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        static int FindNewPositionForItem(int idx, bool lower, IList<T> list, Func<T, T, int> comparer)
        {
            var i = idx;
            if (lower) // replacing element has lower sorting order, find the correct spot towards the beginning
                for (var pos = i - 1; i > 0 && comparer(list[i], list[pos]) < 0; i--, pos--)
                    Swap(list, i, pos);
            else // replacing element has higher sorting order, find the correct spot towards the end
                for (var pos = i + 1; i < list.Count - 1 && comparer(list[i], list[pos]) > 0; i++, pos++)
                    Swap(list, i, pos);
            return i;
        }

        /// <summary>
        /// Swap two elements
        /// </summary>
        /// <param name="list"></param>
        /// <param name="left"></param>
        /// <param name="right"></param>
        static void Swap(IList<T> list, int left, int right)
        {
            var l = list[left];
            list[left] = list[right];
            list[right] = l;
        }

        /// <summary>
        /// Does a binary search for <paramref name="item"/>, and returns
        /// its position. If <paramref name="item"/> is not on the list, returns
        /// where it should be.
        /// </summary>
        /// <param name="list"></param>
        /// <param name="start"></param>
        /// <param name="item"></param>
        /// <param name="comparer"></param>
        /// <returns></returns>
        static int BinarySearch(IList<T> list, int start, T item, Func<T, T, int> comparer)
        {
            int end = start + list.Count - 1;
            while (start <= end)
            {
                var pivot = start + (end - start >> 1);
                var result = comparer(list[pivot], item);
                if (result == 0)
                    return pivot;
                if (result < 0)
                {
                    if (pivot > 0 && comparer(list[pivot + 1], item) >= 0)
                        return pivot;
                    start = pivot + 1;
                }
                else
                {
                    if (pivot < list.Count - 1 && comparer(list[pivot - 1], item) <= 0)
                        return pivot;
                    end = pivot - 1;
                }
            }
            return start;
        }

        static IScheduler GetScheduler(IScheduler scheduler)
        {
            Dispatcher d = null;
            if (scheduler == null)
                d = Dispatcher.FromThread(Thread.CurrentThread);
            return scheduler ?? (d != null ? new DispatcherScheduler(d) : null as IScheduler) ?? CurrentThreadScheduler.Instance;
        }

        bool disposed = false;
        void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (!disposed)
                {
                    disposed = true;
                    queue = null;
                    disposables.Dispose();
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected class ActionData : Tuple<TheAction, T, T, int, int, IList<T>>
        {
            public TheAction TheAction => Item1;
            public T Item => Item2;
            public T OldItem => Item3;
            public int Position => Item4;
            public int OldPosition => Item5;
            public IList<T> List => Item6;

            public int Index { get; set; }
            public int IndexPivot { get; set; }
            public bool IsIncluded { get; set; }

            public ActionData(TheAction action, T item, T oldItem, int position, int oldPosition, IList<T> list)
                : base(action, item, oldItem, position, oldPosition, list)
            {
            }
        }
    }
}