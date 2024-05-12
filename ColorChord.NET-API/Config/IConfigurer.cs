using ColorChord.NET.API.Outputs;
using ColorChord.NET.API.Visualizers;

namespace ColorChord.NET.API.Config;

public interface IConfigurer
{
    /// <summary>Sets all attribute-tagged fields and properties in the given object from the config values provided.</summary>
    /// <remarks>If a value is not set in the config file, or is set to an invalid value, or is set out of range (where applicable), the default value specified in the attribute is used instead. All attribute-tagged fields and properties are therefore set to reasonable values after this method returns true.</remarks>
    /// <param name="targetObj">The object to configure. Must implement <see cref="IConfigurableAttr"/>.</param>
    /// <param name="config">The set of config values to apply.</param>
    /// <param name="warnAboutRemainder">Whether to output a warning to the log if any unused configuration options remain.</param>
    /// <returns>Whether applying the configuration values succeeded.</returns>
    /// <exception cref="InvalidOperationException">If the field or property type is not compatible with the attribute used.</exception>
    /// <exception cref="NotImplementedException">If the attribute is one that is not yet supported.</exception>
    public bool Configure(object targetObj, Dictionary<string, object> config, bool warnAboutRemainder = true);

    /// <summary>Used by outputs. Reads the config, and finds the visualizer instance that this output should attach to.</summary>
    /// <param name="target">The output that will attach to the visualizer.</param>
    /// <param name="config">The config entries which will be used in finding the appropriate visualizer.</param>
    /// <returns>The visualizer that this output should attach to.</returns>
    public IVisualizer? FindVisualizer(IOutput target, Dictionary<string, object> config);

    /// <summary>Used by outputs. Reads the config, and finds the visualizer instance that this output should attach to.</summary>
    /// <param name="target">The output that will attach to the visualizer.</param>
    /// <param name="config">The config entries which will be used in finding the appropriate visualizer.</param>
    /// <param name="acceptableFormat">The <see cref="IVisualizerFormat"/> type that is accepted by this output.</param>
    /// <returns>The visualizer that this output should attach to.</returns>
    public IVisualizer? FindVisualizer(IOutput target, Dictionary<string, object> config, Type acceptableFormat);
}
