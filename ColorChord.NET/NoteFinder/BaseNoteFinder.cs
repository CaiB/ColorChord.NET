using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using ColorChord.NET.API;
using ColorChord.NET.API.Config;
using ColorChord.NET.API.Controllers;
using ColorChord.NET.API.NoteFinder;
using ColorChord.NET.API.Sources;
using ColorChord.NET.Config;

namespace ColorChord.NET.NoteFinder;

/// <summary> Equivalent to the note finder found in the base C colorchord implementation, by cnlohr. </summary>
public sealed class BaseNoteFinder : NoteFinderCommon, IControllableAttr
{
    /// <summary> Whether to keep processing, or shut down operations.  </summary>
    private bool KeepGoing = true;

    /// <summary> The thread doing the actual note data processing. </summary>
    private Thread? ProcessThread;

    /// <summary> The audio data source we are attached to. </summary>
    private readonly IAudioSource AudioSource;

    // Things that are always the same.
    #region Constants
    /// <summary> How many bins compose one octave in the raw DFT data. </summary>
    private const int BINS_PER_OCTAVE = 24;

    /// <summary> Over how many octaves the audio data will be processed. </summary>
    private const int OCTAVES = 5;

    /// <summary> How many bins the DFT will class sound frequency data into. </summary>
    private const int TOTAL_DFT_BINS = BINS_PER_OCTAVE * OCTAVES;

    /// <summary> How many note peaks are attempted to be extracted from the frequency data. </summary>
    private const int NOTE_QTY = BINS_PER_OCTAVE / 2;

    /// <summary> This is a required parameter for the DFT, but the desktop version doesn't use it. </summary>
    private const int DFT_Q = 20;

    /// <summary> This is a required parameter for the DFT, but the desktop version doesn't use it. </summary>
    private const int DFT_SPEEDUP = 1000;

    /// <summary> The sigma value to use for <see cref="NoteDistribution"/> by default. </summary>
    private const float DEFAULT_DISTRIBUTION_SIGMA = 1.4F;

    /// <summary> Used in normalizing all peak amplitudes. </summary>
    private const float PEAK_COMPRESS_COEFF = 1F;

    /// <summary> Used in normalizing all peak amplitudes. </summary>
    private const float PEAK_COMPRESS_EXP = 0.5F;

    /// <summary> How large a note needs to be to not be considered dead (and therefore re-assigned). </summary>
    private const float MIN_NOTE_SURVIVAL_AMPLITUDE = 0.001F;

    /// <summary> How large a distribution needs to be in order to be turned into a brand new note. </summary>
    private const float MIN_NOTE_CREATION_AMPLITUDE = 0.02F;
    #endregion

    // Things that are set by config at startup. Some are controllable (can change at runtime).
    #region Configurable Values
    /// <summary> The frequency at which the DFT output starts. </summary>
    /// <remarks> If this is changed, <see cref="SetSampleRate(int)"/> needs to be called in order to actaully apply the changes to the frequency list. </remarks>
    [Controllable("StartFreq", 1)]
    [ConfigFloat("StartFreq", 0F, 20000F, 55F)]
    private float MinimumFrequency = 55;

    /// <summary> Determines how much the previous frame's DFT data is used in the next frame. Smooths out rapid changes from frame-to-frame, but can cause delay if too strong. </summary>
    /// <remarks> Lower values will mean less inter-frame smoothing. Range: 0.0~1.0 </remarks>
    [Controllable("DFTIIR")]
    [ConfigFloat("DFTIIR", 0F, 1F, 0.65F)]
    private float DFTIIRMultiplier = 0.65F;

    /// <summary> Determines how much the raw DFT data is amplified before being used. </summary>
    /// <remarks> Range 0.0+ </remarks>
    [Controllable("DFTAmplify")]
    [ConfigFloat("DFTAmplify", 0F, 10000F, 2F)]
    private float DFTDataAmplifier = 2F;

