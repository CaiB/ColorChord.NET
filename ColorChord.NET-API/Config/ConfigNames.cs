namespace ColorChord.NET.API.Config;

/// <summary>Use the names contained in this class in config options when applicable.</summary>
/// <remarks>Using these helps prevent different classes having different config names for the same options.</remarks>
public static class ConfigNames
{
    public const string SECTION_SOURCE = "Source";
    public const string SECTION_SOURCE_ARR = "Sources";
    public const string SECTION_NOTE_FINDER = "NoteFinder";
    public const string SECTION_NOTE_FINDER_ARR = "NoteFinders";
    public const string SECTION_VISUALIZER = "Visualizer";
    public const string SECTION_VISUALIZER_ARR = "Visualizers";
    public const string SECTION_OUTPUT = "Output";
    public const string SECTION_OUTPUT_ARR = "Outputs";
    public const string SECTION_CONTROLLER = "Controller";
    public const string SECTION_CONTROLLER_ARR = "Controllers";

    public const string TYPE = "Type";
    public const string NAME = "Name";
    public const string ENABLE = "Enable";
    public const string TARGET = "Target";
    public const string LED_COUNT = "LEDCount";

    /// <summary>Used as the key for when a component needs to know the name of a <see cref="ColorChord.NET.API.Sources.IAudioSource"/> to reference.</summary>
    public const string SOURCE_NAME = "SourceName";
    /// <summary>Used as the key for when a component needs to know the name of a <see cref="ColorChord.NET.API.NoteFinder.NoteFinderCommon"/> to reference.</summary>
    public const string NOTE_FINDER_NAME = "NoteFinderName";
    /// <summary>Used as the key for when a component needs to know the name of a <see cref="ColorChord.NET.API.Outputs.IOutput"/> to reference.</summary>
    public const string OUTPUT_NAME = "OutputName";
    /// <summary>Used as the key for when a component needs to know the name of a <see cref="ColorChord.NET.API.Visualizers.IVisualizer"/> to reference.</summary>
    public const string VIZ_NAME = "VisualizerName";
    /// <summary>Used as the key for when a component needs to know the name of a <see cref="ColorChord.NET.API.Controllers.Controller"/> to reference.</summary>
    public const string CONTROLLER_NAME = "ControllerName";
}
