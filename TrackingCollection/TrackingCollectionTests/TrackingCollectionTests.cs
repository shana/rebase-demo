using System;
using System.Collections.Generic;
using System.Linq;
using GitHub.Collections;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Xunit;
using TrackingCollectionTests;
using Xunit.Abstractions;
using System.Text;
using EntryExitDecoratorInterfaces;

public class TestBase : IEntryExitDecorator
{
    protected readonly ITestOutputHelper output;
    protected StringBuilder testOutput = new StringBuilder();
    public TestBase(ITestOutputHelper output)
    {
        this.output = output;
    }

    public virtual void OnEntry()
    {
    }

    public virtual void OnExit()
    {
    }

    protected void Dump(string msg)
    {
        output.WriteLine(msg);
        testOutput.AppendLine(msg);
    }

    protected void Dump(object prefix, Thing thing)
    {
        output.WriteLine(string.Format("{0} - {1}", prefix, thing.ToString()));
        testOutput.AppendLine(string.Format("{0} - {1}", prefix, thing.ToString()));
    }

    protected void Dump(Thing thing)
    {
        output.WriteLine(thing.ToString());
        testOutput.AppendLine(thing.ToString());
    }
    protected void Dump(string title, IList<Thing> col)
    {
        output.WriteLine(title);
        testOutput.AppendLine(title);
        var i = 0;
        foreach (var l in col)
            Dump(i++, l);
    }

    protected void Dump(IList<Thing> col)
    {
        Dump("Dumping", col);
    }
}

public class Tests : TestBase
{
    public Tests(ITestOutputHelper output)
        : base(output)
    {
    }

    [Fact]
    public void OrderByUpdatedNoFilter()
    {
        var count = 6;
        var col = new TrackingCollection<Thing>(
            Observable.Never<Thing>(),
            OrderedComparer<Thing>.OrderBy(x => x.UpdatedAt).Compare);
        col.ProcessingDelay = new TimeSpan(0);

        var now = new DateTimeOffset(0, TimeSpan.FromTicks(0));
        var list1 = new List<Thing>(Enumerable.Range(1, count).Select(i =>
            new Thing() { Number = i, Title = "Run 1", CreatedAt = now + TimeSpan.FromMinutes(i), UpdatedAt = now + TimeSpan.FromMinutes(count - i) })).ToList();
        var list2 = new List<Thing>(Enumerable.Range(1, count).Select(i =>
            new Thing() { Number = i, Title = "Run 2", CreatedAt = now + TimeSpan.FromMinutes(i), UpdatedAt = now + TimeSpan.FromMinutes(i + count) })).ToList();

        var sub = new Subject<Thing>();

        col.Subscribe(t =>
        {
            if (count == list1.Count)
                return;
            sub.OnNext(t);
            count++;
            if (count == list1.Count)
                sub.OnCompleted();
        }, () => { });

        count = 0;
        // add first items
        foreach (var l in list1)
            col.AddItem(l);

        sub.Wait();

        Assert.Equal(list1.Count, col.Count);

        var j = 0;
        Assert.Collection(col, list1.Select(x => new Action<Thing>(t =>
        {
            Assert.Equal(count - j, t.Number);
            Assert.Equal(t.UpdatedAt, col[j].UpdatedAt);
            j++;
        })).ToArray());

        sub = new Subject<Thing>();

        count = 0;
        // replace items
        foreach (var l in list2)
            col.AddItem(l);

        sub.Wait();
        Assert.Equal(list2.Count, col.Count);

        j = 0;
        Assert.Collection(col, list2.Select(x => new Action<Thing>(t =>
        {
            Assert.Equal(j + 1, t.Number);
            Assert.Equal(t.UpdatedAt, col[j].UpdatedAt);
            j++;
        })).ToArray());

        col.Dispose();
    }

