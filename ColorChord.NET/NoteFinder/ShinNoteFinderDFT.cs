﻿using ColorChord.NET.API;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text;

namespace ColorChord.NET.NoteFinder;

/// <summary> My own note finder implementation. </summary>
public static class ShinNoteFinderDFT
{
    private const int MIN_WINDOW_SIZE = 16;
    private const uint MAX_WINDOW_SIZE = 6144;
    private const uint USHORT_RANGE = ushort.MaxValue + 1;

    private const ushort SINE_TABLE_90_OFFSET = 8;
    private const ushort SINE_TABLE_90_OFFSET_SCALED = 8 << 11;
    private static readonly short[] SinWave = new short[] { 0, 3196, 6270, 9102, 11585, 13622, 15136, 16068, 16383, 16068, 15136, 13622, 11585, 9102, 6270, 3196 };

    // Sine wave vectors generated by:
    /*
    const short AMPLITUDE = short.MaxValue / 2;
    short[] Values = new short[16];
    for (byte Entry = 0; Entry < 16; Entry++) { Values[Entry] = (short)Math.Round(AMPLITUDE * Math.Sin(Entry * Math.Tau / 32)); }
    Console.WriteLine("private static readonly short[] SinWave = new short[] { " + string.Join(", ", Values) + " };");
    string UpperByteList = string.Join(", ", Values.Select(val => "0x" + (val >>   8).ToString("X2")));
    string LowerByteList = string.Join(", ", Values.Select(val => "0x" + (val & 0xFF).ToString("X2")));
    Console.WriteLine($"Vector256<byte> SinWaveVecUpperB = Vector256.Create((byte){UpperByteList}, {UpperByteList});");
    Console.WriteLine($"Vector256<byte> SinWaveVecLowerB = Vector256.Create((byte){LowerByteList}, {LowerByteList});");
    */

    /// <summary> The number of octaves we will analyze. </summary>
    public static byte OctaveCount = 8;

    /// <summary> The number of frequency bins of data we will output per octave. </summary>
    public static byte BinsPerOctave = ShinNoteFinder.BINS_PER_OCTAVE;

    /// <summary> How long our sample window is. </summary>
    public static uint MaxPresentWindowSize = 8192;

    /// <summary> The sample rate of the incoming audio signal, and our reference waveforms. </summary>
    public static uint SampleRate = 48000;

    public static float StartFrequency = 27.5f;

    //private static uint GlobalSampleCounter = 0;

    /// <summary> The total number of bins over all octaves. </summary>
    public static ushort BinCount => (ushort)(OctaveCount * BinsPerOctave);

    /// <summary> Gets the index of the first bin in the topmost octave. </summary>
    private static ushort StartOfTopOctave => (ushort)((OctaveCount - 1) * BinsPerOctave);

    private static float StartFrequencyOfTopOctave => StartFrequency * MathF.Pow(2, OctaveCount - 1);

    /// <summary> Where raw and resampled audio data is stored. </summary>
    /// <remarks> Size is [<see cref="MaxPresentWindowSize"/>] </remarks>
    private static short[] AudioBuffer;//, AudioBuffer256;

    /// <summary> How large the audio buffer should be treated as being, for each bin. </summary>
    /// <remarks> Indexed by [Bin], size is [<see cref="BinCount"/>] </remarks>
    private static uint[] AudioBufferSizes;

    /// <summary> Up to where in the audio buffer data has been added to. </summary>
    private static ushort AudioBufferAddHead;//, AudioBufferAddHead256;

    /// <summary> Where in the audio buffer each bin has removed data up to. </summary>
    /// <remarks> Indexed by [Bin], size is [<see cref="BinCount"/>] </remarks>
    private static ushort[] AudioBufferSubHeads;//, AudioBufferSubHeads256;

    /// <summary> How far forward in the sine table this bin should step with every added sample, such that one full sine wave (wrap back to 0) occurs after the number of steps corresponding to the bin frequency. Format is fixed-point 5b+11b. </summary>
    /// <remarks> Indexed by [Bin], size is [<see cref="BinsPerOctave"/>] </remarks>
    private static DualU16[] SinTableStepSize;

    /// <summary>
    /// Where in the sin table the bin is currently at. This is incremented by <see cref="SinTableStepSize"/> every sample.
    /// Format is fixed-point 5b+11b.
    /// Since addition and subtraction of data does not happen in-phase, both are tracked separately.
    /// </summary>
    /// <remarks> Indexed by [Bin], size is [<see cref="BinCount"/>] </remarks>
    private static DualU16[] SinTableLocationAdd, SinTableLocationSub;//, SinTableLocationAdd256, SinTableLocationSub256;

    /// <summary> Stores the current value of the sin*sample and cos*sample product sums, for each bin. </summary>
    /// <remarks> Used instead of <see cref="ProductAccumulators"/> on non-AVX2 systems. Indexed by [Bin], size is [<see cref="BinCount"/>] </remarks>
    private static DualI64[] SinProductAccumulators, CosProductAccumulators;

    /// <summary> Stores the current value of the sin*sample and cos*sample product sums, for each bin. </summary>
    /// <remarks> Used instead of (<see cref="SinProductAccumulators"/>, <see cref="CosProductAccumulators"/>) on AVX2-enabled systems. Indexed by [Bin], size is [<see cref="BinCount"/>] </remarks>
    private static Vector256<long>[] ProductAccumulators;

