using ColorChord.NET.API;
using ColorChord.NET.API.Config;
using ColorChord.NET.API.Controllers;
using ColorChord.NET.API.NoteFinder;
using ColorChord.NET.API.Outputs;
using ColorChord.NET.API.Sources;
using ColorChord.NET.API.Visualizers;
using ColorChord.NET.Controllers;
using ColorChord.NET.Extensions;
using ColorChord.NET.NoteFinder;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using static ColorChord.NET.API.Config.ConfigNames;

namespace ColorChord.NET
{
    public class ColorChord
    {
        private static string ConfigFile = "config.json";

        public static IAudioSource? Source { get; private set; }
        public static NoteFinderCommon? NoteFinder { get; private set; }
        public static readonly Dictionary<string, IVisualizer> VisualizerInsts = new();
        public static readonly Dictionary<string, IOutput> OutputInsts = new();
        public static readonly Dictionary<string, Controller> ControllerInsts = new();

        public static void Main(string[] args)
        {
            for(int i = 0; i < args.Length; i++)
            {
                if (args[i] == "config" && args.Length > i + 1) { ConfigFile = args[++i]; }
                if (args[i] == "debug") { Log.EnableDebug = true; }
            }

            if (!File.Exists(ConfigFile)) // No config file
            {
                Log.Warn("Could not find config file. Creating and using default.");
                WriteDefaultConfig();
            }
            ExtensionHandler.LoadExtensions();
            ExtensionHandler.InitExtensions();
            ReadConfig();
            ExtensionHandler.PostInitExtensions();
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

            // Note Finder
            NoteFinderCommon? NoteFinder = ReadAndApplyNoteFinder(JSON);
            ColorChord.NoteFinder = NoteFinder;
            ColorChord.NoteFinder?.Start();

            // Audio Source
            IAudioSource? Source = ReadSource(JSON);
            if(Source == null)
            {
                Log.Error("An error occurred while setting up the audio source. Please see above.");
                throw new InvalidDataException("Could not create audio source from config values");
            }
            ColorChord.Source = Source;
            ColorChord.Source.Start();

            // Visualizers
            ReadAndApplyVisualizers(JSON);

            // Outputs
            ReadAndApplyOutputs(JSON);

            // Controllers
            ReadAndApplyControllers(JSON);
            Log.Info("Finished processing config file.");
        }

        public static void Stop()
        {
            Log.Info("Exiting...");
            ExtensionHandler.StopExtensions();
            Source?.Stop();
            NoteFinder?.Stop();
            foreach (IVisualizer Visualizer in VisualizerInsts.Values) { Visualizer.Stop(); }
            foreach (IOutput Output in OutputInsts.Values) { Output.Stop(); }
            foreach (Controller Controller in ControllerInsts.Values) { Controller.Stop(); }
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
                
                IAudioSource? Source = CreateObject<IAudioSource>(SourceType.StartsWith('#') ? SourceType : "ColorChord.NET.Sources." + SourceType, SourceToken);
                if (Source == null) { Log.Error($"Failed to create audio source. Check to make sure the type \"{SourceType}\" is spelled correctly. If it is part of an extension, make sure to start the name with a # character."); return null; }
                Log.Info("Created audio source of type \"" + Source.GetType().FullName + "\".");
                return Source;
            }
            return null;
        }

