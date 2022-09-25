using System;

namespace ColorChord.NET.API.Controllers;

public interface IControllerInterface
{
    /// <summary>Attempts to find the setting by the given path and verifies the target allows it to be controllable.</summary>
    /// <param name="settingPath">The full setting path of the (ComponentType).(Name/Type).(Setting) format</param>
    /// <returns>An object used to get and set the value of the given setting, or null if it was not found or non-controllable</returns>
    public ISetting? FindSetting(string settingPath);

    /// <summary>Finds all of the available settings of all controllable components.</summary>
    /// <remarks>This can be an expensive operation, so it is recommended that you store and reuse the results.</remarks>
    /// <returns><see cref="ISetting"/> items which are controllable settings of active components. Can be empty.</returns>
    public List<ISetting> EnumerateSettings();

    /// <summary>Gets the current value of the given setting.</summary>
    /// <param name="setting">The setting to retrieve the current value of, obtained from <see cref="FindSetting(string)"/></param>
    /// <returns>The current value, or null if retrieval failed</returns>
    public object? GetSettingValue(ISetting setting);

    /// <summary>Used on numerical target settings that have a specified range of valid values to get the minimum value.</summary>
    /// <param name="setting">The setting to retrieve the minimum value of, obtained from <see cref="FindSetting(string)"/></param>
    /// <returns>
    ///     If the setting is of type <see cref="ISetting.SettingType.IntegralNumber"/>, either a <see cref="long"/> of the minimum value if it is defined, or null if it is not defined.
    ///     If the setting is of type <see cref="ISetting.SettingType.DecimalNumber">, either a <see cref="float"/> of the minimum value if it is defined, or null if it is not defined.
    ///     If the setting is any other type, null.
    /// </returns>
    public object? GetMinimumSettingValue(ISetting setting);

    /// <summary>Used on numerical target settings that have a specified range of valid values to get the maximum value.</summary>
    /// <param name="setting">The setting to retrieve the maximum value of, obtained from <see cref="FindSetting(string)"/></param>
    /// <returns>
    ///     If the setting is of type <see cref="ISetting.SettingType.IntegralNumber"/>, either a <see cref="long"/> of the maximum value if it is defined, or null if it is not defined.
    ///     If the setting is of type <see cref="ISetting.SettingType.DecimalNumber">, either a <see cref="float"/> of the maximum value if it is defined, or null if it is not defined.
    ///     If the setting is any other type, null.
    /// </returns>
    public object? GetMaximumSettingValue(ISetting setting);

    /// <summary>Sets the setting to a new value.</summary>
    /// <param name="setting">The setting to change, obtained from <see cref="FindSetting(string)"/></param>
    /// <param name="newValue">The new value to set</param>
    /// <returns>Whether the setting change was successful. Will return false if the new setting is outside of the configurable range</returns>
    public bool SetSettingValue(ISetting setting, object newValue);

    /// <summary>Toggles a boolean setting to the opposite value of what it is currently set to.</summary>
    /// <remarks>This only works for boolean values, use one of the other overloads for other types of settings.</remarks>
    /// <param name="setting">The setting to toggle. <see cref="ISetting.DataType"/> must be <see cref="ISetting.SettingType.Bool"/></param>
    public void ToggleSettingValue(ISetting setting);
}