    /// <summary> Stores the magnitude output of each bin before any filtering is done. </summary>
    /// <remarks> Indexed by [Bin], size is [<see cref="BinCount"/>] </remarks>
    public static float[] RawBinMagnitudes;

    private static int TEMP_CycleCount = 0;
    private static float TEMP_DFTTime = 0F;
    private static Stopwatch TEMP_Timer = new();

    static ShinNoteFinderDFT()
    {
        Log.Info("Starting ShinNoteFinder DFT module");
        Reconfigure();
    }

    /// <summary> Reconfigures all settings and data structures, used when any configuration changes occur that require recalculating internal state. </summary>
    [MemberNotNull(
        nameof(AudioBuffer),
        nameof(AudioBufferSizes),
        nameof(AudioBufferSubHeads),
        nameof(SinTableStepSize), nameof(SinTableLocationAdd), nameof(SinTableLocationSub),
        /*nameof(SinProductAccumulators), nameof(CosProductAccumulators),*/ nameof(ProductAccumulators),
        nameof(RawBinMagnitudes)
    )]
    public static void Reconfigure()
    {
        AudioBufferSizes = new uint[BinCount];
        AudioBufferSubHeads = new ushort[BinCount]; //AudioBufferSubHeads256 = new ushort[BinCount];
        SinTableStepSize = new DualU16[BinCount];
        SinTableLocationAdd = new DualU16[BinCount]; //SinTableLocationAdd256 = new DualU16[BinCount];
        SinTableLocationSub = new DualU16[BinCount]; //SinTableLocationSub256 = new DualU16[BinCount];
        SinProductAccumulators = new DualI64[BinCount];
        CosProductAccumulators = new DualI64[BinCount];
        ProductAccumulators = new Vector256<long>[BinCount];
        RawBinMagnitudes = new float[BinCount];

        //float TopStart = StartFrequencyOfTopOctave;

        uint MaxAudioBufferSize = 0;
        // Operations that occur on all bins
        for (uint Bin = 0; Bin < BinCount; Bin++)
        {
            //uint WrappedBinIndex = Bin % BinsPerOctave;

            float ThisOctaveStart = StartFrequency * MathF.Pow(2, Bin / BinsPerOctave);
            float TopOctaveBinFreq = CalculateNoteFrequency(StartFrequency, BinsPerOctave, Bin);
            float TopOctaveNextBinFreq = CalculateNoteFrequency(StartFrequency, BinsPerOctave, Bin + 2);
            //float IdealWindowSize = WindowSizeForBinWidth(TopOctaveNextBinFreq - TopOctaveBinFreq); // TODO: Add scale factor to shift this from no overlap to -3dB point
            uint ThisBufferSize = RoundedWindowSizeForBinWidth(TopOctaveNextBinFreq - TopOctaveBinFreq, TopOctaveBinFreq, SampleRate);
            //ushort ThisBufferSize = (ushort)Math.Ceiling(IdealWindowSize);
            AudioBufferSizes[Bin] = ThisBufferSize; //Math.Min(MAX_WINDOW_SIZE, ThisBufferSize);

            MaxAudioBufferSize = Math.Max(MaxAudioBufferSize, ThisBufferSize);

            

            //uint BinInTop = StartOfTopOctave + Bin;
            float NCOffset = SampleRate / (AudioBufferSizes[Bin] * 2F);
            float StepSizeNCL = USHORT_RANGE * (CalculateNoteFrequency(StartFrequency, BinsPerOctave, Bin) - NCOffset) / SampleRate;
            float StepSizeNCR = USHORT_RANGE * (CalculateNoteFrequency(StartFrequency, BinsPerOctave, Bin) + NCOffset) / SampleRate;
            SinTableStepSize[Bin].NCLeft = (ushort)Math.Round(StepSizeNCL);
            SinTableStepSize[Bin].NCRight = (ushort)Math.Round(StepSizeNCR);
            //Console.WriteLine($"Bin becomes L {CalculateNoteFrequency(StartFrequency, BinsPerOctave, Bin) - NCOffset} and R {CalculateNoteFrequency(StartFrequency, BinsPerOctave, Bin) + NCOffset} at len {AudioBufferSizes[Bin]}");
        }
        if ((MaxAudioBufferSize & 15) != 0) { MaxAudioBufferSize = ((MaxAudioBufferSize & 0xFFFFFFF0) + 16); } // Round buffer size up to next multiple of 16 to make vector ops nicer
        MaxPresentWindowSize = MaxAudioBufferSize;

        AudioBuffer = new short[MaxAudioBufferSize];
        //AudioBuffer256 = new short[MaxAudioBufferSize];

        for (uint Bin = 0; Bin < BinCount; Bin++)
        {
            //AudioBufferSubHeads[Bin] = (ushort)(Bin == 0 ? 0 : (1 - AudioBufferSizes[Bin] + MaxAudioBufferSize) % MaxAudioBufferSize);
            AudioBufferSubHeads[Bin] = (ushort)((1 - AudioBufferSizes[Bin] + MaxAudioBufferSize) % MaxAudioBufferSize);
            //AudioBufferSubHeads256[Bin] = AudioBufferSubHeads[Bin];
        }

        // Operations that occur on only one octave's worth of bins
        /*for (uint Bin = 0; Bin < BinsPerOctave; Bin++)
        {
            uint BinInTop = StartOfTopOctave + Bin;
            float NCOffset = SampleRate / (AudioBufferSizes[BinInTop] * 2F);
            float StepSizeNCL = USHORT_RANGE * (CalculateNoteFrequency(StartFrequencyOfTopOctave, BinsPerOctave, Bin) - NCOffset) / SampleRate;
            float StepSizeNCR = USHORT_RANGE * (CalculateNoteFrequency(StartFrequencyOfTopOctave, BinsPerOctave, Bin) + NCOffset) / SampleRate;
            SinTableStepSize[Bin].NCLeft  = (ushort)Math.Round(StepSizeNCL);
            SinTableStepSize[Bin].NCRight = (ushort)Math.Round(StepSizeNCR);
        }*/

        // All bins again, but needs data calculated after the previous one
        for (uint Bin = 0; Bin < BinCount; Bin++) // TODO: See if this can be optimized, low priority since it should happen very rarely
        {
            //uint WrappedBinIndex = Bin % BinsPerOctave;
            SinTableLocationSub[Bin].NCLeft  = (ushort)(-(SinTableStepSize[Bin].NCLeft  * (AudioBufferSizes[Bin] - (Bin == 0 ? 0 : 1))));
            SinTableLocationSub[Bin].NCRight = (ushort)(-(SinTableStepSize[Bin].NCRight * (AudioBufferSizes[Bin] - (Bin == 0 ? 0 : 1))));
            //SinTableLocationSub256[Bin] = SinTableLocationSub[Bin];
        }

        Log.Debug(nameof(ShinNoteFinder) + " bin window lengths:");
        for (int Octave = 0; Octave < OctaveCount; Octave++)
        {
            StringBuilder OctaveOutput = new();
            OctaveOutput.Append($"{StartFrequency * Math.Pow(2, Octave):F0}Hz~: ");
            for (uint Bin = 0; Bin < BinsPerOctave; Bin++)
            {
                OctaveOutput.Append(AudioBufferSizes[(Octave * BinsPerOctave) + Bin]);
                OctaveOutput.Append(',');
            }
            Log.Debug(OctaveOutput.ToString());
        }
    }

