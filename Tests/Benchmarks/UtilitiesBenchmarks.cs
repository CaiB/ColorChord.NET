using BenchmarkDotNet.Attributes;
using ColorChord.NET.API.Visualizers;

namespace ColorChord.NET.Tests.Benchmarks;

public class UtilitiesBenchmarks
{
    private const int ARRAY_SIZE = 8192;
    private const int RAND_SEED = 0x811426;
    public volatile float GarbageBinFloat;
    public volatile uint GarbageBinU32;

    [GlobalSetup]
    public void SetupUtilities()
    {
        Random Rand = new(RAND_SEED);

        for (int i = 0; i < ZeroToOneFloats.Length; i++) { ZeroToOneFloats[i] = Rand.NextSingle(); }
    }

    private float[] ZeroToOneFloats = new float[ARRAY_SIZE];

    [Benchmark]
    public void EmptyBaseline()
    {
        float[] Data = ZeroToOneFloats;
        foreach (float Input in Data) { this.GarbageBinFloat = Input; }
    }

    [Benchmark]
    public void VisualizerTools_CCToHue()
    {
        float[] Data = ZeroToOneFloats;
        foreach (float Input in Data) { this.GarbageBinFloat = VisualizerTools.CCToHue(Input); }
    }

    [Benchmark]
    public void VisualizerTools_CCtoRGB()
    {
        float[] Data = ZeroToOneFloats;
        foreach (float Input in Data) { this.GarbageBinU32 = VisualizerTools.CCToRGB(Input, 0.83F, 0.96F); }
    }

    [Benchmark]
    public void VisualizerTools_HSVToRGB()
    {
        float[] Data = ZeroToOneFloats;
        foreach (float Input in Data) { this.GarbageBinU32 = VisualizerTools.HSVToRGB(Input, 0.54F, 0.17F); }
    }

    // TODO: VisualizerTools.RGBToHSV()
}
