using GitHub.Collections;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace ObservableTests
{
    /// <summary>
    /// Interaction logic for Window.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        IDisposable disposable;
        TrackingCollection<Thing> col;
        DateTimeOffset Now = new DateTimeOffset(0, TimeSpan.FromTicks(0));

        public MainWindow()
        {
            InitializeComponent();

            DataContext = this;

            /*
            stuff.CollectionChanged += (s, e) => {
                Console.WriteLine(e.Action);
                e.NewItems?.OfType<Thing>().All(thing => { Console.WriteLine("{0} {1}:{2}", e.Action, thing.Number, thing.UpdatedAt); return true; });
            };
            */

            Activated += (s, e) =>
            {
            };

            col = new TrackingCollection<Thing>(
            OrderedComparer<Thing>.OrderByDescending(x => x.UpdatedAt).Compare);
            col.ProcessingDelay = TimeSpan.FromMilliseconds(30);

        }

        void Button_Click(object sender, RoutedEventArgs e)
        {
            Run2();
        }

        void Run2()
        {
            var count = 10;
            var col = new TrackingCollection<Thing>();
            col.ProcessingDelay = TimeSpan.FromMilliseconds(20);

            col.NewerComparer = OrderedComparer<Thing>.OrderByDescending(x => x.UpdatedAt).Compare;
            list.ItemsSource = col;

            var source = Observable.Merge(
                Observable.Generate(0, i => i < count, i => i + 1, i => i, i => TimeSpan.FromMilliseconds(5))
                    .Select(i => GetThing(i, i, i, "Run 1")),
                Observable.Generate(0, i => i < count, i => i + 1, i => i, i => TimeSpan.FromMilliseconds(7))
                    .Select(i => GetThing(i, i, i + 1, "Run 2"))
            );

            col.Listen(source);
            col.Subscribe();
        }

        void Run1()
        {
            /*
            disposable?.Dispose();

            done.Text = "Working...";
            var now = new DateTimeOffset(0, TimeSpan.FromTicks(0)) + TimeSpan.FromMinutes(10);
            var sequences = Program.GetStuff2();
            Func<Thing, Thing, int> comparer = OrderedComparer<Thing>.OrderByDescending(x => x.UpdatedAt).Compare;
            var stuff = new TrackingCollection<Thing>(sequences,comparer,
                (t, idx, col) => idx >=5 && idx < 10 
                //&& t.CreatedAt > now && t.CreatedAt <= (now + TimeSpan.FromMinutes(10))
                );
            //list.ItemsSource = stuff;
            disposable = stuff.Subscribe(() =>
            {
                bool bad = false;
                //stuff.SetComparer(OrderedComparer<Thing>.OrderBy(x => x.Number).Compare);
                for (int i = 0; i < stuff.Count - 1; i++)
                {
                    if (comparer(stuff[i], stuff[i + 1]) > 0)
                    {
                        bad = true;
                        break;
                    }
                }
                if (bad)
                    done.Text = "Problem";
                else
                    done.Text = "Done";
            });
            */

            var seq = Program.GetStuff(50);

            //var col = new TrackingCollection<Thing>(
            //    OrderedComparer<Thing>.OrderByDescending(x => x.UpdatedAt).Compare);
            //var res = col.Listen(seq);
            //col.ProcessingDelay = TimeSpan.FromMilliseconds(20);

            //col.CollectionChanged += (s, ee) => {
            //    //Console.WriteLine(ee.Action);
            //    ee.NewItems?.OfType<Thing>().All(thing => { Console.WriteLine("{0} {1}:{2}", ee.Action, thing.Number, thing.UpdatedAt); return true; });
            //};

            list.ItemsSource = col;
            //res.TimeInterval()
            //    .Select(i => log.Text += i.Interval + "\r\n")
            //    .Subscribe();
            Subject<DateTime> times = new Subject<DateTime>();
            var count = 0;
            double time = 0;
            times.TimeInterval().Do(i =>
            {
                count++;
                time += i.Interval.TotalMilliseconds;
                log.Text += i.Interval + "\r\n";
            }).Subscribe(_ => { }, () => Debug.WriteLine("AVERAGE " + time / count));

            var res = col.Listen(seq);
            Debug.WriteLine("===== GO ======");
            col.OriginalCompleted.Subscribe(_ => { }, () => times.OnCompleted());
            col.Subscribe(x => times.OnNext(DateTime.Now), () => times.OnCompleted());

            //seq.Subscribe();
            //foreach (var x in seq.ToEnumerable())
            //{
            //    col.AddItem(x);
            //}
        }

        protected void Add(Subject<Thing> source, Thing item)
        {
            source.OnNext(item);
        }

        protected Thing GetThing(int id)
        {
            return new Thing { Number = id };
        }

        protected Thing GetThing(int id, int minutes)
        {
            return new Thing { Number = id, Title = "Run 1", CreatedAt = Now + TimeSpan.FromMinutes(minutes), UpdatedAt = Now + TimeSpan.FromMinutes(minutes) };
        }

        protected Thing GetThing(int id, int minutesc, int minutesu)
        {
            return new Thing { Number = id, Title = "Run 1", CreatedAt = Now + TimeSpan.FromMinutes(minutesc), UpdatedAt = Now + TimeSpan.FromMinutes(minutesu) };
        }

        protected Thing GetThing(int id, string title)
        {
            return new Thing { Number = id, Title = "Run 1" };
        }

        protected Thing GetThing(int id, int minutesc, int minutesu, string title)
        {
            return new Thing { Number = id, Title = title, CreatedAt = Now + TimeSpan.FromMinutes(minutesc), UpdatedAt = Now + TimeSpan.FromMinutes(minutesu) };
        }
    }
}
