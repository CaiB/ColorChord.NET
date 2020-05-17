using ColorChord.NET.Outputs;

namespace ColorChord.NET.Visualizers
{
    public interface IVisualizer: IConfigurable
    {
        string Name { get; }
        void Start();
        void Stop();
        void AttachOutput(IOutput output);
    }
}
