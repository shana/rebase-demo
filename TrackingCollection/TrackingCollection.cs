#define DISABLE_REACTIVEUI
#define DISABLE_THREADED_SORT

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using GitHub.Helpers;
#if !DISABLE_REACTIVEUI
using ReactiveUI;
#endif
using static GitHub.EnumerableExtensions;

namespace GitHub.Collections
{
    /// <summary>
    /// TrackingCollection gets items from an observable sequence and updates its contents
    /// in such a way that two updates to the same object (as defined by an Equals call)
    /// will result in one object on the list being updated (as opposed to having two different
    /// instances of the object added to the list).
    /// This allows us to grab a bunch of objects from cache, load a collection with them, and
    /// then request updated versions of the objects from the server, which when returned will
    /// update the collection in place(and the UI with it), seamlessly.
    /// The collection supports ordering and filtering, and filtering includes enough information
    /// to allow limiting the number of items to return (and display). It can be resorted and
    /// refiltered in place.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class TrackingCollection<T> : ObservableCollection<T>, ITrackingCollection<T>
        where T : class, ICopyable<T>
    {
        enum TheAction
        {
            None,
            Move,
            Add,
            Insert
        }

        CompositeDisposable disposables = new CompositeDisposable();
        IObservable<T> source;
        Func<T, T, int> comparer;
        Func<T, int, IList<T>, bool> filter;
        IScheduler scheduler;
        List<T> original;

        /// <summary>
        /// If you use this constructor, you need to call Listen() with the observable to
        /// track. That method will allow you to hook into the observable for further
        /// processing.
        /// </summary>
        /// <param name="comparer"></param>
        /// <param name="filter"></param>
        /// <param name="scheduler"></param>
        public TrackingCollection(Func<T, T, int> comparer = null, Func<T, int, IList<T>, bool> filter = null, IScheduler scheduler = null)
        {

#if DISABLE_THREADED_SORT
            scheduler = null;
#endif

#if DISABLE_REACTIVEUI
            this.scheduler = scheduler ?? DispatcherScheduler.Current;
#else
            this.scheduler = scheduler ?? RxApp.MainThreadScheduler;
#endif
            this.comparer = comparer;
            this.filter = filter;
            if (filter != null)
                original = new List<T>();
        }

        public TrackingCollection(IObservable<T> source, Func<T, T, int> comparer = null,
            Func<T, int, IList<T>, bool> filter = null, IScheduler scheduler = null)
            : this(comparer, filter, scheduler)
        {
            Listen(source);
        }

        /// <summary>
        /// Use this if you want to do further processing with the original observable
        /// </summary>
        /// <param name="obs"></param>
        /// <returns></returns>
        public IObservable<T> Listen(IObservable<T> obs)
        {
            source = Observable.Defer(() => obs
                // if there's a filter, sorting will happen on a backup list and doesn't
                // need to run on the main thread, since it won't trigger any collection
                // change events
#if DISABLE_THREADED_SORT
                .ObserveOn(scheduler)
#else
                .ObserveOn(filter != null ? TaskPoolScheduler.Default : scheduler)
#endif
                .Select(t => InsertionStep(t))
                .ObserveOn(scheduler)
                .Select(x => FilterStep(x))
                )
                .Publish()
                .RefCount();
            return source;
        }

        /// <summary>
        /// Set a new comparer for the existing data. This will cause the
        /// collection to be resorted and refiltered.
        /// </summary>
        /// <param name="theComparer">The comparer method for sorting, or null if not sorting</param>
        public void SetComparer(Func<T, T, int> theComparer)
        {
            SetAndRecalculateSort(theComparer);
            SetAndRecalculateFilter(filter);
        }

        /// <summary>
        /// Set a new filter. This will cause the collection to be filtered
        /// </summary>
        /// <param name="theFilter">The new filter, or null to not have any filtering</param>
        public void SetFilter(Func<T, int, IList<T>, bool> theFilter)
        {
            SetAndRecalculateFilter(theFilter);
        }

        public IDisposable Subscribe()
        {
            disposables.Add(source.Subscribe());
            return this;
        }

        public IDisposable Subscribe(Action onCompleted)
        {
            disposables.Add(source.Subscribe(_ => { }, onCompleted));
            return this;
        }

        void SetAndRecalculateSort(Func<T, T, int> compare)
        {
            comparer = compare;
            var list = filter != null ? original : Items as List<T>;
            RecalculateSort(list, 0, list.Count);
        }

        void RecalculateSort(List<T> list, int start, int end)
        {
            if (comparer == null)
                return;

            list.Sort(start, end, new LambdaComparer<T>(comparer));

            // if there's a filter, then it's going to trigger events and we don't need to manually trigger them
            if (filter == null)
            {
                OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
                OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            }
        }

        void SetAndRecalculateFilter(Func<T, int, IList<T>, bool> newFilter)
        {
            if (filter == null && newFilter == null)
                return; // nothing to do

            // no more filter, add all the hidden items back
            if (filter != null && newFilter == null)
            {
                for (int i = 0; i < original.Count; i++)
                {
                    if (GetIndexInLiveList(i) < 0)
                        InsertItem(i, original[i]);
                }
                original = null;
                filter = null;
#if !DISABLE_BACKING_DICT
                originalToLiveListMap.Clear();
#endif
                return;
            }

            // there was no filter before, so the Items collection has everything, grab it
            if (filter == null)
                original = new List<T>(Items);
            else
            {
                Items.Clear();
#if !DISABLE_BACKING_DICT
                originalToLiveListMap.Clear();
#endif
            }
            filter = newFilter;

            RecalculateFilter(original, 0, 0, original.Count);
        }

        /// <summary>
        /// Calculates what to do with the element (whether to insert, add, move or nothing).
        /// If it's a move, it will resort as it's calculating the new position, so when it's
        /// done calculating everything will be in place already. If it's an add or insert, it
        /// will return the action to take (but will not actually apply it in the list, this is
        /// done on the next step)
        /// </summary>
        /// <param name="t"></param>
        /// <returns></returns>
        ActionData InsertionStep(T t)
        {
            var list = filter == null ? this : original as IList<T>;
            ActionData ret;

            var idx = list.IndexOf(t);
            if (idx >= 0)
            {
                var old = list[idx];
                var comparison = 0;
                if (comparer != null)
                    comparison = comparer(t, old);
                list[idx].CopyFrom(t);

                // no sorting to be done, just replacing the element in-place
                if (comparer == null || comparison == 0)
                    ret = new ActionData(TheAction.None, idx, idx, t, filter != null ? original : null);
                else
                {
                    // element has moved, locate new position
                    ret = new ActionData(TheAction.Move,
                        FindNewPositionForItem(idx, comparison < 0, list, comparer),
                        idx, old,
                        filter != null ? original : null);

                    // if there's no filter, we raise collection change events here
                    // (list.Add and list.Insert below will also raise collection change events)
                    if (filter == null)
                        RaiseMoveEvent(ret.Item, ret.OldPosition, ret.Position);
                }
            }
            // the element doesn't exist yet
            // figure out whether we're larger than the last element or smaller than the first or
            // if we have to place the new item somewhere in the middle
            else if (comparer != null && list.Count > 0)
            {
                if (comparer(list[0], t) >= 0)
                {
                    ret = new ActionData(TheAction.Insert, 0, -1, t, filter != null ? original : null);
                    list.Insert(0, t);
                }
                else if (comparer(list[list.Count - 1], t) <= 0)
                {
                    ret = new ActionData(TheAction.Add, list.Count, -1, t, filter != null ? original : null);
                    list.Add(t);
                }
                // this happens if the original observable is not sorted, or it's sorting order doesn't
                // match the comparer that has been set
                else
                {
                    idx = BinarySearch(list, 0, t, comparer);
                    if (idx < 0 && ~idx == 0)
                        idx = ~idx;
                    if (idx < 0)
                    {
                        ret = new ActionData(TheAction.Add, list.Count, -1, t, filter != null ? original : null);
                        list.Add(t);
                    }
                    else
                    {
                        ret = new ActionData(TheAction.Insert, 0, -1, t, filter != null ? original : null);
                        list.Insert(0, t);
                    }
                }
            }
            else
            {
                ret = new ActionData(TheAction.Add, list.Count, -1, t, filter != null ? original : null);
                list.Add(t);
            }
            return ret;
        }

        void RaiseMoveEvent(T item, int from, int to)
        {
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Move, item, to, from));
        }

        T FilterStep(ActionData data)
        {
#if DISABLE_THREADED_SORT
            var list = original;
#else
            var list = data.Data;
#endif

            if (filter == null)
                return data.Item;

            var isIncluded = filter(data.Item, data.Position, this);

            // adding is simple, just check if it should be visible and stuff it at the end
            if (FilterAdd(data.TheAction, data.Item, isIncluded, data.Position))
                return data.Item;

            // for all cases except add, we need to know where the touched
            // element is going to be located in the live list, so grab that
            var liveListCurrentIndex = GetLiveListPivot(data.Position, list);

            //if the element was changed but didn't move, then sorting is correct but we 
            //need to check the filter anyway to determine whether to hide or show the element.
            //if that changes, then other elements after it might be affected too, so we need to check
            //all elements from the live list from the element that was changed to the end
            if (FilterNone(data.TheAction, list, data.Item, isIncluded, liveListCurrentIndex, data.Position))
                return data.Item;

            if (FilterInsert(data.TheAction, list, data.Item, isIncluded, liveListCurrentIndex, data.Position))
                return data.Item;

            FilterMove(data.TheAction, list, data.Item, isIncluded, liveListCurrentIndex, data.Position, data.OldPosition);

            return data.Item;
        }

        /// <summary>
        /// adding is simple, just check if it should be visible and stuff it at the end
        /// </summary>
        /// <param name="action"></param>
        /// <param name="obj"></param>
        /// <param name="isIncluded"></param>
        /// <param name="position"></param>
        /// <returns>Whether the filter was applied</returns>
        bool FilterAdd(TheAction action, T obj, bool isIncluded, int position)
        {
            if (action != TheAction.Add)
                return false;

            if (isIncluded)
                AddIntoLiveList(obj, position);
            return true;
        }

        /// <summary>
        /// if the element was changed but didn't move, then sorting is correct but we 
        /// need to check the filter anyway to determine whether to hide or show the element.
        /// if that changes, then other elements after it might be affected too, so we need to check
        /// all elements from the live list from the element that was changed to the end
        /// </summary>
        /// <param name="action"></param>
        /// <param name="list">The unfiltered, sorted list of items</param>
        /// <param name="obj"></param>
        /// <param name="isIncluded"></param>
        /// <param name="liveListCurrentIndex"></param>
        /// <param name="position"></param>
        /// <returns>Whether the filter was applied</returns>
        bool FilterNone(TheAction action, IList<T> list, T obj, bool isIncluded, int liveListCurrentIndex, int position)
        {
            if (action != TheAction.None)
                return false;

            var idx = GetIndexInLiveList(position);

            // nothing has changed as far as the live list is concerned
            if ((isIncluded && idx > 0) || !isIncluded && idx < 0)
                return true;

            // wasn't on the live list, but it is now
            if (isIncluded && idx < 0)
                InsertAndRecalculate(list, obj, liveListCurrentIndex, position);

            // was on the live list but it isn't anymore
            else if (!isIncluded && idx >= 0)
                RemoveAndRecalculate(list, obj, idx, position);

            return true;
        }

        /// <summary>
        /// If the action is an Insert action, apply the filter.
        /// Insert needs to check if the item is visible according to the filter, and if it is
        /// recalculate all the items after it
        /// </summary>
        /// <param name="action"></param>
        /// <param name="list">The unfiltered, sorted list of items</param>
        /// <param name="obj"></param>
        /// <param name="isIncluded"></param>
        /// <param name="liveListCurrentIndex"></param>
        /// <param name="position"></param>
        /// <returns>Whether the filter was applied</returns>
        bool FilterInsert(TheAction action, IList<T> list, T obj, bool isIncluded, int liveListCurrentIndex, int position)
        {
            if (action != TheAction.Insert)
                return false;

            if (isIncluded)
                InsertAndRecalculate(list, obj, liveListCurrentIndex, position);
            return true;
        }

        /// <summary>
        /// Move needs to check if the new position for the item makes its visibility different than
        /// the old position.If yes, then everything needs to be recalculated after the affected visible
        /// positions(either the old, the new, or both)
        /// </summary>
        /// <param name="action"></param>
        /// <param name="list">The unfiltered, sorted list of items</param>
        /// <param name="obj"></param>
        /// <param name="isIncluded"></param>
        /// <param name="liveListCurrentIndex"></param>
        /// <param name="position"></param>
        /// <param name="oldPosition"></param>
        /// <returns>Whether the filter was applied</returns>
        bool FilterMove(TheAction action, IList<T> list, T obj, bool isIncluded,
            int liveListCurrentIndex, int position, int oldPosition)
        {
            if (action != TheAction.Move)
                return false;

            var idx = GetIndexInLiveList(position);
            var startPosition = GetIndexInOriginalList(0);
            var liveListChanged = (Count > 0 && !filter(this[0], startPosition, this));

            // it wasn't included before in the live list, and still isn't and the live list hasn't been affected
            // nothing to do
            if ((!isIncluded && idx < 0) || (isIncluded && idx == liveListCurrentIndex) || !liveListChanged)
                return true;

            // the move caused the object to not be visible in the live list anymore, so remove
            if (!isIncluded)
                RemoveAndRecalculate(list, obj, idx, oldPosition);

            // the move caused the object to become visible in the live list, insert it
            // and recalculate all the other things on the live list after this one
            else if (idx < 0)
                InsertAndRecalculate(list, obj, liveListCurrentIndex, position);

            // the move cause the live list to change for other reasons
            else if (liveListChanged)
                RecalculateFilter(list, 0, startPosition, list.Count);

            // move the object
            else
                MoveAndRecalculate(list, obj, idx, liveListCurrentIndex, oldPosition, position);
            return true;
        }

        /// <summary>
        /// Move an object in the live list and recalculate positions
        /// for all objects between the bounds of the affected indexes
        /// </summary>
        /// <param name="list">The unfiltered, sorted list of items</param>
        /// <param name="obj"></param>
        /// <param name="from">Index in the live list where the object is</param>
        /// <param name="to">Index in the live list where the object is going to be</param>
        /// <param name="fromPosition">Index in the unfiltered, sorted list corresponding to "from"</param>
        /// <param name="toPosition">Index in the unfiltered, sorted list corresponding to "to"</param>
        void MoveAndRecalculate(IList<T> list, T obj, int from, int to, int fromPosition, int toPosition)
        {
            MoveInLiveList(from, to, fromPosition, toPosition);
            RecalculateFilter(list, to, toPosition < fromPosition ? toPosition : fromPosition,
                toPosition < fromPosition ? fromPosition : toPosition);
        }

        /// <summary>
        /// Remove an object from the live list at index and recalculate positions
        /// for all objects after that
        /// </summary>
        /// <param name="list">The unfiltered, sorted list of items</param>
        /// <param name="item"></param>
        /// <param name="index">The index in the live list</param>
        /// <param name="position">The position in the sorted, unfiltered list</param>
        void RemoveAndRecalculate(IList<T> list, T item, int index, int position)
        {
            RemoveFromLiveList(index, position);
            RecalculateFilter(list, index, position, list.Count);
        }

        /// <summary>
        /// Insert an object into the live list at liveListCurrentIndex and recalculate
        /// positions for all objects after that
        /// </summary>
        /// <param name="list">The unfiltered, sorted list of items</param>
        /// <param name="item"></param>
        /// <param name="index"></param>
        /// <param name="position">Index of the unfiltered, sorted list</param>
        void InsertAndRecalculate(IList<T> list, T item, int index, int position)
        {
            InsertIntoLiveList(item, index, position);
            position++;
            index++;
            RecalculateFilter(list, index, position, list.Count);
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
            var liveListCurrentIndex = -1;
            if (position > 0)
            {
                for (int i = position - 1; i >= 0; i--)
                {
                    liveListCurrentIndex = GetIndexInLiveList(i);
                    if (liveListCurrentIndex >= 0)
                    {
                        // found an element to the left of what we want, so now we know the index where to start
                        // manipulating the list
                        liveListCurrentIndex++;
                        break;
                    }
                }
            }

            // there was no element to the left of the one we want, start at the beginning of the live list
            if (liveListCurrentIndex < 0)
                liveListCurrentIndex = 0;
            return liveListCurrentIndex;
        }

        /// <summary>
        /// Go through the list of objects and adjust their "visibility" in the live list
        /// (by removing/inserting as needed). 
        /// </summary>
        /// <param name="liveListCurrentIndex">Index in the live list corresponding to the start index of the object list</param>
        /// <param name="start">Start index of the object list</param>
        /// <param name="end">End index of the object list</param>
        void RecalculateFilter(IList<T> list, int liveListCurrentIndex, int start, int end)
        {
            for (int i = start; i < end; i++)
            {
                var obj = list[i];
                var idx = GetIndexInLiveList(i);
                var isIncluded = filter(obj, i, this);

                // element is still included and hasn't changed positions
                if (isIncluded && idx >= 0)
                    liveListCurrentIndex++;
                // element is included and wasn't before
                else if (isIncluded && idx < 0)
                {
                    if (liveListCurrentIndex == Count)
                        AddIntoLiveList(obj, i);
                    else
                        InsertIntoLiveList(obj, liveListCurrentIndex, i);
                    liveListCurrentIndex++;
                }
                // element is not included and was before
                else if (!isIncluded && idx >= 0)
                    RemoveFromLiveList(idx, i);
            }
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
        /// <param name="item">The index of the <paramref name="item"/> or bitwise complement of
        ///     0 or <paramref name="list"/> size if not found and it's smaller or larger than the
        /// first or last element, accordingly.</param>
        /// <param name="comparer1"></param>
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
                    if (pivot > 0 && comparer(list[pivot+1], item) >= 0)
                        return pivot;
                    start = pivot + 1;
                }
                else
                {
                    if (pivot < list.Count - 1 && comparer(list[pivot-1], item) <= 0)
                        return pivot;
                    end = pivot - 1;
                }
            }
            return ~start;
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

