namespace ColorChord.NET.API.Config;

/// <summary>Use the names contained in this class in config options when applicable.</summary>
/// <remarks>Using these helps prevent different classes having different config names for the same options.</remarks>
public static class ConfigNames
{
    public const string TYPE = "Type";
    public const string NAME = "Name";
    public const string ENABLE = "Enable";
    public const string TARGET = "Target";
    public const string LED_COUNT = "LEDCount";
}
