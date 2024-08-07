﻿using ColorChord.NET.API;
using ColorChord.NET.API.Config;
using ColorChord.NET.API.Controllers;
using ColorChord.NET.API.NoteFinder;
using ColorChord.NET.API.Outputs;
using ColorChord.NET.API.Sources;
using ColorChord.NET.API.Visualizers;
using System;
using System.Linq;
using System.Reflection;
using static ColorChord.NET.API.Controllers.ISetting;

namespace ColorChord.NET.Controllers;

public class ControllerSetting : ISetting
{
    public Component ComponentType { get; private init; }
    public string ComponentName { get; private init; }
    public string SettingName { get; private init; }
    public SettingType DataType { get; private set; } = SettingType.None;
    public string SettingPath { get => $"{this.ComponentType}.{this.ComponentName}.{this.SettingName}"; } // TODO: This cannot handle custom paths yet.

    private IControllableAttr? TargetInstance { get; set; }
    private int ControlID { get; set; }
    private bool InitializeAttempted { get; set; }
    private bool IsInitialized => IsProperty ? Property != null : Field != null;
    private bool IsProperty { get; set; }
    private FieldInfo? Field { get; set; }
    private PropertyInfo? Property { get; set; }
    private ConfigAttribute? ConfigAttribute { get; set; }

    public ControllerSetting()
    {
        this.ComponentType = Component.None;
        this.ComponentName = "";
        this.SettingName = "";
    }

    public ControllerSetting(Component component, string componentName, string settingName)
    {
        this.ComponentType = component;
        this.ComponentName = componentName;
        this.SettingName = settingName;
    }

    internal ControllerSetting(IControllableAttr instance, Component componentType, string componentName, string settingName, MemberInfo targetMember)
    {
        this.ComponentType = componentType;
        this.ComponentName = componentName;
        this.TargetInstance = instance;
        this.InitializeAttempted = true;

        if (targetMember is PropertyInfo PropInfo)
        {
            this.IsProperty = true;
            this.Property = PropInfo;
            this.DataType = TypeToSettingType(PropInfo.PropertyType);
        }
        else if (targetMember is FieldInfo FieldInfo)
        {
            this.IsProperty = false;
            this.Field = FieldInfo;
            this.DataType = TypeToSettingType(FieldInfo.FieldType);

        }
        else { throw new InvalidOperationException($"Cannot initialize {nameof(ControllerSetting)} with a target member that is neither a field nor a property."); }

        ControllableAttribute? AttrControl = targetMember.GetCustomAttribute<ControllableAttribute>();
        ConfigAttribute? AttrConfig = targetMember.GetCustomAttribute<ConfigAttribute>();
        if (AttrConfig == null || AttrControl == null) { throw new InvalidOperationException($"Cannot initialize {nameof(ControllerSetting)} with a target that doesn't have both configurable and controllable attributes."); }

        this.SettingName = AttrControl.ControlName;
        this.ControlID = AttrControl.ControlID;
        this.ConfigAttribute = AttrConfig;
    }

    public bool IsValid()
    {
        if (this.ComponentType == Component.None) { return false; }
        if (this.InitializeAttempted && !this.IsInitialized) { return false; }
        if (!this.InitializeAttempted) { return Initialize(); }
        return this.IsInitialized;
    }

    internal object? GetValue() => this.IsProperty ? this.Property!.GetValue(this.TargetInstance) : this.Field!.GetValue(this.TargetInstance); // Assumes IsValid

    internal bool SetValue(object newValue) // Assumes IsValid
    {
        if (!CheckNewValue(newValue)) { return false; }

        if (ControllableAttribute.CheckNeedsCallback(this.ControlID)) { this.TargetInstance!.SettingWillChange(this.ControlID); }

        if (this.IsProperty) { this.Property!.SetValue(this.TargetInstance, newValue); }
        else { this.Field!.SetValue(this.TargetInstance, newValue); }

        if (ControllableAttribute.CheckNeedsCallback(this.ControlID)) { this.TargetInstance!.SettingChanged(this.ControlID); }

        return true;
    }

    internal void Toggle() // Assumes IsValid
    {
        if (this.DataType != SettingType.Bool) { throw new InvalidOperationException("Cannot use toggle method on a non-bool setting."); }
        if (this.IsProperty)
        {
            bool CurrentState = (bool?)GetValue() ?? throw new InvalidOperationException("Cannot use toggle method on a non-bool setting.");
            this.Property!.SetValue(this.TargetInstance, !CurrentState);
        }
        else
        {
            bool CurrentState = (bool?)GetValue() ?? throw new InvalidOperationException("Cannot use toggle method on a non-bool setting.");
            this.Field!.SetValue(this.TargetInstance, !CurrentState);
        }
        if (ControllableAttribute.CheckNeedsCallback(this.ControlID)) { this.TargetInstance!.SettingChanged(this.ControlID); }
    }

    internal object? GetMinimumValue() // Assumes IsValid
    {
        if (this.ConfigAttribute is ConfigIntAttribute ConfigInt) { return ConfigInt.MinValue; }
        else if (this.ConfigAttribute is ConfigFloatAttribute ConfigFloat) { return ConfigFloat.MinValue; }
        return null;
    }

    internal object? GetMaximumValue() // Assumes IsValid
    {
        if (this.ConfigAttribute is ConfigIntAttribute ConfigInt) { return ConfigInt.MaxValue; }
        else if (this.ConfigAttribute is ConfigFloatAttribute ConfigFloat) { return ConfigFloat.MaxValue; }
        return null;
    }

    private bool Initialize()
    {
        this.InitializeAttempted = true;
        this.TargetInstance = FindTargetInst(this.ComponentType, this.ComponentName);
        if (this.TargetInstance == null) { return false; }
        if (!FindTargetSetting(this.TargetInstance, this.SettingName)) { return false; }
        return IsInitialized;
    }

