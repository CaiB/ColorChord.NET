namespace ColorChord.NET.API;

public static class ColorChordAPI
{
    /// <summary>Use this version as return value for <see cref="Extensions.IExtension.APIVersion"/> in your extension.</summary>
    /// <remarks>This allows ColorChord.NET to know if the installed version of your extension is compatible.</remarks>
    public const uint APIVersion = 17;
}
