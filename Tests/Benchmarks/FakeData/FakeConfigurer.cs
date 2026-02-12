using ColorChord.NET.API;
using ColorChord.NET.API.Config;
using ColorChord.NET.API.Controllers;
using ColorChord.NET.API.NoteFinder;
using ColorChord.NET.API.Outputs;
using ColorChord.NET.API.Sources;
using ColorChord.NET.API.Visualizers;
using ColorChord.NET.Config;

namespace ColorChord.NET.Tests.Benchmarks.FakeData;

public class FakeConfigurer : IConfigurer
{
    public static readonly Dictionary<string, IAudioSource> SourceInsts = new();
    public static readonly Dictionary<string, NoteFinderCommon> NoteFinderInsts = new();
    public static readonly Dictionary<string, IVisualizer> VisualizerInsts = new();
    public static readonly Dictionary<string, IOutput> OutputInsts = new();
    public static readonly Dictionary<string, Controller> ControllerInsts = new();

    public bool Configure(object targetObj, Dictionary<string, object> config, bool warnAboutRemainder = true) => Configurer.Configure(targetObj, config, true); 

    public object? FindComponentByName(Component componentType, string componentName)
    {
        return componentType switch
        {
            Component.Source => SourceInsts[componentName],
            Component.NoteFinder => NoteFinderInsts[componentName],
            Component.Visualizers => VisualizerInsts[componentName],
            Component.Outputs => OutputInsts[componentName],
            Component.Controllers => ControllerInsts[componentName],
            _ => null
        };
    }

    public IAudioSource? FindSource(Dictionary<string, object> config) => SourceInsts[(string)config[ConfigNames.SOURCE_NAME]];
    public NoteFinderCommon? FindNoteFinder(Dictionary<string, object> config) => NoteFinderInsts[(string)config[ConfigNames.NOTE_FINDER_NAME]];
    public IVisualizer? FindVisualizer(Dictionary<string, object> config) => VisualizerInsts[(string)config[ConfigNames.VIZ_NAME]];
    public IOutput? FindOutput(Dictionary<string, object> config) => OutputInsts[(string)config[ConfigNames.OUTPUT_NAME]];
    public Controller? FindController(Dictionary<string, object> config) => ControllerInsts[(string)config[ConfigNames.CONTROLLER_NAME]];

    public IVisualizer? FindVisualizer(IOutput target, Dictionary<string, object> config, Type acceptableFormat)
    {
        IVisualizer? Visualizer = FindVisualizer(config);
        if (Visualizer == null) { return null; }
        if (!acceptableFormat.IsAssignableFrom(Visualizer.GetType())) { throw new Exception($"{target.GetType()?.Name} only supports {acceptableFormat.Name} visualizers, cannot use {Visualizer.GetType()?.Name}"); }
        return Visualizer;
    }
}
