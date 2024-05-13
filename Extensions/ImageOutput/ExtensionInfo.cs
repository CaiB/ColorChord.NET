using ColorChord.NET.API;
using ColorChord.NET.API.Extensions;

namespace ColorChord.NET.Extensions.ImageOutput;

public class ExtensionInfo : IExtension
{
    public string Name => "Image Output Module";
    public string Description => "Adds an Output component which writes visualizer data to image files.";
    public uint APIVersion => ColorChordAPI.APIVersion;

    public void Initialize() { }
    public void PostInitialize() { }
    public void Shutdown() { }
}
