using ColorChord.NET.API;
using ColorChord.NET.API.Config;
using ColorChord.NET.API.Outputs;
using ColorChord.NET.API.Visualizers;
using ColorChord.NET.API.Visualizers.Formats;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace ColorChord.NET.Config;

public sealed class ConfigurerInst : IConfigurer
{
    public static ConfigurerInst Inst { get; private set; } = new();

    public bool Configure(object targetObj, Dictionary<string, object> config, bool warnAboutRemainder = true) => Configurer.Configure(targetObj, config, warnAboutRemainder);
    public IVisualizer? FindVisualizer(IOutput target, Dictionary<string, object> config) => Configurer.FindVisualizer(target, config);
    public IVisualizer? FindVisualizer(IOutput target, Dictionary<string, object> config, Type acceptableFormat) => Configurer.FindVisualizer(target, config, acceptableFormat);
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
        Log.Info("Reading config for " + TargetType.Name + '.');

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
                if (TargetInst is IOutput && (Item == "VisualizerName" || Item == "Modes")) { continue; }
                Log.Warn($"Unknown config entry \"{Item}\" found while configuring {TargetType.FullName}.");
            }
        }

        return true;
    }

    /// <summary>Used by outputs. Reads the config, and finds the visualizer instance that this output should attach to.</summary>
    /// <param name="target">The output that will attach to the visualizer.</param>
    /// <param name="config">The config entries which will be used in finding the appropriate visualizer.</param>
    /// <returns>The visualizer that this output should attach to.</returns>
    public static IVisualizer? FindVisualizer(IOutput target, Dictionary<string, object> config)
    {
        const string VIZ_NAME = "VisualizerName";
        if (!config.ContainsKey(VIZ_NAME) || !ColorChord.VisualizerInsts.ContainsKey((string)config[VIZ_NAME]))
        {
            Log.Error("Tried to create " + target.GetType()?.Name + " with missing or invalid visualizer.");
            return null;
        }
        return ColorChord.VisualizerInsts[(string)config[VIZ_NAME]];
    }

    /// <summary>Used by outputs. Reads the config, and finds the visualizer instance that this output should attach to.</summary>
    /// <param name="target">The output that will attach to the visualizer.</param>
    /// <param name="config">The config entries which will be used in finding the appropriate visualizer.</param>
    /// <param name="acceptableFormat">The <see cref="IVisualizerFormat"/> type that is accepted by this output.</param>
    /// <returns>The visualizer that this output should attach to.</returns>
    public static IVisualizer? FindVisualizer(IOutput target, Dictionary<string, object> config, Type acceptableFormat)
    {
        IVisualizer? Visualizer = FindVisualizer(target, config);
        if (Visualizer == null) { return null; }
        if (!acceptableFormat.IsAssignableFrom(Visualizer.GetType())) { Log.Error($"{target.GetType()?.Name} only supports {acceptableFormat.Name} visualizers, cannot use {Visualizer.GetType()?.Name}"); }
        return Visualizer;
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
