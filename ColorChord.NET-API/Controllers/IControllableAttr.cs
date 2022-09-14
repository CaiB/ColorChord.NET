using System;

namespace ColorChord.NET.API.Controllers;

/// <summary>This must be implemented by all classes that are controllable using the <see cref="ControllableAttribute">Controllable</see> attributes to mark them as such.</summary>
public interface IControllableAttr
{
    /// <summary>Called when a controller changes the value of a setting that requires a callback.</summary>
    /// <param name="controlID">The ID defined in the <see cref="ControllableAttribute">Controllable</see> attribute params</param>
    public void SettingChanged(int controlID);

    /// <summary>Called when a controller is about to change the value of a setting that requires a callback.</summary>
    /// <param name="controlID">The ID defined in the <see cref="ControllableAttribute">Controllable</see> attribute params</param>
    public void SettingWillChange(int controlID);
}

/// <summary>This is implemented by classes that are controllable, but which are unable to use the <see cref="ControllableAttribute">Controllable</see> attributes.</summary>
[Obsolete("Not yet implemented")]
public interface ICustomControllableAttr
{
    /// <summary>Used by controllers to retrieve a list of controllable settings.</summary>
    /// <returns>A list of tuples containing the sub-name of the settings, as well as their types</returns>
    public Tuple<string, Type>[] GetSettings();

    /// <summary>Used by controllers to get the current value of a setting by its name.</summary>
    /// <param name="settingName">The name of the setting to retrieve the value of</param>
    /// <returns>The current value of the given setting, or null if the setting was not found</returns>
    public object? GetSettingValue(string settingName);

    /// <summary>Used by controllers to change the given setting.</summary>
    /// <param name="settingName">The name of the setting to change (use <see cref="GetSettings"/> to retrieve a list of settings)</param>
    /// <param name="newValue">The new value to set</param>
    /// <returns>Whether the setting change was successful</returns>
    public bool SetSettingValue(string settingName, object newValue);
}