    /// <summary> The slope of the extra frequency-dependent amplification done to raw DFT data. Positive values increase sensitivity at higher frequencies. </summary>
    /// <remarks> Amplification is 1.0 at the minimum frequency, and 1.0 + (<see cref="DFTSensitivitySlope"/> * <see cref="TOTAL_DFT_BINS"/>) at the highest, increasing by <see cref="DFTSensitivitySlope"/> at each bin. </remarks>
    [Controllable("DFTSlope")]
    [ConfigFloat("DFTSlope", -100F, 100F, 0.1F)]
    private float DFTSensitivitySlope = 0.1F;

    /// <summary> How often to run the octave data filter. This smoothes out each bin with adjacent ones. </summary>
    [Controllable("OctaveFilterIterations")]
    [ConfigInt("OctaveFilterIterations", 0, 10000, 2)]
    private int OctaveFilterIterations = 2;

    /// <summary> How strong the octave data filter is. Higher values mean each bin is more aggresively averaged with adjacent bins. </summary>
    /// <remarks> Higher values mean less glitchy, but also less clear note peaks. Range: 0.0~1.0 </remarks>
    [Controllable("OctaveFilterStrength")]
    [ConfigFloat("OctaveFilterStrength", 0F, 1F, 0.5F)]
    private float OctaveFilterStrength = 0.5F;

    /// <summary> How close a note needs to be to a distribution peak in order to be merged. </summary>
    [Controllable("NoteInfluenceDist")]
    [ConfigFloat("NoteInfluenceDist", 0F, 100F, 1.8F)]
    private float MinNoteInfluenceDistance = 1.8F;

    /// <summary> How strongly the note merging filter affects the note frequency. Stronger filter means notes take longer to shift positions to move together. </summary>
    /// <remarks> Range: 0.0~1.0 </remarks>
    [Controllable("NoteAttachFreqIIR")]
    [ConfigFloat("NoteAttachFreqIIR", 0F, 1F, 0.3F)]
    private float NoteAttachFrequencyIIRMultiplier = 0.3F;

    /// <summary> How strongly the note merging filter affects the note amplitude. Stronger filter means notes take longer to merge fully in amplitude. </summary>
    /// <remarks> Range: 0.0~1.0 </remarks>
    [Controllable("NoteAttachAmpIIR")]
    [ConfigFloat("NoteAttachAmpIIR", 0F, 1F, 0.35F)]
    private float NoteAttachAmplitudeIIRMultiplier = 0.35F;

    /// <summary> This filter is applied to notes between cycles in order to smooth their amplitudes over time. </summary>
    /// <remarks> Lower values cause smoother but more delayed note amplitude transitions. Range: 0.0~1.0 </remarks>
    [Controllable("NoteAttachAmpIIR2")]
    [ConfigFloat("NoteAttachAmpIIR2", 0F, 1F, 0.25F)]
    private float NoteAttachAmplitudeIIRMultiplier2 = 0.25F;

    /// <summary> How close two existing notes need to be in order to get combined into a single note. </summary>
    /// <remarks> A distance of 2 means that a perfect A can combine with a perfect Bb etc. </remarks>
    [Controllable("NoteCombineDistance")]
    [ConfigFloat("NoteCombineDistance", 0F, 100F, 0.5F)]
    private float MinNoteCombineDistance = 0.5F;

    /// <summary> Notes below this value get zeroed in <see cref="Note.AmplitudeFinal"/>. </summary>
    /// <remarks> Increase if low-amplitude notes are causing noise in output. </remarks>
    [Controllable("NoteOutputChop")]
    [ConfigFloat("NoteOutputChop", 0F, 100F, 0.05F)]
    private float NoteOutputChop = 0.05F;
    #endregion

    // Data that is used in Cycle/DoProcessing, and not used elsewhere.
    // These would make more sense to just be created and assigned inside, but keeping them out here reduces the number of memory allocations done.
    #region CycleData
    /// <summary> The buffer for audio data gathered from a system device. Circular buffer, with the current write position stored in <see cref="AudioBufferHeadWrite"/>. </summary>
    public short[] AudioBuffer { get; private set; } = new short[8192]; // TODO: Make buffer size adjustable or auto-set based on sample rate (might be too short for super-high rates)