    [Fact]
    public void OrderByUpdatedFilter()
    {
        var count = 3;
        var col = new TrackingCollection<Thing>(
            Observable.Never<Thing>(),
            OrderedComparer<Thing>.OrderBy(x => x.UpdatedAt).Compare,
            (item, position, list) => true);
        col.ProcessingDelay = new TimeSpan(0);

        var now = new DateTimeOffset(0, TimeSpan.FromTicks(0));
        var list1 = new List<Thing>(Enumerable.Range(1, count).Select(i =>
            new Thing() { Number = i, Title = "Run 1", CreatedAt = now + TimeSpan.FromMinutes(i), UpdatedAt = now + TimeSpan.FromMinutes(count - i) })).ToList();
        var list2 = new List<Thing>(Enumerable.Range(1, count).Select(i =>
            new Thing() { Number = i, Title = "Run 2", CreatedAt = now + TimeSpan.FromMinutes(i), UpdatedAt = now + TimeSpan.FromMinutes(i + count) })).ToList();

        var sub = new Subject<Thing>();

        col.Subscribe(t =>
        {
            if (count == list1.Count)
                return;
            sub.OnNext(t);
            count++;
            if (count == list1.Count)
                sub.OnCompleted();
        }, () => { });

        count = 0;
        // add first items
        foreach (var l in list1)
            col.AddItem(l);

        sub.Wait();

        Assert.Equal(list1.Count, col.Count);

        var j = 0;
        Assert.Collection(col, list1.Select(x => new Action<Thing>(t =>
        {
            Assert.Equal(count - j, t.Number);
            Assert.Equal(t.UpdatedAt, col[j].UpdatedAt);
            j++;
        })).ToArray());

        sub = new Subject<Thing>();

        count = 0;
        // replace items
        foreach (var l in list2)
            col.AddItem(l);

        sub.Wait();
        Assert.Equal(list2.Count, col.Count);

        j = 0;
        Assert.Collection(col, list2.Select(x => new Action<Thing>(t =>
        {
            Assert.Equal(j + 1, t.Number);
            Assert.Equal(t.UpdatedAt, col[j].UpdatedAt);
            j++;
        })).ToArray());

        col.Dispose();
    }

    [Fact]
    public void OnlyIndexes2To4()
    {
        var count = 6;

        var now = new DateTimeOffset(0, TimeSpan.FromTicks(0));
        var list1 = new List<Thing>(Enumerable.Range(1, count).Select(i =>
            new Thing() { Number = i, Title = "Run 1", CreatedAt = now + TimeSpan.FromMinutes(i), UpdatedAt = now + TimeSpan.FromMinutes(count - i) })).ToList();

        var col = new TrackingCollection<Thing>(
            Observable.Never<Thing>(),
            OrderedComparer<Thing>.OrderBy(x => x.UpdatedAt).Compare,
            (item, position, list) => position >= 2 && position <= 4);
        col.ProcessingDelay = new TimeSpan(0);

        var sub = new Subject<Thing>();

        col.Subscribe(t =>
        {
            if (count == list1.Count)
                return;
            sub.OnNext(t);
            count++;
            if (count == list1.Count)
                sub.OnCompleted();
        }, () => { });

        count = 0;
        // add first items
        foreach (var l in list1)
            col.AddItem(l);

        sub.Wait();
        Assert.Equal(3, col.Count);

        var txtSourceList = @"Source list
0 - id:1 title:Run 1 created:0001-01-01 00:01:00Z updated:0001-01-01 00:05:00Z
1 - id:2 title:Run 1 created:0001-01-01 00:02:00Z updated:0001-01-01 00:04:00Z
2 - id:3 title:Run 1 created:0001-01-01 00:03:00Z updated:0001-01-01 00:03:00Z
3 - id:4 title:Run 1 created:0001-01-01 00:04:00Z updated:0001-01-01 00:02:00Z
4 - id:5 title:Run 1 created:0001-01-01 00:05:00Z updated:0001-01-01 00:01:00Z
5 - id:6 title:Run 1 created:0001-01-01 00:06:00Z updated:0001-01-01 00:00:00Z
";
        var txtInternalList = @"Sorted internal list
0 - id:6 title:Run 1 created:0001-01-01 00:06:00Z updated:0001-01-01 00:00:00Z
1 - id:5 title:Run 1 created:0001-01-01 00:05:00Z updated:0001-01-01 00:01:00Z
2 - id:4 title:Run 1 created:0001-01-01 00:04:00Z updated:0001-01-01 00:02:00Z
3 - id:3 title:Run 1 created:0001-01-01 00:03:00Z updated:0001-01-01 00:03:00Z
4 - id:2 title:Run 1 created:0001-01-01 00:02:00Z updated:0001-01-01 00:04:00Z
5 - id:1 title:Run 1 created:0001-01-01 00:01:00Z updated:0001-01-01 00:05:00Z
";
        var txtFilteredList = @"Filtered list
0 - id:4 title:Run 1 created:0001-01-01 00:04:00Z updated:0001-01-01 00:02:00Z
1 - id:3 title:Run 1 created:0001-01-01 00:03:00Z updated:0001-01-01 00:03:00Z
2 - id:2 title:Run 1 created:0001-01-01 00:02:00Z updated:0001-01-01 00:04:00Z
";
        testOutput.Clear();
        Dump("Source list", list1);
        Assert.Equal(txtSourceList, testOutput.ToString());

        testOutput.Clear();
        Dump("Sorted internal list", col.DebugInternalList);
        Assert.Equal(txtInternalList, testOutput.ToString());

        testOutput.Clear();
        Dump("Filtered list", col);
        Assert.Equal(txtFilteredList, testOutput.ToString());

        Assert.Collection(col, new Action<Thing>[]
        {
            new Action<Thing>(t =>
            {
                Assert.Equal(list1[3], t);
            }),
            new Action<Thing>(t =>
            {
                Assert.Equal(list1[2], t);
            }),
            new Action<Thing>(t =>
            {
                Assert.Equal(list1[1], t);
            })
        });

        col.Dispose();
    }


