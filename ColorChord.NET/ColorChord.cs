using ColorChord.NET.API;
using ColorChord.NET.API.Config;
using ColorChord.NET.API.Controllers;
using ColorChord.NET.API.NoteFinder;
using ColorChord.NET.API.Outputs;
using ColorChord.NET.API.Sources;
using ColorChord.NET.API.Utility;
using ColorChord.NET.API.Visualizers;
using ColorChord.NET.Config;
using ColorChord.NET.Controllers;
using ColorChord.NET.Extensions;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using static ColorChord.NET.API.Config.ConfigNames;

namespace ColorChord.NET
{
    public static class ColorChord
    {
        private static string ConfigFile = "config.json";

        public static readonly Dictionary<string, IAudioSource> SourceInsts = new();
        public static readonly Dictionary<string, NoteFinderCommon> NoteFinderInsts = new();
        public static readonly Dictionary<string, IVisualizer> VisualizerInsts = new();
        public static readonly Dictionary<string, IOutput> OutputInsts = new();
        public static readonly Dictionary<string, Controller> ControllerInsts = new();

        private static readonly List<Thread> InstanceThreads = new();
        private static readonly ManualResetEventSlim StopSignal = new();

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

            foreach (IOutput Output in OutputInsts.Values) { Output.Start(); }
            foreach (IVisualizer Visualizer in VisualizerInsts.Values) { Visualizer.Start(); }
            foreach (NoteFinderCommon NoteFinder in NoteFinderInsts.Values) { NoteFinder.Start(); }
            foreach (IAudioSource Source in SourceInsts.Values) { Source.Start(); }
            foreach (Controller Controller in ControllerInsts.Values) { Controller.Start(); }
            ExtensionHandler.PostInitExtensions();

            StopSignal.Wait(); // The main thread will pause here until something requests application termination.
            Log.Info("Exiting...");
            ExtensionHandler.StopExtensions();
            foreach (IAudioSource Source in SourceInsts.Values) { Source.Stop(); }
            foreach (NoteFinderCommon NoteFinder in NoteFinderInsts.Values) { NoteFinder.Stop(); }
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

            ReadConfigSection(JSON, SECTION_SOURCE, SECTION_SOURCE_ARR, "ColorChord.NET.Sources", SourceInsts, "audio source", "audio sources");
            ReadConfigSection(JSON, SECTION_NOTE_FINDER, SECTION_NOTE_FINDER_ARR, "ColorChord.NET.NoteFinder", NoteFinderInsts, "NoteFinder", "NoteFinders");
            ReadConfigSection(JSON, SECTION_VISUALIZER, SECTION_VISUALIZER_ARR, "ColorChord.NET.Visualizers", VisualizerInsts, "visualizer", "visualizers");
            ReadConfigSection(JSON, SECTION_OUTPUT, SECTION_OUTPUT_ARR, "ColorChord.NET.Outputs", OutputInsts, "output", "outputs");
            ReadConfigSection(JSON, SECTION_CONTROLLER, SECTION_CONTROLLER_ARR, "ColorChord.NET.Controllers", ControllerInsts, "controller", "controllers");
            Log.Info("Finished processing config file.");
        }

        public static void Stop() => StopSignal.Set();