    public static void UpdateSampleRate(uint newSampleRate)
    {
        if (newSampleRate == SampleRate) { return; }
        SampleRate = newSampleRate;
        Reconfigure();
    }

    public static void AddAudioData(Span<short> newData, bool useVec = USE_VECTORIZED)
    {
        if (Avx2.IsSupported && useVec)
        {
            Debug.Assert((newData.Length & 15) == 0, $"Length of new audio data ({newData.Length}) is not a multiple of 16. This makes SIMD mode sad :(");
            for (int i = 0; i < newData.Length; i += 16) { AddAudioData256(Vector256.LoadUnsafe(ref newData[i])); }
        }
        else
        {
            for (int i = 0; i < newData.Length; i++)
            {
                AddAudioDataToOctave(newData[i]);
                //GlobalSampleCounter++;
            }
        }
    }

    public static void AddAudioData(Span<float> newData, bool useVec = USE_VECTORIZED) // TODO: Consider removing support for float audio data?
    {
        TEMP_CycleCount++;
        TEMP_Timer.Restart();
        if (Avx2.IsSupported && useVec)
        {
            //Debug.Assert((newData.Length & 15) == 0, $"Length of new audio data ({newData.Length}) is not a multiple of 16. This makes SIMD mode sad :(");
            int i = 0;
            for (i = 0; i < newData.Length && (i + 16) <= newData.Length; i += 16) // TODO: Ugh get rid of all this garbage by just taking in shorts instead
            {
                Vector256<float> NewDataF32A = Vector256.LoadUnsafe(ref newData[i]); // TODO: Figure out how to use ReadOnlySpan with this instead
                Vector256<int> NewDataI32A = Avx.ConvertToVector256Int32(Avx.Multiply(NewDataF32A, Vector256.Create(short.MaxValue - 0.5F)));
                Vector256<float> NewDataF32B = Vector256.LoadUnsafe(ref newData[i + 8]);
                Vector256<int> NewDataI32B = Avx.ConvertToVector256Int32(Avx.Multiply(NewDataF32B, Vector256.Create(short.MaxValue - 0.5F)));
                Vector256<int> NewDataI32SwappedA = Avx2.Permute2x128(NewDataI32A, NewDataI32B, 0b00100000);
                Vector256<int> NewDataI32SwappedB = Avx2.Permute2x128(NewDataI32A, NewDataI32B, 0b00110001);
                Vector256<short> NewData = Avx2.PackSignedSaturate(NewDataI32SwappedA, NewDataI32SwappedB);
                AddAudioData256(NewData);
            }
            if (i != newData.Length)
            {
                Console.WriteLine($"Awkward {newData.Length - i} items left (did {i} of {newData.Length} vectorized)");
                for (; i < newData.Length; i++)
                {
                    short NewData = (short)(newData[i] * short.MaxValue);
                    AddAudioDataToOctave(NewData);
                }
            }
        }
        else
        {
            for (int i = 0; i < newData.Length; i++)
            {
                short NewData = (short)(newData[i] * short.MaxValue);
                AddAudioDataToOctave(NewData);
                //GlobalSampleCounter++;
            }
        }
        TEMP_Timer.Stop();
        float TicksPerSample = (float)TEMP_Timer.ElapsedTicks / newData.Length;
        if (newData.Length > 10) { TEMP_DFTTime = (0.97F * TEMP_DFTTime) + (0.03F * TicksPerSample); }
        if (TEMP_CycleCount % 50 == 0) { Console.WriteLine($"Currently taking {TEMP_DFTTime * 100} ns per audio sample."); }
    }

