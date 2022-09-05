using ColorChord.NET.API.Config;
using ColorChord.NET.API.Controllers;
using ColorChord.NET.API.Outputs;
using ColorChord.NET.API.Sources;
using ColorChord.NET.API.Visualizers;
using System;
using System.Reflection;
using static ColorChord.NET.API.Controllers.ISetting;

namespace ColorChord.NET.Controllers;

public class ControllerSetting : ISetting
{
    public Component ComponentType { get; private init; }
    public string ComponentName { get; private init; }
    public string SettingName { get; private init; }
    public SettingType DataType { get; private set; } = SettingType.None;

    private IControllableAttr? TargetInstance { get; set; }
    private bool InitializeAttempted { get; set; }
    private bool IsInitialized => IsProperty ? Property != null : Field != null;
    private bool IsProperty { get; set; }
    private FieldInfo? Field { get; set; }
    private PropertyInfo? Property { get; set; }

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

    public bool IsValid()
    {
        if (this.ComponentType == Component.None) { return false; }
        if (this.InitializeAttempted && !this.IsInitialized) { return false; }
        if (!this.InitializeAttempted) { return Initialize(); }
        return this.IsInitialized;
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
            if (ColorChord.Source?.GetType().Name == componentName && ColorChord.Source is IControllableAttr ControllableSource) { return ControllableSource; }
            else { return null; }
        }
        else if (componentType == Component.NoteFinder)
        {
            if (ColorChord.NoteFinder?.GetType().Name == componentName && ColorChord.NoteFinder is IControllableAttr ControllableNoteFinder) { return ControllableNoteFinder; }
            else { return null; }
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
                this.DataType = TypeToSettingType(Property.GetType());
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
                this.DataType = TypeToSettingType(Field.GetType());
                return true;
            }
        }
        return false;
    }

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
