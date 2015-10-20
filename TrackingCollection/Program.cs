using GitHub.Collections;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;

namespace ObservableTests
{

    public class Program
    {
        static IEnumerable<IObservable<long>> GetSequences()
        {
            Console.WriteLine("GetSequences() called");
            Console.WriteLine("Yield 1st sequence");
            yield return Observable.Create<long>(o =>
            {
                Console.WriteLine("1st subscribed to");
                return Observable.Timer(TimeSpan.FromMilliseconds(500))
                .Select(i => 1L)
                .Subscribe(o);
            });
            Console.WriteLine("Yield 2nd sequence");
            yield return Observable.Create<long>(o =>
            {
                Console.WriteLine("2nd subscribed to");
                return Observable.Timer(TimeSpan.FromMilliseconds(300))
                .Select(i => 2L)
                .Subscribe(o);
            });
            Thread.Sleep(1000);     //Force a delay
            Console.WriteLine("Yield 3rd sequence");
            yield return Observable.Create<long>(o =>
            {
                Console.WriteLine("3rd subscribed to");
                return Observable.Timer(TimeSpan.FromMilliseconds(100))
                .Select(i => 3L)
                .Subscribe(o);
            });
            Console.WriteLine("GetSequences() complete");
        }

        public static IObservable<Thing> GetStuff()
        {
            var now = new DateTimeOffset(0, TimeSpan.FromTicks(0));
            var count = 1000;
            var rnd = new Random(214748364);
            var list = new List<Thing>(Enumerable.Range(1, count).Select(i =>
                new Thing() { Number = i, Title = "Run 1", CreatedAt = now + TimeSpan.FromMinutes(i), UpdatedAt = now + TimeSpan.FromMinutes(i) }));

            var cache = list.ToObservable();
            var live = Observable.Generate(
                1,
                i => i != count,
                i => i + 1,
                i => new Thing()
                {
                    Number = i,
                    Title = "Run 2",
                    CreatedAt = now + TimeSpan.FromMinutes(i),
                    UpdatedAt = now + TimeSpan.FromMinutes(rnd.Next(count, count * 2))
                }//,

                //i => TimeSpan.FromMilliseconds(10)
            )
            .Delay(TimeSpan.FromMilliseconds(10))
            ;

            return cache.Merge(live).Publish().RefCount();
            //collection.ForEach(thing => Console.WriteLine("{0} {1}", thing.Number, thing.Title));

            //.Do(thing => Console.WriteLine("thing {0}", thing));
            //.Publish().RefCount();

            //sequences.Do(x => Console.WriteLine(x.Number)).Subscribe();

            //Console.ReadKey();
        }

        public static IObservable<Thing> GetStuff2()
        {
            var now = new DateTimeOffset(0, TimeSpan.FromTicks(0));
            var count = 20;
            var rnd = new Random(214748364);

            var titles1 = Enumerable.Range(1, count).Select(x => ((char)('a' + x)).ToString()).ToList();
            var dates1 = Enumerable.Range(1, count).Select(x => now + TimeSpan.FromMinutes(x)).ToList();

            var idstack1 = new Stack<int>(Enumerable.Range(1, count).OrderBy(rnd.Next));
            var datestack1 = new Stack<DateTimeOffset>(dates1);
            var titlestack1 = new Stack<string>(titles1.OrderBy(_ => rnd.Next()));

            var titles2 = Enumerable.Range(1, count).Select(x => ((char)('l' + x)).ToString()).ToList();
            var dates2 = Enumerable.Range(1, count).Select(x => now + TimeSpan.FromMinutes(x)).ToList();

            var idstack2 = new Stack<int>(Enumerable.Range(1, count).OrderBy(rnd.Next));
            var datestack2 = new Stack<DateTimeOffset>(dates2.OrderBy(_ => rnd.Next()));
            var titlestack2 = new Stack<string>(titles2.OrderBy(_ => rnd.Next()));

            var list = new List<Thing>(Enumerable.Range(1, count)
                .Select(i =>
                {
                    var id = idstack1.Pop();
                    var date = datestack1.Pop();
                    var title = titlestack1.Pop();
                    return new Thing(id, title, date, date);
                }));

            var cache = list.ToObservable();
            var live = Observable
                .Generate(
                1,
                i => i != count+1,
                i => i + 1,
                i =>
                {
                    var id = idstack2.Pop();
                    var date = datestack2.Pop();
                    var title = titlestack2.Pop();
                    return new Thing(id, title, date, date);
                }//,
                //i => TimeSpan.FromMilliseconds(300)
            )
            .DelaySubscription(TimeSpan.FromMilliseconds(100));

            return cache.Merge(live).Replay().RefCount();
        }

