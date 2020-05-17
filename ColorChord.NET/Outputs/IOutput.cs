namespace ColorChord.NET.Outputs
{
    public interface IOutput : IConfigurable
    {
        void Start();
        void Stop();
        void Dispatch();
    }
}
