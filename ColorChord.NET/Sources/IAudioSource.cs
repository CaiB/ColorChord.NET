using ColorChord.NET.Config;

namespace ColorChord.NET.Sources
{
    public interface IAudioSource : IConfigurableAttr
    {
        void Start();
        void Stop();
    }
}
