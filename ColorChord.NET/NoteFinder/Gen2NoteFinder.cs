using ColorChord.NET.API;
using ColorChord.NET.API.Config;
using ColorChord.NET.API.NoteFinder;
using ColorChord.NET.API.Sources;
using ColorChord.NET.Config;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Threading;

namespace ColorChord.NET.NoteFinder;

public sealed class Gen2NoteFinder : NoteFinderCommon, ITimingSource
{
    private const int NOTE_QTY = 12;

    private Gen2NoteFinderDFT DFT;
    private readonly IAudioSource AudioSource;

    public override string Name { get; protected init; }

    [ConfigInt("Octaves", 1, 20, 6)]
    public uint OctaveCount { get; private set; }

    [ConfigFloat("StartFreq", 0F, 20000F, 55F)]
    public float StartFrequency { get; private set; } = 55F;

    [ConfigFloat("LoudnessCorrection", 0F, 1F, 0.33F)]
    public float LoudnessCorrectionAmount { get; private set; } = 0.33F;

    public uint SampleRate { get; private set; }

    public override ReadOnlySpan<float> AllBinValues => this.DFT.AllBinValues.AsSpan(1, this.DFT.AllBinValues.Length - 2);
    public override ReadOnlySpan<float> OctaveBinValues => this.DFT.OctaveBinValues;

    private Note[] P_Notes;
    public override ReadOnlySpan<Note> Notes => this.P_Notes;

    private int[] P_PersistentNoteIDs;
    public override ReadOnlySpan<int> PersistentNoteIDs => this.P_PersistentNoteIDs;

    /// <summary> The frequencies (in Hz) of each of the raw bins in <see cref="AllBinValues"/>. </summary>
    public ReadOnlySpan<float> BinFrequencies => this.DFT.RawBinFrequencies;

    private float AllBinMax = 0.01F, AllBinMaxSmoothed = 0.01F;

    public float[] AllBinValuesScaled;

    public byte[] PeakBits, WidebandBits;
    private byte[] AllowedBinWidths;

    private const float RECENT_BIN_CHANGE_IIR = 0.65F;
    private float[] PreviousBinValues;
    public float[] RecentBinChanges;

    private Thread? ProcessThread;
    private bool KeepGoing = true;

    private Stopwatch CycleTimer = new();
    private float CycleTimeTicks;
    private uint CycleCount = 0;

    private bool IsTimingSource = false;
    private TimingReceiverData[] TimingReceivers = Array.Empty<TimingReceiverData>();

    public Gen2NoteFinder(string name, Dictionary<string, object> config)
    {
        P_Notes = new Note[NOTE_QTY];
        P_PersistentNoteIDs = new int[NOTE_QTY];
        this.Name = name;
        Configurer.Configure(this, config);
        this.AudioSource = Configurer.FindSource(config) ?? throw new Exception($"{nameof(Gen2NoteFinder)} \"{name}\" could not find the audio source to get data from.");
        this.AudioSource.AttachNoteFinder(this);

        SetupBuffers();
        this.SampleRate = 48000; // TODO: Temporary until source is connected ahead of time
        this.DFT = new(this.OctaveCount, this.SampleRate, this.StartFrequency, this.LoudnessCorrectionAmount, RunTimingReceivers);
        Reconfigure();
    }

    [MemberNotNull(nameof(PeakBits), nameof(WidebandBits), nameof(AllowedBinWidths), nameof(PreviousBinValues), nameof(RecentBinChanges))]
    private void Reconfigure()
    {
        int BinCount = this.DFT.BinCount;
        PeakBits = new byte[(BinCount + 7) / 8]; // + 7 is to ensure there's always enough space, even in the odd case where number of bins isn't evenly divisible by 8
        WidebandBits = new byte[(BinCount + 7) / 8];
        AllowedBinWidths = new byte[BinCount];
        PreviousBinValues = new float[BinCount];
        RecentBinChanges = new float[BinCount];
        AllBinValuesScaled = new float[BinCount];

        for (int i = 0; i < AllowedBinWidths.Length; i++) { AllowedBinWidths[i] = (byte)MathF.Max(4F, 1.5F * MathF.Ceiling(this.DFT.RawBinWidths[i])); }
    }

    public override int NoteCount => NOTE_QTY;
    public override int BinsPerOctave => (int)this.DFT.BinsPerOctave;

