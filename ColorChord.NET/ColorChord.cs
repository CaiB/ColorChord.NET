using ColorChord.NET.Outputs;
using ColorChord.NET.Sources;
using ColorChord.NET.Visualizers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace ColorChord.NET
{
    public class ColorChord
    {
        private const string CONFIG_FILE = "config.json";
        public static bool Debug = false;

        public static void Main(string[] args)
        {
            NoteFinder.Start();

            if (!File.Exists(CONFIG_FILE)) // No config file
            {
                Log.Warn("Could not find config file. Creating and using default.");
                try
                {
                    Assembly Asm = Assembly.GetExecutingAssembly();
                    using (Stream InStream = Asm.GetManifestResourceStream("ColorChord.NET.sample-config.json"))
                    {
                        using (FileStream OutStream = File.Create(CONFIG_FILE))
                        {
                            InStream.Seek(0, SeekOrigin.Begin);
                            InStream.CopyTo(OutStream);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("Failed to create default config file.");
                    throw ex; // The program cannot execute without configuration.
                }
            }

            ReadConfig();
        }

        public static Dictionary<string, IVisualizer> VisualizerInsts;
        public static Dictionary<string, IOutput> OutputInsts;
        //public static Dictionary<string, IController> Controllers;
        public static IAudioSource Source;

        public static void ReadConfig()
        {
            VisualizerInsts = new Dictionary<string, IVisualizer>();
            OutputInsts = new Dictionary<string, IOutput>();
            // Controllers = new Dictionary<string IController>();
            JObject JSON;
            using (StreamReader Reader = File.OpenText(CONFIG_FILE)) { JSON = JObject.Parse(Reader.ReadToEnd()); }
            Log.Info("Reading and applying configuration file...");

            // Audio Source
            if (!JSON.ContainsKey("source") || !JSON["source"].HasValues) { Log.Warn("Could not find valid \"source\" definition. No audio source will be configured."); }
            else
            {
                IAudioSource Source = CreateObject<IAudioSource>("ColorChord.NET.Sources." + (string)JSON["source"]["type"], JSON["source"]);
                if (Source != null)
                {
                    ColorChord.Source = Source;
                    Log.Info("Created audio source of type \"" + Source.GetType().FullName + "\".");
                    ColorChord.Source.Start();
                }
                else { Log.Error("Failed to create audio source. Check to make sure the type is spelled correctly."); }
            }

            // Visualizers
            if (!JSON.ContainsKey("visualizers") || !JSON["visualizers"].HasValues || ((JArray)JSON["visualizers"]).Count <= 0) { Log.Warn("Could not find valid \"visualizers\" definition. No visualizers will be configured."); }
            else
            {
                foreach (JToken Entry in (JArray)JSON["visualizers"])
                {
                    IVisualizer Vis = CreateObject<IVisualizer>("ColorChord.NET.Visualizers." + (string)Entry["type"], Entry);
                    Vis.Start();
                    VisualizerInsts.Add((string)Entry["name"], Vis);
                }
            }

            // Outputs
            if (!JSON.ContainsKey("outputs") || !JSON["outputs"].HasValues || ((JArray)JSON["visualizers"]).Count <= 0) { Log.Warn("Could not find valid \"outputs\" definition. No outputs will be configured."); }
            else
            {
                foreach (JToken Entry in (JArray)JSON["outputs"])
                {
                    IOutput Out = CreateObject<IOutput>("ColorChord.NET.Outputs." + (string)Entry["type"], Entry);
                    Out.Start();
                    OutputInsts.Add((string)Entry["name"], Out);
                }
            }
            Log.Info("Finished processing config file.");
        }

        /// <summary> Creates a new instance of the specified type, and checks to make sure it implements the given interface. </summary>
        /// <typeparam name="InterfaceType"> The interface that the resulting object must implement. </typeparam>
        /// <param name="fullName"> The full name (namespace + type) of the object to create. </param>
        /// <param name="configEntry"> The config entry containing options that should be applied to the resulting object. </param>
        /// <returns> A configured, ready object, or null if it was not able to be made. </returns>
        private static InterfaceType CreateObject<InterfaceType>(string fullName, JToken configEntry) where InterfaceType: IConfigurable
        {
            Type ObjType = Type.GetType(fullName);
            if (!typeof(InterfaceType).IsAssignableFrom(ObjType)) { return default; } // Does not implement the right interface.
            object Instance = ObjType == null ? null : Activator.CreateInstance(ObjType, (string)configEntry["name"]);
            if (Instance != null)
            {
                InterfaceType Instance2 = (InterfaceType)Instance;
                Instance2.ApplyConfig(ToDict(configEntry));
                return Instance2;
            }
            return default;
        }

        /// <summary> Takes a JSON token, and converts all single-valued children into a Dictionary. Arrays and objects are ignored. </summary>
        /// <param name="parent"> The JSON token whose child elements should be converted. </param>
        /// <returns> A Dictionary containing all properties contained in the parent element. </returns>
        private static Dictionary<string, object> ToDict(JToken parent)
        {
            Dictionary<string, object> Items = new Dictionary<string, object>();
            foreach(JToken ItemToken in parent.Children())
            {
                JProperty Item = (JProperty)ItemToken;
                if (Item.Type == JTokenType.Array || Item.Type == JTokenType.Object) { continue; } // TODO: Consider supporting these if needed.
                Items.Add(Item.Name, Item.Value.ToString());
            }
            return Items;
        }

    }
}
