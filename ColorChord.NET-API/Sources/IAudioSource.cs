using ColorChord.NET.API.Config;

namespace ColorChord.NET.API.Sources;

public interface IAudioSource : IConfigurableAttr
{
    void Start();
    void Stop();
}