    public override void AdjustOutputSpeed(uint period)
    {
        if (period < ShortestPeriod) { ShortestPeriod = period; }
    }

    public override void SetSampleRate(int sampleRate)
    {
        this.SampleRate = (uint)sampleRate;
        this.DFT = new(this.OctaveCount, this.SampleRate, this.StartFrequency, this.LoudnessCorrectionAmount, RunTimingReceivers);
        Reconfigure();
        Log.Debug($"There are {TimingReceivers.Length} timing receivers");
        for (int i = 0; i < TimingReceivers.Length; i++)
        {
            float OriginalPeriod = TimingReceivers[i].OriginalPeriod;
            TimingReceivers[i].Period = (OriginalPeriod <= 0) ? (uint)MathF.Round(-OriginalPeriod) : (uint)MathF.Round(OriginalPeriod * this.SampleRate);
            Log.Debug($"{nameof(Gen2NoteFinder)} timing receiver [{i}] now has period {TimingReceivers[i].Period} (requested {OriginalPeriod}).");
        }
    }

    public override void Start()
    {
        KeepGoing = true;
        ProcessThread = new Thread(DoProcessing) { Name = nameof(Gen2NoteFinder) };
        ProcessThread.Start();
    }

    private void DoProcessing()
    {
        while (this.KeepGoing)
        {
            InputDataEvent.WaitOne();
            Cycle();
        }
    }

    private void Cycle()
    {
        bool MoreBuffers;
        do
        {
            short[]? Buffer = GetBufferToRead(out int NFBufferRef, out uint BufferSize, out MoreBuffers);
            if (Buffer == null) { break; }

            CycleTimer.Restart();
            this.DFT.AddAudioData(Buffer.AsSpan(0, (int)BufferSize));
            CycleTimer.Stop();

            FinishBufferRead(NFBufferRef);

            const float TIMER_IIR = 0.97F;
            if (BufferSize > 32) { CycleTimeTicks = (CycleTimeTicks * TIMER_IIR) + ((float)CycleTimer.ElapsedTicks / BufferSize * (1F - TIMER_IIR)); }
            if (++CycleCount % 500 == 0) { Log.Debug($"{nameof(Gen2NoteFinder)} DFT is taking {CycleTimeTicks * 0.1F:F3}us per sample."); }

        } while (MoreBuffers);
    }

