using ColorChord.NET.API;
using ColorChord.NET.API.Extensions;

namespace ColorChord.NET.Extensions.FFTNoteFinder;

public class ExtensionInfo : IExtension
{
    public string Name => "FFT-Based NoteFinder";
    public string Description => "Adds a NoteFinder component which uses a standard FFT-based Fourier transform.";
    public uint APIVersion => ColorChordAPI.APIVersion;

    public void Initialize() { }
    public void PostInitialize() { }
    public void Shutdown() { }
}