        private static NoteFinderCommon? ReadAndApplyNoteFinder(JObject JSON)
        {
            const string NOTEFINDER = "NoteFinder";
            Type DefaultType = typeof(BaseNoteFinder); // Defined once more below as well.
            NoteFinderCommon? NoteFinder;

            if (!JSON.ContainsKey(NOTEFINDER) || JSON[NOTEFINDER] == null) { Log.Warn($"Could not find valid \"{NOTEFINDER}\" definition. All defaults will be used."); }
            else
            {
                JToken NFToken = JSON[NOTEFINDER]!;
                string? NFType = NFToken.Value<string?>(TYPE);
                if (NFType == null)
                {
                    NFType = DefaultType.Name;
                    Log.Warn($"{NOTEFINDER} {TYPE} was not defined, using the default ({NFType}).");
                }

                NoteFinder = CreateObject<NoteFinderCommon>(NFType.StartsWith('#') ? NFType : "ColorChord.NET.NoteFinder." + NFType, NFToken);
                if (NoteFinder == null) { Log.Error($"Failed to create note finder. Check to make sure the type \"{NFType}\" is spelled correctly. If it is part of an extension, make sure to start the name with a # character."); return null; }
                Log.Info("Created audio source of type \"" + NoteFinder.GetType().FullName + "\".");
                return NoteFinder;
            }

            NoteFinder = new BaseNoteFinder(NOTEFINDER, new Dictionary<string, object>());
            Log.Info("Created audio source of type \"" + NoteFinder.GetType().FullName + "\".");
            return NoteFinder;
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

                IVisualizer? Vis = CreateObject<IVisualizer>(VisType.StartsWith('#') ? VisType : "ColorChord.NET.Visualizers." + VisType, Entry);
                if (Vis == null) { Log.Error($"Could not create visualizer \"{VisName}\". Check to make sure the type \"{VisType}\" is spelled correctly. If it is part of an extension, make sure to start the name with a # character."); continue; }
                Vis.Start();
                VisualizerInsts.Add(VisName, Vis);
            }
        }

        private static void ReadAndApplyOutputs(JObject JSON)
        {
            const string OUTPUTS = "Outputs";
            if (!JSON.ContainsKey(OUTPUTS) || JSON[OUTPUTS] == null || !JSON[OUTPUTS]!.HasValues) { Log.Warn($"Could not find valid \"{OUTPUTS}\" definition. No outputs will be configured."); return; }
            if (!typeof(JArray).IsAssignableFrom(JSON[OUTPUTS]!.GetType())) { Log.Error($"{OUTPUTS} definition needs to be an array, but it was not. No outputs will load."); return; }
            JArray OutputEntries = (JArray)JSON[OUTPUTS]!;
            if (OutputEntries.Count <= 0) { Log.Warn("No outputs were defined in the config file. Check to make sure the formatting is correct."); }

            foreach (JToken Entry in OutputEntries)
            {
                string? OutType = Entry.Value<string>(TYPE);
                string? OutName = Entry.Value<string>(NAME);
                if (OutType == null) { Log.Error($"An output is missing a \"{TYPE}\" definition, and will therefore not be initialized."); continue; }
                if (OutName == null) { Log.Error($"An output is missing a \"{NAME}\" definition, and will therefore not be initialized."); continue; }

                IOutput? Out = CreateObject<IOutput>(OutType.StartsWith('#') ? OutType : "ColorChord.NET.Outputs." + OutType, Entry, true);
                if (Out == null) { Log.Error($"Could not create output \"{OutName}\". Check to make sure the type \"{OutType}\" is spelled correctly. If it is part of an extension, make sure to start the name with a # character."); continue; }
                Out.Start();
                OutputInsts.Add(OutName, Out);
            }
        }

        private static void ReadAndApplyControllers(JObject JSON)
        {
            const string CONTROLLERS = "Controllers";
            if (!JSON.ContainsKey(CONTROLLERS) || JSON[CONTROLLERS] == null || !JSON[CONTROLLERS]!.HasValues) { Log.Warn($"Could not find valid \"{CONTROLLERS}\" definition. No controllers will be configured."); return; }
            if (!typeof(JArray).IsAssignableFrom(JSON[CONTROLLERS]!.GetType())) { Log.Error($"{CONTROLLERS} definition needs to be an array, but it was not. No controllers will load."); return; }
            JArray ControllerEntries = (JArray)JSON[CONTROLLERS]!;
            
            foreach (JToken Entry in ControllerEntries)
            {
                string? ControlType = Entry.Value<string>(TYPE);
                string? ControlName = Entry.Value<string>(NAME);
                if (ControlType == null) { Log.Error($"A controller is missing a \"{TYPE}\" definition, and will therefore not be initialized."); continue; }
                if (ControlName == null) { Log.Error($"A controller is missing a \"{NAME}\" definition, and will therefore not be initialized."); continue; }

                Controller? Ctrl = CreateObject<Controller>(ControlType.StartsWith('#') ? ControlType : "ColorChord.NET.Controllers." + ControlType, Entry, true, true);
                if (Ctrl == null) { Log.Error($"Could not create controller \"{ControlName}\". Check to make sure the type \"{ControlType}\" is spelled correctly. If it is part of an extension, make sure to start the name with a # character."); continue; }
                Ctrl.Start();
                ControllerInsts.Add(ControlName, Ctrl);
            }
        }

        /// <summary> Creates a new instance of the specified type, and checks to make sure it implements the given interface. </summary>
        /// <typeparam name="BaseType"> The interface that the resulting object must implement. </typeparam>
        /// <param name="fullName"> The full name (namespace + type) of the object to create. </param>
        /// <param name="configEntry"> The config entry containing options that should be applied to the resulting object. </param>
        /// <returns> A configured, ready object, or null if it was not able to be made. </returns>
        private static BaseType? CreateObject<BaseType>(string fullName, JToken configEntry, bool complexConfig = false, bool provideControllerInterface = false) where BaseType : IConfigurableAttr
        {
            Type? ObjType;
            if (fullName.StartsWith('#'))
            {
                fullName = fullName.Substring(1);
                ObjType = ExtensionHandler.FindType(fullName);
            }
            else { ObjType = Type.GetType(fullName); }
            if (ObjType == null || !typeof(BaseType).IsAssignableFrom(ObjType)) { return default; } // Type doesn't exist, or does not implement the right interface.

            string? ObjName = configEntry.Value<string>(NAME);
            if (typeof(BaseType) == typeof(IAudioSource) || typeof(BaseType) == typeof(NoteFinderCommon)) { ObjName = ObjType.Name; }
            if (ObjName == null) { return default; }

            object? Instance = 
            provideControllerInterface
                ? Activator.CreateInstance(ObjType, ObjName, ToDict(configEntry, complexConfig), ControllerInterface.Instance)
                : Activator.CreateInstance(ObjType, ObjName, ToDict(configEntry, complexConfig));
            return (BaseType?)Instance;
        }

        /// <summary> Takes a JSON token, and converts all single-valued children into a Dictionary. </summary>
        /// <param name="parent"> The JSON token whose child elements should be converted. </param>
        /// <param name="convertComplex"> Whether to also convert arrays and objects, or to just convert single value items. </param>
        /// <returns> A Dictionary containing all properties contained in the parent element. </returns>
        private static Dictionary<string, object> ToDict(JToken parent, bool convertComplex = false)
        {
            Dictionary<string, object> Items = new();
            foreach(JToken ItemToken in parent.Children())
            {
                JProperty Item = (JProperty)ItemToken;
                if (Item.Value is JArray Array && convertComplex) // TODO: See if Object needs to be handled.
                {
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
