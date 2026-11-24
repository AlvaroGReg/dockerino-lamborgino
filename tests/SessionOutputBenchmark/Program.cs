using System.Diagnostics;
using System.Text;
using DockerinoLamborgino.Core;

// Microbenchmark of the old whole-buffer copy versus the retained history.
// Run: dotnet run -c Release --project tests/SessionOutputBenchmark
// Illustrative budgets for the new in-process read: <64 KiB allocated/read and
// <5 ms/read, including contention; not an end-to-end CLI or UI latency budget.
const int size = 4 * 1024 * 1024;
const int iterations = 40;
var text = new string('x', size);
var old = new StringBuilder(text);
var history = new SessionOutputHistory();
history.Append(text);
object oldGate = new();

string Before(int offset)
{
    lock (oldGate)
    {
        var all = old.ToString();
        return all[offset..];
    }
}
string After(int offset) => history.Read(offset).Text;

void Measure(string name, Func<int, string> read, int offset, int followers)
{
    read(offset);
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    var allocated = GC.GetTotalAllocatedBytes(true);
    var watch = Stopwatch.StartNew();
    Parallel.For(0, followers, _ =>
    {
        for (var i = 0; i < iterations; i++)
            GC.KeepAlive(read(offset));
    });
    watch.Stop();
    var count = followers * iterations;
    Console.WriteLine($"{name,-26} {watch.Elapsed.TotalMilliseconds / count,9:F3} ms/read " +
        $"{(GC.GetTotalAllocatedBytes(true) - allocated) / count,10} B/read");
}

Console.WriteLine($"History: {size:N0} UTF-16 units; iterations: {iterations}; .NET {Environment.Version}");
foreach (var (label, offset, followers) in new[]
{
    ("empty poll", size, 1), ("1 Ki suffix", size - 1024, 1), ("4 followers empty", size, 4)
})
{
    Measure("before " + label, Before, offset, followers);
    Measure("after  " + label, After, offset, followers);
}