        public static IObservable<Thing> GetStuff3()
        {
            var now = new DateTimeOffset(0, TimeSpan.FromTicks(0));
            var count = 20;
            var rnd = new Random(214748364);

            var titles1 = Enumerable.Range(1, count).Select(x => ((char)('a' + x)).ToString()).ToList();
            var dates1 = Enumerable.Range(1, count).Select(x => now + TimeSpan.FromMinutes(x)).ToList();

            var idstack1 = new Stack<int>(Enumerable.Range(1, count).OrderBy(rnd.Next));
            //var datestack1 = new Stack<DateTimeOffset>(dates1.OrderBy(_ => rnd.Next()));
            //var datestack1 = new Stack<DateTimeOffset>(new List<DateTimeOffset>() { dates1[0], dates1[1], dates1[2] });
            var datestack1 = new Stack<DateTimeOffset>(dates1);
            var titlestack1 = new Stack<string>(titles1.OrderBy(_ => rnd.Next()));

            var titles2 = Enumerable.Range(1, count).Select(x => ((char)('c' + x)).ToString()).ToList();
            var dates2 = Enumerable.Range(1, count).Select(x => now + TimeSpan.FromMinutes(x)).ToList();

            var idstack2 = new Stack<int>(Enumerable.Range(1, count).OrderBy(rnd.Next));
            //var datestack2 = new Stack<DateTimeOffset>(dates2.OrderBy(_ => rnd.Next()));
            //var datestack2 = new Stack<DateTimeOffset>(dates2);
            var datestack2 = new Stack<DateTimeOffset>(new List<DateTimeOffset>() {
                dates2[2],
                dates2[0],
                dates2[1],
                dates2[3],
                dates2[5],
                dates2[9],
                dates2[15],
                dates2[6],
                dates2[7],
                dates2[8],
                dates2[13],
                dates2[10],
                dates2[16],
                dates2[11],
                dates2[12],
                dates2[14],
                dates2[17],
                dates2[18],
                dates2[19],
                dates2[4],
            });
            var titlestack2 = new Stack<string>(titles2.OrderBy(_ => rnd.Next()));
            var list = new List<Thing>(Enumerable.Range(1, count)
                .Select(i =>
                {
                    var id = idstack1.Pop();
                    var date = datestack1.Pop();
                    var title = titlestack1.Pop();
                    return new Thing(id, title, date, date);
                }));

            var cache = list.ToObservable();
            var live = Observable
                .Generate(
                1,
                i => i != count + 1,
                i => i + 1,
                i =>
                {
                    var id = idstack2.Pop();
                    var date = datestack2.Pop();
                    var title = titlestack2.Pop();
                    return new Thing(id, title, date, date);
                }
                //,i => TimeSpan.FromMilliseconds(300)
            )
            .DelaySubscription(TimeSpan.FromMilliseconds(100));

            return cache.Merge(live).Replay().RefCount();
        }

        public static IObservable<Thing> GetStuff(int amount)
        {
            var now = new DateTimeOffset(0, TimeSpan.FromTicks(0));
            var count = amount;
            var rnd = new Random(214748364);

            var titles1 = Enumerable.Range(1, count).Select(x => ((char)('A' + x)).ToString()).ToList();
            var dates1 = Enumerable.Range(1, count).Select(x => now + TimeSpan.FromMinutes(x)).ToList();

            var idstack1 = new Stack<int>(Enumerable.Range(1, count).OrderBy(rnd.Next));
            var datestack1 = new Stack<DateTimeOffset>(dates1);
            var titlestack1 = new Stack<string>(titles1.OrderBy(_ => rnd.Next()));

            var titles2 = Enumerable.Range(1, count).Select(x => ((char)('A' + x)).ToString()).ToList();
            var dates2 = Enumerable.Range(1, count).Select(x => now + TimeSpan.FromMinutes(x)).ToList();

            var idstack2 = new Stack<int>(Enumerable.Range(1, count).OrderBy(rnd.Next));
            var datestack2 = new Stack<DateTimeOffset>(dates2.OrderBy(_ => rnd.Next()));
            var titlestack2 = new Stack<string>(titles2.OrderBy(_ => rnd.Next()));

            var list = new List<Thing>(Enumerable.Range(1, count)
                .Select(i =>
                {
                    var id = idstack1.Pop();
                    var date = datestack1.Pop();
                    var title = titlestack1.Pop();
                    return new Thing(id, title, date, date);
                }));

            var cache = list.ToObservable();
            var live = Observable
                .Generate(
                1,
                i => i != count + 1,
                i => i + 1,
                i =>
                {
                    var id = idstack2.Pop();
                    var date = datestack2.Pop();
                    var title = titlestack2.Pop();
                    return new Thing(id, title, date, date);
                }
                //, i => TimeSpan.FromMilliseconds(300)
            )
            .DelaySubscription(TimeSpan.FromMilliseconds(100));

            return cache.Merge(live).Replay().RefCount();
        }

    }


    public static class EnumerableExtensions
    {
        public static IEnumerable<T> ForEach<T>(this IEnumerable<T> source, Action<T> action)
        {
            foreach (var item in source) action(item);
            return source;
        }
    }
}
