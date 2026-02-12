using ColorChord.NET.API.Config;
using ColorChord.NET.API.NoteFinder;
using ColorChord.NET.API.Outputs;
using ColorChord.NET.API.Visualizers;
using ColorChord.NET.API.Visualizers.Formats;
using ColorChord.NET.Config;
using ColorChord.NET.NoteFinder;
using ColorChord.NET.Outputs;
using System;
using System.Collections.Generic;

namespace ColorChord.NET.Visualizers;

public class NoteFinderPassthrough : IVisualizer, IDiscrete1D
{
    public string Name { get; private init; }

    public NoteFinderCommon NoteFinder { get; private init; }

    [ConfigFloat("TimePeriod", -1000000, 1000000, 100)]
    private float TimePeriod = 100F;

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
        if (this.NoteFinder is Gen2NoteFinder G2NF) { G2NF.AddTimingReceiver(GetData, this.TimePeriod); } // TODO: Generalize
    }

    public void GetData()
    {
        ReadOnlySpan<float> RawData = this.NoteFinder.AllBinValues;
        if (this.Data.Length != RawData.Length) { this.Data = new uint[RawData.Length]; }
        for (int i = 0; i < RawData.Length; i++) { this.Data[i] = VisualizerTools.CCToRGB((float)i / this.NoteFinder.BinsPerOctave, 1F, MathF.Pow(RawData[i] * this.SaturationAmplifier, this.SaturationExponent)); }
        foreach (IOutput Out in this.Outputs) { Out.Dispatch(); }
    }

    public void AttachOutput(IOutput output)
    {
        this.Outputs.Add(output);
        if (this.NoteFinder is not Gen2NoteFinder && output is DisplayOpenGL DOGL) { DOGL.AddTimingReceiver(GetData, 1); }
    }

    public int GetCountDiscrete() => this.Data.Length;

    public uint[] GetDataDiscrete() => this.Data;

    public void Stop() { }
}
