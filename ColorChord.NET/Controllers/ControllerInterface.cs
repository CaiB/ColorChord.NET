using ColorChord.NET.API.Controllers;
using System;
using static ColorChord.NET.API.Controllers.ISetting;

namespace ColorChord.NET.Controllers;

public class ControllerInterface : IControllerInterface
{
    private static ControllerInterface? P_Instance;
    public static ControllerInterface Instance
    {
        get
        {
            P_Instance ??= new();
            return P_Instance;
        }
    }

    public ISetting? FindSetting(string settingPath)
    {
        ControllerSetting Setting = ParsePath(settingPath);
        return Setting.IsValid() ? Setting : null;
    }

    public object? GetSettingValue(ISetting setting)
    {
        if (!setting.IsValid()) { throw new InvalidOperationException("The provided setting object is not valid."); }
        if (setting is not ControllerSetting CtrlSetting) { throw new InvalidOperationException("The provided setting object is not a recognized type. Do not attempt to implement ISetting."); }
        return CtrlSetting.GetValue();
    }

    public bool SetSettingValue(ISetting setting, object newValue)
    {
        if (!setting.IsValid()) { throw new InvalidOperationException("The provided setting object is not valid."); }
        if (setting is not ControllerSetting CtrlSetting) { throw new InvalidOperationException("The provided setting object is not a recognized type. Do not attempt to implement ISetting."); }
        return CtrlSetting.SetValue(newValue);
    }

    public void ToggleSettingValue(ISetting setting) // bool only
    {
        if (!setting.IsValid()) { throw new InvalidOperationException("The provided setting object is not valid."); }
        if (setting is not ControllerSetting CtrlSetting) { throw new InvalidOperationException("The provided setting object is not a recognized type. Do not attempt to implement ISetting."); }
        CtrlSetting.Toggle();
    }

    //TODO: Enumerate all settings for UIs

    private static ControllerSetting ParsePath(string settingPath)
    {
        int IndexSep1 = settingPath.IndexOf('.');
        if (IndexSep1 < 0) { return new(); }
        ReadOnlySpan<char> ComponentType = settingPath.AsSpan(0, IndexSep1);
        Component Component = Component.None;
        if (Enum.TryParse(ComponentType, true, out Component ParsedComponent)) { Component = ParsedComponent; }

        int IndexSep2 = settingPath.IndexOf('.', IndexSep1 + 1);
        if (IndexSep2 < 0) { return new(); }
        string ComponentName = settingPath.Substring(IndexSep1 + 1, IndexSep2 - IndexSep1 - 1);

        if (IndexSep2 == settingPath.Length - 1) { return new(); }
        string SettingName = settingPath.Substring(IndexSep2 + 1);
        return new(Component, ComponentName, SettingName);
    }
}