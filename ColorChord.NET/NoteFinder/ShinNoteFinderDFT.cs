﻿using ColorChord.NET.API;
using System;
using System.Diagnostics.CodeAnalysis;

namespace ColorChord.NET.NoteFinder;

/// <summary> My own note finder implementation. </summary>
public static class ShinNoteFinderDFT
{
    private const int MIN_WINDOW_SIZE = 256;
    private const int MAX_WINDOW_SIZE = 32768;
    private const ushort SINE_TABLE_90_OFFSET = 64;

    private static readonly short[] SinWave = new short[256]
    {
        0, 402, 804, 1205, 1606, 2005, 2404, 2801, 3196, 3590, 3981, 4370, 4756, 5139, 5519, 5896,
        6270, 6639, 7005, 7366, 7723, 8075, 8423, 8765, 9102, 9433, 9759, 10079, 10393, 10701, 11002, 11297,
        11585, 11865, 12139, 12405, 12664, 12915, 13159, 13394, 13622, 13841, 14052, 14255, 14449, 14634, 14810, 14977,
        15136, 15285, 15425, 15556, 15678, 15790, 15892, 15985, 16068, 16142, 16206, 16260, 16304, 16339, 16363, 16378,
        16383, 16378, 16363, 16339, 16304, 16260, 16206, 16142, 16068, 15985, 15892, 15790, 15678, 15556, 15425, 15285,
        15136, 14977, 14810, 14634, 14449, 14255, 14052, 13841, 13622, 13394, 13159, 12915, 12664, 12405, 12139, 11865,
        11585, 11297, 11002, 10701, 10393, 10079, 9759, 9433, 9102, 8765, 8423, 8075, 7723, 7366, 7005, 6639,
        6270, 5896, 5519, 5139, 4756, 4370, 3981, 3590, 3196, 2801, 2404, 2005, 1606, 1205, 804, 402,
        0, -402, -804, -1205, -1606, -2005, -2404, -2801, -3196, -3590, -3981, -4370, -4756, -5139, -5519, -5896,
        -6270, -6639, -7005, -7366, -7723, -8075, -8423, -8765, -9102, -9433, -9759, -10079, -10393, -10701, -11002, -11297,
        -11585, -11865, -12139, -12405, -12664, -12915, -13159, -13394, -13622, -13841, -14052, -14255, -14449, -14634, -14810, -14977,
        -15136, -15285, -15425, -15556, -15678, -15790, -15892, -15985, -16068, -16142, -16206, -16260, -16304, -16339, -16363, -16378,
        -16383, -16378, -16363, -16339, -16304, -16260, -16206, -16142, -16068, -15985, -15892, -15790, -15678, -15556, -15425, -15285,
        -15136, -14977, -14810, -14634, -14449, -14255, -14052, -13841, -13622, -13394, -13159, -12915, -12664, -12405, -12139, -11865,
        -11585, -11297, -11002, -10701, -10393, -10079, -9759, -9433, -9102, -8765, -8423, -8075, -7723, -7366, -7005, -6639,
        -6270, -5896, -5519, -5139, -4756, -4370, -3981, -3590, -3196, -2801, -2404, -2005, -1606, -1205, -804, -402,
    };

    // The above generated by:
    /*
        const short AMPLITUDE = short.MaxValue / 2;
        for (byte Line = 0; Line < 16; Line++)
        {
            for (byte Entry = 0; Entry < 16; Entry++)
            {
                Console.Write(Math.Round(AMPLITUDE * Math.Sin(((Line * 16) + Entry) * Math.Tau / 256)));
                Console.Write(", ");
            }
            Console.WriteLine();
        }
    */

    /// <summary> The number of octaves we will analyze. </summary>
    public static byte OctaveCount = 5;

    /// <summary> The number of frequency bins of data we will output per octave. </summary>
    public static byte BinsPerOctave = 24;

    /// <summary> How long our sample window is. </summary>
    public static ushort MaxWindowSize = 8192;

    /// <summary> The sample rate of the incoming audio signal, and our reference waveforms. </summary>
    public static uint SampleRate = 48000;

    public static float StartFrequency = 55F;

    /// <summary> The total number of bins over all octaves. </summary>
    public static ushort BinCount => (ushort)(OctaveCount * BinsPerOctave);

    /// <summary> Gets the index of the first bin in the topmost octave. </summary>
    private static ushort StartOfTopOctave => (ushort)((OctaveCount - 1) * BinsPerOctave);