    /// <summary> Where in the <see cref="AudioBuffer"/> we are currently adding new audio data. </summary>
    public int AudioBufferHeadWrite { get; set; } = 0;

    private Stopwatch CycleTimer = new();
    private float CycleTimeTicks;
    private uint CycleCount = 0;

    /// <summary> The frequency in Hz, that each of the raw bins from the DFT corresponds to. </summary>
    /// <remarks> Data contained from previous cycles not used during next cycle. </remarks>
    private readonly float[] RawBinFrequencies = new float[TOTAL_DFT_BINS];

    private int CurrentNoteID = 1;

    /// <summary> The individual note distributions (peaks) detected this cycle. </summary>
    /// <remarks> Data contained from previous cycles not used during next cycle. </remarks>
    public readonly NoteDistribution[] NoteDistributions = new NoteDistribution[NOTE_QTY];

    public float LastLowFreqSum = 0;

    public int LastLowFreqCount = 0;

    public float LastBinSum = 0;

    #endregion

    // Info for other systems:
    public override string Name { get; protected init; }
    public override int NoteCount { get => NOTE_QTY; }
    public override int BinsPerOctave { get => BINS_PER_OCTAVE; } // TODO: This is only really necessary because we don't scale the note output values. Consider doing that.

    private Note[] P_Notes;
    public override ReadOnlySpan<Note> Notes => P_Notes;

    private int[] P_PersistentNoteIDs;
    public override ReadOnlySpan<int> PersistentNoteIDs => P_PersistentNoteIDs;

    private float[] P_OctaveBinValues;
    public override ReadOnlySpan<float> OctaveBinValues => P_OctaveBinValues;

    private float[] P_AllBinValues;
    public override ReadOnlySpan<float> AllBinValues => P_AllBinValues;

    public BaseNoteFinder(string name, Dictionary<string, object> config)
    {
        Configurer.Configure(this, config);
        this.AudioSource = Configurer.FindSource(config) ?? throw new Exception($"{nameof(Gen2NoteFinder)} \"{name}\" could not find the audio source to get data from.");
        this.AudioSource.AttachNoteFinder(this);
        SetSampleRate(SampleRate); // Changing the minimum frequency needs an update of the frequency bins, which is done by SetSampleRate().
        this.P_Notes = new Note[NOTE_QTY];
        this.P_PersistentNoteIDs = new int[NOTE_QTY];
        this.P_OctaveBinValues = new float[BINS_PER_OCTAVE];
        this.P_AllBinValues = new float[TOTAL_DFT_BINS];
        this.Name = name;
        SetupBuffers();
    }

    /// <summary> Updates the sample rate if the audio source has changed. Also recalculates bin frequencies to match. </summary>
    public override void SetSampleRate(int sampleRate)
    {
        this.SampleRate = sampleRate;
        for (int RawBinIndex = 0; RawBinIndex < TOTAL_DFT_BINS; RawBinIndex++)
        {
            this.RawBinFrequencies[RawBinIndex] = (sampleRate / MinimumFrequency) / MathF.Pow(2, (float)RawBinIndex / BINS_PER_OCTAVE);
        }
    }

    private int SampleRate = 44100;

    /// <summary> Adjusts the note finder run interval if the newly added visualizer/output needs it to run faster, otherwise does nothing. </summary>
    /// <param name="period"> The period, in milliseconds, that you need the note finder to run at or faster than. </param>
    public override void AdjustOutputSpeed(uint period)
    {
        if (period < this.ShortestPeriod) { this.ShortestPeriod = period; }
    }

    /// <summary> Starts the processing thread. </summary>
    public override void Start()
    {
        this.KeepGoing = true;
        this.ProcessThread = new Thread(DoProcessing) { Name = $"{nameof(BaseNoteFinder)} {this.Name}" };
        this.ProcessThread.Start();
    }

    /// <summary> Stops the processing thread. </summary>
    public override void Stop()
    {
        this.KeepGoing = false;
        this.InputDataEvent.Set();
        this.ProcessThread?.Join();
    }

    public void SettingWillChange(int controlID) { }

