using ColorChord.NET.API;
using ColorChord.NET.API.Config;
using ColorChord.NET.API.NoteFinder;
using ColorChord.NET.API.Outputs;
using ColorChord.NET.API.Sources;
using ColorChord.NET.API.Utility;
using ColorChord.NET.API.Visualizers;
using ColorChord.NET.API.Visualizers.Formats;
using ColorChord.NET.Config;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace ColorChord.NET.Visualizers;

public class DirectProminent : IVisualizer, IDiscrete1D
{
    public string Name { get; private init; }
    public NoteFinderCommon NoteFinder { get; private init; }

    [ConfigString("TimeSource", "this")]
    private string TimeSource { get; set; } = "this";

    [ConfigFloat("TimePeriod", -1000000, 1000000, 100)]
    private float TimePeriod = 100F;

    [ConfigFloat("SaturationAmplifier", 0F, 100F, 1F)]
    private float SaturationAmplifier = 1F;

    [ConfigFloat("SaturationExponent", 0F, 20F, 2F)]
    private float SaturationExponent = 2F;

    private bool IsOwnTimeSource = true;
    private ITimingSource? ConnectedTimingSource { get; set; } = null;

    private uint[] Data = new uint[1];

    private List<IOutput> Outputs = new();

    public DirectProminent(string name, Dictionary<string, object> config)
    {
        this.Name = name;
        Configurer.Configure(this, config);
        this.NoteFinder = Configurer.FindNoteFinder(config) ?? throw new Exception($"{nameof(DirectProminent)} could not find NoteFinder to attach to.");
    }

    private void HookTimeSource()
    {
        ITimingSource? NewSource;
        if (this.TimeSource.ToLower() == "this") { NewSource = new GenericTimingSourceSingle(); }
        else { NewSource = ColorChord.GetInstanceFromPath(this.TimeSource) as ITimingSource; }
        this.IsOwnTimeSource = this.TimeSource.ToLower() == "this";

        this.ConnectedTimingSource?.RemoveTimingReceiver(Update);
        NewSource?.AddTimingReceiver(Update, this.TimePeriod);
        this.ConnectedTimingSource = NewSource;
    }

    private void UnhookTimeSource()
    {
        this.ConnectedTimingSource?.RemoveTimingReceiver(Update);
        this.ConnectedTimingSource = null;
    }

    public void Start() => HookTimeSource();

    public void AttachOutput(IOutput output) => this.Outputs.Add(output);

    public int GetCountDiscrete() => 1;

    public uint[] GetDataDiscrete() => this.Data;

    public void Update()
    {
        if (this.IsOwnTimeSource) { this.NoteFinder.UpdateOutputs(); }

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

    public void Stop() => UnhookTimeSource();
}
