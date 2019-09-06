using Newtonsoft.Json.Linq;
using System;

namespace ColorChord.NET
{
    public static class ConfigTools
    {

        public static float CheckFloat(JToken category, string name, float min, float max, float def, bool remove)
        {
            if(category != null && category[name] != null)
            {
                if(float.TryParse(category[name].ToString(), out float Value))
                {
                    //if (remove) { category[name].Remove(); }
                    if (Value > max || Value < min)
                    {
                        Console.WriteLine("[WARN] Value of \"" + name + "\" was out of range, defaulting to " + def);
                        return def;
                    }
                    return Value;
                }
            }
            return def;
        }

        public static int CheckInt(JToken category, string name, int min, int max, int def, bool remove)
        {
            if (category != null && category[name] != null)
            {
                if (int.TryParse(category[name].ToString(), out int Value))
                {
                    //if (remove) { category[name].Remove(); }
                    if (Value > max || Value < min)
                    {
                        Console.WriteLine("[WARN] Value of \"" + name + "\" was out of range, defaulting to " + def);
                        return def;
                    }
                    return Value;
                }
            }
            return def;
        }

        public static bool CheckBool(JToken category, string name, bool def, bool remove)
        {
            if (category != null && category[name] != null)
            {
                if (bool.TryParse(category[name].ToString(), out bool Value))
                {
                    //if (remove) { category[name].Remove(); }
                    return Value;
                }
            }
            return def;
        }

        public static void WarnAboutRemainder(JToken category)
        {
            foreach (JToken Item in category)
            {
                Console.WriteLine("[WARN] Unknown config entry found: \"" + Item.ToString() + "\"");
            }
        }

    }

    public interface IConfigurable
    {
        void ApplyConfig(JToken configSection);
    }
}
