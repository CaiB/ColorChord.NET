using ColorChord.NET.API;
using ColorChord.NET.API.Extensions;

namespace ColorChord.NET.Extensions.AudioFileSource;

public class ExtensionInfo : IExtension
{
    public string Name => "Audio File Source Module";
    public string Description => "Adds a Source component which reads audio from a file instead of from the system.";
    public uint APIVersion => ColorChordAPI.APIVersion;

    public void Initialize() { }
    public void PostInitialize() { AudioFile.Instance?.StartReading(); }
    public void Shutdown() { }
}