    [Fact]
    public void OnlyTimesEqualOrHigherThan3Minutes()
    {
        var count = 6;

        var now = new DateTimeOffset(0, TimeSpan.FromTicks(0));
        var list1 = new List<Thing>(Enumerable.Range(1, count).Select(i =>
            new Thing() { Number = i, Title = "Run 1", CreatedAt = now + TimeSpan.FromMinutes(i), UpdatedAt = now + TimeSpan.FromMinutes(count - i) })).ToList();

        var col = new TrackingCollection<Thing>(
            Observable.Never<Thing>(),
            OrderedComparer<Thing>.OrderBy(x => x.UpdatedAt).Compare,
            (item, position, list) => item.UpdatedAt >= now + TimeSpan.FromMinutes(3) && item.UpdatedAt <= now + TimeSpan.FromMinutes(5));
        col.ProcessingDelay = new TimeSpan(0);

        var sub = new Subject<Thing>();

        col.Subscribe(t =>
        {
            if (count == list1.Count)
                return;
            sub.OnNext(t);
            count++;
            if (count == list1.Count)
                sub.OnCompleted();
        }, () => { });

        count = 0;
        // add first items
        foreach (var l in list1)
            col.AddItem(l);

        sub.Wait();
        Assert.Equal(3, col.Count);

        var txtSourceList = @"Source list
0 - id:1 title:Run 1 created:0001-01-01 00:01:00Z updated:0001-01-01 00:05:00Z
1 - id:2 title:Run 1 created:0001-01-01 00:02:00Z updated:0001-01-01 00:04:00Z
2 - id:3 title:Run 1 created:0001-01-01 00:03:00Z updated:0001-01-01 00:03:00Z
3 - id:4 title:Run 1 created:0001-01-01 00:04:00Z updated:0001-01-01 00:02:00Z
4 - id:5 title:Run 1 created:0001-01-01 00:05:00Z updated:0001-01-01 00:01:00Z
5 - id:6 title:Run 1 created:0001-01-01 00:06:00Z updated:0001-01-01 00:00:00Z
";
        var txtInternalList = @"Sorted internal list
0 - id:6 title:Run 1 created:0001-01-01 00:06:00Z updated:0001-01-01 00:00:00Z
1 - id:5 title:Run 1 created:0001-01-01 00:05:00Z updated:0001-01-01 00:01:00Z
2 - id:4 title:Run 1 created:0001-01-01 00:04:00Z updated:0001-01-01 00:02:00Z
3 - id:3 title:Run 1 created:0001-01-01 00:03:00Z updated:0001-01-01 00:03:00Z
4 - id:2 title:Run 1 created:0001-01-01 00:02:00Z updated:0001-01-01 00:04:00Z
5 - id:1 title:Run 1 created:0001-01-01 00:01:00Z updated:0001-01-01 00:05:00Z
";
        var txtFilteredList = @"Filtered list
0 - id:3 title:Run 1 created:0001-01-01 00:03:00Z updated:0001-01-01 00:03:00Z
1 - id:2 title:Run 1 created:0001-01-01 00:02:00Z updated:0001-01-01 00:04:00Z
2 - id:1 title:Run 1 created:0001-01-01 00:01:00Z updated:0001-01-01 00:05:00Z
";
        testOutput.Clear();
        Dump("Source list", list1);
        Assert.Equal(txtSourceList, testOutput.ToString());

        testOutput.Clear();
        Dump("Sorted internal list", col.DebugInternalList);
        Assert.Equal(txtInternalList, testOutput.ToString());

        testOutput.Clear();
        Dump("Filtered list", col);
        Assert.Equal(txtFilteredList, testOutput.ToString());

        Assert.Collection(col, new Action<Thing>[]
        {
            new Action<Thing>(t =>
            {
                Assert.Equal(list1[2], t);
            }),
            new Action<Thing>(t =>
            {
                Assert.Equal(list1[1], t);
            }),
            new Action<Thing>(t =>
            {
                Assert.Equal(list1[0], t);
            })
        });

        col.Dispose();
    }

