using ColorChord.NET.API;
using ColorChord.NET.API.Config;
using ColorChord.NET.API.Controllers;
using ColorChord.NET.API.NoteFinder;
using ColorChord.NET.API.Outputs;
using ColorChord.NET.API.Sources;
using ColorChord.NET.API.Visualizers;
using ColorChord.NET.Config;
using ColorChord.NET.Controllers;
using ColorChord.NET.Extensions;
using ColorChord.NET.NoteFinder;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using static ColorChord.NET.API.Config.ConfigNames;

namespace ColorChord.NET
{
    public static class ColorChord
    {
        private static string ConfigFile = "config.json";

        public static IAudioSource? Source { get; private set; }
        public static NoteFinderCommon? NoteFinder { get; private set; }
        public static readonly Dictionary<string, IVisualizer> VisualizerInsts = new();
        public static readonly Dictionary<string, IOutput> OutputInsts = new();
        public static readonly Dictionary<string, Controller> ControllerInsts = new();

        private static readonly List<Thread> InstanceThreads = new();
        private static ManualResetEventSlim StopSignal = new();

        public static void Main(string[] args)
        {
            for(int i = 0; i < args.Length; i++)
            {
                string ThisArg = args[i].ToLower();
                if (ThisArg == "--config" && args.Length > i + 1) { ConfigFile = args[++i]; }
                if (ThisArg == "--debug") { Log.EnableDebug = true; }
                if (ThisArg == "--help" || ThisArg == "-h" || ThisArg == "-?" || ThisArg == "/h" || ThisArg == "/help" || ThisArg == "/?") { WriteHelp(); Environment.Exit(0); }
            }

            if (!File.Exists(ConfigFile)) // No config file
            {
                Log.Warn("Could not find config file. Creating and using default.");
                WriteDefaultConfig();
            }

            ColorChordAPI.Configurer = ConfigurerInst.Inst;

            ExtensionHandler.LoadExtensions();
            ExtensionHandler.InitExtensions();
            ReadConfig();
            ExtensionHandler.PostInitExtensions();

            StopSignal.Wait();
            Log.Info("Exiting...");
            ExtensionHandler.StopExtensions();
            Source?.Stop();
            NoteFinder?.Stop();
            foreach (IVisualizer Visualizer in VisualizerInsts.Values) { Visualizer.Stop(); }
            foreach (IOutput Output in OutputInsts.Values) { Output.Stop(); }
            foreach (Controller Controller in ControllerInsts.Values) { Controller.Stop(); }
            foreach (Thread Thread in InstanceThreads) { Thread.Join(); }
        }

        private static void WriteHelp()
        {
            Console.WriteLine("--config <file>: Specifies the config file that ColorChord.NET should read");
            Console.WriteLine("--debug: Outputs additional debug information");
            Console.WriteLine("--help: Outputs usage information (this)");
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
            using (FileStream Reader = File.Open(ConfigFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                // Parse the config file to JSON
                using (StreamReader StringReader = new(Reader, leaveOpen: true))
                {
                    JSON = JObject.Parse(StringReader.ReadToEnd());
                    Log.Info("Reading and applying configuration file \"" + ConfigFile + "\"...");
                }

                // Check the MD5 of the config file against the default
                Reader.Seek(0, SeekOrigin.Begin);
                using (MD5 MD5 = MD5.Create())
                {
                    byte[] ConfigHash = MD5.ComputeHash(Reader);
                    if (ConfigHash.Length != DefaultConfigInfo.DefaultConfigFileMD5.Length / 2) { Log.Warn("Failed to check if the config file is default due to hashes not matching in length"); }
                    else
                    {
                        bool IsEqual = true;
                        for (int i = 0; i < ConfigHash.Length; i++)
                        {
                            byte DefaultHashHere = byte.Parse(DefaultConfigInfo.DefaultConfigFileMD5.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture); // TODO: Replace this with a ReadOnlySpan of bytes instead of parsing a string
                            if (DefaultHashHere != ConfigHash[i])
                            {
                                IsEqual = false;
                                break;
                            }
                        }
                        if (IsEqual) { Log.Warn($"It appears you are using the default config file, located at \"{Reader.Name}\". Make any changes as needed."); }
                    }
                }
            }

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

        public static void Stop() => StopSignal.Set();

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

            if (!JSON.ContainsKey(NOTEFINDER) || JSON[NOTEFINDER] == null)
            {
                Log.Warn($"Could not find valid \"{NOTEFINDER}\" definition. All defaults will be used.");
                NoteFinder = new BaseNoteFinder(NOTEFINDER, new Dictionary<string, object>());
                Log.Info("Created audio source of type \"" + NoteFinder.GetType().FullName + "\".");
                return NoteFinder;
            }
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

                IOutput? Out = CreateObject<IOutput>(OutType.StartsWith('#') ? OutType : "ColorChord.NET.Outputs." + OutType, Entry);
                if (Out == null) { Log.Error($"Could not create output \"{OutName}\". Check to make sure the type \"{OutType}\" is spelled correctly. If it is part of an extension, make sure to start the name with a # character."); continue; }
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

                Controller? Ctrl = CreateObject<Controller>(ControlType.StartsWith('#') ? ControlType : "ColorChord.NET.Controllers." + ControlType, Entry, true);
                if (Ctrl == null) { Log.Error($"Could not create controller \"{ControlName}\". Check to make sure the type \"{ControlType}\" is spelled correctly. If it is part of an extension, make sure to start the name with a # character."); continue; }
                ControllerInsts.Add(ControlName, Ctrl);
            }
        }

