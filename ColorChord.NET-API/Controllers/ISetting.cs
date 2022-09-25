namespace ColorChord.NET.API.Controllers;

/// <summary>Used by controllers when manipulating other component's settings.</summary>
/// <remarks>Do not implement this interface in your extension, the only accepted implementation is the ColorChord.NET internal one.</remarks>
public interface ISetting
{
    /// <summary>The type of component that this setting is configuring.</summary>
    public Component ComponentType { get; }

    /// <summary>The unique instance name of the component this setting is configuring.</summary>
    public string ComponentName { get; }

    /// <summary>The name of the option in the instance this setting is configuring, as defined in the  <see cref="ControllableAttribute">Controllable</see> attribute.</summary>
    public string SettingName { get; }

    /// <summary>The underlying type that this setting is configuring.</summary>
    public SettingType DataType { get; }

    /// <summary>Gets the full path of this setting.</summary>
    public string SettingPath { get; }

    /// <summary>Checks whether this setting is valid and configurable.</summary>
    public bool IsValid();

    /// <summary>Defines the available parts of the ColorChord.NET system.</summary>
    public enum Component : byte
    {
        None = 0,
        Source = 1,
        NoteFinder = 2,
        Visualizers = 3,
        Outputs = 4,
        Controllers = 5
    }

    /// <summary>Defines the classes of types of data that are configurable and controllable.</summary>
    public enum SettingType : byte
    {
        None = 0,
        IntegralNumber = 1,
        DecimalNumber = 2,
        String = 3,
        Bool = 4
    }
}