    public void SettingChanged(int controlID)
    {
        if (controlID == 1) { SetSampleRate(this.SampleRate); }
    }

    /// <summary> Runs until <see cref="KeepGoing"/> becomes false, processing incoming audio data. </summary>
    private void DoProcessing()
    {
        while (this.KeepGoing)
        {
            this.InputDataEvent.WaitOne();

            bool MoreBuffers;
            uint NewDataRead = 0;
            do
            {
                short[]? NewBuffer = GetBufferToRead(out int NFBufferRef, out uint BufferSize, out MoreBuffers);
                if (NewBuffer == null) { break; }

                for (int i = 0; i < BufferSize; i++)
                {
                    this.AudioBuffer[this.AudioBufferHeadWrite] = NewBuffer[i];
                    this.AudioBufferHeadWrite = (this.AudioBufferHeadWrite + 1) % this.AudioBuffer.Length;
                }
                NewDataRead += BufferSize;

                FinishBufferRead(NFBufferRef);

                this.CycleTimer.Restart();
                Cycle();
                this.CycleTimer.Stop();

                const float TIMER_IIR = 0.97F;
                if (BufferSize > 32) { this.CycleTimeTicks = (this.CycleTimeTicks * TIMER_IIR) + ((float)this.CycleTimer.ElapsedTicks / BufferSize * (1F - TIMER_IIR)); }
                if (++this.CycleCount % 500 == 0) { Log.Debug($"{nameof(BaseNoteFinder)} is taking {this.CycleTimeTicks * 0.1F:F3}us per sample."); }
            }
            while (MoreBuffers);
        }
    }

    public override void UpdateOutputs() { }

