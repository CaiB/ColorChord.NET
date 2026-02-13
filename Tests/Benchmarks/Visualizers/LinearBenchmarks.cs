using BenchmarkDotNet.Attributes;
using ColorChord.NET.API;
using ColorChord.NET.API.Config;
using ColorChord.NET.API.NoteFinder;
using ColorChord.NET.Tests.Benchmarks.FakeData;
using ColorChord.NET.Visualizers;

namespace ColorChord.NET.Tests.Benchmarks.Visualizers;

public class LinearBenchmarks
{
    [Params(50, 2000)]
    public int LEDCount;

    [Params(true, false)]
    public bool IsCircular;

    [Params(true, false)]
    public bool IsOrdered;

    private NoteFinderCommon? Source;
    private Linear? Target;

    [GlobalSetup]
    public void Prepare()
    {
        FakeConfigurer Configurer = new();
        ColorChordAPI.Configurer = Configurer;

        const string NF_NAME = "Fake NoteFinder";
        this.Source = new FakeNoteFinder(NF_NAME);
        FakeConfigurer.NoteFinderInsts.Add(NF_NAME, this.Source);

        const string INST_NAME = "Linear Under Test";
        Dictionary<string, object> TargetConfig = new()
        {
            { ConfigNames.LED_COUNT, this.LEDCount },
            { "IsCircular", this.IsCircular },
            { "IsOrdered", this.IsOrdered },
            { ConfigNames.NOTE_FINDER_NAME, NF_NAME },
            { "TestBullshit", "YES" }
        };
        this.Target = new(INST_NAME, TargetConfig);
        FakeConfigurer.VisualizerInsts.Add(INST_NAME, this.Target);
    }

    [Benchmark]
    public void Update()
    {
        this.Target!.Update();
    }
}
