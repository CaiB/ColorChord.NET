using ColorChord.NET.Config;
using ColorChord.NET.Outputs;

namespace ColorChord.NET.Visualizers
{
    public interface IVisualizer: IConfigurableAttr
    {
        string Name { get; }
        void Start();
        void Stop();
        void AttachOutput(IOutput output);
    }
}