    private static float StartFrequencyOfTopOctave => StartFrequency * MathF.Pow(2, OctaveCount - 1);

    /// <summary> Where raw and resampled audio data is stored. </summary>
    /// <remarks> Indexed by [Octave][Sample], size is [<see cref="OctaveCount"/>][<see cref="MaxWindowSize"/>] </remarks>
    private static short[][] AudioBuffer;

    /// <summary> How large the audio buffer should be treated as being, for each bin. </summary>
    /// <remarks> Indexed by [Bin], size is [<see cref="BinCount"/>] </remarks>
    private static ushort[] AudioBufferSizes;

    /// <summary> Where in the audio buffer each bin has added data up to. </summary>
    /// <remarks> Indexed by [Octave], size is [<see cref="OctaveCount"/>] </remarks>
    private static ushort[] AudioBufferAddHeads;

    /// <summary> Where in the audio buffer each bin has removed data up to. </summary>
    /// <remarks> Indexed by [Bin], size is [<see cref="BinCount"/>] </remarks>
    private static ushort[] AudioBufferSubHeads;

    /// <summary> How far forward in the sine table this bin should step with every added sample. Format is fixed-point 8b+8b. </summary>
    /// <remarks> Indexed by [Bin], size is [<see cref="BinsPerOctave"/>] </remarks>
    private static DualU16[] SinTableStepSize;

    /// <summary>
    /// Where in the sin table the bin is currently at. This is incremented by <see cref="SinTableStepSize"/> every sample.
    /// Format is fixed-point 8b+8b.
    /// Since addition and subtraction of data does not happen in-phase, both are tracked separately.
    /// </summary>
    /// <remarks> Indexed by [Bin], size is [<see cref="BinsPerOctave"/>] </remarks>
    private static DualU16[] SinTableLocationAdd, SinTableLocationSub;

    /// <summary> Stores the current value of the sin*sample and cos*sample product sums, for each bin. </summary>
    /// <remarks> Indexed by [Bin], size is [<see cref="BinCount"/>] </remarks>
    private static DualI64[] SinProductAccumulators, CosProductAccumulators;

    /// <summary> Stores the magnitude output of each bin before any filtering is done. </summary>
    /// <remarks> Indexed by [Bin], size is [<see cref="BinCount"/>] </remarks>
    public static float[] RawBinMagnitudes;

    private static ushort[] AddHeadIncrements;

    static ShinNoteFinderDFT()
    {
        Log.Info("Starting ShinNoteFinder DFT module");
        Reconfigure();
    }

