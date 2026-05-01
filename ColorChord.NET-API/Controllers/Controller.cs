using ColorChord.NET.API.Config;

namespace ColorChord.NET.API.Controllers;

public abstract class Controller(string name, Dictionary<string, object> config, IControllerInterface controllerInterface) : IConfigurableAttr
{
    /// <summary>The name of this specific controller instance, must be unique.</summary>
    public string Name { get; private init; } = name;

    /// <summary>The interface with which this controller interacts with ColorChord.NET through.</summary>
    public IControllerInterface Interface { get; private init; } = controllerInterface;

    /// <summary>Called when the controller is started, after having its configurable settings configured.</summary>
    public abstract void Start();

    /// <summary>Called when ColorChord.NET is exiting gracefully. Use this to clean up any system resources.</summary>
    public abstract void Stop();
}
