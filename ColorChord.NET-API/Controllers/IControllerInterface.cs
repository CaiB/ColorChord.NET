namespace ColorChord.NET.API.Controllers;

public interface IControllerInterface
{
    /// <summary>Attempts to find the setting by the given path and verifies the target allows it to be controllable.</summary>
    /// <param name="settingPath">The full setting path of the (ComponentType).(Name/Type).(Setting) format</param>
    /// <returns>An object used to get and set the value of the given setting, or null if it was not found or non-controllable</returns>
    public ISetting? FindSetting(string settingPath);

    /// <summary>Gets the current value of the given setting.</summary>
    /// <param name="setting">The setting to retrieve the current value of, obtained from <see cref="FindSetting(string)"/></param>
    /// <returns>The current value</returns>
    public object GetSettingValue(ISetting setting);

    /// <summary>Sets the setting to a new value.</summary>
    /// <param name="setting">The setting to change, obtained from <see cref="FindSetting(string)"/></param>
    /// <param name="newSetting">The new value to set</param>
    /// <returns>Whether the setting change was successful</returns>
    public bool SetSettingValue(ISetting setting, object newSetting);
}