#if !DISABLE_BACKING_DICT
        readonly Dictionary<int, int> originalToLiveListMap = new Dictionary<int, int>();
        readonly List<int> liveListToOriginalMap = new List<int>();
#endif

        int GetIndexInLiveList(int position)
        {
            if (filter == null)
                return position;

#if !DISABLE_BACKING_DICT
            int ret;
            if (originalToLiveListMap.TryGetValue(position, out ret))
                return ret;
            return -1;
#else
            return IndexOf(item);
#endif
        }

        int GetIndexInOriginalList(int index)
        {
            if (filter == null)
                return index;

#if !DISABLE_BACKING_DICT
            return liveListToOriginalMap[index];
#else
            return original.IndexOf(this[index]);
#endif
        }

        void AddIntoLiveList(T item, int position)
        {
#if !DISABLE_BACKING_DICT
            originalToLiveListMap.Add(position, Count);
            liveListToOriginalMap.Add(position);
#endif
            Add(item);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="item"></param>
        /// <param name="index">Index in the live list</param>
        /// <param name="position">Position in the sorted, unfiltered list</param>
        void InsertIntoLiveList(T item, int index, int position)
        {
            InsertItem(index, item);
#if !DISABLE_BACKING_DICT
            originalToLiveListMap.Add(position, index);
            liveListToOriginalMap.Insert(index, position);

            var count = Count;
            for (int i = index + 1; i < count; i++)
                originalToLiveListMap[i] = i;
#endif
        }

        void MoveInLiveList(int from, int to, int fromPosition, int toPosition)
        {
            MoveItem(from, from < to ? to - 1 : to);
#if !DISABLE_BACKING_DICT
            originalToLiveListMap.Remove(fromPosition);
            liveListToOriginalMap.Remove(from);
            liveListToOriginalMap.Insert(from < to ? to - 1 : to, toPosition);

            var count = to < from ? Count : to;
            for (int i = from < to ? from : to; i < count; i++)
                originalToLiveListMap[i] = i;
#endif
        }

        void RemoveFromLiveList(int index, int position)
        {
            RemoveItem(index);
#if !DISABLE_BACKING_DICT
            originalToLiveListMap.Remove(position);
            liveListToOriginalMap.Remove(index);
            var count = Count;
            for (int i = index; i < count; i++)
                originalToLiveListMap[i] = i;
#endif
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        bool disposed = false;
        void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (!disposed)
                {
                    disposables.Dispose();
                    disposed = true;
                }
            }
        }
        class ActionData
        {
            readonly public TheAction TheAction;
            readonly public T Item;
            readonly public int Position;
            readonly public int OldPosition;
            readonly public IList<T> Data;

            public ActionData(TheAction action, int position, int oldPosition, T item, IList<T> data)
            {
                TheAction = action;
                Position = position;
                OldPosition = oldPosition;
                Item = item;
#if !DISABLE_THREADED_SORT
                Data = new List<T>(data);
                //Data = data;
#endif
            }
        }

    }
}
