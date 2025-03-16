using BenchmarkDotNet.Running;

namespace ColorChord.NET.Tests.Benchmarks;

public class BenchmarksTop
{
    public static void Main(string[] args)
    {
        BenchmarkSwitcher.FromAssembly(typeof(BenchmarksTop).Assembly).Run(args);
    }
}