    /// <summary> Reconfigures all settings and data structures, used when any configuration changes occur that require recalculating internal state. </summary>
    [MemberNotNull(
        nameof(AudioBuffer),
        nameof(AudioBufferSizes),
        nameof(AudioBufferAddHeads), nameof(AudioBufferSubHeads),
        nameof(SinTableStepSize), nameof(SinTableLocationAdd), nameof(SinTableLocationSub),
        nameof(SinProductAccumulators), nameof(CosProductAccumulators),
        nameof(RawBinMagnitudes)
    )]
    public static void Reconfigure()
    {
        AudioBuffer = new short[OctaveCount][];
        AudioBufferSizes = new ushort[BinCount];
        AudioBufferAddHeads = new ushort[OctaveCount];
        AudioBufferSubHeads = new ushort[BinCount];
        SinTableStepSize = new DualU16[BinsPerOctave];
        SinTableLocationAdd = new DualU16[BinsPerOctave];
        SinTableLocationSub = new DualU16[BinsPerOctave];
        SinProductAccumulators = new DualI64[BinCount];
        CosProductAccumulators = new DualI64[BinCount];
        RawBinMagnitudes = new float[BinCount];

        AddHeadIncrements = new ushort[BinCount];

        float TopStart = StartFrequencyOfTopOctave;

        ushort MaxAudioBufferSize = 0;
        // Operations that occur on all bins
        for (uint Bin = 0; Bin < BinCount; Bin++)
        {
            uint WrappedBinIndex = Bin % BinsPerOctave;

            float TopOctaveBinFreq = CalculateNoteFrequency(TopStart, BinsPerOctave, WrappedBinIndex);
            float TopOctaveNextBinFreq = CalculateNoteFrequency(TopStart, BinsPerOctave, WrappedBinIndex + 1);
            //float IdealWindowSize = WindowSizeForBinWidth(TopOctaveNextBinFreq - TopOctaveBinFreq); // TODO: Add scale factor to shift this from no overlap to -3dB point
            ushort ThisBufferSize = RoundedWindowSizeForBinWidth(TopOctaveNextBinFreq - TopOctaveBinFreq, TopOctaveBinFreq, SampleRate);//(ushort)Math.Ceiling(IdealWindowSize);
            AudioBufferSizes[Bin] = ThisBufferSize;

            if (Bin == 0) { MaxAudioBufferSize = ThisBufferSize; }

            AudioBufferSubHeads[Bin] = (ushort)(WrappedBinIndex == 0 ? 0 : (1 - ThisBufferSize + MaxAudioBufferSize) % MaxAudioBufferSize);
        }
        MaxWindowSize = MaxAudioBufferSize;

        // Operations that occur on only one octave's worth of bins
        for (uint Bin = 0; Bin < BinsPerOctave; Bin++)
        {
            uint BinInTop = StartOfTopOctave + Bin;
            float NCOffset = SampleRate / (AudioBufferSizes[BinInTop] * 2F);
            float StepSizeNCL = (ushort.MaxValue + 1.0F) * (CalculateNoteFrequency(StartFrequencyOfTopOctave, BinsPerOctave, Bin) - NCOffset) / SampleRate;
            float StepSizeNCR = (ushort.MaxValue + 1.0F) * (CalculateNoteFrequency(StartFrequencyOfTopOctave, BinsPerOctave, Bin) + NCOffset) / SampleRate;
            SinTableStepSize[Bin].NCLeft  = (ushort)Math.Round(StepSizeNCL);
            SinTableStepSize[Bin].NCRight = (ushort)Math.Round(StepSizeNCR);

            SinTableLocationSub[Bin].NCLeft  = (ushort)(-(SinTableStepSize[Bin].NCLeft  * (AudioBufferSizes[BinInTop] - (Bin == 0 ? 0 : 1))));
            SinTableLocationSub[Bin].NCRight = (ushort)(-(SinTableStepSize[Bin].NCRight * (AudioBufferSizes[BinInTop] - (Bin == 0 ? 0 : 1))));
        }

        // Operations that occur for each octave
        for (int Octave = 0; Octave < OctaveCount; Octave++)
        {
            AudioBuffer[Octave] = new short[MaxAudioBufferSize];
        }
    }

    public static void UpdateSampleRate(uint newSampleRate)
    {
        if (newSampleRate == SampleRate) { return; }
        SampleRate = newSampleRate;
        Reconfigure();
    }

    public static void AddAudioData(short[] newData)
    {
        // TODO: This handles the top octave only
        for (int i = 0; i < newData.Length; i++)
        {
            AddAudioDataToOctave(newData[i], OctaveCount - 1);
        }
    }