        /// <summary> Creates a new instance of the specified type, and checks to make sure it implements the given interface. </summary>
        /// <typeparam name="BaseType"> The interface that the resulting object must implement. </typeparam>
        /// <param name="fullName"> The full name (namespace + type) of the object to create. </param>
        /// <param name="configEntry"> The config entry containing options that should be applied to the resulting object. </param>
        /// <param name="provideControllerInterface">Whether the constructor needs to be provided with an <see cref="IControllerInterface"/></param>
        /// <returns> A configured, ready object, or null if it was not able to be made. </returns>
        private static BaseType? CreateObject<BaseType>(string fullName, JToken configEntry, bool provideControllerInterface = false) where BaseType : IConfigurableAttr
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

            object? Instance = null;
            if (ObjType.GetCustomAttribute<ThreadedInstanceAttribute>() is not null) // Start this on a thread
            {
                ManualResetEventSlim InstInitialized = new();
                Thread InstThread = new(() =>
                {
                    Instance = CreateInstWithParams(ObjType, ObjName, ToDict(configEntry), provideControllerInterface);
                    InstInitialized.Set();
                    if (Instance is IVisualizer Vis) { Vis.Start(); }
                    else if (Instance is IOutput Outp) { Outp.Start(); }
                    else if (Instance is Controller Ctrl) { Ctrl.Start(); }
                }) { Name = $"{ObjType.Name} {ObjName}" };
                InstThread.Start();
                InstInitialized.Wait(); // Wait for the instance to have its constructor run before retruning, otherwise we might return null too early
                InstInitialized.Dispose(); // TODO: Check if the above is actually needed?
                InstanceThreads.Add(InstThread);
            }
            else
            {
                Instance = CreateInstWithParams(ObjType, ObjName, ToDict(configEntry), provideControllerInterface);
                if (Instance is IVisualizer Vis) { Vis.Start(); } // TODO: Starting these here creates a rare crash where visualizers have an output added in the middle of iterating through them to dispatch data.
                else if (Instance is IOutput Outp) { Outp.Start(); }
                else if (Instance is Controller Ctrl) { Ctrl.Start(); }
            }
            return (BaseType?)Instance;
        }

        private static object? CreateInstWithParams(Type type, string name, Dictionary<string, object> config, bool provideControllerInterface)
        {
            return provideControllerInterface
                    ? Activator.CreateInstance(type, name, config, ControllerInterface.Instance)
                    : Activator.CreateInstance(type, name, config);
        }

        /// <summary> Takes a JSON token, and converts all single-valued children into a Dictionary. </summary>
        /// <param name="parent"> The JSON token whose child elements should be converted. </param>
        /// <returns> A Dictionary containing all properties contained in the parent element. </returns>
        private static Dictionary<string, object> ToDict(JToken parent)
        {
            Dictionary<string, object> Items = new();
            foreach(JToken ItemToken in parent.Children())
            {
                JProperty Item = (JProperty)ItemToken;
                if (Item.Value is JArray Array) // TODO: See if Object needs to be handled.
                {
                    if (Array.First == null) { continue; }
                    if (Array.First.Type == JTokenType.Object)
                    {
                        Dictionary<string, object>[] ArrayItems = new Dictionary<string, object>[Array.Count];
                        for (int i = 0; i < ArrayItems.Length; i++)
                        {
                            ArrayItems[i] = ToDict(Array[i]);
                        }
                        Items.Add(Item.Name, ArrayItems);
                    }
                    else
                    {
                        object? ParsedArray = null;
                        if (Array.First.Type == JTokenType.Boolean) { ParsedArray = Array.ToObject<bool[]>(); }
                        if (Array.First.Type == JTokenType.String) { ParsedArray = Array.ToObject<string[]>(); }
                        if (Array.First.Type == JTokenType.Float) { ParsedArray = Array.ToObject<float[]>(); }
                        if (Array.First.Type == JTokenType.Integer) { ParsedArray = Array.ToObject<int[]>(); }

                        if (ParsedArray != null) { Items.Add(Item.Name, ParsedArray); }
                    }
                }
                else
                {
                    Items.Add(Item.Name, Item.Value.ToString());
                }
            }
            return Items;
        }

        /// <summary>Tries to find a currently loaded component based on its role and name.</summary>
        /// <param name="path">A path in the format "<see cref="Component"/>.Name", except for Source and NoteFinder components, where only the first part is needed, and the rest is ignored if present.</param>
        /// <returns>null if the component couldn't be found, otherwise the instance</returns>
        public static object? GetInstanceFromPath(string path)
        {
            int IndexSep1 = path.IndexOf('.');
            ReadOnlySpan<char> ComponentType = (IndexSep1 < 0) ? path : path.AsSpan(0, IndexSep1);
            Component Component = Component.None;
            if (Enum.TryParse(ComponentType, true, out Component ParsedComponent)) { Component = ParsedComponent; }

            if (Component == Component.Source) { return Source; }
            else if (Component == Component.NoteFinder) { return NoteFinder; }

            int IndexSep2 = path.IndexOf('.', IndexSep1 + 1);
            if (IndexSep2 < 0) { return null; }
            string ComponentName = path.Substring(IndexSep1 + 1, IndexSep2 - IndexSep1 - 1);

            if (Component == Component.Visualizers) { return VisualizerInsts.TryGetValue(ComponentName, out IVisualizer? Visualizer) ? Visualizer : null; }
            else if (Component == Component.Outputs) { return OutputInsts.TryGetValue(ComponentName, out IOutput? Output) ? Output : null; }
            else if (Component == Component.Controllers) { return ControllerInsts.TryGetValue(ComponentName, out Controller? Controller) ? Controller : null; }
            return null;
        }

    }
}
