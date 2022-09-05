namespace ColorChord.NET.API.Extensions;

public interface IExtension
{
    /// <summary>The name of your extension.</summary>
    public string Name { get; }

    /// <summary>A brief description of your extension.</summary>
    public string Description { get; }

    /// <summary>Set this to the API version found in <see cref="ColorChordAPI.APIVersion"/> that you built the extension against.</summary>
    /// <remarks>Because it is set as const, it is safe and recommended to directly implement this as: <code>public uint APIVersion => ColorChordAPI.APIVersion;</code></remarks>
    public uint APIVersion { get; }

    /// <summary>Called when ColorChord.NET is ready to load your extension.</summary>
    /// <remarks>
    /// The point during initialization when this happens is not guaranteed, so do not rely on any specific part of ColorChord.NET to be initialized.
    /// However, this will be called before any of the other classes inside your plugin are instantiated.
    /// </remarks>
    /// <returns></returns>
    public void Initialize();

    /// <summary>Called when ColorChord.NET is fully loaded.</summary>
    /// <remarks>When this is called, all active components of ColorChord.NET are instantiated and running.</remarks>
    public void PostInitialize();

    /// <summary>Called when ColorChord.NET is shutting down gracefully.</summary>
    public void Shutdown();
}
