using ColorChord.NET.API;
using ColorChord.NET.API.Config;
using ColorChord.NET.API.Controllers;
using ColorChord.NET.API.NoteFinder;
using ColorChord.NET.API.Outputs;
using ColorChord.NET.API.Sources;
using ColorChord.NET.API.Visualizers;
using ColorChord.NET.API.Visualizers.Formats;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using static ColorChord.NET.API.Config.ConfigNames;

namespace ColorChord.NET.Config;

public sealed class ConfigurerInst : IConfigurer
{
    public static ConfigurerInst Inst { get; private set; } = new();

    public bool Configure(object targetObj, Dictionary<string, object> config, bool warnAboutRemainder = true) => Configurer.Configure(targetObj, config, warnAboutRemainder);
    public IAudioSource? FindSource(Dictionary<string, object> config) => Configurer.FindSource(config);
    public NoteFinderCommon? FindNoteFinder(Dictionary<string, object> config) => Configurer.FindNoteFinder(config);
    public IVisualizer? FindVisualizer(Dictionary<string, object> config) => Configurer.FindVisualizer(config);
    public IVisualizer? FindVisualizer(IOutput target, Dictionary<string, object> config, Type acceptableFormat) => Configurer.FindVisualizer(target, config, acceptableFormat);
    public IOutput? FindOutput(Dictionary<string, object> config) => Configurer.FindOutput(config);
    public Controller? FindController(Dictionary<string, object> config) => Configurer.FindController(config);
    public object? FindComponentByName(Component componentType, string componentName) => Configurer.FindComponentByName(componentType, componentName);
}

public static class Configurer
{
    /// <summary>Sets all attribute-tagged fields and properties in the given object from the config values provided.</summary>
    /// <remarks>If a value is not set in the config file, or is set to an invalid value, or is set out of range (where applicable), the default value specified in the attribute is used instead. All attribute-tagged fields and properties are therefore set to reasonable values after this method returns true.</remarks>
    /// <param name="targetObj">The object to configure. Must implement <see cref="IConfigurableAttr"/>.</param>
    /// <param name="config">The set of config values to apply.</param>
    /// <param name="warnAboutRemainder">Whether to output a warning to the log if any unused configuration options remain.</param>
    /// <returns>Whether applying the configuration values succeeded.</returns>
    /// <exception cref="InvalidOperationException">If the field or property type is not compatible with the attribute used.</exception>
    /// <exception cref="NotImplementedException">If the attribute is one that is not yet supported.</exception>
    public static bool Configure(object targetObj, Dictionary<string, object> config, bool warnAboutRemainder = true)
    {
        Type? TargetType;
        IConfigurableAttr? TargetInst = null; // Only valid for instances (non-static classes)
        if (targetObj is Type) { TargetType = (Type?)targetObj; } // For configuring static classes
        else
        {
            TargetType = targetObj?.GetType();
            if (targetObj is not IConfigurableAttr Target)
            {
                Log.Warn("Tried to configure non-configurable object " + targetObj);
                return false;
            }
            TargetInst = Target;
        }
        if (TargetType == null) { Log.Error("Tried to configure object whose type cannot be determined."); return false; }
        if (!config.TryGetValue(TYPE, out object? TypeNameObj) || TypeNameObj is not string TypeName) { TypeName = TargetType.Name; }
        if (!config.TryGetValue(NAME, out object? InstNameObj) || InstNameObj is not string InstName) { InstName = string.Empty; }
        Log.Info($"Reading config for {TypeName} '{InstName}'");

        FieldInfo[] Fields = TargetType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
        PropertyInfo[] Properties = TargetType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);

