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
using OpenTK.Windowing.Desktop;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
            // Janky workaround for OpenTK insisting the application's main thread is the only possible way a user could want to use their library.
            // This can't be done inside DisplayOpenGL because the constructor's base() call has to be first, and throws an exception is this is not already set before it runs.
            // https://github.com/opentk/opentk/issues/1206
            GLFWProvider.CheckForMainThread = false;

            ExtensionHandler.LoadExtensions();
            ExtensionHandler.InitExtensions();
            HandleConfig();

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

        public static void HandleConfig()
        {
            const int FILE_SIZE_LIMIT_MB = 10;
            byte[] FileContent;

            using (FileStream Reader = File.Open(ConfigFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (Reader.Length > FILE_SIZE_LIMIT_MB * 1024 * 1024) { throw new Exception($"The config JSON file is over the {FILE_SIZE_LIMIT_MB} MiB size limit."); }
                FileContent = new byte[Reader.Length];
                Reader.Read(FileContent, 0, FileContent.Length);
            }

            byte[] ConfigHash = MD5.HashData(FileContent);
            if (DefaultConfigInfo.DefaultConfigFileMD5.SequenceEqual(ConfigHash)) { Log.Warn($"It appears you are using the default config file, located at \"{Path.GetFullPath(ConfigFile)}\". Make any changes as needed."); }

            Log.Info("Reading and applying configuration file \"" + ConfigFile + "\"...");
            ReadOnlySpan<byte> FileAsUTF8 = FileToUTF8.ConvertSpan(FileContent);
            Utf8JsonReader JSONReader = new(FileAsUTF8, new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            ReadConfig(ref JSONReader, FileAsUTF8);
            Log.Info("Finished processing config file.");
        }

        public static void Stop() => StopSignal.Set();

        private static void ReadConfig(ref Utf8JsonReader reader, ReadOnlySpan<byte> fileData)
        {
            if (!reader.Read()) { PrintJSONError(ref reader, fileData, "File was empty"); }
            if (reader.TokenType != JsonTokenType.StartObject) { PrintJSONError(ref reader, fileData, "Top-level object not found. You must wrap the entire config file in { ... }"); }

            List<Dictionary<string, object>> SourcesData = new(), NoteFindersData = new(), VisualizersData = new(), OutputsData = new(), ControllersData = new();

            try
            {
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject) { break; }

                    if (reader.TokenType != JsonTokenType.PropertyName) { PrintJSONError(ref reader, fileData, $"Expected section definition, instead found {reader.TokenType}"); }
                    string? SectionName = reader.GetString();
                    switch (SectionName)
                    {
                        case SECTION_SOURCE:
                        case SECTION_SOURCE_ARR:
                            ReadConfigSection(ref reader, fileData, SourcesData, "audio source", "audio sources");
                            break;
                        case SECTION_NOTE_FINDER:
                        case SECTION_NOTE_FINDER_ARR:
                            ReadConfigSection(ref reader, fileData, NoteFindersData, "NoteFinder", "NoteFinders");
                            break;
                        case SECTION_VISUALIZER:
                        case SECTION_VISUALIZER_ARR:
                            ReadConfigSection(ref reader, fileData, VisualizersData, "visualizer", "visualizers");
                            break;
                        case SECTION_OUTPUT:
                        case SECTION_OUTPUT_ARR:
                            ReadConfigSection(ref reader, fileData, OutputsData, "output", "outputs");
                            break;
                        case SECTION_CONTROLLER:
                        case SECTION_CONTROLLER_ARR:
                            ReadConfigSection(ref reader, fileData, ControllersData, "controller", "controllers");
                            break;
                        default:
                            Log.Warn($"Found unknown section in config file: '{SectionName}'");
                            ReadConfigSection(ref reader, fileData, new(), $"unknown '{SectionName}'", $"unknown '{SectionName}'");
                            break;
                    }
                }
            }
            catch (Exception e)
            {
                PrintJSONError(ref reader, fileData, e.Message);
            }

            CreateInstances(SourcesData, "ColorChord.NET.Sources", SourceInsts, "audio source", "audio sources");
            CreateInstances(NoteFindersData, "ColorChord.NET.NoteFinder", NoteFinderInsts, "NoteFinder", "NoteFinders");
            CreateInstances(VisualizersData, "ColorChord.NET.Visualizers", VisualizerInsts, "visualizer", "visualizers");
            CreateInstances(OutputsData, "ColorChord.NET.Outputs", OutputInsts, "output", "outputs");
            CreateInstances(ControllersData, "ColorChord.NET.Controllers", ControllerInsts, "controller", "controllers");
        }

        private static void ReadConfigSection(ref Utf8JsonReader reader, ReadOnlySpan<byte> fileData, List<Dictionary<string, object>> output, string friendlyNameSingular, string friendlyNamePlural)
        {
            reader.Read();
            if (reader.TokenType == JsonTokenType.StartArray)
            {
                reader.Read();
                while (reader.TokenType == JsonTokenType.StartObject)
                {
                    output.Add(ReadConfigObject(ref reader, fileData));
                    reader.Read();
                }
            }
            else if (reader.TokenType == JsonTokenType.StartObject)
            {
                output.Add(ReadConfigObject(ref reader, fileData));
            }
            else { PrintJSONError(ref reader, fileData, $"Found unexpected {reader.TokenType} in {friendlyNamePlural} section"); }
        }

        private static Dictionary<string, object> ReadConfigObject(ref Utf8JsonReader reader, ReadOnlySpan<byte> fileData)
        {
            Dictionary<string, object> Result = new();
            int OuterDepth = reader.CurrentDepth;

            string? PropertyName = null;
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.PropertyName:
                        PropertyName = reader.GetString();
                        break;
                    case JsonTokenType.Null:
                        break;
                    case JsonTokenType.Number:
                        if (PropertyName == null) { PrintJSONError(ref reader, fileData, "No property name was specified for this value"); }
                        if (reader.TryGetUInt64(out ulong ValueU64)) { Result.Add(PropertyName, ValueU64); }
                        else if (reader.TryGetInt64(out long ValueI64)) { Result.Add(PropertyName, ValueI64); }
                        else if (reader.TryGetDouble(out double ValueF64)) { Result.Add(PropertyName, ValueF64); }
                        else { PrintJSONError(ref reader, fileData, "Could not parse number"); }
                        break;
                    case JsonTokenType.True:
                    case JsonTokenType.False:
                        if (PropertyName == null) { PrintJSONError(ref reader, fileData, "No property name was specified for this value"); }
                        Result.Add(PropertyName, reader.TokenType == JsonTokenType.True);
                        break;
                    case JsonTokenType.String:
                        if (PropertyName == null) { PrintJSONError(ref reader, fileData, "No property name was specified for this value"); }
                        Result.Add(PropertyName, reader.GetString() ?? string.Empty);
                        break;
                    case JsonTokenType.StartArray:
                        if (PropertyName == null) { PrintJSONError(ref reader, fileData, "No property name was specified for this value"); }
                        object Array = ReadConfigArray(ref reader, fileData);
                        Result.Add(PropertyName, Array);
                        break;
                    case JsonTokenType.StartObject:
                        if (PropertyName == null) { PrintJSONError(ref reader, fileData, "No property name was specified for this value"); }
                        Dictionary<string, object> Object = ReadConfigObject(ref reader, fileData);
                        Result.Add(PropertyName, Object);
                        break;
                    case JsonTokenType.EndObject:
                        if (reader.CurrentDepth == OuterDepth) { goto EndLoop; }
                        else { PrintJSONError(ref reader, fileData, "Object ended and hierarchy levels didn't match"); }
                        break;
                    default:
                        PrintJSONError(ref reader, fileData, $"Found unexpected {reader.TokenType} while parsing config object");
                        break;
                }
            }
            EndLoop:
            return Result;
        }

        private static object ReadConfigArray(ref Utf8JsonReader reader, ReadOnlySpan<byte> fileData)
        {
            reader.Read();
            switch (reader.TokenType)
            {
                case JsonTokenType.Number:
                    if (reader.TryGetInt64(out long ValueI64))
                    {
                        List<long> ArrI64 = [ValueI64];
                        reader.Read();
                        while (reader.TokenType != JsonTokenType.EndArray)
                        {
                            if (!reader.TryGetInt64(out long NextValueI64)) { PrintJSONError(ref reader, fileData, "Array was detected to be integers, however a later item could not be parsed as an integer"); }
                            ArrI64.Add(NextValueI64);
                            reader.Read();
                        }
                        return ArrI64;
                    }
                    else if (reader.TryGetDouble(out double ValueF64))
                    {
                        List<double> ArrF64 = [ValueF64];
                        reader.Read();
                        while (reader.TokenType != JsonTokenType.EndArray)
                        {
                            if (!reader.TryGetDouble(out double NextValueF64)) { PrintJSONError(ref reader, fileData, "Array was detected to be floats, however a later item could not be parsed as a float"); }
                            ArrF64.Add(NextValueF64);
                            reader.Read();
                        }
                        return ArrF64;
                    }
                    else { PrintJSONError(ref reader, fileData, "Could not parse first number in array"); }
                    break;
                case JsonTokenType.True:
                case JsonTokenType.False:
                    List<bool> ArrB = [reader.TokenType == JsonTokenType.True];
                    reader.Read();
                    while (reader.TokenType != JsonTokenType.EndArray)
                    {
                        if (reader.TokenType != JsonTokenType.True && reader.TokenType != JsonTokenType.False) { PrintJSONError(ref reader, fileData, "Array of bools contained a non-bool value"); }
                        ArrB.Add(reader.TokenType == JsonTokenType.True);
                        reader.Read();
                    }
                    return ArrB;
                case JsonTokenType.String:
                    List<string> ArrS = [reader.GetString()];
                    reader.Read();
                    while (reader.TokenType != JsonTokenType.EndArray)
                    {
                        if (reader.TokenType != JsonTokenType.String) { PrintJSONError(ref reader, fileData, "Array of strings contained a non-string value"); }
                        ArrS.Add(reader.GetString() ?? string.Empty);
                        reader.Read();
                    }
                    return ArrS;
                case JsonTokenType.StartObject:
                    List<object?> ArrO = [ReadConfigObject(ref reader, fileData)];
                    reader.Read();
                    while (reader.TokenType != JsonTokenType.EndArray)
                    {
                        if (reader.TokenType != JsonTokenType.StartObject) { PrintJSONError(ref reader, fileData, "Array of objects contained a non-object value"); }
                        ArrO.Add(ReadConfigObject(ref reader, fileData));
                        reader.Read();
                    }
                    return ArrO;
                case JsonTokenType.StartArray:
                    List<object?> ArrA = [ReadConfigArray(ref reader, fileData)];
                    reader.Read();
                    while (reader.TokenType != JsonTokenType.EndArray)
                    {
                        if (reader.TokenType != JsonTokenType.StartArray) { PrintJSONError(ref reader, fileData, "Array of arrays contained a non-array value"); }
                        ArrA.Add(ReadConfigArray(ref reader, fileData));
                        reader.Read();
                    }
                    return ArrA;
                default:
                    PrintJSONError(ref reader, fileData, $"Found unexpected {reader.TokenType} while parsing array");
                    break;
            }
            return new();
        }

        [DoesNotReturn]
        private static void PrintJSONError(ref Utf8JsonReader reader, ReadOnlySpan<byte> fileData, string message)
        {
            Log.Error(message);

            int PreviousLineStart = 0, PreviousLineEnd = 0, LineStart = 0, LineEnd = 0, LineNum = 0, CurrentIndex = 0;
            while (LineEnd < reader.TokenStartIndex)
            {
                PreviousLineStart = LineStart;
                PreviousLineEnd = LineEnd;
                LineStart = CurrentIndex;
                while (fileData[CurrentIndex] != 0x0A && fileData[CurrentIndex] != 0x0D) { CurrentIndex++; }
                if (fileData[CurrentIndex] == 0x0D && fileData[CurrentIndex + 1] == 0x0A) { LineEnd = CurrentIndex - 1; CurrentIndex += 2; }
                else { LineEnd = CurrentIndex - 1; CurrentIndex++; }
                LineNum++;
            }
            Log.Error("This error was detected here:");
            Log.Error($"Line {LineNum - 1}: {Encoding.UTF8.GetString(fileData.Slice(PreviousLineStart, PreviousLineEnd - PreviousLineStart + 1))}");
            string LineNumText = $"Line {LineNum}: ";
            Log.Error($"{LineNumText}{Encoding.UTF8.GetString(fileData.Slice(LineStart, LineEnd - LineStart + 1))}");
            Log.Error($"{new string(' ', LineNumText.Length + (int)(reader.TokenStartIndex - LineStart))}{new string('^', (int)(reader.BytesConsumed - reader.TokenStartIndex + 1))}");
            //Log.Error($"  at {ConfigFile} bytes 0x{reader.TokenStartIndex:X} - 0x{reader.BytesConsumed:X}, linestart 0x{LineStart:X} end 0x{LineEnd:X}");

            Environment.Exit(-2);
        }

        private static void CreateInstances<T>(List<Dictionary<string, object>> configData, string defaultNamespace, Dictionary<string, T> output, string friendlyNameSingular, string friendlyNamePlural) where T : IConfigurableAttr
        {
            if (configData.Count > 0) { Log.Info($"Creating {configData.Count} {(configData.Count == 1 ? friendlyNameSingular : friendlyNamePlural)}..."); }
            else { Log.Info($"No {friendlyNamePlural} were found in the config file."); }

            foreach (Dictionary<string, object> ConfigItem in configData)
            {
                if (!ConfigItem.TryGetValue(TYPE, out object? EntryTypeObj) || EntryTypeObj is not string EntryType) { Log.Error($"A {friendlyNameSingular} does not have a valid \"{TYPE}\" definition, and will therefore not be initialized."); continue; }
                if (!ConfigItem.TryGetValue(NAME, out object? EntryNameObj) || EntryNameObj is not string EntryName) { Log.Error($"A {friendlyNameSingular} does not have a valid \"{NAME}\" definition, and will therefore not be initialized."); continue; }

                T? Inst = CreateObject<T>(EntryType.StartsWith('#') ? EntryType : $"{defaultNamespace}.{EntryType}", ConfigItem);
                if (Inst == null) { Log.Error($"Failed to create {friendlyNameSingular} \"{EntryName}\". Check to make sure the type \"{EntryType}\" is spelled correctly. If it is part of an extension, make sure to start the name with a # character."); continue; }
                output.Add(EntryName, Inst);
                Log.Debug($"Created {friendlyNameSingular} of type \"{Inst.GetType().FullName}\".");
            }
        }

        /// <summary> Creates a new instance of the specified type, and checks to make sure it implements the given interface. </summary>
        /// <typeparam name="BaseType"> The interface that the resulting object must implement. </typeparam>
        /// <param name="fullName"> The full name (namespace + type) of the object to create. </param>
        /// <param name="configEntry"> The config entry containing options that should be applied to the resulting object. </param>
        /// <param name="provideControllerInterface">Whether the constructor needs to be provided with an <see cref="IControllerInterface"/></param>
        /// <returns> A configured, ready object, or null if it was not able to be made. </returns>
        private static BaseType? CreateObject<BaseType>(string fullName, Dictionary<string, object> configEntry, bool provideControllerInterface = false) where BaseType : IConfigurableAttr
        {
            Type? ObjType;
            if (fullName.StartsWith('#'))
            {
                fullName = fullName.Substring(1);
                ObjType = ExtensionHandler.FindType(fullName);
            }
            else { ObjType = Type.GetType(fullName); }
            if (ObjType == null || !typeof(BaseType).IsAssignableFrom(ObjType)) { return default; } // Type doesn't exist, or does not implement the right interface.

            string? ObjName = configEntry[NAME] as string;
            if (typeof(BaseType) == typeof(IAudioSource) || typeof(BaseType) == typeof(NoteFinderCommon)) { ObjName = ObjType.Name; }
            if (ObjName == null) { return default; }

            object? Instance = null;
            if (typeof(IThreadedInstance).IsAssignableFrom(ObjType)) // Start this on a thread
            {
                ManualResetEventSlim InstInitialized = new();
                Thread InstThread = new(() =>
                {
                    Instance = CreateInstWithParams(ObjType, ObjName, configEntry, provideControllerInterface);
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
                Instance = CreateInstWithParams(ObjType, ObjName, configEntry, provideControllerInterface);
            }
            return (BaseType?)Instance;
        }

        private static object? CreateInstWithParams(Type type, string name, Dictionary<string, object> config, bool provideControllerInterface)
        {
            return provideControllerInterface
                    ? Activator.CreateInstance(type, name, config, ControllerInterface.Instance)
                    : Activator.CreateInstance(type, name, config);
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
