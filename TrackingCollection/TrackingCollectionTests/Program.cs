using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace TrackingCollectionTests
{
    class Output : ITestOutputHelper
    {
        public void WriteLine(string message)
        {
            Console.WriteLine(message);
        }

        public void WriteLine(string format, params object[] args)
        {
            Console.WriteLine(format, args);
        }
    }

    class Program
    {
        public static void Main()
        {
            new TrackingTests(new Output()).OrderByDoesntMatchOriginalOrderTimings();
        }
    }
}