    private const bool USE_VECTORIZED = true;

    private static void AddAudioData256(Vector256<short> newData)
    {
        Vector256<ushort> IncrementingFrom0 = Vector256.Create((ushort)0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15);
        Vector256<ushort> CosShift = Vector256.Create(SINE_TABLE_90_OFFSET_SCALED);

        ushort BinCount = ShinNoteFinderDFT.BinCount;
        for (int Bin = 0; Bin < BinCount; Bin++)
        {
            Vector256<short> OldData;
            ushort SubHeadHere = AudioBufferSubHeads[Bin];
            if ((SubHeadHere + 16) > MaxPresentWindowSize) // Wrapping around the end of the audio buffer, manually copy the data instead
            { // TODO: This should be optimized to 2 vector loads and a merge, just requires the buffer be padded such that running up to 16xU16s off the end is OK.
                short[] OldDataArr = new short[16];
                for (int i = 0; i < OldDataArr.Length; i++)
                {
                    OldDataArr[i] = AudioBuffer[SubHeadHere];
                    SubHeadHere = (ushort)((SubHeadHere + 1) % MaxPresentWindowSize);
                }
                OldData = Vector256.Create(OldDataArr);
            }
            else
            {
                OldData = Vector256.LoadUnsafe(ref AudioBuffer[SubHeadHere]);
                SubHeadHere = (ushort)((SubHeadHere + 16) % MaxPresentWindowSize);
            }

            DualU16 SinTableStep = SinTableStepSize[Bin];
            Vector256<ushort> LeftSteps  = Avx2.MultiplyLow(Vector256.Create(SinTableStep.NCLeft ), IncrementingFrom0);
            Vector256<ushort> RightSteps = Avx2.MultiplyLow(Vector256.Create(SinTableStep.NCRight), IncrementingFrom0);

            Vector256<long> OldProducts;
            {
                DualU16 SinTableLocSub = SinTableLocationSub[Bin];
                Vector256<ushort> SinLocationsL = Avx2.Add(Vector256.Create(SinTableLocSub.NCLeft), LeftSteps);
                Vector256<ushort> SinLocationsR = Avx2.Add(Vector256.Create(SinTableLocSub.NCRight), RightSteps);

                Vector256<short> SinValuesL = GetSine256(SinLocationsL);
                Vector256<int> SinLCombined = Avx2.MultiplyAddAdjacent(SinValuesL, OldData);

                Vector256<short> SinValuesR = GetSine256(SinLocationsR);
                Vector256<int> SinRCombined = Avx2.MultiplyAddAdjacent(SinValuesR, OldData);

                Vector256<short> CosValuesL = GetSine256(Avx2.Add(SinLocationsL, CosShift));
                Vector256<int> CosLCombined = Avx2.MultiplyAddAdjacent(CosValuesL, OldData);

                Vector256<short> CosValuesR = GetSine256(Avx2.Add(SinLocationsR, CosShift));
                Vector256<int> CosRCombined = Avx2.MultiplyAddAdjacent(CosValuesR, OldData);
                // None of the above 4 steps will overflow unless both audio data and sine value are -32768.

                Vector256<int> SinCombined = Avx2.HorizontalAdd(SinLCombined, SinRCombined); // Result: [SinL, SinL, SinR, SinR, SinL, SinL, SinR, SinR]
                Vector256<int> CosCombined = Avx2.HorizontalAdd(CosLCombined, CosRCombined); // Result: [CosL, CosL, CosR, CosR, CosL, CosL, CosR, CosR]
                Vector256<int> MixedCombined = Avx2.HorizontalAdd(SinCombined, CosCombined); // Result: [SinL, SinR, CosL, CosR, SinL, SinR, CosL, CosR]
                OldProducts = Avx2.Add(Avx2.ConvertToVector256Int64(MixedCombined.GetLower()), Avx2.ConvertToVector256Int64(MixedCombined.GetUpper())); // Result: [SinL, SinR, CosL, CosR]
                // TODO: THIS IS ONLY GUARANTEED NOT TO OVERFLOW IF THE SIN TABLE AMPLITUDE IS REDUCED TO 16384?
            }

            Vector256<long> NewProducts;
            {
                DualU16 SinTableLocAdd = SinTableLocationAdd[Bin];
                Vector256<ushort> SinLocationsL = Avx2.Add(Vector256.Create(SinTableLocAdd.NCLeft), LeftSteps);
                Vector256<ushort> SinLocationsR = Avx2.Add(Vector256.Create(SinTableLocAdd.NCRight), RightSteps);

                Vector256<short> SinValuesL = GetSine256(SinLocationsL);
                Vector256<int> SinLCombined = Avx2.MultiplyAddAdjacent(SinValuesL, newData);

                Vector256<short> SinValuesR = GetSine256(SinLocationsR);
                Vector256<int> SinRCombined = Avx2.MultiplyAddAdjacent(SinValuesR, newData);

                Vector256<short> CosValuesL = GetSine256(Avx2.Add(SinLocationsL, CosShift));
                Vector256<int> CosLCombined = Avx2.MultiplyAddAdjacent(CosValuesL, newData);

                Vector256<short> CosValuesR = GetSine256(Avx2.Add(SinLocationsR, CosShift));
                Vector256<int> CosRCombined = Avx2.MultiplyAddAdjacent(CosValuesR, newData);
                // None of the above 4 steps will overflow unless both audio data and sine value are -32768.

                Vector256<int> SinCombined = Avx2.HorizontalAdd(SinLCombined, SinRCombined); // Result: [SinL, SinL, SinR, SinR, SinL, SinL, SinR, SinR]
                Vector256<int> CosCombined = Avx2.HorizontalAdd(CosLCombined, CosRCombined); // Result: [CosL, CosL, CosR, CosR, CosL, CosL, CosR, CosR]
                Vector256<int> MixedCombined = Avx2.HorizontalAdd(SinCombined, CosCombined); // Result: [SinL, SinR, CosL, CosR, SinL, SinR, CosL, CosR]
                NewProducts = Avx2.Add(Avx2.ConvertToVector256Int64(MixedCombined.GetLower()), Avx2.ConvertToVector256Int64(MixedCombined.GetUpper())); // Result: [SinL, SinR, CosL, CosR]
                // TODO: THIS IS ONLY GUARANTEED NOT TO OVERFLOW IF THE SIN TABLE AMPLITUDE IS REDUCED TO 16384?

                if(Bin == 191)
                {
                    int b = 5;
                }
            }

            Vector256<long> AccumulatorDeltas = Avx2.Subtract(NewProducts, OldProducts);
            ProductAccumulators[Bin] = Avx2.Add(ProductAccumulators[Bin], AccumulatorDeltas);

            AudioBufferSubHeads[Bin] = SubHeadHere;
            SinTableLocationSub[Bin].NCLeft  += (ushort)(SinTableStep.NCLeft  * 16); // TODO: Pre-calc and store these?
            SinTableLocationSub[Bin].NCRight += (ushort)(SinTableStep.NCRight * 16);

            SinTableLocationAdd[Bin].NCLeft  += (ushort)(SinTableStep.NCLeft  * 16);
            SinTableLocationAdd[Bin].NCRight += (ushort)(SinTableStep.NCRight * 16);
        }

        ushort AddHead = AudioBufferAddHead;
        for (int i = 0; i < 16; i++)
        {
            AudioBuffer[AddHead] = newData[i];
            AddHead = (ushort)((AddHead + 1) % MaxPresentWindowSize);
        }
        AudioBufferAddHead = AddHead;
    }

