using ColorChord.NET.Outputs;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace ColorChord.NET.Config
{
    public static class Configurer
    {
        public static bool Configure(object targetObj, Dictionary<string, object> config)
        {
            if (targetObj is not IConfigurableAttr Target)
            {
                Log.Warn("Tried to configure non-configurable object " + targetObj);
                return false;
            }

            FieldInfo[] Fields = Target.GetType().GetFields();
            PropertyInfo[] Properties = Target.GetType().GetProperties();

            foreach(FieldInfo Field in Fields)
            {
                Attribute Attr = Attribute.GetCustomAttribute(Field, typeof(ConfigAttribute));
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
                    else { throw new Exception($"Field {IntAttr.Name} in {Target.GetType().FullName} used ConfigInt but is not a numeric type."); }
                    
                    config.Remove(IntAttr.Name);
                }
                else if(Attr is ConfigStringAttribute StrAttr)
                {
                    string Value = CheckString(config, StrAttr);

                    if (Field.FieldType == typeof(string)) { Field.SetValue(targetObj, Value); }
                    else { throw new Exception($"Field {StrAttr.Name} in {Target.GetType().FullName} used ConfigString but is not a string type."); }

                    config.Remove(StrAttr.Name);
                }
                else if(Attr is ConfigBoolAttribute BoolAttr)
                {
                    bool Value = CheckBool(config, BoolAttr);

                    if (Field.FieldType == typeof(bool)) { Field.SetValue(targetObj, Value); }
                    else { throw new Exception($"Field {BoolAttr.Name} in {Target.GetType().FullName} used ConfigBool but is not a bool type."); }

                    config.Remove(BoolAttr.Name);
                }
                else if(Attr is ConfigFloatAttribute FltAttr)
                {
                    float Value = CheckFloat(config, FltAttr);

                    if      (Field.FieldType == typeof(float))   { Field.SetValue(targetObj, Value); }
                    else if (Field.FieldType == typeof(double))  { Field.SetValue(targetObj, (double)Value); }
                    else if (Field.FieldType == typeof(decimal)) { Field.SetValue(targetObj, (decimal)Value); }
                    else { throw new Exception($"Field {FltAttr.Name} in {Target.GetType().FullName} used ConfigFloat but is not a float type."); }

                    config.Remove(FltAttr.Name);
                }
                else { throw new Exception("Unsupported config type encountered: " + Attr.GetType().FullName); }
            }

            foreach(PropertyInfo Prop in Properties)
            {
                Attribute Attr = Attribute.GetCustomAttribute(Prop, typeof(ConfigAttribute));
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
                    else { throw new Exception($"Property {IntAttr.Name} in {Target.GetType().FullName} used ConfigInt but is not a numeric type."); }

                    config.Remove(IntAttr.Name);
                }
                else if (Attr is ConfigStringAttribute StrAttr)
                {
                    string Value = CheckString(config, StrAttr);

                    if (Prop.PropertyType == typeof(string)) { Prop.SetValue(targetObj, Value); }
                    else { throw new Exception($"Property {StrAttr.Name} in {Target.GetType().FullName} used ConfigString but is not a string type."); }

                    config.Remove(StrAttr.Name);
                }
                else if (Attr is ConfigBoolAttribute BoolAttr)
                {
                    bool Value = CheckBool(config, BoolAttr);

                    if (Prop.PropertyType == typeof(bool)) { Prop.SetValue(targetObj, Value); }
                    else { throw new Exception($"Field {BoolAttr.Name} in {Target.GetType().FullName} used ConfigBool but is not a bool type."); }

                    config.Remove(BoolAttr.Name);
                }
                else if (Attr is ConfigFloatAttribute FltAttr)
                {
                    float Value = CheckFloat(config, FltAttr);

                    if      (Prop.PropertyType == typeof(float))   { Prop.SetValue(targetObj, Value); }
                    else if (Prop.PropertyType == typeof(double))  { Prop.SetValue(targetObj, (double)Value); }
                    else if (Prop.PropertyType == typeof(decimal)) { Prop.SetValue(targetObj, (decimal)Value); }
                    else { throw new Exception($"Field {FltAttr.Name} in {Target.GetType().FullName} used ConfigFloat but is not a float type."); }

                    config.Remove(FltAttr.Name);
                }
                else { throw new Exception("Unsupported config type encountered: " + Attr.GetType().FullName); }
            }

            // Warn about any remaining items
            foreach (string Item in config.Keys)
            {
                if (Item == "Type" || Item == "Name") { continue; }
                if (Target is IOutput && (Item == "VisualizerName" || Item == "Modes")) { continue; }
                Log.Warn($"Unknown config entry \"{Item}\" found while configuring {Target.GetType().FullName}.");
            }

            return true;
        }

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

        private static string CheckString(Dictionary<string, object> config, ConfigStringAttribute strAttr)
        {
            return config.ContainsKey(strAttr.Name) ? config[strAttr.Name].ToString() : strAttr.DefaultValue;
        }
    }
}
