namespace ColorChord.NET.Sources
{
    public interface IAudioSource : IConfigurable
    {
        void Start();
        void Stop();
    }
}