    private static void AddAudioDataToOctave(short newData)
    {
        ushort BinCount = ShinNoteFinderDFT.BinCount;
        // Subtract old data from accumulators
        for (int Bin = 0; Bin < BinCount; Bin++)
        {
            // Find where we are in the sine table
            DualU16 SinTableLoc = SinTableLocationSub[Bin];
            DualI16 SinValue = GetSine(SinTableLoc, false);
            DualI16 CosValue = GetSine(SinTableLoc, true);

            // Multiply the outgoing sample by the correct sine sample
            short OldBufferData = AudioBuffer[AudioBufferSubHeads[Bin]];
            int OldSinProductNCL = SinValue.NCLeft  * OldBufferData;
            int OldSinProductNCR = SinValue.NCRight * OldBufferData;
            int OldCosProductNCL = CosValue.NCLeft  * OldBufferData;
            int OldCosProductNCR = CosValue.NCRight * OldBufferData;

            // Remove the product from the accumulators
            //ProductAccumulators[Bin] = ProductAccumulators[Bin] - Vector256.Create(OldSinProductNCL, OldSinProductNCR, OldCosProductNCL, OldCosProductNCR); // TODO: WTF? This just absolutely explodes sometimes???
            checked
            {
                Vector256<long> CurrentAccVal = ProductAccumulators[Bin]; // TODO: This does really weird things in release mode. Need to investigate!
                Vector256<long> NewAccVal = Vector256.Create(CurrentAccVal[0] - OldSinProductNCL, CurrentAccVal[1] - OldSinProductNCR, CurrentAccVal[2] - OldCosProductNCL, CurrentAccVal[3] - OldCosProductNCR);
                ProductAccumulators[Bin] = NewAccVal;
                SinProductAccumulators[Bin].NCLeft -= OldSinProductNCL;
                SinProductAccumulators[Bin].NCRight -= OldSinProductNCR;
                CosProductAccumulators[Bin].NCLeft -= OldCosProductNCL;
                CosProductAccumulators[Bin].NCRight -= OldCosProductNCR;
            }

            // Advance the buffer and sine table locations
            AudioBufferSubHeads[Bin] = (ushort)((AudioBufferSubHeads[Bin] + 1) % MaxPresentWindowSize);

            DualU16 SinTableStep = SinTableStepSize[Bin];
            SinTableLocationSub[Bin].NCLeft  += SinTableStep.NCLeft;
            SinTableLocationSub[Bin].NCRight += SinTableStep.NCRight;
        }

        // Write new data
        ushort HeadBefore = AudioBufferAddHead;
        AudioBuffer[HeadBefore] = newData;
        AudioBufferAddHead = (ushort)((HeadBefore + 1) % MaxPresentWindowSize);

        // Add new data to accumulators
        for (int Bin = 0; Bin < BinCount; Bin++)
        {
            // Find where we are in the sine table
            DualU16 SinTableLoc = SinTableLocationAdd[Bin];
            DualI16 SinValue = GetSine(SinTableLoc, false);
            DualI16 CosValue = GetSine(SinTableLoc, true);

            // Multiply the incoming sample by the correct sine sample
            int NewSinProductNCL = SinValue.NCLeft  * newData;
            int NewSinProductNCR = SinValue.NCRight * newData;
            int NewCosProductNCL = CosValue.NCLeft  * newData;
            int NewCosProductNCR = CosValue.NCRight * newData;

            // Add the product to the accumulators
            //ProductAccumulators[Bin] = ProductAccumulators[Bin] + Vector256.Create(NewSinProductNCL, NewSinProductNCR, NewCosProductNCL, NewCosProductNCR);
            checked
            {
                Vector256<long> CurrentAccVal = ProductAccumulators[Bin];
                Vector256<long> NewAccVal = Vector256.Create(CurrentAccVal[0] + NewSinProductNCL, CurrentAccVal[1] + NewSinProductNCR, CurrentAccVal[2] + NewCosProductNCL, CurrentAccVal[3] + NewCosProductNCR);
                ProductAccumulators[Bin] = NewAccVal;
                SinProductAccumulators[Bin].NCLeft += NewSinProductNCL;
                SinProductAccumulators[Bin].NCRight += NewSinProductNCR;
                CosProductAccumulators[Bin].NCLeft += NewCosProductNCL;
                CosProductAccumulators[Bin].NCRight += NewCosProductNCR;
            }

            // Advance the sine table locations
            DualU16 SinTableStep = SinTableStepSize[Bin];
            SinTableLocationAdd[Bin].NCLeft  += SinTableStep.NCLeft;
            SinTableLocationAdd[Bin].NCRight += SinTableStep.NCRight;
            //if (Bin == 191) { Debug.WriteLine($"{SinTableLoc.NCLeft},{SinTableLoc.NCRight},{SinValue.NCLeft},{SinValue.NCRight},{CosValue.NCLeft},{CosValue.NCRight},{NewSinProductNCL},{NewSinProductNCR},{NewCosProductNCL},{NewCosProductNCR}"); }
        }
    }

