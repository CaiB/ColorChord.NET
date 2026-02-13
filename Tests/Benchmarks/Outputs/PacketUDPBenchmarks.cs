using BenchmarkDotNet.Attributes;
using ColorChord.NET.API;
using ColorChord.NET.API.Config;
using ColorChord.NET.Outputs;
using ColorChord.NET.Tests.Benchmarks.FakeData;

namespace ColorChord.NET.Tests.Benchmarks.Outputs;

public class PacketUDPBenchmarks
{
    [Params("Raw", "TPM2.NET")]
    public string Protocol { get; set; } = "Raw";

    [Params(50)]
    public int LEDCount { get; set; }

    private FakeDiscrete1D? Source;
    private PacketUDP? Target;

    [GlobalSetup]
    public void Prepare()
    {
        FakeConfigurer Configurer = new();
        ColorChordAPI.Configurer = Configurer;

        const string SOURCE_NAME = "Fake Discrete1D Source";
        this.Source = new(SOURCE_NAME, this.LEDCount);
        FakeConfigurer.VisualizerInsts.Add(SOURCE_NAME, this.Source);

        const string DEST_NAME = "PacketUDP Under Test";
        Dictionary<string, object> TargetConfig = new()
        {
            { ConfigNames.VIZ_NAME, SOURCE_NAME },
            { "IP", "127.0.0.1" },
            { "Port", 19955 },
            { "Protocol", this.Protocol }
        };
        this.Target = new(DEST_NAME, TargetConfig);
        FakeConfigurer.OutputInsts.Add(DEST_NAME, this.Target);
    }

    [Benchmark]
    public void Dispatch()
    {
        this.Target!.Dispatch();
    }
}
