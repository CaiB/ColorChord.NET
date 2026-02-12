using ColorChord.NET.API.NoteFinder;
using ColorChord.NET.API.Outputs;
using ColorChord.NET.API.Visualizers;
using ColorChord.NET.API.Visualizers.Formats;

namespace ColorChord.NET.Tests.Benchmarks.FakeData;

public class FakeDiscrete1D : IVisualizer, IDiscrete1D
{
    private const int RANDOM_SEED = 0x5843F;
    private uint[] Data;
    private List<IOutput> Outputs;

    public FakeDiscrete1D(string name, int count)
    {
        this.Name = name;
        this.Data = new uint[count];
        Random Rand = new(RANDOM_SEED);
        for (int i = 0; i < this.Data.Length; i++) { this.Data[i] = (uint)Rand.Next(0x1000000); }
        this.Outputs = new(4);
    }

    public string Name { get; private init; }

    public NoteFinderCommon? NoteFinder => null;

    public void AttachOutput(IOutput output) => this.Outputs.Add(output);

    public int GetCountDiscrete() => this.Data.Length;

    public uint[] GetDataDiscrete() => this.Data;

    public void DoDispatch()
    {
        foreach (IOutput Output in this.Outputs) { Output.Dispatch(); }
    }

    public void Start() { }

    public void Stop() { }
}
