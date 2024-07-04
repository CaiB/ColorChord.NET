using ColorChord.NET.API;
using ColorChord.NET.API.Controllers;
using ColorChord.NET.API.NoteFinder;
using ColorChord.NET.API.Outputs;
using ColorChord.NET.API.Sources;
using ColorChord.NET.API.Visualizers;
using System;
using System.Collections.Generic;
using System.Reflection;

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

    public object? GetMinimumSettingValue(ISetting setting)
    {
        if (!setting.IsValid()) { throw new InvalidOperationException("The provided setting object is not valid."); }
        if (setting is not ControllerSetting CtrlSetting) { throw new InvalidOperationException("The provided setting object is not a recognized type. Do not attempt to implement ISetting."); }
        return CtrlSetting.GetMinimumValue();
    }

    public object? GetMaximumSettingValue(ISetting setting)
    {
        if (!setting.IsValid()) { throw new InvalidOperationException("The provided setting object is not valid."); }
        if (setting is not ControllerSetting CtrlSetting) { throw new InvalidOperationException("The provided setting object is not a recognized type. Do not attempt to implement ISetting."); }
        return CtrlSetting.GetMaximumValue();
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

    public List<ISetting> EnumerateSettings()
    {
        List<ISetting> Results = new();

        foreach (IAudioSource Source in ColorChord.SourceInsts.Values)
        {
            if (Source is IControllableAttr ControllableSource) { AddComponentSettingsToList(ControllableSource, Results, Component.Source, Source.Name); }
        }
        foreach (NoteFinderCommon NoteFinder in ColorChord.NoteFinderInsts.Values)
        {
            if (NoteFinder is IControllableAttr ControllableNoteFinder) { AddComponentSettingsToList(ControllableNoteFinder, Results, Component.NoteFinder, NoteFinder.Name); }
        }
        foreach (IVisualizer Visualizer in ColorChord.VisualizerInsts.Values)
        {
            if (Visualizer is IControllableAttr ControllableVisualizer) { AddComponentSettingsToList(ControllableVisualizer, Results, Component.Visualizers, Visualizer.Name); }
        }
        foreach (IOutput Output in ColorChord.OutputInsts.Values)
        {
            if (Output is IControllableAttr ControllableOutput) { AddComponentSettingsToList(ControllableOutput, Results, Component.Outputs, Output.Name); }
        }
        foreach (Controller Controller in ColorChord.ControllerInsts.Values)
        {
            if (Controller is IControllableAttr ControllableController) { AddComponentSettingsToList(ControllableController, Results, Component.Controllers, Controller.Name); }
        }
        // TODO: Add any special internal functions here

        return Results;
    }

    private void AddComponentSettingsToList(IControllableAttr component, List<ISetting> targetList, Component componentType, string instanceName)
    {
        Type InstType = component.GetType();
        foreach (PropertyInfo Property in InstType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
        {
            ControllableAttribute? Controllable = Property.GetCustomAttribute<ControllableAttribute>();
            if (Controllable != null)
            {
                targetList.Add(new ControllerSetting(component, componentType, instanceName, Controllable.ControlName, Property));
            }
        }
        foreach (FieldInfo Field in InstType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
        {
            ControllableAttribute? Controllable = Field.GetCustomAttribute<ControllableAttribute>();
            if (Controllable != null)
            {
                targetList.Add(new ControllerSetting(component, componentType, instanceName, Controllable.ControlName, Field));
            }
        }
    }

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