using ColorChord.NET.NoteFinder;
using System.Runtime.InteropServices;

namespace Gen2DFTLib;

public static unsafe class Gen2DFT
{
    private static Gen2NoteFinderDFT DFT;

    /// <summary> Prepares the DFT for use, initializing internal data structures. </summary>
    /// <param name="octaveCount"> The number of octaves to analyze </param>
    /// <param name="sampleRate"> The sample rate of the input audio </param>
    /// <param name="startFrequency"> The desired frequency of the lowest bin </param>
    /// <param name="loudnessCorrection"> The amount of human-modelled loudness equalization to apply to the output bins </param>
    [UnmanagedCallersOnly(EntryPoint = "Gen2DFT_Init")]
    public static void Init(uint octaveCount, uint sampleRate, float startFrequency, float loudnessCorrection)
    {
        DFT = new(octaveCount, sampleRate, startFrequency, loudnessCorrection, null);
    }

    /// <summary> Processes the given audio data into the DFT. </summary>
    /// <param name="newData"> The data to process, as full-range I16s </param>
    /// <param name="count"> The length of the data array to read and process </param>
    [UnmanagedCallersOnly(EntryPoint = "Gen2DFT_AddAudioData")]
    public static void AddAudioData(short* newData, uint count)
    {
        ReadOnlySpan<short> Data = new(newData, (int)count);
        DFT.AddAudioData(Data);
    }

    /// <summary> Updates the output values for reading based on the audio data that has been added until now. </summary>
    /// <remarks> Read the audio data using <see cref="GetBinMagnitudes"/>. </remarks>
    [UnmanagedCallersOnly(EntryPoint = "Gen2DFT_CalculateOutput")]
    public static void CalculateOutput()
    {
        DFT.CalculateOutput();
    }

    /// <summary> Gets the number of output bins per octave. </summary>
    [UnmanagedCallersOnly(EntryPoint = "Gen2DFT_GetBinsPerOctave")]
    public static uint GetBinsPerOctave() => DFT.BinsPerOctave;

    /// <summary> Gets the number of bins in total, across all octaves. </summary>
    [UnmanagedCallersOnly(EntryPoint = "Gen2DFT_GetBinCount")]
    public static uint GetBinCount() => DFT.BinCount;

    /// <summary> Gets the current DFT output data. Make sure to call <see cref="CalculateOutput"/> before this. </summary>
    /// <returns> The raw magnitudes of each DFT bin, length is <see cref="GetBinCount"/>. </returns>
    [UnmanagedCallersOnly(EntryPoint = "Gen2DFT_GetBinMagnitudes")]
    public static float* GetBinMagnitudes()
    {
        fixed (float* BinMags = DFT.RawBinMagnitudes) { return BinMags; }
    }

    /// <summary> Gets the central response frequencies of each DFT bin. </summary>
    /// <returns> The frequency of each DFT bin in Hz, length is <see cref="GetBinCount"/>. </returns>
    [UnmanagedCallersOnly(EntryPoint = "Gen2DFT_GetBinFrequencies")]
    public static float* GetBinFrequencies()
    {
        fixed (float* BinFreqs = DFT.RawBinFrequencies) { return  BinFreqs; }
    }

    /// <summary> Gets the sensitivity range width of each bin, in number of bins. </summary>
    /// <returns> The width of each bin in number of bins, length is <see cref="GetBinCount"/>. A value of 2 would mean that this bin stops responding to frequencies around the center of the bin above and below (this bin's range = 1, plus 0.5 bins on either side) </returns>
    [UnmanagedCallersOnly(EntryPoint = "Gen2DFT_GetBinWidths")]
    public static float* GetBinWidths()
    {
        fixed (float* BinWidths = DFT.RawBinWidths) { return BinWidths; }
    }
}