    [Fact]
    public void OrderByDescendingNoFilter()
    {
        var count = 6;

        var now = new DateTimeOffset(0, TimeSpan.FromTicks(0));
        var list1 = new List<Thing>(Enumerable.Range(1, count).Select(i =>
            new Thing() { Number = i, Title = "Run 1", CreatedAt = now + TimeSpan.FromMinutes(i), UpdatedAt = now + TimeSpan.FromMinutes(count - i) })).ToList();
        var list2 = new List<Thing>(Enumerable.Range(1, count).Select(i =>
            new Thing() { Number = i, Title = "Run 2", CreatedAt = now + TimeSpan.FromMinutes(i), UpdatedAt = now + TimeSpan.FromMinutes(i) })).ToList();

        var col = new TrackingCollection<Thing>(
            Observable.Never<Thing>(),
            OrderedComparer<Thing>.OrderByDescending(x => x.UpdatedAt).Compare);
        col.ProcessingDelay = new TimeSpan(0);

        var sub = new Subject<Thing>();

        col.Subscribe(t =>
        {
            if (count == list1.Count)
                return;
            sub.OnNext(t);
            count++;
            if (count == list1.Count)
                sub.OnCompleted();
        }, () => { });

        count = 0;
        // add first items
        foreach (var l in list1)
            col.AddItem(l);

        sub.Wait();
        Assert.Equal(6, col.Count);

        var txtSourceList = @"Source list
0 - id:1 title:Run 1 created:0001-01-01 00:01:00Z updated:0001-01-01 00:05:00Z
1 - id:2 title:Run 1 created:0001-01-01 00:02:00Z updated:0001-01-01 00:04:00Z
2 - id:3 title:Run 1 created:0001-01-01 00:03:00Z updated:0001-01-01 00:03:00Z
3 - id:4 title:Run 1 created:0001-01-01 00:04:00Z updated:0001-01-01 00:02:00Z
4 - id:5 title:Run 1 created:0001-01-01 00:05:00Z updated:0001-01-01 00:01:00Z
5 - id:6 title:Run 1 created:0001-01-01 00:06:00Z updated:0001-01-01 00:00:00Z
";
        var txtInternalList = @"Sorted internal list
0 - id:1 title:Run 1 created:0001-01-01 00:01:00Z updated:0001-01-01 00:05:00Z
1 - id:2 title:Run 1 created:0001-01-01 00:02:00Z updated:0001-01-01 00:04:00Z
2 - id:3 title:Run 1 created:0001-01-01 00:03:00Z updated:0001-01-01 00:03:00Z
3 - id:4 title:Run 1 created:0001-01-01 00:04:00Z updated:0001-01-01 00:02:00Z
4 - id:5 title:Run 1 created:0001-01-01 00:05:00Z updated:0001-01-01 00:01:00Z
5 - id:6 title:Run 1 created:0001-01-01 00:06:00Z updated:0001-01-01 00:00:00Z
";
        var txtFilteredList = @"Filtered list
0 - id:1 title:Run 1 created:0001-01-01 00:01:00Z updated:0001-01-01 00:05:00Z
1 - id:2 title:Run 1 created:0001-01-01 00:02:00Z updated:0001-01-01 00:04:00Z
2 - id:3 title:Run 1 created:0001-01-01 00:03:00Z updated:0001-01-01 00:03:00Z
3 - id:4 title:Run 1 created:0001-01-01 00:04:00Z updated:0001-01-01 00:02:00Z
4 - id:5 title:Run 1 created:0001-01-01 00:05:00Z updated:0001-01-01 00:01:00Z
5 - id:6 title:Run 1 created:0001-01-01 00:06:00Z updated:0001-01-01 00:00:00Z
";
        testOutput.Clear();
        Dump("Source list", list1);
        Assert.Equal(txtSourceList, testOutput.ToString());

        testOutput.Clear();
        Dump("Sorted internal list", col.DebugInternalList);
        Assert.Equal(txtInternalList, testOutput.ToString());

        testOutput.Clear();
        Dump("Filtered list", col);
        Assert.Equal(txtFilteredList, testOutput.ToString());

        var k = 0;
        Assert.Collection(col, list1.Select(x => new Action<Thing>(t => {
                Assert.Equal(list1[k++], t);
            })).ToArray());

        sub = new Subject<Thing>();
        count = 0;
        // add first items
        foreach (var l in list2)
            col.AddItem(l);

        sub.Wait();
        Assert.Equal(6, col.Count);

        col.Dispose();
    }