    private void Cycle()
    {
        // DFT outputs only a small number of bins, we'll need to process this data a lot to get smooth note positions.
        float[] DFTBinData = new float[TOTAL_DFT_BINS];

        // This will read all buffer data from where it was called last up to [AudioBufferHeadWrite] in order to catch up.
        BaseNoteFinderDFT.DoDFTProgressive32(ref DFTBinData, this.RawBinFrequencies, this.AudioBuffer, this.AudioBufferHeadWrite);

        for (int BinInd = 0; BinInd < 24; BinInd++)
        {
            this.LastLowFreqSum += DFTBinData[BinInd];
        }
        this.LastLowFreqCount++;

        // Pre-process input DFT data.
        for (int RawBinIndex = 0; RawBinIndex < TOTAL_DFT_BINS; RawBinIndex++)
        {
            float NewData = DFTBinData[RawBinIndex]; // The raw DFT data for this bin
            NewData *= this.DFTDataAmplifier; // Amplify incoming data by a constant
            NewData *= (1 + (this.DFTSensitivitySlope * RawBinIndex)); // Apply a frequency-dependent amplifier to increase sensitivity at higher frequencies.

            this.P_AllBinValues[RawBinIndex] = (this.P_AllBinValues[RawBinIndex] * this.DFTIIRMultiplier) + // Keep data from last frame, but reduce by a factor.
                                         (NewData * (1 - this.DFTIIRMultiplier)); // Add new data
        }

        // Taper off the first and last octave.
        // This is to prevent frequency content at the edges of our bin set from creating discontinuities in the folded bins.
        for (int OctaveBinIndex = 0; OctaveBinIndex < BINS_PER_OCTAVE; OctaveBinIndex++)
        {
            this.P_AllBinValues[OctaveBinIndex] *= (OctaveBinIndex + 1F) / BINS_PER_OCTAVE; // Taper the first octave
            this.P_AllBinValues[TOTAL_DFT_BINS - OctaveBinIndex - 1] *= (OctaveBinIndex + 1F) / BINS_PER_OCTAVE; // Taper the last octave
        }

        // Fold the bins to make one single octave-length array, where all like notes (e.g. C2, C3, C4) are combined, regardless of their original octave.
        for (int BinIndex = 0; BinIndex < BINS_PER_OCTAVE; BinIndex++)
        {
            float Amplitude = 0;
            for (int Octave = 0; Octave < OCTAVES; Octave++) { Amplitude += this.P_AllBinValues[(Octave * BINS_PER_OCTAVE) + BinIndex]; }
            this.P_OctaveBinValues[BinIndex] = Amplitude;
        }

        float LastBinSum = 0;
        for (int i = 0; i < this.P_OctaveBinValues.Length; i++) { LastBinSum += this.P_OctaveBinValues[i]; }
        this.LastBinSum = LastBinSum;

        // Do some filtering on the now-folded bins to remove meaningless peaks.
        // Averages out each bin a little bit with adjacent bins.
        // Runs [OctaveFilterIterations] times, averaging with strength [OctaveFilterStrength].
        float[] OctaveBinValuesPre = new float[BINS_PER_OCTAVE];
        for (int Iteration = 0; Iteration < this.OctaveFilterIterations; Iteration++)
        {
            Array.Copy(this.P_OctaveBinValues, OctaveBinValuesPre, BINS_PER_OCTAVE); // COpy the octave data into our temporary array.
            for (int BinIndex = 0; BinIndex < BINS_PER_OCTAVE; BinIndex++)
            {
                int IndexRight = (BinIndex + BINS_PER_OCTAVE + 1) % BINS_PER_OCTAVE; // The next bin to the right (wrapping around if needed).
                int IndexLeft = (BinIndex + BINS_PER_OCTAVE - 1) % BINS_PER_OCTAVE; // The next bin to the left (wrapping around if needed).
                float ValueRight = OctaveBinValuesPre[IndexRight];
                float ValueLeft = OctaveBinValuesPre[IndexLeft];

                float NewValue = this.P_OctaveBinValues[BinIndex] * (1F - this.OctaveFilterStrength); // Some of the current value in the bin
                NewValue += ((ValueLeft + ValueRight) / 2) * this.OctaveFilterStrength; // Add the average of the adjacent bins, scaled by the filter strength

                this.P_OctaveBinValues[BinIndex] = NewValue;
            }
        }

        // Reset all note distributions to off state.
        for (int NoteIndex = 0; NoteIndex < NOTE_QTY; NoteIndex++) { this.NoteDistributions[NoteIndex].HasNote = false; }

        // Find note distributions.
        // NOTE: This is decompose.c/DecomposeHistogram in TURBO_DECOMPOSE mode (single iteration).
        //       Non-TURBO_DECOMPOSE mode is currently not implemented here, as it doesn't seem to be used upstream.

        int DistributionsFound = 0;

        for (int BinIndex = 0; BinIndex < BINS_PER_OCTAVE; BinIndex++)
        {
            int IndexLeft = (BinIndex - 1 + BINS_PER_OCTAVE) % BINS_PER_OCTAVE;
            int IndexRight = (BinIndex + 1) % BINS_PER_OCTAVE;
            float ValueLeft = this.P_OctaveBinValues[IndexLeft];
            float ValueHere = this.P_OctaveBinValues[BinIndex];
            float ValueRight = this.P_OctaveBinValues[IndexRight];

            if (ValueLeft >= ValueHere || ValueRight > ValueHere) { continue; } // Adjacent bins are higher, this is not a peak.
            if (ValueLeft == ValueHere && ValueRight == ValueHere) { continue; } // Adjacent bins are both equal, this is a plateau (e.g. all 0).

            // TODO: These descriptions aren't 100% certain.
            // This bin is a peak, adjacent values are lower.
            // Now we try to locate where the peak should be within this one bin.
            float TotalAdjacentDifference = (ValueHere - ValueLeft) + (ValueHere - ValueRight);
            float ProportionalDifferenceLeft = (ValueHere - ValueLeft) / TotalAdjacentDifference;
            float ProportionalDifferenceRight = (ValueHere - ValueRight) / TotalAdjacentDifference;

            float InternalOffset; // Where in this bin the peak is.
            if (ProportionalDifferenceLeft < ProportionalDifferenceRight) { InternalOffset = -(0.5F - ProportionalDifferenceLeft); } // In the left half of this bin.
            else { InternalOffset = (0.5F - ProportionalDifferenceRight); } // In the right half of this bin.

            // Output the distribution information.
            this.NoteDistributions[DistributionsFound].Mean = BinIndex + InternalOffset;
            this.NoteDistributions[DistributionsFound].Amplitude = ValueHere * 4;
            this.NoteDistributions[DistributionsFound].Sigma = DEFAULT_DISTRIBUTION_SIGMA;

            DistributionsFound++;
        }

        // Clear out the distributions that are not currently active.
        for (int DistrIndex = DistributionsFound; DistrIndex < NOTE_QTY; DistrIndex++)
        {
            this.NoteDistributions[DistrIndex].Mean = -1F;
            this.NoteDistributions[DistrIndex].Amplitude = 0F;
            this.NoteDistributions[DistrIndex].Sigma = DEFAULT_DISTRIBUTION_SIGMA;
        }

        // Normalize distribution amplitudes.
        // Start by summing all peak amplitudes.
        float AmplitudeSum = 0;
        for (int DistrIndex = 0; DistrIndex < DistributionsFound; DistrIndex++) { AmplitudeSum += this.NoteDistributions[DistrIndex].Amplitude; }

        // Find coefficient to multiply all by.
        float AmplitudeCoefficient = PEAK_COMPRESS_COEFF / MathF.Pow(AmplitudeSum * PEAK_COMPRESS_COEFF, PEAK_COMPRESS_EXP);

        // Scale peaks.
        for (int DistrIndex = 0; DistrIndex < DistributionsFound; DistrIndex++) { this.NoteDistributions[DistrIndex].Amplitude *= AmplitudeCoefficient; }

        // Sort peaks so they are in high-to-low amplitude order.
        Array.Sort(this.NoteDistributions);

        // Stores whether the given note peak has been associated and influenced by a distribution this cycle.
        bool[] NotesAssociated = new bool[NOTE_QTY];

        // Try to find peaks that are close together (in respect to frequency).
        // This modifies [Notes] by using new data from [NoteDistributions].
        for (int PeakIndex = 0; PeakIndex < NOTE_QTY; PeakIndex++)
        {
            // For each note peak, check if a distribution is close by. If so, adjust the peak location and amplitude to use this new information.
            for (byte DistrIndex = 0; DistrIndex < DistributionsFound; DistrIndex++)
            {
                if (!this.NoteDistributions[DistrIndex].HasNote && // If this distribution is not already influencing another note.
                         !NotesAssociated[PeakIndex] && // If this note is not already being influenced by another distribution
                          LoopDistance(this.P_Notes[PeakIndex].Position, this.NoteDistributions[DistrIndex].Mean, BINS_PER_OCTAVE) < this.MinNoteInfluenceDistance && // The locations are close enough to merge
                    this.NoteDistributions[DistrIndex].Amplitude > 0.00001F) // The new data is significant
                {
                    // note_peaks_to_dists_mapping can be implemented here if needed.
                    // TODO: I'm honestly a little bit lost as to what happens in here...
                    this.NoteDistributions[DistrIndex].HasNote = true; // Don't let this distribution affect other notes.
                    if (this.P_PersistentNoteIDs[PeakIndex] == 0) { this.P_PersistentNoteIDs[PeakIndex] = this.CurrentNoteID++; }
                    NotesAssociated[PeakIndex] = true; // This note has been influenced by a distribution, so is still active.
                    this.P_Notes[PeakIndex].Position = LoopAverageWeighted(this.P_Notes[PeakIndex].Position, (1F - this.NoteAttachFrequencyIIRMultiplier), this.NoteDistributions[DistrIndex].Mean, this.NoteAttachFrequencyIIRMultiplier, BINS_PER_OCTAVE);

                    float NewAmplitude = this.P_Notes[PeakIndex].Amplitude;
                    NewAmplitude *= (1F - this.NoteAttachAmplitudeIIRMultiplier);
                    NewAmplitude += (this.NoteDistributions[DistrIndex].Amplitude * this.NoteAttachAmplitudeIIRMultiplier);
                    this.P_Notes[PeakIndex].Amplitude = NewAmplitude;
                }
            }
        }

        // Combine notes if they are close enough.
        for (int NoteIndex1 = 0; NoteIndex1 < NOTE_QTY; NoteIndex1++)
        {
            for (int NoteIndex2 = 0; NoteIndex2 < NOTE_QTY; NoteIndex2++)
            {
                if (NoteIndex1 == NoteIndex2) { continue; } // Don't try to compare a note with itself.

                if (LoopDistance(this.P_Notes[NoteIndex1].Position, this.P_Notes[NoteIndex2].Position, BINS_PER_OCTAVE) < this.MinNoteCombineDistance && // The two notes are close enough
                    this.P_Notes[NoteIndex1].Amplitude > 0 && // v
                    this.P_Notes[NoteIndex2].Amplitude > 0) // Both notes need to be significant to be combined.
                {
                    bool Is1Bigger = this.P_Notes[NoteIndex1].Amplitude > this.P_Notes[NoteIndex2].Amplitude;
                    int IndexPrimary = Is1Bigger ? NoteIndex1 : NoteIndex2; // The index of the larger note.
                    int IndexSecondary = Is1Bigger ? NoteIndex2 : NoteIndex1; // The index of the smaller note.

                    float NewPosition = LoopAverageWeighted(this.P_Notes[IndexPrimary].Position, this.P_Notes[IndexPrimary].Amplitude, this.P_Notes[IndexSecondary].Position, this.P_Notes[IndexSecondary].Amplitude, BINS_PER_OCTAVE);

                    // Merge secondary into primary at new position
                    this.P_Notes[IndexPrimary].Amplitude += this.P_Notes[IndexSecondary].Amplitude;
                    this.P_Notes[IndexPrimary].Position = NewPosition;

                    // Delete secondary note
                    this.P_Notes[IndexSecondary].Amplitude = 0;
                    this.P_Notes[IndexSecondary].Position = -100;
                    this.P_PersistentNoteIDs[IndexSecondary] = 0;
                }
            }
        }

        // Note slots that are empty should be assigned to not yet used distributions, if there are any.
        for (int NoteIndex = 0; NoteIndex < NOTE_QTY; NoteIndex++)
        {
            if (this.P_Notes[NoteIndex].Amplitude < MIN_NOTE_SURVIVAL_AMPLITUDE)
            {
                this.P_PersistentNoteIDs[NoteIndex] = 0;

                // Find a new peak for this note.
                for (int DistrIndex = 0; DistrIndex < DistributionsFound; DistrIndex++)
                {
                    if (!this.NoteDistributions[DistrIndex].HasNote && // Hasn't already been turned into a note.
                         this.NoteDistributions[DistrIndex].Amplitude > MIN_NOTE_CREATION_AMPLITUDE) // The distribution is large enough to be worth turning into a new note.
                    {
                        // Create a new note with information from this distribution.
                        this.P_PersistentNoteIDs[NoteIndex] = this.CurrentNoteID++;
                        this.NoteDistributions[DistrIndex].HasNote = true; // Don't let this create/affect other notes this cycle.
                        this.P_Notes[NoteIndex].Amplitude = this.NoteDistributions[DistrIndex].Amplitude;
                        this.P_Notes[NoteIndex].Position = this.NoteDistributions[DistrIndex].Mean;
                        NotesAssociated[NoteIndex] = true; // This note was just created, so it is active this cycle.
                    }
                }
            }
        }

        // Decay inactive notes.
        for (int NoteIndex = 0; NoteIndex < NOTE_QTY; NoteIndex++)
        {
            // Any notes that do not have a corresponding distribution this cycle should be decayed.
            if (!NotesAssociated[NoteIndex]) { this.P_Notes[NoteIndex].Amplitude *= (1F - this.NoteAttachAmplitudeIIRMultiplier); }

            float NewFiltered = this.P_Notes[NoteIndex].AmplitudeFiltered;
            NewFiltered *= (1F - this.NoteAttachAmplitudeIIRMultiplier2);
            NewFiltered += this.P_Notes[NoteIndex].Amplitude * this.NoteAttachAmplitudeIIRMultiplier2;
            this.P_Notes[NoteIndex].AmplitudeFiltered = NewFiltered; // A combination of the previous filtered value, pushed towards the current value.

            if (this.P_Notes[NoteIndex].AmplitudeFiltered < MIN_NOTE_SURVIVAL_AMPLITUDE) // The amplitude is very small, just cut it to avoid noise.
            {
                this.P_Notes[NoteIndex].Amplitude = 0;
                this.P_Notes[NoteIndex].AmplitudeFiltered = 0;
            }
        }

        // Nudge down amplitude of all notes, zeroing out ones that don't make the cut.
        for (int NoteIndex = 0; NoteIndex < NOTE_QTY; NoteIndex++)
        {
            this.P_Notes[NoteIndex].AmplitudeFinal = this.P_Notes[NoteIndex].Amplitude - this.NoteOutputChop; // Reduce all notes by a small amount.
            if (this.P_Notes[NoteIndex].AmplitudeFinal < 0) { this.P_Notes[NoteIndex].AmplitudeFinal = 0; } // If this note is too small, zero it.
        }
    }