    private static double[] SmoothedSin = new double[500];
    private static double[] SmoothedCos = new double[500];

    public static void CalculateOutput(bool useVec = USE_VECTORIZED)
    {
        for (int Bin = 0; Bin < BinsPerOctave; Bin++) { ShinNoteFinder.OctaveBinValues[Bin] = 0; }

        checked
        {
            for (int Bin = 0; Bin < BinCount; Bin++)
            {
                //if (Bin >= 96) { Console.WriteLine($"{Bin},{SinProductAccumulators[Bin].NCLeft},{SinProductAccumulators[Bin].NCRight},{CosProductAccumulators[Bin].NCLeft},{CosProductAccumulators[Bin].NCRight}"); }
                
                // TODO: MegaJanky. Just for developing the SIMD version, will fix later
                (double Sin, double Cos) RotatedL;
                (double Sin, double Cos) RotatedR;
                if (Avx2.IsSupported && useVec)
                {
                    DualU16 CurrentSineLocations = SinTableLocationAdd[Bin];
                    float AngleL = CurrentSineLocations.NCLeft * MathF.Tau / USHORT_RANGE;
                    float AngleR = CurrentSineLocations.NCRight * MathF.Tau / USHORT_RANGE;

                    RotatedL = RotatePoint(ProductAccumulators[Bin][0], ProductAccumulators[Bin][2], AngleL);
                    RotatedR = RotatePoint(ProductAccumulators[Bin][1], ProductAccumulators[Bin][3], AngleR);
                }
                else
                {
                    DualU16 CurrentSineLocations = SinTableLocationAdd[Bin];
                    float AngleL = CurrentSineLocations.NCLeft * MathF.Tau / USHORT_RANGE;
                    float AngleR = CurrentSineLocations.NCRight * MathF.Tau / USHORT_RANGE;

                    //RotatedL = RotatePoint(SinProductAccumulators[Bin].NCLeft,  CosProductAccumulators[Bin].NCLeft,  AngleL);
                    //RotatedR = RotatePoint(SinProductAccumulators[Bin].NCRight, CosProductAccumulators[Bin].NCRight, AngleR);
                    RotatedL = RotatePoint(ProductAccumulators[Bin][0], ProductAccumulators[Bin][2], AngleL);
                    RotatedR = RotatePoint(ProductAccumulators[Bin][1], ProductAccumulators[Bin][3], AngleR);
                }

                double Sin = RotatedL.Sin * RotatedR.Sin;
                double Cos = RotatedL.Cos * RotatedR.Cos;

                float IIR_CONST = 0.65F;
                //float IIR_CONST = 1.0F;
                SmoothedSin[Bin] = (SmoothedSin[Bin] * (1 - IIR_CONST)) + (Sin * IIR_CONST);
                SmoothedCos[Bin] = (SmoothedCos[Bin] * (1 - IIR_CONST)) + (Cos * IIR_CONST);


                //float Magnitude = (float)Math.Sqrt(Math.Max(0F, -(Sin + Cos)));
                float Magnitude = (float)Math.Sqrt(Math.Max(0F, -(SmoothedSin[Bin] + SmoothedCos[Bin])));
                RawBinMagnitudes[Bin] = Magnitude / AudioBufferSizes[Bin];

                // Traditional DFT for debugging
                //double SimpleSq = ((double)SinProductAccumulators[Bin].NCLeft * SinProductAccumulators[Bin].NCLeft) + ((double)CosProductAccumulators[Bin].NCLeft * CosProductAccumulators[Bin].NCLeft);
                //RawBinMagnitudes[Bin] = (float)Math.Sqrt(Math.Max(0, SimpleSq)) / AudioBufferSizes[Bin];


                float OutBinVal = MathF.Sqrt(RawBinMagnitudes[Bin]) / 3000;
                ShinNoteFinder.OctaveBinValues[Bin % BinsPerOctave] += OutBinVal / OctaveCount;
                ShinNoteFinder.AllBinValues[Bin] = OutBinVal;
            }
        }
    }

