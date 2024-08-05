using ColorChord.NET.API.Controllers;
using ColorChord.NET.API.NoteFinder;
using ColorChord.NET.API.Outputs;
using ColorChord.NET.API.Sources;
using ColorChord.NET.API.Visualizers;
using ColorChord.NET.API.Visualizers.Formats;

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

    /// <summary>Reads the given config and finds the corresponding loaded <see cref="IAudioSource"/> instance.</summary>
    /// <remarks>Intended to be used by <see cref="NoteFinderCommon"/> instances to find their audio source to attach to.</remarks>
    /// <param name="config">The config section of a component which needs to find an audio source, the <see cref="ConfigNames.SOURCE_NAME"/> key will be used to find it by name. If the key is missing and there is only one audio source present, it is returned.</param>
    /// <returns>The audio source instance if it was found, null otherwise.</returns>
    public IAudioSource? FindSource(Dictionary<string, object> config);

    /// <summary>Reads the given config and finds the corresponding loaded <see cref="NoteFinderCommon"/> instance.</summary>
    /// <remarks>Intended to be used by <see cref="IVisualizer"/> instances to find their NoteFinder to attach to.</remarks>
    /// <param name="config">The config section of a component which needs to find a NoteFinder, the <see cref="ConfigNames.NOTE_FINDER_NAME"/> key will be used to find it by name. If the key is missing and there is only one NoteFinder present, it is returned.</param>
    /// <returns>The NoteFinder instance if it was found, null otherwise.</returns>
    public NoteFinderCommon? FindNoteFinder(Dictionary<string, object> config);

    /// <summary>Reads the given config and finds the corresponding loaded <see cref="IVisualizer"/> instance.</summary>
    /// <remarks>Intended to be used by <see cref="IOutput"/> instances to find their visualizer to attach to.</remarks>
    /// <param name="config">The config section of a component which needs to find a visualizer, the <see cref="ConfigNames.VIZ_NAME"/> key will be used to find it by name.</param>
    /// <returns>The visualizer instance if it was found, null otherwise.</returns>
    public IVisualizer? FindVisualizer(Dictionary<string, object> config);

    /// <summary>Used by outputs. Reads the config, and finds the visualizer instance that this output should attach to.</summary>
    /// <param name="target">The output that will attach to the visualizer.</param>
    /// <param name="config">The config entries which will be used in finding the appropriate visualizer.</param>
    /// <param name="acceptableFormat">The <see cref="IVisualizerFormat"/> type that is accepted by this output.</param>
    /// <returns>The visualizer that this output should attach to if it was found, null otherwise.</returns>
    public IVisualizer? FindVisualizer(IOutput target, Dictionary<string, object> config, Type acceptableFormat);

    /// <summary>Reads the given config and finds the corresponding loaded <see cref="IOutput"/> instance.</summary>
    /// <param name="config">The config section of a component which needs to find an output, the <see cref="ConfigNames.OUTPUT_NAME"/> key will be used to find it by name.</param>
    /// <returns>The output instance if it was found, null otherwise.</returns>
    public IOutput? FindOutput(Dictionary<string, object> config);

    /// <summary>Reads the given config and finds the corresponding loaded <see cref="Controller"/> instance.</summary>
    /// <param name="config">The config section of a component which needs to find a controller, the <see cref="ConfigNames.CONTROLLER_NAME"/> key will be used to find it by name.</param>
    /// <returns>The controller instance if it was found, null otherwise.</returns>
    public Controller? FindController(Dictionary<string, object> config);

    /// <summary>Finds a specific component by its name. More specific methods of this interface should be preferred in most cases.</summary>
    /// <param name="componentType">The type of component to find</param>
    /// <param name="componentName">The "Name" parameter of that component to search for, ignored in the case of <see cref="Component.Source"/> or <see cref="Component.NoteFinder"/> if there is only 1 loaded of that type.</param>
    /// <returns>THe component if found, null otherwise</returns>
    public object? FindComponentByName(Component componentType, string componentName);
}