    [Fact]
    public void OrderByDescendingNoFilter1000()
    {
        var count = 1000;
        var total = 1000;

        var now = new DateTimeOffset(0, TimeSpan.FromTicks(0));
        var list1 = new List<Thing>(Enumerable.Range(1, count).Select(i =>
            new Thing() { Number = i, Title = "Run 1", CreatedAt = now + TimeSpan.FromMinutes(i), UpdatedAt = now + TimeSpan.FromMinutes(count - i) })).ToList();
        var list2 = new List<Thing>(Enumerable.Range(1, count).Select(i =>
            new Thing() { Number = i, Title = "Run 2", CreatedAt = now + TimeSpan.FromMinutes(i), UpdatedAt = now + TimeSpan.FromMinutes(i) })).ToList();

        var col = new TrackingCollection<Thing>(
            Observable.Never<Thing>(),
            OrderedComparer<Thing>.OrderByDescending(x => x.UpdatedAt).Compare);
        col.ProcessingDelay = new TimeSpan(0);

        var sub = new Subject<Thing>();

        col.Subscribe(t =>
        {
            if (count == list1.Count)
                return;
            sub.OnNext(t);
            count++;
            if (count == list1.Count)
                sub.OnCompleted();
        }, () => { });

        count = 0;
        // add first items
        foreach (var l in list1)
            col.AddItem(l);

        sub.Wait();
        Assert.Equal(total, col.Count);

        var k = 0;
        Assert.Collection(col, list1.Select(x => new Action<Thing>(t => {
            Assert.Equal(list1[k++], t);
        })).ToArray());

        sub = new Subject<Thing>();
        count = 0;
        // add first items
        foreach (var l in list2)
            col.AddItem(l);

        sub.Wait();
        Assert.Equal(total, col.Count);

        col.Dispose();
    }