    private static ValueTuple<double, double> RotatePoint(double x, double y, double angle)
    {
        double AngleSin = Math.Sin(angle);
        double AngleCos = Math.Cos(angle);
        double NewX = (x * AngleCos) - (y * AngleSin);
        double NewY = (x * AngleSin) + (y * AngleCos);
        return new(NewX, NewY);
    }

    public static DualI16 GetSine(DualU16 sineTablePosition, bool shiftForCos) // TODO: SIMD this >:D
    {
        byte WholeLocationL = (byte)(((sineTablePosition.NCLeft  >> 11) + (shiftForCos ? SINE_TABLE_90_OFFSET : 0)) & 0b11111);
        byte WholeLocationR = (byte)(((sineTablePosition.NCRight >> 11) + (shiftForCos ? SINE_TABLE_90_OFFSET : 0)) & 0b11111);
        int LocationModifierL = (WholeLocationL << 27) >> 31;
        int LocationModifierR = (WholeLocationR << 27) >> 31;
        short ValueLowerL = (short)((SinWave[WholeLocationL & 0b1111] ^ LocationModifierL) - LocationModifierL); // The sine vector only contains the positive half of the wave, indices in the range 16...31 are just 0...15 but negative value
        short ValueLowerR = (short)((SinWave[WholeLocationR & 0b1111] ^ LocationModifierR) - LocationModifierR);

        byte AdjacentLocationL = (byte)((WholeLocationL + 1) & 0b11111);
        byte AdjacentLocationR = (byte)((WholeLocationR + 1) & 0b11111);
        int AdjLocationModifierL = (AdjacentLocationL << 27) >> 31;
        int AdjLocationModifierR = (AdjacentLocationR << 27) >> 31;
        short ValueUpperL = (short)((SinWave[AdjacentLocationL & 0b1111] ^ AdjLocationModifierL) - AdjLocationModifierL);
        short ValueUpperR = (short)((SinWave[AdjacentLocationR & 0b1111] ^ AdjLocationModifierR) - AdjLocationModifierR);

        short FractionalPartL = (short)((((ValueUpperL - ValueLowerL) << 8) * ((sineTablePosition.NCLeft  >> 3) & 0xFF)) >> 16); // Wasting the bottom 3 bits of position precision, but otherwise multiplication may overflow int
        short FractionalPartR = (short)((((ValueUpperR - ValueLowerR) << 8) * ((sineTablePosition.NCRight >> 3) & 0xFF)) >> 16);
        return new() { NCLeft = (short)(ValueLowerL + FractionalPartL), NCRight = (short)(ValueLowerR + FractionalPartR) };
    }