    private static void AddAudioDataToOctave(short newData, int octave)
    {
        int OctaveBinOffset = octave * BinsPerOctave;

        // Subtract old data from accumulators
        for (int Bin = 0; Bin < BinsPerOctave; Bin++)
        {
            int FullBinIndex = OctaveBinOffset + Bin;
            ushort SubHeadBefore = AudioBufferSubHeads[FullBinIndex];

            if (AddHeadIncrements[FullBinIndex] == AudioBufferSizes[FullBinIndex] - 1) { SinTableLocationSub[Bin] = new(); }

            // Find where we are in the sine table
            //if (SubHeadBefore == 0) { SinTableLocationSub[Bin] = new(); }
            DualU16 SinTableLoc = SinTableLocationSub[Bin];
            DualI16 SinValue = GetSine(SinTableLoc, false);
            DualI16 CosValue = GetSine(SinTableLoc, true);
            byte SinTableLocNCL = (byte)(SinTableLoc.NCLeft >> 8);
            byte SinTableLocNCR = (byte)(SinTableLoc.NCRight >> 8);

            //if (Bin == 0 && AudioBufferSubHeads[FullBinIndex] == 0) { Console.WriteLine("Subtracting at 0, pos: " + SinTableLoc); }

            // Multiply the outgoing sample by the correct sine sample
            short OldBufferData = AudioBuffer[octave][AudioBufferSubHeads[FullBinIndex]];
            //int OldSinProductNCL = SinWave[SinTableLocNCL] * OldBufferData;
            //int OldSinProductNCR = SinWave[SinTableLocNCR] * OldBufferData;
            //int OldCosProductNCL = SinWave[(byte)(SinTableLocNCL + SINE_TABLE_90_OFFSET)] * OldBufferData;
            //int OldCosProductNCR = SinWave[(byte)(SinTableLocNCR + SINE_TABLE_90_OFFSET)] * OldBufferData;
            int OldSinProductNCL = SinValue.NCLeft * OldBufferData;
            int OldSinProductNCR = SinValue.NCRight * OldBufferData;
            int OldCosProductNCL = CosValue.NCLeft * OldBufferData;
            int OldCosProductNCR = CosValue.NCRight * OldBufferData;

            // Remove the product from the accumulators
            SinProductAccumulators[FullBinIndex].NCLeft -= OldSinProductNCL;
            SinProductAccumulators[FullBinIndex].NCRight -= OldSinProductNCR;
            CosProductAccumulators[FullBinIndex].NCLeft -= OldCosProductNCL;
            CosProductAccumulators[FullBinIndex].NCRight -= OldCosProductNCR;

            // Advance the buffer and sine table locations
            AudioBufferSubHeads[FullBinIndex] = (ushort)((AudioBufferSubHeads[FullBinIndex] + 1) % MaxWindowSize);

            DualU16 SinTableStep = SinTableStepSize[Bin];
            SinTableLocationSub[Bin].NCLeft += SinTableStep.NCLeft;
            SinTableLocationSub[Bin].NCRight += SinTableStep.NCRight;

            if (Bin == 6) { Console.Write($"{SubHeadBefore},{SinTableLocNCL},{OldSinProductNCL},"); }
        }

        // Write new data
        ushort HeadBefore = AudioBufferAddHeads[octave];
        AudioBuffer[octave][AudioBufferAddHeads[octave]] = newData;
        AudioBufferAddHeads[octave] = (ushort)((AudioBufferAddHeads[octave] + 1) % MaxWindowSize);

        // Add new data to accumulators
        for (int Bin = 0; Bin < BinsPerOctave; Bin++)
        {
            int FullBinIndex = OctaveBinOffset + Bin;

            // Find where we are in the sine table
            AddHeadIncrements[FullBinIndex]++;
            if (AddHeadIncrements[FullBinIndex] == AudioBufferSizes[FullBinIndex]) { SinTableLocationAdd[Bin] = new(); AddHeadIncrements[FullBinIndex] = 0; }

            //if (HeadBefore == 0) { SinTableLocationAdd[Bin] = new(); }
            DualU16 SinTableLoc = SinTableLocationAdd[Bin];
            DualI16 SinValue = GetSine(SinTableLoc, false);
            DualI16 CosValue = GetSine(SinTableLoc, true);
            byte SinTableLocNCL = (byte)(SinTableLoc.NCLeft >> 8);
            byte SinTableLocNCR = (byte)(SinTableLoc.NCRight >> 8);

            //if (Bin == 0 && HeadBefore == 0) { Console.WriteLine("### This Step is " + SinTableStepSize[Bin] + "\nAdding at 0, pos: " + SinTableLoc); }

            // Multiply the incoming sample by the correct sine sample
            //int NewSinProductNCL = SinWave[SinTableLocNCL] * newData;
            //int NewSinProductNCR = SinWave[SinTableLocNCR] * newData;
            //int NewCosProductNCL = SinWave[(byte)(SinTableLocNCL + SINE_TABLE_90_OFFSET)] * newData;
            //int NewCosProductNCR = SinWave[(byte)(SinTableLocNCR + SINE_TABLE_90_OFFSET)] * newData;
            int NewSinProductNCL = SinValue.NCLeft * newData;
            int NewSinProductNCR = SinValue.NCRight * newData;
            int NewCosProductNCL = CosValue.NCLeft * newData;
            int NewCosProductNCR = CosValue.NCRight * newData;

            // Add the product to the accumulators
            SinProductAccumulators[FullBinIndex].NCLeft  += NewSinProductNCL;
            SinProductAccumulators[FullBinIndex].NCRight += NewSinProductNCR;
            CosProductAccumulators[FullBinIndex].NCLeft  += NewCosProductNCL;
            CosProductAccumulators[FullBinIndex].NCRight += NewCosProductNCR;

            // Advance the sine table locations
            DualU16 SinTableStep = SinTableStepSize[Bin];
            SinTableLocationAdd[Bin].NCLeft  += SinTableStep.NCLeft; // TODO: This needs to be FullBinIndex
            SinTableLocationAdd[Bin].NCRight += SinTableStep.NCRight;

            if (Bin == 6) { Console.WriteLine($"{HeadBefore},{SinTableLocNCL},{NewSinProductNCL},{SinProductAccumulators[FullBinIndex].NCLeft}"); }
        }
    }

