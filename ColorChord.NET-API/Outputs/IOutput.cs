using ColorChord.NET.API.Config;

namespace ColorChord.NET.API.Outputs;

public interface IOutput : IConfigurableAttr
{
    string Name { get; }

    // TODO: Document exactly when this happens
    void Start();

    /// <summary>Called when the output needs to be stopped, such as when CC.NET is exiting.</summary>
    /// <remarks>Use this to clean up any system resources such as open handles.</remarks>
    void Stop();

    /// <summary>Called every frame to trigger the output of data.</summary>
    void Dispatch();
}