    private static IControllableAttr? FindTargetInst(Component componentType, string componentName)
    {
        if (componentType == Component.Source)
        {
            IAudioSource? Source;
            if (ColorChord.SourceInsts.Count == 1) { Source = ColorChord.SourceInsts.First().Value; }
            else if (!ColorChord.SourceInsts.TryGetValue(componentName, out Source)) { return null; }

            if (Source != null && Source is IControllableAttr ControllableSource) { return ControllableSource; }
            return null;
        }
        else if (componentType == Component.NoteFinder)
        {
            NoteFinderCommon? NoteFinder;
            if (ColorChord.NoteFinderInsts.Count == 1) { NoteFinder = ColorChord.NoteFinderInsts.First().Value; }
            else if (!ColorChord.NoteFinderInsts.TryGetValue(componentName, out NoteFinder)) { return null; }

            if (NoteFinder != null && NoteFinder is IControllableAttr ControllableNoteFinder) { return ControllableNoteFinder; }
            return null;
        }
        else if (componentType == Component.Visualizers)
        {
            if (!ColorChord.VisualizerInsts.TryGetValue(componentName, out IVisualizer? Visualizer)) { return null; }
            if (Visualizer != null && Visualizer is IControllableAttr ControllableVisualizer) { return ControllableVisualizer; }
            return null;
        }
        else if (componentType == Component.Outputs)
        {
            if (!ColorChord.OutputInsts.TryGetValue(componentName, out IOutput? Output)) { return null; }
            if (Output != null && Output is IControllableAttr ControllableOutput) { return ControllableOutput; }
            return null;
        }
        else if (componentType == Component.Controllers)
        {
            if (!ColorChord.ControllerInsts.TryGetValue(componentName, out Controller? Controller)) { return null; }
            if (Controller != null && Controller is IControllableAttr ControllableController) { return ControllableController; }
            return null;
        }
        return null;
    }

    private bool FindTargetSetting(IControllableAttr target, string controlName)
    {
        Type TargetType = target.GetType();
        
        PropertyInfo[] Properties = TargetType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
        foreach (PropertyInfo Property in Properties)
        {
            Attribute? Attr = Attribute.GetCustomAttribute(Property, typeof(ControllableAttribute));
            if (Attr is null) { continue; }
            if (Attr is ControllableAttribute ControlAttr && ControlAttr.ControlName == controlName)
            {
                this.IsProperty = true;
                this.Property = Property;
                this.DataType = TypeToSettingType(Property.PropertyType);
                this.ControlID = ControlAttr.ControlID;
                this.ConfigAttribute = Property.GetCustomAttribute<ConfigAttribute>();
                return true;
            }
        }

        FieldInfo[] Fields = TargetType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
        foreach (FieldInfo Field in Fields)
        {
            Attribute? Attr = Attribute.GetCustomAttribute(Field, typeof(ControllableAttribute));
            if (Attr is null) { continue; }
            if (Attr is ControllableAttribute ControlAttr && ControlAttr.ControlName == controlName)
            {
                this.IsProperty = false;
                this.Field = Field;
                this.DataType = TypeToSettingType(Field.FieldType);
                this.ControlID = ControlAttr.ControlID;
                this.ConfigAttribute = Field.GetCustomAttribute<ConfigAttribute>();
                return true;
            }
        }
        return false;
    }

    private bool CheckNewValue(object newValue)
    {
        if (this.ConfigAttribute is ConfigIntAttribute ConfigInt)
        {
            if (newValue is not long NewValue) { throw new InvalidOperationException("Cannot configure an integral numeric setting with a value that is not castable to a long."); }
            if (NewValue > ConfigInt.MaxValue || NewValue < ConfigInt.MinValue) { return false; }
        }
        else if (this.ConfigAttribute is ConfigFloatAttribute ConfigFloat)
        {
            if (newValue is not double NewValue) { throw new InvalidOperationException("Cannot configure a decimal numeric setting with a value that is not castable to a double."); }
            if (NewValue > ConfigFloat.MaxValue || NewValue < ConfigFloat.MinValue) { return false; }
        }

        if (this.DataType == SettingType.IntegralNumber)
        {
            if (newValue is not long) { throw new InvalidOperationException("Cannot configure an integral numeric setting with a value that is not castable to a long."); }
        }
        else if (this.DataType == SettingType.DecimalNumber)
        {
            if (newValue is not double) { throw new InvalidOperationException("Cannot configure a decimal numeric setting with a value that is not castable to a double."); }
        }
        else if (this.DataType == SettingType.Bool)
        {
            if (newValue is not bool) { throw new InvalidOperationException("Cannot configure a bool setting with a value that is not castable to a bool."); }
        }
        else if (this.DataType == SettingType.String)
        {
            if (newValue is not string) { throw new InvalidOperationException("Cannot configure a string setting with a value that is not castable to a string."); }
        }
        return true;
    } // Assumes IsValid

    private static SettingType TypeToSettingType(Type type)
    {
        switch (Type.GetTypeCode(type))
        {
            case TypeCode.Byte:
            case TypeCode.SByte:
            case TypeCode.UInt16:
            case TypeCode.Int16:
            case TypeCode.UInt32:
            case TypeCode.Int32:
            case TypeCode.UInt64:
            case TypeCode.Int64: return SettingType.IntegralNumber;

            case TypeCode.Single:
            case TypeCode.Double:
            case TypeCode.Decimal: return SettingType.DecimalNumber;

            case TypeCode.String: return SettingType.String;

            case TypeCode.Boolean: return SettingType.Bool;

            default: return SettingType.None;
        }
    }
}