    public static void CalculateOutput()
    {
        checked
        {
            for (int Bin = 0; Bin < BinCount; Bin++)
            {
                if (Bin >= 96) { Console.WriteLine($"{Bin},{SinProductAccumulators[Bin].NCLeft},{SinProductAccumulators[Bin].NCRight},{CosProductAccumulators[Bin].NCLeft},{CosProductAccumulators[Bin].NCRight}"); }
                double Sin = (double)SinProductAccumulators[Bin].NCLeft * SinProductAccumulators[Bin].NCRight;
                double Cos = (double)CosProductAccumulators[Bin].NCLeft * CosProductAccumulators[Bin].NCRight;
                float Magnitude = (float)Math.Sqrt(Math.Max(0F, -(Sin + Cos)));
                RawBinMagnitudes[Bin] = Magnitude;
                //double SimpleSq = ((double)SinProductAccumulators[Bin].NCLeft * SinProductAccumulators[Bin].NCLeft) + ((double)CosProductAccumulators[Bin].NCLeft * CosProductAccumulators[Bin].NCLeft);
                //RawBinMagnitudes[Bin] = (float)Math.Sqrt(Math.Max(0, SimpleSq)) / AudioBufferSizes[Bin];
            }
        }
    }

    public static DualI16 GetSine(DualU16 sineTablePosition, bool shiftForCos) // TODO: Determine if this added complexity is worth it
    {
        byte WholeLocationL = (byte)((sineTablePosition.NCLeft  >> 8) + (shiftForCos ? SINE_TABLE_90_OFFSET : 0));
        byte WholeLocationR = (byte)((sineTablePosition.NCRight >> 8) + (shiftForCos ? SINE_TABLE_90_OFFSET : 0));
        short ValueLowerL = SinWave[WholeLocationL];
        short ValueLowerR = SinWave[WholeLocationR];
        short ValueUpperL = SinWave[(byte)(WholeLocationL + 1)];
        short ValueUpperR = SinWave[(byte)(WholeLocationR + 1)];
        short FractionalPartL = (short)((((ValueUpperL - ValueLowerL) << 8) * (sineTablePosition.NCLeft  & 0xFF)) >> 16);
        short FractionalPartR = (short)((((ValueUpperR - ValueLowerR) << 8) * (sineTablePosition.NCRight & 0xFF)) >> 16);
        return new() { NCLeft = (short)(ValueLowerL + FractionalPartL), NCRight = (short)(ValueLowerR + FractionalPartR) };
    }

    public struct DualU16
    {
        public ushort NCLeft, NCRight;
        public override string ToString() => $"2xU16 L={this.NCLeft}, R={this.NCRight}";
    }
    public struct DualI16
    {
        public short NCLeft, NCRight;
        public override string ToString() => $"2xI16 L={this.NCLeft}, R={this.NCRight}";
    }
    public struct DualI64
    {
        public long NCLeft, NCRight;
        public override string ToString() => $"2xI32 L={this.NCLeft}, R={this.NCRight}";
    }

    // Everyone loves magic numbers :)
    // These were determined through simulations and regressions, which can be found in the Simulations folder in the root of the ColorChord.NET repository.
    private static float BinWidthAtWindowSize(float windowSize) => 50222.5926786413F / (windowSize + 11.483904495504245F);
    private static float WindowSizeForBinWidth(float binWidth) => Math.Min(MAX_WINDOW_SIZE, Math.Max(MIN_WINDOW_SIZE, (50222.5926786413F / binWidth) - 11.483904495504245F));

    private static ushort RoundedWindowSizeForBinWidth(float binWidth, float frequency, float sampleRate)
    {
        float IdealWindowSize = WindowSizeForBinWidth(binWidth);
        float PeriodInSamples = sampleRate / frequency;
        float PeriodsInWindow = IdealWindowSize / PeriodInSamples;
        return (ushort)MathF.Round(MathF.Round(PeriodsInWindow) * PeriodInSamples);
    }

    private static float CalculateNoteFrequency(float octaveStart, uint binsPerOctave, uint binIndex) => octaveStart * GetNoteFrequencyMultiplier(binsPerOctave, binIndex);
    private static float GetNoteFrequencyMultiplier(uint binsPerOctave, uint binIndex) => MathF.Pow(2F, (float)binIndex / binsPerOctave);
}