    [Fact]
    public void ProcessingDelayPingsRegularly()
    {
        int count, total;
        count = total = 1000;

        var now = new DateTimeOffset(0, TimeSpan.FromTicks(0));
        var list1 = new List<Thing>(Enumerable.Range(1, count).Select(i =>
            new Thing() { Number = i, Title = "Run 1", CreatedAt = now + TimeSpan.FromMinutes(i), UpdatedAt = now + TimeSpan.FromMinutes(count - i) })).ToList();

        var col = new TrackingCollection<Thing>(
            list1.ToObservable().Delay(TimeSpan.FromMilliseconds(10)),
            OrderedComparer<Thing>.OrderByDescending(x => x.UpdatedAt).Compare);
        col.ProcessingDelay = TimeSpan.FromMilliseconds(10);

        var sub = new Subject<Thing>();

        var times = new List<DateTimeOffset>();
        sub.Subscribe(t =>
        {
            times.Add(DateTimeOffset.UtcNow);
        });

        count = 0;

        col.Subscribe(t =>
        {
            if (count == list1.Count)
                return;
            sub.OnNext(t);
            count++;
            if (count == list1.Count)
                sub.OnCompleted();
        }, () => { });


        sub.Wait();
        Assert.Equal(total, col.Count);

        var k = 0;
        Assert.Collection(col, list1.Select(x => new Action<Thing>(t => {
            Assert.Equal(list1[k++], t);
        })).ToArray());


        long totalTime = 0;
        
        for (var j = 1; j < times.Count; j++)
            totalTime += (times[j] - times[j - 1]).Ticks;
        var avg = TimeSpan.FromTicks(totalTime / times.Count).TotalMilliseconds;
        Assert.InRange(avg, 10, 11);
        col.Dispose();
    }

    [Fact]
    public void NotInitializedCorrectlyThrows1()
    {
        var col = new TrackingCollection<Thing>(OrderedComparer<Thing>.OrderByDescending(x => x.UpdatedAt).Compare);
        Assert.Throws<InvalidOperationException>(() => col.Subscribe());
    }

    [Fact]
    public void NotInitializedCorrectlyThrows2()
    {
        var col = new TrackingCollection<Thing>(OrderedComparer<Thing>.OrderByDescending(x => x.UpdatedAt).Compare);
        Assert.Throws<InvalidOperationException>(() => col.Subscribe(_ => { }, () => { }));
    }

    [Fact]
    public void NoChangingAfterDisposed1()
    {
        var col = new TrackingCollection<Thing>(Observable.Never<Thing>(), OrderedComparer<Thing>.OrderByDescending(x => x.UpdatedAt).Compare);
        col.Dispose();
        Assert.Throws<ObjectDisposedException>(() => col.AddItem(new Thing()));
    }

    [Fact]
    public void NoChangingAfterDisposed2()
    {
        var col = new TrackingCollection<Thing>(Observable.Never<Thing>(), OrderedComparer<Thing>.OrderByDescending(x => x.UpdatedAt).Compare);
        col.Dispose();
        Assert.Throws<ObjectDisposedException>(() => col.RemoveItem(new Thing()));
    }


    [Fact]
    public void FilterTitleRun2()
    {
        var count = 0;
        var total = 1000;

        var now = new DateTimeOffset(0, TimeSpan.FromTicks(0));
        var list1 = new List<Thing>(Enumerable.Range(1, total).Select(i =>
            new Thing() { Number = i, Title = "Run 1", CreatedAt = now + TimeSpan.FromMinutes(i), UpdatedAt = now + TimeSpan.FromMinutes(i) })).ToList();
        var list2 = new List<Thing>(Enumerable.Range(1, total).Select(i =>
            new Thing() { Number = i, Title = "Run 2", CreatedAt = now + TimeSpan.FromMinutes(i), UpdatedAt = now + TimeSpan.FromMinutes(i) })).ToList();

        var col = new TrackingCollection<Thing>(
            list1.ToObservable(),
            OrderedComparer<Thing>.OrderByDescending(x => x.UpdatedAt).Compare,
            (item, position, list) => item.Title.Equals("Run 2"));
        col.ProcessingDelay = new TimeSpan(0);

        var sub = new Subject<Thing>();

        count = 0;
        col.Subscribe(t =>
        {
            if (count == list1.Count)
                return;
            sub.OnNext(t);
            count++;
            if (count == list1.Count)
                sub.OnCompleted();
        }, () => { });

        sub.Wait();

        Assert.Equal(total, count);
        Assert.Equal(0, col.Count);

        sub = new Subject<Thing>();
        count = 0;

        // add new items
        foreach (var l in list2)
            col.AddItem(l);

        sub.Wait();

        Assert.Equal(total, count);
        Assert.Equal(total, col.Count);

        Assert.Collection(col, list2.Reverse<Thing>().Select(x => new Action<Thing>(t => {
            Assert.Equal(x, t);
        })).ToArray());

        col.Dispose();
    }

