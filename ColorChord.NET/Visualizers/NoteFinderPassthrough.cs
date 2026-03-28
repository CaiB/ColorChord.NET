using System;
using System.Collections.Generic;
using ColorChord.NET.API.Config;
using ColorChord.NET.API.NoteFinder;
using ColorChord.NET.API.Outputs;
using ColorChord.NET.API.Timing;
using ColorChord.NET.API.Visualizers;
using ColorChord.NET.API.Visualizers.Formats;
using ColorChord.NET.Config;

namespace ColorChord.NET.Visualizers;

public class NoteFinderPassthrough : IVisualizer, IDiscrete1D, ITimingReceiver
{
    public string Name { get; private init; }

    public NoteFinderCommon NoteFinder { get; private init; }

    [ConfigTimeSource]
    private TimingConnection? TimeSource { get; set; }

    [ConfigFloat("SaturationAmplifier", 0, 1000, 4.5F)]
    private float SaturationAmplifier = 4.5F;

    [ConfigFloat("SaturationExponent", 0, 100, 2F)]
    private float SaturationExponent = 2F;

    private List<IOutput> Outputs = new(4);

    private uint[] Data;

    public NoteFinderPassthrough(string name, Dictionary<string, object> config)
    {
        this.Name = name;
        Configurer.Configure(this, config);
        this.NoteFinder = Configurer.FindNoteFinder(config) ?? throw new Exception($"{nameof(NoteFinderPassthrough)} could not find NoteFinder to attach to.");
        this.Data = new uint[this.NoteFinder.AllBinValues.Length];
    }

    public void Start()
    {
        if (this.TimeSource == null && this.NoteFinder is ITimingSource NoteFinderAsTiming) { this.TimeSource = new(NoteFinderAsTiming, TimePeriod.Minimum, this); }
    }

    public void TimingCallback(object? sender)
    {
        ReadOnlySpan<float> RawData = this.NoteFinder.AllBinValues;
        if (this.Data.Length != RawData.Length) { this.Data = new uint[RawData.Length]; }
        for (int i = 0; i < RawData.Length; i++) { this.Data[i] = VisualizerTools.CCToRGB((float)i / this.NoteFinder.BinsPerOctave, 1F, MathF.Pow(RawData[i] * this.SaturationAmplifier, this.SaturationExponent)); }
        foreach (IOutput Out in this.Outputs) { Out.Dispatch(); }
    }

    public void AttachOutput(IOutput output)
    {
        this.Outputs.Add(output);
    }

    public int GetCountDiscrete() => this.Data.Length;

    public uint[] GetDataDiscrete() => this.Data;

    public void Stop() => this.TimeSource?.Remove();
}
