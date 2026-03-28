using BenchmarkDotNet.Attributes;
using ColorChord.NET.API;
using ColorChord.NET.API.Config;
using ColorChord.NET.API.NoteFinder;
using ColorChord.NET.Tests.Benchmarks.FakeData;
using ColorChord.NET.Visualizers;
using System;
using System.Collections.Generic;
using System.Text;

namespace ColorChord.NET.Tests.Benchmarks.Visualizers;

public class CellsBenchmarks
{
    [Params(50, 2000)]
    public int LEDCount;

    [Params(true, false)]
    public bool TimeBased;

    private NoteFinderCommon? Source;
    private Cells? Target;

    [GlobalSetup]
    public void Prepare()
    {
        FakeConfigurer Configurer = new();
        ColorChordAPI.Configurer = Configurer;

        const string NF_NAME = "Fake NoteFinder";
        this.Source = new FakeNoteFinder(NF_NAME);
        FakeConfigurer.NoteFinderInsts.Add(NF_NAME, this.Source);

        const string INST_NAME = "Cells Under Test";
        Dictionary<string, object> TargetConfig = new()
        {
            { ConfigNames.LED_COUNT, this.LEDCount },
            { ConfigNames.NOTE_FINDER_NAME, NF_NAME },
            { "TimeBased", this.TimeBased },
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
