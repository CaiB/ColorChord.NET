using ColorChord.NET.API.Config;
using ColorChord.NET.API.NoteFinder;
using ColorChord.NET.API.Outputs;
using ColorChord.NET.API.Visualizers;
using ColorChord.NET.API.Visualizers.Formats;
using ColorChord.NET.Config;
using ColorChord.NET.NoteFinder;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ColorChord.NET.Visualizers;

public class NoteFinderPassthrough : IVisualizer, IDiscrete1D
{
    public string Name { get; private init; }

    [ConfigFloat("TimePeriod", -1000000, 1000000, 100)]
    private float TimePeriod = 100F;

    private List<IOutput> Outputs = new(4);

    private uint[] Data;

    public NoteFinderPassthrough(string name, Dictionary<string, object> config)
    {
        this.Name = name;
        Configurer.Configure(this, config);
        this.Data = new uint[NoteFinderCommon.AllBinValues.Length];
    }

    public void Start()
    {
        ((ShinNoteFinder)ColorChord.NoteFinder).AddTimingReceiver(GetData, this.TimePeriod);
    }

    public void GetData()
    {
        float[] RawData = NoteFinderCommon.AllBinValues!;
        if (this.Data.Length != RawData.Length) { this.Data = new uint[RawData.Length]; }
        for (int i = 0; i < RawData.Length; i++) { this.Data[i] = VisualizerTools.CCToRGB((i % 24) / 24F, 1F, MathF.Pow(RawData[i] * 2.5F, 3F)); }
        foreach(IOutput Out in this.Outputs) { Out.Dispatch(); }
    }

    public void AttachOutput(IOutput output) => this.Outputs.Add(output);

    public int GetCountDiscrete() => this.Data.Length;

    public uint[] GetDataDiscrete() => this.Data;

    public void Stop() { }
}
