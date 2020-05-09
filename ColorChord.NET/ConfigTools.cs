using ColorChord.NET.Outputs;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace ColorChord.NET
{
    public static class ConfigTools
    {

        public static float CheckFloat(Dictionary<string, object> category, string name, float min, float max, float def, bool remove)
        {
            if(category != null && category.ContainsKey(name))
            {
                if (float.TryParse(category[name].ToString(), out float Value))
                {
                    if (remove) { category.Remove(name); }
                    if (Value > max || Value < min)
                    {
                        Log.Warn("Value of \"" + name + "\" was out of range, defaulting to " + def + " (expected " + min + " to " + max + ")");
                        return def;
                    }
                    return Value;
                }
                Log.Warn("Value of \"" + name + "\" was invalid, defaulting to " + def + " (expected float type)");
            }
            return def;
        }

        public static int CheckInt(Dictionary<string, object> category, string name, int min, int max, int def, bool remove)
        {
            if (category != null && category.ContainsKey(name))
            {
                if (int.TryParse(category[name].ToString(), out int Value))
                {
                    if (remove) { category.Remove(name); }
                    if (Value > max || Value < min)
                    {
                        Log.Warn("Value of \"" + name + "\" was out of range, defaulting to " + def + " (expected " + min + " to " + max + ")");
                        return def;
                    }
                    return Value;
                }
                Log.Warn("Value of \"" + name + "\" was invalid, defaulting to " + def + " (expected int type)");
            }
            return def;
        }

        public static bool CheckBool(Dictionary<string, object> category, string name, bool def, bool remove)
        {
            if (category != null && category.ContainsKey(name))
            {
                if (bool.TryParse(category[name].ToString(), out bool Value))
                {
                    if (remove) { category.Remove(name); }
                    return Value;
                }
                Log.Warn("Value of \"" + name + "\" was invalid, defaulting to " + def + " (expected bool type)");
            }
            return def;
        }

        public static string CheckString(Dictionary<string, object> category, string name, string def, bool remove)
        {
            if (category != null && category.ContainsKey(name))
            {
                if (!string.IsNullOrEmpty(category[name].ToString()))
                {
                    string Value = category[name].ToString();
                    if (remove) { category.Remove(name); }
                    return Value;
                }
                Log.Warn("Value of \"" + name + "\" was invalid, defaulting to " + def + " (expected string type)");
            }
            return def;
        }

        public static void WarnAboutRemainder(Dictionary<string, object> category, Type interfaceType)
        {
            foreach (string Item in category.Keys)
            {
                if (Item == "type" || Item == "name") { continue; }
                if (interfaceType == typeof(IOutput) && Item == "visualizerName") { continue; }
                Log.Warn("Unknown config entry found: \"" + Item + "\". Ignoring.");
            }
        }

    }

    public interface IConfigurable
    {
        void ApplyConfig(Dictionary<string, object> configSection);
    }
}