    public static short GetSine1(ushort sineTablePosition)
    {
        byte WholeLocation = (byte)((sineTablePosition >> 11) & 0b11111);
        int LocationModifier = (WholeLocation << 27) >> 31;
        short ValueLower = (short)((SinWave[WholeLocation & 0b1111] ^ LocationModifier) - LocationModifier); // The sine vector only contains the positive half of the wave, indices in the range 16...31 are just 0...15 but negative value

        byte AdjacentLocation = (byte)((WholeLocation + 1) & 0b11111);
        int AdjLocationModifier = (AdjacentLocation << 27) >> 31;
        short ValueUpper = (short)((SinWave[AdjacentLocation & 0b1111] ^ AdjLocationModifier) - AdjLocationModifier);

        short FractionalPart = (short)((((ValueUpper - ValueLower) << 8) * ((sineTablePosition >> 3) & 0xFF)) >> 16); // Wasting the bottom 3 bits of position precision, but otherwise multiplication may overflow int
        return (short)(ValueLower + FractionalPart);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector256<short> GetSine256(Vector256<ushort> positions)
    {
        // See code at the top of this file for how these 2 are generated.
        Vector256<byte> SinWaveVecUpperB = Vector256.Create((byte)0x00, 0x0C, 0x18, 0x23, 0x2D, 0x35, 0x3B, 0x3E, 0x3F, 0x3E, 0x3B, 0x35, 0x2D, 0x23, 0x18, 0x0C, 0x00, 0x0C, 0x18, 0x23, 0x2D, 0x35, 0x3B, 0x3E, 0x3F, 0x3E, 0x3B, 0x35, 0x2D, 0x23, 0x18, 0x0C);
        Vector256<byte> SinWaveVecLowerB = Vector256.Create((byte)0x00, 0x7C, 0x7E, 0x8E, 0x41, 0x36, 0x20, 0xC4, 0xFF, 0xC4, 0x20, 0x36, 0x41, 0x8E, 0x7E, 0x7C, 0x00, 0x7C, 0x7E, 0x8E, 0x41, 0x36, 0x20, 0xC4, 0xFF, 0xC4, 0x20, 0x36, 0x41, 0x8E, 0x7E, 0x7C);

        Vector256<ushort> LocationsLeft = Avx2.ShiftRightLogical(positions, 11); // Between 0 and 31, shuffles map this as [0~31] => [0~15, 0~15]
        Vector256<ushort> LocationsRight = Avx2.Add(LocationsLeft, Vector256.Create((ushort)1)); // Between 1 and 32, but because the shuffles only look at the bottom 4 bits, is effectively mapped as [1~32] => [1~15, 0~15, 0]

        Vector256<byte> MixedLocations = Avx2.PackUnsignedSaturate(LocationsLeft.AsInt16(), LocationsRight.AsInt16()); // [LeftLocations[0:7], RightLocations[0:7], LeftLocations[8:15], RightLocations[8:15]]
        Vector256<byte> MixedSinValuesLower = Avx2.Shuffle(SinWaveVecLowerB, MixedLocations);
        Vector256<byte> MixedSinValuesUpper = Avx2.Shuffle(SinWaveVecUpperB, MixedLocations);

        Vector256<ushort> ValuesUnsignedLeft  = Avx2.UnpackLow (MixedSinValuesLower, MixedSinValuesUpper).AsUInt16();
        Vector256<ushort> ValuesUnsignedRight = Avx2.UnpackHigh(MixedSinValuesLower, MixedSinValuesUpper).AsUInt16();
        Vector256<short> ValuesLeft  = Avx2.Sign(ValuesUnsignedLeft.AsInt16(),  positions.AsInt16());
        Vector256<short> ValuesRight = Avx2.Sign(ValuesUnsignedRight.AsInt16(), Avx2.ShiftLeftLogical(LocationsRight, 11).AsInt16());

        Vector256<short> Values;
        {
            // Sneaky trick to avoid overflow: instead of doing ((ValueDiffs << 8) * (Positions >> 3)) >> 16, instead do ((ValueDiffs << 1) * ((Positions << 5) >> 1)) >> 16 which is the same.
            Vector256<short> ValueDiffs = Avx2.ShiftLeftLogical(Avx2.Subtract(ValuesRight, ValuesLeft), 1);
            Vector256<short> FractionalPositions = Avx2.ShiftRightLogical(Avx2.ShiftLeftLogical(positions, 5), 1).AsInt16();
            Vector256<short> FractionalValues = Avx2.MultiplyHigh(ValueDiffs, FractionalPositions);
            Values = Avx2.Add(ValuesLeft, FractionalValues);
        }
        
        return Values;
    }

    public struct DualU16
    {
        public ushort NCLeft, NCRight;
        public override readonly string ToString() => $"2xU16 L={this.NCLeft}, R={this.NCRight}";
    }
    public struct DualI16
    {
        public short NCLeft, NCRight;
        public override readonly string ToString() => $"2xI16 L={this.NCLeft}, R={this.NCRight}";
    }
    public struct DualI64
    {
        public long NCLeft, NCRight;
        public override readonly string ToString() => $"2xI32 L={this.NCLeft}, R={this.NCRight}";
    }

    // Everyone loves magic numbers :)
    // These were determined through simulations and regressions, which can be found in the Simulations folder in the root of the ColorChord.NET repository.
    private static float BinWidthAtWindowSize(float windowSize) => 50222.5926786413F / (windowSize + 11.483904495504245F);
    private static float WindowSizeForBinWidth(float binWidth) => (50222.5926786413F / binWidth) - 11.483904495504245F;

    private static uint RoundedWindowSizeForBinWidth(float binWidth, float frequency, float sampleRate)
    {
        float IdealWindowSize = WindowSizeForBinWidth(binWidth);
        float PeriodInSamples = sampleRate / frequency;
        float PeriodsInWindow = IdealWindowSize / PeriodInSamples;
        float MaxWindowSizePeriods = MathF.Ceiling(MAX_WINDOW_SIZE / PeriodInSamples);
        float MinWindowSizePeriods = MathF.Floor(MIN_WINDOW_SIZE / PeriodInSamples);

        //Console.WriteLine($"Period is {PeriodInSamples}, want {PeriodsInWindow}x");
        return (uint)MathF.Round(MathF.Max(MinWindowSizePeriods, MathF.Min(MaxWindowSizePeriods, MathF.Round(PeriodsInWindow))) * PeriodInSamples + (PeriodInSamples * 0.5F));
    }

    private static float CalculateNoteFrequency(float octaveStart, uint binsPerOctave, uint binIndex) => octaveStart * GetNoteFrequencyMultiplier(binsPerOctave, binIndex);
    private static float GetNoteFrequencyMultiplier(uint binsPerOctave, uint binIndex) => MathF.Pow(2F, (float)binIndex / binsPerOctave);
}
