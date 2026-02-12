using ColorChord.NET.API;
using ColorChord.NET.API.Extensions;

namespace ColorChord.NET.Extensions.FileOutputs;

public class ExtensionInfo : IExtension
{
    public string Name => "Miscellaneous File Outputs Module";
    public string Description => "Adds Output components which write visualizer data to image, CSV, and other files.";
    public uint APIVersion => ColorChordAPI.APIVersion;

    public void Initialize() { }
    public void PostInitialize() { }
    public void Shutdown() { }
}