    [Fact]
    public void OrderByDoesntMatchOriginalOrderTimings()
    {
        var count = 0;
        var total = 1000;

        var now = new DateTimeOffset(0, TimeSpan.FromTicks(0));
        var list1 = new List<Thing>(Enumerable.Range(1, total).Select(i =>
            new Thing() { Number = i, Title = "Run 1", CreatedAt = now + TimeSpan.FromMinutes(i), UpdatedAt = now + TimeSpan.FromMinutes(i) })).ToList();
        var list2 = new List<Thing>(Enumerable.Range(1, total).Select(i =>
            new Thing() { Number = i, Title = "Run 2", CreatedAt = now + TimeSpan.FromMinutes(i), UpdatedAt = now + TimeSpan.FromMinutes(i) })).ToList();

        var col = new TrackingCollection<Thing>(
            list1.ToObservable(),
            OrderedComparer<Thing>.OrderByDescending(x => x.UpdatedAt).Compare,
            (item, position, list) => item.Title.Equals("Run 2"));
        col.ProcessingDelay = new TimeSpan(0);

        var sub = new Subject<Thing>();

        count = 0;
        col.Subscribe(t =>
        {
            if (count == list1.Count)
                return;
            sub.OnNext(t);
            count++;
            if (count == list1.Count)
                sub.OnCompleted();
        }, () => { });

        sub.Wait();

        Assert.Equal(total, count);
        Assert.Equal(0, col.Count);

        sub = new Subject<Thing>();
        count = 0;

        // add new items
        foreach (var l in list2)
            col.AddItem(l);

        sub.Wait();

        Assert.Equal(total, count);
        Assert.Equal(total, col.Count);

        Assert.Collection(col, list2.Reverse<Thing>().Select(x => new Action<Thing>(t => {
            Assert.Equal(x, t);
        })).ToArray());

        col.Dispose();
    }

    [Fact]
    public void OrderByMatchesOriginalOrderTimings()
    {
        var count = 0;
        var total = 1000;

        var now = new DateTimeOffset(0, TimeSpan.FromTicks(0));
        var list1 = new List<Thing>(Enumerable.Range(1, total).Select(i =>
            new Thing() { Number = i, Title = "Run 1", CreatedAt = now + TimeSpan.FromMinutes(i), UpdatedAt = now + TimeSpan.FromMinutes(total - i) })).ToList();
        var list2 = new List<Thing>(Enumerable.Range(1, total).Select(i =>
            new Thing() { Number = i, Title = "Run 2", CreatedAt = now + TimeSpan.FromMinutes(i), UpdatedAt = now + TimeSpan.FromMinutes(total - i) })).ToList();

        var col = new TrackingCollection<Thing>(
            list1.ToObservable(),
            OrderedComparer<Thing>.OrderByDescending(x => x.UpdatedAt).Compare,
            (item, position, list) => item.Title.Equals("Run 2"));
        col.ProcessingDelay = new TimeSpan(0);

        var sub = new Subject<Thing>();

        count = 0;
        col.Subscribe(t =>
        {
            if (count == list1.Count)
                return;
            sub.OnNext(t);
            count++;
            if (count == list1.Count)
                sub.OnCompleted();
        }, () => { });

        sub.Wait();

        Assert.Equal(total, count);
        Assert.Equal(0, col.Count);

        sub = new Subject<Thing>();
        count = 0;

        // add new items
        foreach (var l in list2)
            col.AddItem(l);

        sub.Wait();

        Assert.Equal(total, count);
        Assert.Equal(total, col.Count);

        Assert.Collection(col, list2.Select(x => new Action<Thing>(t => {
            Assert.Equal(x, t);
        })).ToArray());

        col.Dispose();
    }
}