    public override void UpdateOutputs()
    {
        this.DFT.CalculateOutput();

        float[] RawBinValuesPadded = this.DFT.AllBinValues;

        Debug.Assert(RawBinValuesPadded[0] == 0F, $"{nameof(Gen2NoteFinderDFT.AllBinValues)} left boundary value was changed, it should always be 0.");
        Debug.Assert(RawBinValuesPadded[^1] == 0F, $"{nameof(Gen2NoteFinderDFT.AllBinValues)} right boundary value was changed, it should always be 0.");

        byte[] PeakBitsPacked = this.PeakBits;
        byte[] WidebandBitsPacked = this.WidebandBits;
        float RecentBinChangeIIRb = 1F - RECENT_BIN_CHANGE_IIR;

        for (int i = 0; i < WidebandBitsPacked.Length; i++) { WidebandBitsPacked[i] = 0; }

        Debug.Assert(((RawBinValuesPadded.Length - 2) % 8) == 0, "Bin count must be a multiple of 8"); // TODO: Add non-SIMD version to avoid this
        Vector256<float> BinMaxIntermediate = Vector256<float>.Zero;
        for (int Step = 0; Step <= RawBinValuesPadded.Length - 10; Step += 8)
        {
            Vector256<float> LeftValues = Vector256.LoadUnsafe(ref RawBinValuesPadded[Step]); // TODO: Is there a better way to do this?
            Vector256<float> MiddleValues = Vector256.LoadUnsafe(ref RawBinValuesPadded[Step + 1]);
            Vector256<float> RightValues = Vector256.LoadUnsafe(ref RawBinValuesPadded[Step + 2]);

            Vector256<float> DiffLeft = Avx.Subtract(MiddleValues, LeftValues);
            Vector256<float> DiffRight = Avx.Subtract(MiddleValues, RightValues);
            Vector256<float> DifferenceSum = Avx.Add(DiffLeft, DiffRight);
            Vector256<float> Significant = Avx.Subtract(MiddleValues, Vector256.Create(0.2F)); // Avx.Subtract(Avx.Divide(DifferenceSum, MiddleValues), Vector256.Create(0.9F));//
            //Vector256<float> ScaledDiffLeft = Avx.Divide(DiffLeft, DifferenceSum);
            //Vector256<float> ScaledDiffRight = Avx.Divide(DiffRight, DifferenceSum);

            Vector256<float> PeakDetect = Avx.And(Avx.And(Avx.CompareGreaterThanOrEqual(DiffLeft, Vector256<float>.Zero), Avx.CompareGreaterThan(DiffRight, Vector256<float>.Zero)), Avx.CompareGreaterThanOrEqual(Significant, Vector256<float>.Zero));
            byte PeakBitsHere = (byte)Avx.MoveMask(PeakDetect);

            PeakBitsPacked[Step / 8] = PeakBitsHere;

            Vector256<float> PreviousValues = Vector256.LoadUnsafe(ref PreviousBinValues[Step]);
            Vector256<float> AbsDiffFromPrev = Avx.AndNot(Vector256.Create(-0.0F), Avx.Subtract(Avx.Multiply(PreviousValues, PreviousValues), Avx.Multiply(MiddleValues, MiddleValues)));
            //AbsDiffFromPrev = Avx.Multiply(AbsDiffFromPrev, AbsDiffFromPrev);
            Vector256<float> OldRecentChange = Vector256.LoadUnsafe(ref RecentBinChanges[Step]);
            Vector256<float> NewRecentChange = Avx.Add(Avx.Multiply(Vector256.Create(RECENT_BIN_CHANGE_IIR), OldRecentChange), Avx.Multiply(Vector256.Create(RecentBinChangeIIRb), AbsDiffFromPrev));
            NewRecentChange.StoreUnsafe(ref RecentBinChanges[Step]);
            MiddleValues.StoreUnsafe(ref PreviousBinValues[Step]);
            BinMaxIntermediate = Avx.Max(BinMaxIntermediate, MiddleValues);
        }
        Vector128<float> BinMaxCondense1 = Sse.Max(BinMaxIntermediate.GetLower(), BinMaxIntermediate.GetUpper());
        float NewAllBinMax = MathF.Max(MathF.Max(BinMaxCondense1[0], BinMaxCondense1[1]), MathF.Max(BinMaxCondense1[2], BinMaxCondense1[3]));
        this.AllBinMaxSmoothed = this.AllBinMax < NewAllBinMax ? (this.AllBinMax * 0.8F) + (NewAllBinMax * 0.2F) : (this.AllBinMax * 0.995F) + (NewAllBinMax * 0.005F);
        this.AllBinMax = Math.Max(0.01F, AllBinMaxSmoothed);

        for (int Step = 0; Step < AllBinValuesScaled.Length; Step += 8)
        {
            Vector256<float> MiddleValues = Vector256.LoadUnsafe(ref RawBinValuesPadded[Step + 1]);
            Vector256<float> Scaled = Avx.Divide(MiddleValues, Vector256.Create(this.AllBinMaxSmoothed * 2.2F));
            Scaled.StoreUnsafe(ref AllBinValuesScaled[Step]);
        }

        for (int Outer = 0; Outer < PeakBitsPacked.Length; Outer++)
        {
            if (PeakBitsPacked[Outer] == 0) { continue; }
            for (int Inner = 0; Inner < 8; Inner++)
            {
                if (((PeakBitsPacked[Outer] >> Inner) & 1) != 0) // Can optimize this by changing the loop var to be shifted and just anding here
                {
                    int Index = (Outer * 8) + Inner + 1; // Offset by 1 for front padding
                    int SideBinsWithContent = 0;
                    int LeftmostSideBin = Index - 1;
                    int RightmostSideBin = Index + 1;
                    float TotalContent = RawBinValuesPadded[Index];
                    float Threshold = 0.1F; // MUST be greater than 0
                    while (RawBinValuesPadded[LeftmostSideBin] > Threshold) // This is guaranteed to stop at or before 0, given that RawBinValuesPadded[0] == 0 (padding intact)
                    {
                        SideBinsWithContent++;
                        TotalContent += RawBinValuesPadded[LeftmostSideBin];
                        LeftmostSideBin--;
                    }
                    while (RawBinValuesPadded[RightmostSideBin] > Threshold) // Same but at the upper end
                    {
                        SideBinsWithContent++;
                        TotalContent += RawBinValuesPadded[RightmostSideBin];
                        RightmostSideBin++;
                    }

                    // Offset these over to the left, since we need the non-padded index
                    LeftmostSideBin--;
                    RightmostSideBin--;

                    if (SideBinsWithContent > AllowedBinWidths[Index - 1])
                    {
                        for (int i = LeftmostSideBin; i < RightmostSideBin; i++) { WidebandBitsPacked[i / 8] |= (byte)(1 << (i % 8)); }
                    }
                }
            }
        }

        for (int i = 0; i < PeakBitsPacked.Length; i++)
        {
            PeakBitsPacked[i] = (byte)(PeakBitsPacked[i] & ~WidebandBitsPacked[i]);
        }
    }

