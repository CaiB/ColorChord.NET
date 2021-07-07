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
using static ColorChord.NET.Config.ConfigNames;

namespace ColorChord.NET
{
    public class ColorChord
    {
        private static string ConfigFile = "config.json";
        public static bool Debug = false;

        public static readonly Dictionary<string, IVisualizer> VisualizerInsts = new Dictionary<string, IVisualizer>();
        public static readonly Dictionary<string, IOutput> OutputInsts = new Dictionary<string, IOutput>();
        //public static Dictionary<string, IController> Controllers;
        public static IAudioSource? Source;

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
                using (Stream? InStream = Asm.GetManifestResourceStream("ColorChord.NET.sample-config.json"))
                {
                    if (InStream == null) { throw new InvalidOperationException("Cannot read sample config file"); }
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
            IAudioSource? Source = ReadSource(JSON);
            if(Source == null)
            {
                Log.Error("An error occurred while setting up the audio source. Please see above.");
                throw new InvalidDataException("Could not create audio source from config values");
            }
            ColorChord.Source = Source;
            ColorChord.Source.Start();

            // Note Finder
            ReadAndApplyNoteFinder(JSON);

            // Visualizers
            ReadAndApplyVisualizers(JSON);

            // Outputs
            ReadAndApplyOutputs(JSON);

            // Controllers
            // Not yet implemented...
            Log.Info("Finished processing config file.");
        }

        private static IAudioSource? ReadSource(JObject JSON)
        {
            const string SOURCE = "Source";
            if (!JSON.ContainsKey(SOURCE) || JSON[SOURCE] == null || !JSON[SOURCE]!.HasValues) { Log.Error($"Could not find valid \"{SOURCE}\" definition. ColorChord.NET cannot work without audio."); }
            else
            {
                JToken SourceToken = JSON[SOURCE]!;
                string? SourceType = SourceToken.Value<string?>(TYPE);
                if (SourceType == null) { Log.Error($"{SOURCE} section in config did not contain valid \"{TYPE}\" definition"); return null; }
                
                IAudioSource? Source = CreateObject<IAudioSource>("ColorChord.NET.Sources." + SourceType, SourceToken);
                if (Source == null) { Log.Error($"Failed to create audio source. Check to make sure the type \"{SourceType}\" is spelled correctly."); return null; }
                Log.Info("Created audio source of type \"" + Source.GetType().FullName + "\".");
                return Source;
            }
            return null;
        }

        private static void ReadAndApplyNoteFinder(JObject JSON)
        {
            // TODO: allow swapping of note finders, requiring instantiation
            const string NOTEFINDER = "NoteFinder";
            if (!JSON.ContainsKey(NOTEFINDER) || JSON[NOTEFINDER] == null) { Log.Warn($"Could not find valid \"{NOTEFINDER}\" definition. All defaults will be used."); }
            else { BaseNoteFinder.ApplyConfig(ToDict(JSON[NOTEFINDER]!)); }
        }

        private static void ReadAndApplyVisualizers(JObject JSON)
        {
            const string VISUALIZERS = "Visualizers";
            if (!JSON.ContainsKey(VISUALIZERS) || JSON[VISUALIZERS] == null || !JSON[VISUALIZERS]!.HasValues) { Log.Warn($"Could not find valid \"{VISUALIZERS}\" definition. No visualizers will be configured."); return; }
            if (!typeof(JArray).IsAssignableFrom(JSON[VISUALIZERS]!.GetType())) { Log.Error($"{VISUALIZERS} definition needs to be an array, but it was not. No visualizers will load."); return; }
            JArray VisualizerEntries = (JArray)JSON[VISUALIZERS]!;
            if (VisualizerEntries.Count <= 0) { Log.Warn("No visualizers were defined in the config file. Check to make sure the formatting is correct."); }

            foreach (JToken Entry in VisualizerEntries)
            {
                string? VisType = Entry.Value<string>(TYPE);
                string? VisName = Entry.Value<string>(NAME);
                if (VisType == null) { Log.Error($"A visualizer is missing a \"{TYPE}\" definition, and will therefore not be initialized."); continue; }
                if (VisName == null) { Log.Error($"A visualizer is missing a \"{NAME}\" definition, and will therefore not be initialized."); continue; }

                IVisualizer? Vis = CreateObject<IVisualizer>("ColorChord.NET.Visualizers." + VisType, Entry);
                if (Vis == null) { Log.Error($"Could not create visualizer \"{VisName}\". Check to make sure the type \"{VisType}\" is spelled correctly."); continue; }
                Vis.Start();
                VisualizerInsts.Add(VisName, Vis);
            }
        }

        private static void ReadAndApplyOutputs(JObject JSON)
        {
            const string OUTPUTS = "Outputs";
            if (!JSON.ContainsKey(OUTPUTS) || JSON[OUTPUTS] == null || !JSON[OUTPUTS]!.HasValues) { Log.Warn($"Could not find valid \"{OUTPUTS}\" definition. No outputs will be configured."); return; }
            if (!typeof(JArray).IsAssignableFrom(JSON[OUTPUTS]!.GetType())) { Log.Error($"{OUTPUTS} definition needs to be an array, but it was not. No visualizers will load."); return; }
            JArray OutputEntries = (JArray)JSON[OUTPUTS]!;
            if (OutputEntries.Count <= 0) { Log.Warn("No outputs were defined in the config file. Check to make sure the formatting is correct."); }

            foreach (JToken Entry in OutputEntries)
            {
                string? OutType = Entry.Value<string>(TYPE);
                string? OutName = Entry.Value<string>(NAME);
                if (OutType == null) { Log.Error($"An output is missing a \"{TYPE}\" definition, and will therefore not be initialized."); continue; }
                if (OutName == null) { Log.Error($"An output is missing a \"{NAME}\" definition, and will therefore not be initialized."); continue; }

                IOutput? Out = CreateObject<IOutput>("ColorChord.NET.Outputs." + OutType, Entry, true);
                if (Out == null) { Log.Error($"Could not create output \"{OutName}\". Check to make sure the type \"{OutType}\" is spelled correctly."); continue; }
                Out.Start();
                OutputInsts.Add(OutName, Out);
            }
        }

        /// <summary> Creates a new instance of the specified type, and checks to make sure it implements the given interface. </summary>
        /// <typeparam name="InterfaceType"> The interface that the resulting object must implement. </typeparam>
        /// <param name="fullName"> The full name (namespace + type) of the object to create. </param>
        /// <param name="configEntry"> The config entry containing options that should be applied to the resulting object. </param>
        /// <returns> A configured, ready object, or null if it was not able to be made. </returns>
        private static InterfaceType? CreateObject<InterfaceType>(string fullName, JToken configEntry, bool complexConfig = false) where InterfaceType : IConfigurableAttr
        {
            Type? ObjType = Type.GetType(fullName);
            if (ObjType == null || !typeof(InterfaceType).IsAssignableFrom(ObjType)) { return default; } // Type doesn't exist, or does not implement the right interface.

            string? ObjName = configEntry.Value<string>(NAME);
            if (typeof(InterfaceType) == typeof(IAudioSource) /* || typeof(InterfaceType) == typeof(INoteFinder) */) { ObjName = "NoName"; }
            if (ObjName == null) { return default; }

            object? Instance = Activator.CreateInstance(ObjType, ObjName, ToDict(configEntry, complexConfig));
            return (InterfaceType?)Instance;
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
