using ColorChord.NET.API.Config;
using ColorChord.NET.API.NoteFinder;

namespace ColorChord.NET.API.Sources;

public interface IAudioSource : IConfigurableAttr
{
    string Name { get; }
    void Start();
    void Stop();
    uint GetSampleRate();
    void AttachNoteFinder(NoteFinderCommon noteFinder);
}
