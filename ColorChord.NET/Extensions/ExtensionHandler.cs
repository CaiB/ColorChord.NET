using ColorChord.NET.API;
using ColorChord.NET.API.Extensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace ColorChord.NET.Extensions;

public static class ExtensionHandler
{
    private static List<Assembly>? Assemblies;
    private static List<IExtension>? Extensions;

    /// <summary>Finds all suitable extension DLLs, loads them, then instantiates all contained classes that implement <see cref="IExtension"/>.</summary>
    public static void LoadExtensions() { (Assemblies, Extensions) = LoadExtensions(FindExtensions()); }

    /// <summary>Calls <see cref="IExtension.Initialize"/> on all loaded extensions.</summary>
    public static void InitExtensions()
    {
        foreach (IExtension Extension in Extensions!) { Extension.Initialize(); }
    }

    /// <summary>Calls <see cref="IExtension.PostInitialize"/> on all loaded extensions.</summary>
    public static void PostInitExtensions()
    {
        foreach (IExtension Extension in Extensions!) { Extension.PostInitialize(); }
    }

    /// <summary>Calls <see cref="IExtension.Shutdown"/> on all loaded extensions.</summary>
    public static void StopExtensions()
    {
        foreach (IExtension Extension in Extensions!) { Extension.Shutdown(); }
    }

    /// <summary>Attempts to find a type by name in any assemblies of loaded extensions.</summary>
    /// <param name="typeName">The namespace + type name to search for</param>
    /// <returns>The <see cref="Type"/> if it was found in any loaded extensions, otherwise null</returns>
    public static Type? FindType(string typeName)
    {
        if (Assemblies == null) { return null; }
        foreach (Assembly Assembly in Assemblies)
        {
            try
            {
                Type? FoundType = Assembly.GetType(typeName);
                if (FoundType != null) { return FoundType; }
            }
            catch { continue; }
        }
        return null;
    }

    /// <summary>Finds the names of any suitable DLL files in the Extensions directory. If the directory doesn't exist, it will be created.</summary>
    /// <remarks>Suitable DLL files are those whose name stars with "CC.NET" and have an extension of ".dll".</remarks>
    /// <returns>A list of all extension DLL file names found</returns>
    private static List<string> FindExtensions()
    {
        string MyExecutable = Assembly.GetExecutingAssembly().Location;
        string? MyLocation = Path.GetDirectoryName(MyExecutable);
        if (MyLocation == null) { Log.Error("Could not load extensions because getting the location of ColorChord.NET failed."); return new(); }
        string ExtensionsFolder = Path.Join(MyLocation, "Extensions");
        if (!Directory.Exists(ExtensionsFolder))
        {
            try { Directory.CreateDirectory(ExtensionsFolder); }
            catch { Log.Warn("Could not create the extensions directory."); }
        }
        else
        {
            try { return Directory.EnumerateFiles(ExtensionsFolder, "CC.NET*.dll").ToList(); }
            catch (Exception Ex)
            {
                Log.Warn("Failed to search the extensions folder for DLL files.");
                Log.Warn(Ex.ToString());
            }
        }
        return new();
    }

    /// <summary>Attempts to load the given extension DLLs if they are compatible.</summary>
    /// <remarks>Extension classes must implement <see cref="IExtension"/> and have a parameterless constructor.</remarks>
    /// <param name="extensionFiles">A list of DLL file names to attempt to load</param>
    /// <returns>A list of assemblies loaded, and a list of extension classes constructed</returns>
    private static (List<Assembly>, List<IExtension>) LoadExtensions(List<string> extensionFiles)
    {
        List<Assembly> Assemblies = new();
        List<Type> ExtensionTypes = new();
        foreach (string ExtensionFile in extensionFiles)
        {
            Assembly? Extension = null;
            try
            {
                Extension = Assembly.LoadFile(ExtensionFile);
                ExtensionTypes.AddRange(Extension.GetExportedTypes().Where(x => typeof(IExtension).IsAssignableFrom(x)));
            }
            catch(Exception Ex)
            {
                Log.Warn($"Failed to load extension \"{ExtensionFile}\".");
                Log.Warn(Ex.ToString());
            }
            if (Extension != null) { Assemblies.Add(Extension); }
        }
        Log.Debug(string.Format("Found {0} extension{1}.", ExtensionTypes.Count, (ExtensionTypes.Count == 1 ? "" : 's')));
        List<IExtension> Extensions = new(ExtensionTypes.Count);
        foreach (Type ExtensionType in ExtensionTypes)
        {
            object? Instance = Activator.CreateInstance(ExtensionType);
            if (Instance is not IExtension ExtensionInstance) { Log.Warn($"Failed to instantiate extension \"{ExtensionType.FullName}\" from the assembly \"{ExtensionType.Assembly.FullName}\"."); }
            else
            {
                Log.Debug($"Extension \"{ExtensionInstance.Name}\" was loaded from type \"{ExtensionType.FullName}\" from the assembly \"{ExtensionType.Assembly.FullName}\". It was built with API version {ExtensionInstance.APIVersion}.");
                if (ExtensionInstance.APIVersion > ColorChordAPI.APIVersion)
                {
                    Log.Error($"The extension \"{ExtensionInstance.Name}\" expects a newer version of ColorChord.NET (API version {ExtensionInstance.APIVersion}) than the one you have installed (API version {ColorChordAPI.APIVersion}). It will not work until you update ColorChord.NET.");
                    continue;
                }
                else if (ExtensionInstance.APIVersion < ColorChordAPI.APIVersion) { Log.Warn($"The extension \"{ExtensionInstance.Name}\" was built for an older version of ColorChord.NET (API version {ExtensionInstance.APIVersion}) than the one you have installed (API version {ColorChordAPI.APIVersion}). If you encounter problems with it, please try installing a newer version of it."); }
                Extensions.Add(ExtensionInstance);
            }
        }
        return (Assemblies, Extensions);
    }
}