    /// <summary> Gets the distance between two elements that are on a circle that wraps between 0 and loopLength. </summary>
    /// <param name="a"> The location of point A. </param>
    /// <param name="b"> The location of point B. </param>
    /// <param name="loopLength"> The circumference of the circle. </param>
    /// <returns> The distance between the two points, always positive and max (loopLength / 2).</returns>
    private static float LoopDistance(float a, float b, float loopLength)
    {
        float Distance = Math.Abs(a - b); // The distance to go directly.
        Distance %= loopLength;
        if (Distance > loopLength / 2F) { Distance = loopLength - Distance; } // Distance around is shorter, so wrap around.
        return Distance; // Distance direct is shorter, just go directly.
    }

    /// <summary> Find the center point between two points, all on a circle, but with weights for each of the inputs. </summary>
    /// <param name="positionA"> The location of point A. </param>
    /// <param name="weightA"> The size/weight of point A. </param>
    /// <param name="positionB"> The location of point B. </param>
    /// <param name="weightB"> The size/weight of point B. </param>
    /// <param name="loopLength"> The circumference of the circle. </param>
    /// <returns> The location of the weighted center point. </returns>
    private static float LoopAverageWeighted(float positionA, float weightA, float positionB, float weightB, float loopLength)
    {
        float WeightSum = weightA + weightB;

        if (Math.Abs(positionA - positionB) > loopLength / 2F) // Looping around outside is shorter
        {
            if (positionA < positionB) { positionA += loopLength; } // Move A so that the direct distance B->A is positive and the shorter version.
            else { positionB += loopLength; } // Move B so that the direct distance A->B is positive and the shorter version.
        }
        // Because the distance has been corrected by moving a point if needed, this is now a standard weighted average.
        float Midpoint = ((positionA * weightA) + (positionB * weightB)) / WeightSum;
        return Midpoint % loopLength; // Move the point back onto the circle if it is outside.
    }

    /// <summary> A note distribution, represented as a location, amplitude, and sigma, defining a normal (Gaussian) distribution. </summary>
    /// <remarks> These are created directly from DFT output, and later associate and interact with <see cref="Note"/> objects to update the current set of active notes. </remarks>
    public struct NoteDistribution : IComparable<NoteDistribution>
    {
        /// <summary> The amplitude (relative strength) of the note. </summary>
        public float Amplitude;

        /// <summary> The mean (location) of the note in the frequency spectrum. </summary>
        /// <remarks> Range: 0.0 to <see cref="BINS_PER_OCTAVE"/>. Fractional part shows where in the bin the peak is. </remarks>
        public float Mean;

        /// <summary> The sigma (spread) of the note. </summary>
        public float Sigma;

        /// <summary> Whether this distribution is affecting a note already. </summary>
        public bool HasNote;

        /// <summary> Compares the note amplitudes. </summary>
        /// <param name="other"> The note distribution to compare to. </param>
        /// <returns> </returns>
        public int CompareTo(NoteDistribution other)
        {
            if (this.Amplitude > other.Amplitude) { return -1; }
            else if (this.Amplitude < other.Amplitude) { return 1; }
            else { return 0; }
        }
    }
}
