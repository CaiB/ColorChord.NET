using ColorChord.NET.API;
using ColorChord.NET.API.Config;
using ColorChord.NET.API.NoteFinder;
using ColorChord.NET.API.Outputs;
using ColorChord.NET.API.Timing;
using ColorChord.NET.API.Visualizers;
using ColorChord.NET.API.Visualizers.Formats;
using System;
using System.Collections.Generic;

namespace ColorChord.NET.Visualizers;

public class DirectProminent : IVisualizer, IDiscrete1D, ITimingReceiver
{
    public string Name { get; private init; }
    public NoteFinderCommon NoteFinder { get; private init; }

    [ConfigTimeSource(genericDefaultTime: 1000 / 60F)]
    private TimingConnection? TimeSource { get; set; }

    [ConfigFloat("SaturationAmplifier", 0F, 100F, 1F)]
    private float SaturationAmplifier = 1F;

    [ConfigFloat("SaturationExponent", 0F, 20F, 2F)]
    private float SaturationExponent = 2F;

    private uint[] Data = new uint[1];

    private List<IOutput> Outputs = new();

    public DirectProminent(string name, Dictionary<string, object> config)
    {
        this.Name = name;
        ColorChordAPI.Configurer.Configure(this, config);
        this.NoteFinder = ColorChordAPI.Configurer.FindNoteFinder(config) ?? throw new Exception($"{nameof(DirectProminent)} could not find NoteFinder to attach to.");
    }

    public void Start() { }

    public void AttachOutput(IOutput output) => this.Outputs.Add(output);

    public int GetCountDiscrete() => 1;

    public uint[] GetDataDiscrete() => this.Data;

    public void TimingCallback(object? sender) => Update();

    public void Update()
    {
        if (this.TimeSource?.IsOwnGeneric ?? true) { this.NoteFinder.UpdateOutputs(); }

        float MaxValue = 0F;
        int MaxIndex = 0;
        for (int i = 0; i < this.NoteFinder.OctaveBinValues.Length; i++)
        {
            if (MaxValue < this.NoteFinder.OctaveBinValues[i])
            {
                MaxValue = this.NoteFinder.OctaveBinValues[i];
                MaxIndex = i;
            }
        }

        if (MaxValue == 0F) { this.Data[0] = 0; }
        else
        {
            this.Data[0] = VisualizerTools.CCToRGB((float)MaxIndex / this.NoteFinder.OctaveBinValues.Length, 1F, MathF.Pow(MaxValue, this.SaturationExponent) * this.SaturationAmplifier);
        }

        foreach (IOutput Output in this.Outputs) { Output.Dispatch(); }
    }

    public void Stop() => this.TimeSource?.Remove();
}
