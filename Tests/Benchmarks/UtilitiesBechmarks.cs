using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Diagnosers;
using ColorChord.NET.API.Visualizers;

namespace ColorChord.NET.Tests.Benchmarks;

[MemoryDiagnoser]
[HardwareCounters(HardwareCounter.BranchInstructions, HardwareCounter.BranchMispredictions)]
[DisassemblyDiagnoser]
public class UtilitiesBechmarks
{
    private const int ARRAY_SIZE = 8192;
    private const int RAND_SEED = 0x811426;

    [GlobalSetup]
    public void SetupUtilities()
    {
        Random Rand = new(RAND_SEED);

        for (int i = 0; i < ZeroToOneFloats.Length; i++) { ZeroToOneFloats[i] = Rand.NextSingle(); }
    }

    public float[] ZeroToOneFloats = new float[ARRAY_SIZE];

    [Benchmark]
    public float EmptyBaseline()
    {
        float Result = 0F;
        foreach (float Input in ZeroToOneFloats) { Result += Input; }
        return Result;
    }

    [Benchmark]
    public float VisualizerTools_CCToHue()
    {
        float Result = 0F;
        foreach (float Input in ZeroToOneFloats) { Result += VisualizerTools.CCToHue(Input); }
        return Result;
    }

    [Benchmark]
    public float VisualizerTools_CCtoRGB()
    {
        float Result = 0F;
        foreach (float Input in ZeroToOneFloats) { Result += VisualizerTools.CCToRGB(Input, 0.83F, 0.96F); }
        return Result;
    }

    [Benchmark]
    public uint VisualizerTools_HSVToRGB()
    {
        uint Result = 0;
        foreach (float Input in ZeroToOneFloats) { Result += VisualizerTools.HSVToRGB(Input, 0.54F, 0.17F); }
        return Result;
    }

    // TODO: VisualizerTools.RGBToHSV()
}