        foreach(FieldInfo Field in Fields)
        {
            Attribute? Attr = Attribute.GetCustomAttribute(Field, typeof(ConfigAttribute));
            if(Attr is null) { continue; } // Field without attribute, ignore
            if(Attr is ConfigIntAttribute IntAttr)
            {
                long Value = CheckInt(config, IntAttr);

                if      (Field.FieldType == typeof(int))    { Field.SetValue(targetObj, (int)Value); }
                else if (Field.FieldType == typeof(uint))   { Field.SetValue(targetObj, (uint)Value); }
                else if (Field.FieldType == typeof(short))  { Field.SetValue(targetObj, (short)Value); }
                else if (Field.FieldType == typeof(ushort)) { Field.SetValue(targetObj, (ushort)Value); }
                else if (Field.FieldType == typeof(byte))   { Field.SetValue(targetObj, (byte)Value); }
                else if (Field.FieldType == typeof(sbyte))  { Field.SetValue(targetObj, (sbyte)Value); }
                else if (Field.FieldType == typeof(long))   { Field.SetValue(targetObj, (long)Value); }
                else if (Field.FieldType == typeof(ulong))  { Field.SetValue(targetObj, (ulong)Value); }
                else { throw new InvalidOperationException($"Field {IntAttr.Name} in {TargetType.FullName} used {nameof(ConfigIntAttribute)} but is not a numeric type."); }
                
                config.Remove(IntAttr.Name);
            }
            else if(Attr is ConfigStringAttribute StrAttr)
            {
                string Value = CheckString(config, StrAttr);

                if (Field.FieldType == typeof(string)) { Field.SetValue(targetObj, Value); }
                else { throw new InvalidOperationException($"Field {StrAttr.Name} in {TargetType.FullName} used {nameof(ConfigStringAttribute)} but is not a string type."); }

                config.Remove(StrAttr.Name);
            }
            else if(Attr is ConfigBoolAttribute BoolAttr)
            {
                bool Value = CheckBool(config, BoolAttr);

                if (Field.FieldType == typeof(bool)) { Field.SetValue(targetObj, Value); }
                else { throw new InvalidOperationException($"Field {BoolAttr.Name} in {TargetType.FullName} used {nameof(ConfigBoolAttribute)} but is not a bool type."); }

                config.Remove(BoolAttr.Name);
            }
            else if(Attr is ConfigFloatAttribute FltAttr)
            {
                float Value = CheckFloat(config, FltAttr);

                if      (Field.FieldType == typeof(float))   { Field.SetValue(targetObj, Value); }
                else if (Field.FieldType == typeof(double))  { Field.SetValue(targetObj, (double)Value); }
                else if (Field.FieldType == typeof(decimal)) { Field.SetValue(targetObj, (decimal)Value); }
                else { throw new InvalidOperationException($"Field {FltAttr.Name} in {TargetType.FullName} used {nameof(ConfigFloatAttribute)} but is not a float type."); }

                config.Remove(FltAttr.Name);
            }
            else if(Attr is ConfigStringListAttribute ListAttr)
            {
                List<string> Value = CheckStringList(config, ListAttr);

                if (Field.FieldType == typeof(List<string>)) { Field.SetValue(targetObj, Value); }
                else { throw new InvalidOperationException($"Field {ListAttr.Name} in {TargetType.FullName} used {nameof(ConfigStringListAttribute)} but is not a List<string> type."); }

                config.Remove(ListAttr.Name);
            }
            else { throw new NotImplementedException("Unsupported config type encountered: " + Attr?.GetType()?.FullName); }
        }