    public override void Stop()
    {
        KeepGoing = false;
        InputDataEvent.Set();
        ProcessThread?.Join();
    }

    public void AddTimingReceiver(TimingReceiver receiver, float period)
    {
        lock (TimingReceivers)
        {
            TimingReceiverData[] OldData = TimingReceivers;
            TimingReceiverData[] NewData = new TimingReceiverData[OldData.Length + 1];
            Array.Copy(OldData, NewData, OldData.Length);
            NewData[^1] = new()
            {
                Receiver = receiver,
                OriginalPeriod = period,
                Period = (period <= 0) ? (uint)MathF.Round(-period) : (uint)MathF.Round(period * this.SampleRate)
            };

            TimingReceivers = NewData;
            IsTimingSource = true;
        }
    }

    public void RemoveTimingReceiver(TimingReceiver receiver)
    {
        lock (TimingReceivers)
        {
            int Index = Array.FindIndex(TimingReceivers, x => x.Receiver == receiver);
            if (Index < 0) { return; }

            if (TimingReceivers.Length == 1) // Last receiver
            {
                TimingReceivers = Array.Empty<TimingReceiverData>();
                IsTimingSource = false;
                return;
            }

            TimingReceiverData[] OldData = TimingReceivers;
            TimingReceiverData[] NewData = new TimingReceiverData[OldData.Length - 1];
            if (Index > 0) { Array.Copy(OldData, 0, NewData, 0, Index); } // Copy items on the left of the removed item
            else if (Index < OldData.Length - 1) { Array.Copy(OldData, Index + 1, NewData, Index, OldData.Length - Index - 1); } // Copy items on the right of the removed item

            TimingReceivers = NewData;
        }
    }

    internal void RunTimingReceivers(uint samplesProcessed)
    {
        if (!this.IsTimingSource) { return; }
        bool CalculatedOutput = false;
        lock (this.TimingReceivers)
        {
            for (int i = 0; i < this.TimingReceivers.Length; i++)
            {
                ref TimingReceiverData Receiver = ref this.TimingReceivers[i];
                Receiver.CurrentIncrement += samplesProcessed;
                if (Receiver.CurrentIncrement >= Receiver.Period)
                {
                    if (!CalculatedOutput) { this.DFT.CalculateOutput(); CalculatedOutput = true; }
                    Receiver.Receiver.Invoke();
                    Receiver.CurrentIncrement -= Receiver.Period;
                    if (Receiver.Period != 0 && Receiver.CurrentIncrement > Receiver.Period * 16)
                    {
                        Log.Warn($"{nameof(Gen2NoteFinder)} has timing receiver that is falling behind, the receiver {Receiver.Receiver} has a period of {Receiver.Period} samples which is too short to effectively call.");
                    }
                }
            }
        }
    }

    private struct TimingReceiverData
    {
        /// <summary> The receiver to call whenever this event occurs. </summary>
        public TimingReceiver Receiver { get; init; }

        /// <summary> The original request from the receiver. If a period in seconds was requested, the internal period in samples needs to be updated if the sample rate changes. </summary>
        public float OriginalPeriod { get; init; }

        /// <summary> The period on which to send this event, in samples. </summary>
        public uint Period { get; set; }

        /// <summary> How many samples have been processed since this callback was last dispatched. </summary>
        public uint CurrentIncrement { get; set; }
    }
}
