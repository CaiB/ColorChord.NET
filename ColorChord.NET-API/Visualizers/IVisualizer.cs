using ColorChord.NET.API.Config;
using ColorChord.NET.API.Outputs;

namespace ColorChord.NET.API.Visualizers;

public interface IVisualizer : IConfigurableAttr
{
    string Name { get; }
    void Start();
    void Stop();
    void AttachOutput(IOutput output);
}
