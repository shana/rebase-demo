using System;
using Xunit;
using Xunit.Sdk;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
[XunitTestCaseDiscoverer("STAFactDiscoverer", "TrackingCollectionTests")]
public class STAFactAttribute : FactAttribute
{
}