        /// <summary> Reads a given section from the config, and attempts to load all components defined in it. </summary>
        /// <typeparam name="T"> The type of component to handle, such as <see cref="IAudioSource"/> </typeparam>
        /// <param name="JSON"> The JSON object representing the config file. The given section will be searched for as a direct child of this object </param>
        /// <param name="sectionName"> The config section name for a single component of this type, only that one instance will be loaded </param>
        /// <param name="sectionNameArr"> The config section name for an array of definitions of these components, all will be loaded. Preferred over the singular section when present </param>
        /// <param name="defaultNamespace"> If the type name does not start with # to indicate an extension, this namespace will be prepended instead. Exclude the trailing period </param>
        /// <param name="output"> The dictionary where to store the loaded instances </param>
        /// <param name="friendlyNameSingular"> Used in user-facing text to describe a singular of this type of component </param>
        /// <param name="friendlyNamePlural"> Used in user-facing text to describe a plural set of this type of component </param>
        private static void ReadConfigSection<T>(JObject JSON, string sectionName, string sectionNameArr, string defaultNamespace, Dictionary<string, T> output, string friendlyNameSingular, string friendlyNamePlural) where T : IConfigurableAttr
        {
            void HandleEntry(JToken entry, string? defaultName = null)
            {
                string? EntryType = entry.Value<string>(TYPE);
                string? EntryName = entry.Value<string>(NAME) ?? defaultName;
                if (EntryType == null) { Log.Error($"A {friendlyNameSingular} is missing a \"{TYPE}\" definition, and will therefore not be initialized."); return; }
                if (EntryName == null) { Log.Error($"A {friendlyNameSingular} is missing a \"{NAME}\" definition, and will therefore not be initialized."); return; }

                T? Inst = CreateObject<T>(EntryType.StartsWith('#') ? EntryType : $"{defaultNamespace}.{EntryType}", entry);
                if (Inst == null) { Log.Error($"Failed to create {friendlyNameSingular} \"{EntryName}\". Check to make sure the type \"{EntryType}\" is spelled correctly. If it is part of an extension, make sure to start the name with a # character."); return; }
                output.Add(EntryName, Inst);
                Log.Info($"Created {friendlyNameSingular} of type \"{Inst.GetType().FullName}\".");
            }

            if (JSON.TryGetValue(sectionNameArr, out JToken? SectionArray))
            {
                if (SectionArray == null || !SectionArray.HasValues) { Log.Error($"\"{sectionNameArr}\" definition in the config file is empty/invalid. No {friendlyNamePlural} will load."); return; }
                if (!typeof(JArray).IsAssignableFrom(SectionArray.GetType())) { Log.Error($"\"{sectionNameArr}\" definition in the config file needs to be an array. If you intended to configure only one, call the section \"{sectionName}\" (non-plural) instead. No {friendlyNamePlural} will load."); return;  }
                JArray Entries = (JArray)SectionArray;
                if (Entries.Count == 0) { Log.Warn($"No {friendlyNamePlural} were defined in the config file. Check to make sure the formatting is correct."); return; }

                foreach (JToken Entry in Entries) { HandleEntry(Entry); }
                return;
            }
            if (JSON.TryGetValue(sectionName, out JToken? SectionItem))
            {
                if (SectionItem == null || !SectionItem.HasValues) { Log.Error($"\"{sectionName}\" definition in the config file is empty/invalid. No {friendlyNamePlural} will load."); return; }
                HandleEntry(SectionItem, "unnamed");
                return;
            }

            Log.Error($"Could not find valid \"{sectionName}\"/\"{sectionNameArr}\" definition in the config file. No {friendlyNamePlural} will load.");
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
            if (typeof(IThreadedInstance).IsAssignableFrom(ObjType)) // Start this on a thread
            {
                ManualResetEventSlim InstInitialized = new();
                Thread InstThread = new(() =>
                {
                    Instance = CreateInstWithParams(ObjType, ObjName, ToDict(configEntry), provideControllerInterface);
                    InstInitialized.Set();
                    ((IThreadedInstance?)Instance)?.InstThreadPostInit();
                }) { Name = $"{ObjType.Name} {ObjName} (CC.NET-managed)" };
                InstThread.Start();
                InstInitialized.Wait(); // Wait for the instance to have its constructor run before retruning, otherwise we might return null too early
                InstInitialized.Dispose(); // TODO: Check if the above is actually needed?
                InstanceThreads.Add(InstThread);
            }
            else
            {
                Instance = CreateInstWithParams(ObjType, ObjName, ToDict(configEntry), provideControllerInterface);
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
        /// <param name="path">A path in the format "<see cref="Component"/>.Name", except for Source and NoteFinder components, where only the first part is needed if there is only one instance loaded.</param>
        /// <returns>null if the component couldn't be found, otherwise the instance</returns>
        public static object? GetInstanceFromPath(string path)
        {
            int IndexSep1 = path.IndexOf('.');
            ReadOnlySpan<char> ComponentType = (IndexSep1 < 0) ? path : path.AsSpan(0, IndexSep1);
            Component Component = Component.None;
            if (Enum.TryParse(ComponentType, true, out Component ParsedComponent)) { Component = ParsedComponent; }

            if (Component == Component.Source && SourceInsts.Count == 1) { return SourceInsts.First().Value; }
            else if (Component == Component.NoteFinder && NoteFinderInsts.Count == 1) { return NoteFinderInsts.First().Value; }

            int IndexSep2 = path.IndexOf('.', IndexSep1 + 1);
            if (IndexSep2 < 0) { return null; }
            string ComponentName = path.Substring(IndexSep1 + 1, IndexSep2 - IndexSep1 - 1);

            if (Component == Component.Source) { return SourceInsts.TryGetValue(ComponentName, out IAudioSource? Source) ? Source : null; }
            else if (Component == Component.NoteFinder) { return NoteFinderInsts.TryGetValue(ComponentName, out NoteFinderCommon? NoteFinder) ? NoteFinder : null; }
            else if (Component == Component.Visualizers) { return VisualizerInsts.TryGetValue(ComponentName, out IVisualizer? Visualizer) ? Visualizer : null; }
            else if (Component == Component.Outputs) { return OutputInsts.TryGetValue(ComponentName, out IOutput? Output) ? Output : null; }
            else if (Component == Component.Controllers) { return ControllerInsts.TryGetValue(ComponentName, out Controller? Controller) ? Controller : null; }
            return null;
        }

    }
}