        foreach(PropertyInfo Prop in Properties)
        {
            Attribute? Attr = Attribute.GetCustomAttribute(Prop, typeof(ConfigAttribute));
            if (Attr is null) { continue; } // Property without attribute, ignore
            if (Attr is ConfigIntAttribute IntAttr)
            {
                long Value = CheckInt(config, IntAttr);

                if      (Prop.PropertyType == typeof(int))    { Prop.SetValue(targetObj, (int)Value); }
                else if (Prop.PropertyType == typeof(uint))   { Prop.SetValue(targetObj, (uint)Value); }
                else if (Prop.PropertyType == typeof(short))  { Prop.SetValue(targetObj, (short)Value); }
                else if (Prop.PropertyType == typeof(ushort)) { Prop.SetValue(targetObj, (ushort)Value); }
                else if (Prop.PropertyType == typeof(byte))   { Prop.SetValue(targetObj, (byte)Value); }
                else if (Prop.PropertyType == typeof(sbyte))  { Prop.SetValue(targetObj, (sbyte)Value); }
                else if (Prop.PropertyType == typeof(long))   { Prop.SetValue(targetObj, (long)Value); }
                else if (Prop.PropertyType == typeof(ulong))  { Prop.SetValue(targetObj, (ulong)Value); }
                else { throw new InvalidOperationException($"Property {IntAttr.Name} in {TargetType.FullName} used {nameof(ConfigIntAttribute)} but is not a numeric type."); }

                config.Remove(IntAttr.Name);
            }
            else if (Attr is ConfigStringAttribute StrAttr)
            {
                string Value = CheckString(config, StrAttr);

                if (Prop.PropertyType == typeof(string)) { Prop.SetValue(targetObj, Value); }
                else { throw new InvalidOperationException($"Property {StrAttr.Name} in {TargetType.FullName} used {nameof(ConfigStringAttribute)} but is not a string type."); }

                config.Remove(StrAttr.Name);
            }
            else if (Attr is ConfigBoolAttribute BoolAttr)
            {
                bool Value = CheckBool(config, BoolAttr);

                if (Prop.PropertyType == typeof(bool)) { Prop.SetValue(targetObj, Value); }
                else { throw new InvalidOperationException($"Field {BoolAttr.Name} in {TargetType.FullName} used {nameof(ConfigBoolAttribute)} but is not a bool type."); }

                config.Remove(BoolAttr.Name);
            }
            else if (Attr is ConfigFloatAttribute FltAttr)
            {
                float Value = CheckFloat(config, FltAttr);

                if      (Prop.PropertyType == typeof(float))   { Prop.SetValue(targetObj, Value); }
                else if (Prop.PropertyType == typeof(double))  { Prop.SetValue(targetObj, (double)Value); }
                else if (Prop.PropertyType == typeof(decimal)) { Prop.SetValue(targetObj, (decimal)Value); }
                else { throw new InvalidOperationException($"Field {FltAttr.Name} in {TargetType.FullName} used {nameof(ConfigFloatAttribute)} but is not a float type."); }

                config.Remove(FltAttr.Name);
            }
            else if (Attr is ConfigStringListAttribute ListAttr)
            {
                List<string> Value = CheckStringList(config, ListAttr);

                if (Prop.PropertyType == typeof(List<string>)) { Prop.SetValue(targetObj, Value); }
                else { throw new InvalidOperationException($"Property {ListAttr.Name} in {TargetType.FullName} used {nameof(ConfigStringListAttribute)} but is not a List<string> type."); }

                config.Remove(ListAttr.Name);
            }
            else { throw new NotImplementedException("Unsupported config type encountered: " + Attr.GetType().FullName); }
        }

        if (warnAboutRemainder) // Warn about any remaining items
        {
            foreach (string Item in config.Keys)
            {
                if (Item == "Type" || Item == "Name") { continue; }
                if (TargetInst is NoteFinderCommon && Item == "SourceName") { continue; }
                if (TargetInst is IVisualizer && Item == "NoteFinderName") { continue; }
                if (TargetInst is IOutput && (Item == "VisualizerName" || Item == "Modes")) { continue; }
                Log.Warn($"Unknown config entry \"{Item}\" found while configuring {TargetType.FullName}.");
            }
        }

