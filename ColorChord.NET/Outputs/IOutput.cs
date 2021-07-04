using ColorChord.NET.Config;

namespace ColorChord.NET.Outputs
{
    public interface IOutput : IConfigurableAttr
    {
        void Start();
        void Stop();
        void Dispatch();
    }
}
