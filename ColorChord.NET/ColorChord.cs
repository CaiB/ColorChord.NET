using ColorChord.NET.Config;
using ColorChord.NET.Outputs;
using ColorChord.NET.Sources;
using ColorChord.NET.Visualizers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace ColorChord.NET
{
    public class ColorChord
    {
        private static string ConfigFile = "config.json";
        public static bool Debug = false;

        public static readonly Dictionary<string, IVisualizer> VisualizerInsts = new Dictionary<string, IVisualizer>();
        public static readonly Dictionary<string, IOutput> OutputInsts = new Dictionary<string, IOutput>();
        //public static Dictionary<string, IController> Controllers;
        public static IAudioSource Source;

        public static void Main(string[] args)
        {
            if (args != null && args.Length > 0)
            {
                if (args[0].Equals("config", StringComparison.InvariantCultureIgnoreCase) && args.Length >= 2) { ConfigFile = args[1]; }
            }
            BaseNoteFinder.Start();

            if (!File.Exists(ConfigFile)) // No config file
            {
                Log.Warn("Could not find config file. Creating and using default.");
                WriteDefaultConfig();
            }

            ReadConfig();
        }

        private static void WriteDefaultConfig()
        {
            try
            {
                Assembly Asm = Assembly.GetExecutingAssembly();
                using (Stream InStream = Asm.GetManifestResourceStream("ColorChord.NET.sample-config.json"))
                {
                    using (FileStream OutStream = File.Create(ConfigFile))
                    {
                        InStream.Seek(0, SeekOrigin.Begin);
                        InStream.CopyTo(OutStream);
                    }
                }
            }
            catch
            {
                Log.Error("Failed to create default config file.");
                throw; // The program cannot execute without configuration.
            }
        }

        public static void ReadConfig()
        {
            JObject JSON;
            using (StreamReader Reader = File.OpenText(ConfigFile)) { JSON = JObject.Parse(Reader.ReadToEnd()); }
            Log.Info("Reading and applying configuration file \"" + ConfigFile + "\"...");

            // Audio Source
            if (!JSON.ContainsKey("Source") || !JSON["Source"].HasValues) { Log.Warn("Could not find valid \"Source\" definition. No audio source will be configured."); }
            else
            {
                IAudioSource Source = CreateObjectAttr<IAudioSource>("ColorChord.NET.Sources." + (string)JSON["Source"]["Type"], JSON["Source"]);
                if (Source != null)
                {
                    ColorChord.Source = Source;
                    Log.Info("Created audio source of type \"" + Source.GetType().FullName + "\".");
                    ColorChord.Source.Start();
                }
                else { Log.Error("Failed to create audio source. Check to make sure the type is spelled correctly."); }
            }

            // Note Finder
            if (!JSON.ContainsKey("NoteFinder")) { Log.Warn("Could not find valid \"NoteFinder\" definition. All defaults will be used."); }
            else
            {
                BaseNoteFinder.ApplyConfig(ToDict(JSON["NoteFinder"]));
            }

            // Visualizers
            if (!JSON.ContainsKey("Visualizers") || !JSON["Visualizers"].HasValues || ((JArray)JSON["Visualizers"]).Count <= 0) { Log.Warn("Could not find valid \"Visualizers\" definition. No visualizers will be configured."); }
            else
            {
                foreach (JToken Entry in (JArray)JSON["Visualizers"])
                {
                    IVisualizer Vis = CreateObject<IVisualizer>("ColorChord.NET.Visualizers." + (string)Entry["Type"], Entry);
                    Vis.Start();
                    VisualizerInsts.Add((string)Entry["Name"], Vis);
                }
            }

            // Outputs
            if (!JSON.ContainsKey("Outputs") || !JSON["Outputs"].HasValues || ((JArray)JSON["Visualizers"]).Count <= 0) { Log.Warn("Could not find valid \"Outputs\" definition. No outputs will be configured."); }
            else
            {
                foreach (JToken Entry in (JArray)JSON["Outputs"])
                {
                    IOutput Out = CreateObject<IOutput>("ColorChord.NET.Outputs." + (string)Entry["Type"], Entry, true);
                    Out.Start();
                    OutputInsts.Add((string)Entry["Name"], Out);
                }
            }
            Log.Info("Finished processing config file.");
        }

        /// <summary> Creates a new instance of the specified type, and checks to make sure it implements the given interface. </summary>
        /// <typeparam name="InterfaceType"> The interface that the resulting object must implement. </typeparam>
        /// <param name="fullName"> The full name (namespace + type) of the object to create. </param>
        /// <param name="configEntry"> The config entry containing options that should be applied to the resulting object. </param>
        /// <returns> A configured, ready object, or null if it was not able to be made. </returns>
        private static InterfaceType CreateObject<InterfaceType>(string fullName, JToken configEntry, bool complexConfig = false) where InterfaceType: IConfigurable
        {
            Type ObjType = Type.GetType(fullName);
            if (!typeof(InterfaceType).IsAssignableFrom(ObjType)) { return default; } // Does not implement the right interface.
            object Instance = ObjType == null ? null : Activator.CreateInstance(ObjType, (string)configEntry["Name"]);
            if (Instance != null)
            {
                InterfaceType Instance2 = (InterfaceType)Instance;
                
                Instance2.ApplyConfig(ToDict(configEntry, complexConfig));
                return Instance2;
            }
            return default;
        }

        /// <summary> Creates a new instance of the specified type, and checks to make sure it implements the given interface. </summary>
        /// <typeparam name="InterfaceType"> The interface that the resulting object must implement. </typeparam>
        /// <param name="fullName"> The full name (namespace + type) of the object to create. </param>
        /// <param name="configEntry"> The config entry containing options that should be applied to the resulting object. </param>
        /// <returns> A configured, ready object, or null if it was not able to be made. </returns>
        private static InterfaceType CreateObjectAttr<InterfaceType>(string fullName, JToken configEntry, bool complexConfig = false) where InterfaceType : IConfigurableAttr // TODO: Replace original
        {
            Type ObjType = Type.GetType(fullName);
            if (ObjType == null || !typeof(InterfaceType).IsAssignableFrom(ObjType)) { return default; } // Type doesn't exist, or does not implement the right interface.
            object Instance = Activator.CreateInstance(ObjType, ToDict(configEntry, complexConfig));
            return (InterfaceType)Instance;
        }

        /// <summary> Takes a JSON token, and converts all single-valued children into a Dictionary. </summary>
        /// <param name="parent"> The JSON token whose child elements should be converted. </param>
        /// <param name="convertComplex"> Whether to also convert arrays and objects, or to just convert single value items. </param>
        /// <returns> A Dictionary containing all properties contained in the parent element. </returns>
        private static Dictionary<string, object> ToDict(JToken parent, bool convertComplex = false)
        {
            Dictionary<string, object> Items = new Dictionary<string, object>();
            foreach(JToken ItemToken in parent.Children())
            {
                JProperty Item = (JProperty)ItemToken;
                if (Item.Value is JArray && convertComplex) // TODO: See if Object needs to be handled.
                {
                    JArray Array = (JArray)Item.Value;
                    Dictionary<string, object>[] ArrayItems = new Dictionary<string, object>[Array.Count];
                    for (int i = 0; i < ArrayItems.Length; i++)
                    {
                        ArrayItems[i] = ToDict(Array[i], true);
                    }
                    Items.Add(Item.Name, ArrayItems);
                }
                else
                {
                    Items.Add(Item.Name, Item.Value.ToString());
                }
            }
            return Items;
        }

    }
}