        return true;
    }

    /// <summary>Reads the given config and finds the corresponding loaded <see cref="IAudioSource"/> instance.</summary>
    /// <remarks>Intended to be used by <see cref="NoteFinderCommon"/> instances to find their audio source to attach to.</remarks>
    /// <param name="config">The config section of a component which needs to find an audio source, the <see cref="SOURCE_NAME"/> key will be used to find it by name. If the key is missing and there is only one audio source present, it is returned.</param>
    /// <returns>The audio source instance if it was found, null otherwise.</returns>
    public static IAudioSource? FindSource(Dictionary<string, object> config)
    {
        if (config.TryGetValue(SOURCE_NAME, out object? SourceNameObj))
        {
            return (ColorChord.SourceInsts.TryGetValue((string)SourceNameObj, out IAudioSource? Source)) ? Source : null;
        }
        else if (ColorChord.SourceInsts.Count == 1) { return ColorChord.SourceInsts.First().Value; }
        return null;
    }

    /// <summary>Reads the given config and finds the corresponding loaded <see cref="NoteFinderCommon"/> instance.</summary>
    /// <remarks>Intended to be used by <see cref="IVisualizer"/> instances to find their NoteFinder to attach to.</remarks>
    /// <param name="config">The config section of a component which needs to find a NoteFinder, the <see cref="NOTE_FINDER_NAME"/> key will be used to find it by name. If the key is missing and there is only one NoteFinder present, it is returned.</param>
    /// <returns>The NoteFinder instance if it was found, null otherwise.</returns>
    public static NoteFinderCommon? FindNoteFinder(Dictionary<string, object> config)
    {
        if (config.TryGetValue(NOTE_FINDER_NAME, out object? NoteFinderNameObj))
        {
            return (ColorChord.NoteFinderInsts.TryGetValue((string)NoteFinderNameObj, out NoteFinderCommon? NoteFinder)) ? NoteFinder : null;
        }
        else if (ColorChord.NoteFinderInsts.Count == 1) { return ColorChord.NoteFinderInsts.First().Value; }
        return null;
    }

    /// <summary>Reads the given config and finds the corresponding loaded <see cref="IVisualizer"/> instance.</summary>
    /// <remarks>Intended to be used by <see cref="IOutput"/> instances to find their visualizer to attach to.</remarks>
    /// <param name="config">The config section of a component which needs to find a visualizer, the <see cref="VIZ_NAME"/> key will be used to find it by name.</param>
    /// <returns>The visualizer instance if it was found, null otherwise.</returns>
    public static IVisualizer? FindVisualizer(Dictionary<string, object> config)
    {
        if (!config.TryGetValue(VIZ_NAME, out object? VisualizerNameObj) || !ColorChord.VisualizerInsts.TryGetValue((string)VisualizerNameObj, out IVisualizer? Visualizer)) { return null; }
        return Visualizer;
    }

    /// <summary>Used by outputs. Reads the config, and finds the visualizer instance that this output should attach to.</summary>
    /// <param name="target">The output that will attach to the visualizer.</param>
    /// <param name="config">The config entries which will be used in finding the appropriate visualizer.</param>
    /// <param name="acceptableFormat">The <see cref="IVisualizerFormat"/> type that is accepted by this output.</param>
    /// <returns>The visualizer that this output should attach to if it was found, null otherwise.</returns>
    public static IVisualizer? FindVisualizer(IOutput target, Dictionary<string, object> config, Type acceptableFormat)
    {
        IVisualizer? Visualizer = FindVisualizer(config);
        if (Visualizer == null) { return null; }
        if (!acceptableFormat.IsAssignableFrom(Visualizer.GetType())) { Log.Error($"{target.GetType()?.Name} only supports {acceptableFormat.Name} visualizers, cannot use {Visualizer.GetType()?.Name}"); }
        return Visualizer;
    }

    /// <summary>Reads the given config and finds the corresponding loaded <see cref="IOutput"/> instance.</summary>
    /// <param name="config">The config section of a component which needs to find an output, the <see cref="OUTPUT_NAME"/> key will be used to find it by name.</param>
    /// <returns>The output instance if it was found, null otherwise.</returns>
    public static IOutput? FindOutput(Dictionary<string, object> config)
    {
        if (!config.TryGetValue(OUTPUT_NAME, out object? OutputNameObj) || !ColorChord.OutputInsts.TryGetValue((string)OutputNameObj, out IOutput? Output)) { return null; }
        return Output;
    }

    /// <summary>Reads the given config and finds the corresponding loaded <see cref="Controller"/> instance.</summary>
    /// <param name="config">The config section of a component which needs to find a controller, the <see cref="CONTROLLER_NAME"/> key will be used to find it by name.</param>
    /// <returns>The controller instance if it was found, null otherwise.</returns>
    public static Controller? FindController(Dictionary<string, object> config)
    {
        if (!config.TryGetValue(CONTROLLER_NAME, out object? ControllerNameObj) || !ColorChord.ControllerInsts.TryGetValue((string)ControllerNameObj, out Controller? Controller)) { return null; }
        return Controller;
    }

    /// <summary>Finds a specific component by its name. More specific methods of this interface should be preferred in most cases.</summary>
    /// <param name="componentType">The type of component to find</param>
    /// <param name="componentName">The "Name" parameter of that component to search for, ignored in the case of <see cref="Component.Source"/> or <see cref="Component.NoteFinder"/> if there is only 1 loaded of that type.</param>
    /// <returns>THe component if found, null otherwise</returns>
    public static object? FindComponentByName(Component componentType, string componentName)
    {
        if (componentType == Component.Source)
        {
            IAudioSource? Source;
            if (ColorChord.SourceInsts.Count == 1) { Source = ColorChord.SourceInsts.First().Value; }
            else { ColorChord.SourceInsts.TryGetValue(componentName, out Source); }
            return Source;
        }
        else if (componentType == Component.NoteFinder)
        {
            NoteFinderCommon? NoteFinder;
            if (ColorChord.NoteFinderInsts.Count == 1) { NoteFinder = ColorChord.NoteFinderInsts.First().Value; }
            else { ColorChord.NoteFinderInsts.TryGetValue(componentName, out NoteFinder); }
            return NoteFinder;
        }
        else if (componentType == Component.Visualizers)
        {
            ColorChord.VisualizerInsts.TryGetValue(componentName, out IVisualizer? Visualizer);
            return Visualizer;
        }
        else if (componentType == Component.Outputs)
        {
            ColorChord.OutputInsts.TryGetValue(componentName, out IOutput? Output);
            return Output;
        }
        else if (componentType == Component.Controllers)
        {
            ColorChord.ControllerInsts.TryGetValue(componentName, out Controller? Controller);
            return Controller;
        }
        return null;
    }

    /// <summary>Checks the config to see if a reasonable value is provided, otherwise uses the default and outputs a warning.</summary>
    /// <param name="config">The configuration to read from.</param>
    /// <param name="fltAttr">The attribute on the item to configure.</param>
    /// <returns>A float within the range specified.</returns>
    private static float CheckFloat(Dictionary<string, object> config, ConfigFloatAttribute fltAttr)
    {
        float Value = fltAttr.DefaultValue;
        if (config.ContainsKey(fltAttr.Name) && !float.TryParse(config[fltAttr.Name].ToString(), out Value))
        {
            Log.Warn($"Value of {fltAttr.Name} was invalid (expected float). Defaulting to {fltAttr.DefaultValue}.");
            Value = fltAttr.DefaultValue;
        }
        if (Value > fltAttr.MaxValue || Value < fltAttr.MinValue)
        {
            Log.Warn($"Value of {fltAttr.Name} was out of range ({fltAttr.MinValue} to {fltAttr.MaxValue}). Defaulting to {fltAttr.DefaultValue}.");
            Value = fltAttr.DefaultValue;
        }
        return Value;
    }

    /// <summary>Checks the config to see if a reasonable value is provided, otherwise uses the default and outputs a warning.</summary>
    /// <param name="config">The configuration to read from.</param>
    /// <param name="intAttr">The attribute on the item to configure.</param>
    /// <returns>A long within the range specified.</returns>
    private static long CheckInt(Dictionary<string, object> config, ConfigIntAttribute intAttr)
    {
        long Value = intAttr.DefaultValue;
        if (config.ContainsKey(intAttr.Name) && !long.TryParse(config[intAttr.Name].ToString(), out Value))
        {
            Log.Warn($"Value of {intAttr.Name} was invalid (expected integer). Defaulting to {intAttr.DefaultValue}.");
            Value = intAttr.DefaultValue;
        }
        if (Value > intAttr.MaxValue || Value < intAttr.MinValue)
        {
            Log.Warn($"Value of {intAttr.Name} was out of range ({intAttr.MinValue} to {intAttr.MaxValue}). Defaulting to {intAttr.DefaultValue}.");
            Value = intAttr.DefaultValue;
        }
        return Value;
    }

    /// <summary>Checks the config to see if a reasonable value is provided, otherwise uses the default and outputs a warning.</summary>
    /// <param name="config">The configuration to read from.</param>
    /// <param name="boolAttr">The attribute on the item to configure.</param>
    /// <returns>Either the configured value, or default.</returns>
    private static bool CheckBool(Dictionary<string, object> config, ConfigBoolAttribute boolAttr)
    {
        bool Value = boolAttr.DefaultValue;
        if (config.ContainsKey(boolAttr.Name) && !bool.TryParse(config[boolAttr.Name].ToString(), out Value))
        {
            Log.Warn($"Value of {boolAttr.Name} was invalid (expected integer). Defaulting to {boolAttr.DefaultValue}.");
            Value = boolAttr.DefaultValue;
        }
        return Value;
    }

    /// <summary>Checks the config to see if a value is provided, otherwise uses the default.</summary>
    /// <param name="config">The configuration to read from.</param>
    /// <param name="strAttr">The attribute on the item to configure.</param>
    /// <returns>Either the configured value, or the default. No validity checking is done.</returns>
    private static string CheckString(Dictionary<string, object> config, ConfigStringAttribute strAttr)
    {
        if(!config.ContainsKey(strAttr.Name)) { return strAttr.DefaultValue; }
        string? Output = config[strAttr.Name].ToString();
        return Output ?? strAttr.DefaultValue;
    }

    /// <summary>Checks the config to see if a value is provided and is a valid string list, otherwise returns an empty list.</summary>
    /// <param name="config">The configuration to read from.</param>
    /// <param name="strListAttr">The attribute on the item to configure.</param>
    /// <returns>Either the configured value, or an empty list. No validity checking is done on list items.</returns>
    private static List<string> CheckStringList(Dictionary<string, object> config, ConfigStringListAttribute strListAttr)
    {
        if (!config.ContainsKey(strListAttr.Name) || config[strListAttr.Name] is not string[] Value) { return new(); }
        return new(Value);
    }
}
