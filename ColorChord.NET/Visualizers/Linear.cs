using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Threading;
using ColorChord.NET.API;
using ColorChord.NET.API.Config;
using ColorChord.NET.API.Controllers;
using ColorChord.NET.API.NoteFinder;
using ColorChord.NET.API.Outputs;
using ColorChord.NET.API.Timing;
using ColorChord.NET.API.Visualizers;
using ColorChord.NET.API.Visualizers.Formats;

namespace ColorChord.NET.Visualizers;

public class Linear : IVisualizer, IDiscrete1D, IContinuous1D, IControllableAttr, ITimingReceiver
{
    /// <summary> A unique name for this visualizer instance, used for referring to it from other components. </summary>
    public string Name { get; private init; }

    public NoteFinderCommon NoteFinder { get; private init; }

    /// <summary> The number of discrete elements outputted by this visualizer. </summary>
    /// <remarks> If only using continuous mode, set this to 12 or 24. </remarks>
    [Controllable(ConfigNames.LED_COUNT, 1)]
    [ConfigInt(ConfigNames.LED_COUNT, 1, 100000, 50)]
    public int LEDCount { get; set; }

    /// <summary> Whether the visualizer is currently processing/outputting. </summary>
    [Controllable(ConfigNames.ENABLE)]
    [ConfigBool(ConfigNames.ENABLE, true)]
    public bool Enabled { get; set; }

    [ConfigTimeSource]
    private TimingConnection? TimeSource { get; set; }

    /// <summary> Whether the output should be treated as a line with ends, or a continuous circle with the ends joined. </summary>
    [Controllable("IsCircular")]
    [ConfigBool("IsCircular", false)]
    public bool IsCircular { get; set; }

    /// <summary> Exponent used to convert raw note amplitudes to strength. </summary>
    [Controllable("LightSiding")]
    [ConfigFloat("LightSiding", 0F, 100F, 1F)]
    public float LightSiding { get; set; }

    /// <summary>Whether new notes should appear in semi-random locations (false), or all notes should always remain sorted by their colour (true).</summary>
    [Controllable("IsOrdered")]
    [ConfigBool("IsOrdered", false)]
    public bool IsOrdered { get; set; }

    /// <summary> Whether to use smoothed input data (to reduce flicker at the cost of higher response time), or only the latest data. </summary>
    [Controllable("SteadyBright")]
    [ConfigBool("SteadyBright", false)]
    public bool SteadyBright { get; set; }

    /// <summary> The minimum relative amplitude of a note required to consider it for output. </summary>
    [Controllable("LEDFloor")]
    [ConfigFloat("LEDFloor", 0F, 1F, 0.1F)]
    public float LEDFloor { get; set; }

    /// <summary> The maximum brightness to cap output at. </summary>
    /// <remarks> Useful to limit current consumption in physical systems. </remarks>
    [Controllable("LEDLimit")]
    [ConfigFloat("LEDLimit", 0F, 1F, 1F)]
    public float LEDLimit { get; set; }

    /// <summary> How much to amplify saturation (LED brightness) after processing. </summary>
    [Controllable("SaturationAmplifier")]
    [ConfigFloat("SaturationAmplifier", 0F, 100F, 1.6F)]
    public float SaturationAmplifier { get; set; }

    /// <summary> All outputs that need to be notified when new data is available. </summary>
    private readonly List<IOutput> Outputs = [];

    private uint[] OutputDataDiscrete = [];

    private readonly ContinuousDataUnit[] OutputDataContinuous;
    private int OutputCountContinuous;
    private float OutputAdvanceContinuous;

    /// <summary> Whether to continue processing, or stop threads and finish up in preparation for closing the application. </summary>
    private bool KeepGoing = true;

    private readonly AutoResetEvent OutputDispatchTrigger;
    private Thread? OutputDispatchThread;

    /// <summary>Used to make sure a controller changing a setting does not happen in the middle of a processing cycle.</summary>
    private readonly Lock SettingUpdateLock = new();

    private readonly int NoteCount, BinsPerOctave;

    public Linear(string name, Dictionary<string, object> config)
    {
        this.Name = name;
        ColorChordAPI.Configurer.Configure(this, config);
        this.NoteFinder = ColorChordAPI.Configurer.FindNoteFinder(config) ?? throw new Exception($"{nameof(Linear)} \"{name}\" could not find the NoteFinder to attach to.");
        this.NoteCount = this.NoteFinder.NoteCount;
        this.BinsPerOctave = this.NoteFinder.BinsPerOctave;
        this.OutputDataContinuous = new ContinuousDataUnit[this.NoteCount];
        for (int i = 0; i < this.OutputDataContinuous.Length; i++) { this.OutputDataContinuous[i] = new ContinuousDataUnit(); }
        this.OutputDispatchTrigger = new(false);
        UpdateSize();
    }

    /// <summary> Used to update internal structures when the number of LEDs changes. </summary>
    [MemberNotNull(nameof(OutputDataDiscrete), nameof(NoteChromas), nameof(NoteAmplitudes), nameof(BlockLocations), nameof(BlockNoteIDs), nameof(PrevBlockLocations), nameof(PrevBlockChromas))]
    private void UpdateSize()
    {
        this.OutputDataDiscrete = new uint[this.LEDCount];
        this.NoteChromas = new float[this.NoteCount];
        this.NoteAmplitudes = new InternalNoteAmplitude[this.NoteCount];
        this.BlockLocations = new float[this.NoteCount + 1];
        this.BlockNoteIDs = new short[this.NoteCount + 1];
        this.PrevBlockLocations = new float[this.NoteCount + 1];
        this.PrevBlockChromas = new float[this.NoteCount + 1];
    }

    public void Start()
    {
        if (this.TimeSource == null && this.NoteFinder is ITimingSource NoteFinderAsTiming) { this.TimeSource = new(NoteFinderAsTiming, new TimePeriod(1, TimeUnit.Buffer), this); }
        this.KeepGoing = true;
        if (this.TimeSource != null && !this.TimeSource.IsSynchronous)
        {
            this.OutputDispatchThread = new(RunOutputDispatch) { Name = $"{nameof(Linear)} '{this.Name}' Output Dispatch" };
            this.OutputDispatchThread.Start();
        }
    }

    public void Stop()
    {
        this.TimeSource?.Remove();
        this.KeepGoing = false;
        this.OutputDispatchTrigger.Set();
        this.OutputDispatchThread?.Join();
    }

    public void AttachOutput(IOutput output) { this.Outputs.Add(output); }

    public void TimingCallback(object? sender)
    {
        Update();
        if (this.TimeSource!.IsSynchronous) { DoOutputDispatch(); }
        else { this.OutputDispatchTrigger.Set(); }
    }

    private void RunOutputDispatch()
    {
        while (this.KeepGoing)
        {
            this.OutputDispatchTrigger.WaitOne();
            DoOutputDispatch();
        }
    }

    private void DoOutputDispatch()
    {
        foreach (IOutput Out in this.Outputs) { Out.Dispatch(); }
    }

    public void SettingWillChange(int controlID)
    {
        if (controlID == 1) { this.SettingUpdateLock.Enter(); }
    }

    public void SettingChanged(int controlID)
    {
        if (controlID == 1)
        {
            UpdateSize();
            this.SettingUpdateLock.Exit();
        }
    }

    public int GetCountDiscrete() => this.LEDCount;
    public uint[] GetDataDiscrete() => this.OutputDataDiscrete;

    public int GetCountContinuous() => this.OutputCountContinuous;
    public ContinuousDataUnit[] GetDataContinuous() => this.OutputDataContinuous;
    public float GetAdvanceContinuous() => this.OutputAdvanceContinuous;
    public int MaxPossibleUnits { get => this.NoteCount; }


    private struct InternalNoteAmplitude
    {
        /// <summary>The amplitudes of each note, time-smoothed</summary>
        public float Smooth;
        /// <summary>The amplitudes of each note, with minimal time-smoothing</summary>
        public float Fast;
    }

    // These variables are only used to keep inter-frame info for Update(). Do not touch.
    private float[] NoteChromas;
    private InternalNoteAmplitude[] NoteAmplitudes;

    private float[] BlockLocations;
    private short[] BlockNoteIDs;
    private short BlockCount;

    private float[] PrevBlockLocations;
    private float[] PrevBlockChromas;
    private short PrevBlockCount;

    public void Update()
    {
        lock (this.SettingUpdateLock)
        {
            if (this.OutputDataDiscrete.Length != this.LEDCount) { UpdateSize(); }
            float AmplitudeSum = 0;

            // Populate data from the NoteFinder.
            for (int i = 0; i < this.NoteCount; i++)
            {
                NoteChromas[i] = this.NoteFinder.Notes[i].Position / this.BinsPerOctave;
                NoteAmplitudes[i].Smooth = MathF.Pow(this.NoteFinder.Notes[i].AmplitudeFiltered, this.LightSiding);
                NoteAmplitudes[i].Fast = MathF.Pow(this.NoteFinder.Notes[i].Amplitude, this.LightSiding);
                AmplitudeSum += NoteAmplitudes[i].Smooth;
            }

            // Adjust AmplitudeSum to remove notes that are too weak to be included.
            float AmplitudeSumAdj = 0;
            int AmpIndex = 0;
            if (Avx2.IsSupported)
            {
                Vector128<float> SumAdj = Vector128<float>.Zero;
                while (AmpIndex + 4 <= this.NoteCount)
                {
                    Vector128<float> Floor = Vector128.Create(this.LEDFloor);
                    Vector128<float> AmplitudeSumVec = Vector128.Create(AmplitudeSum);

                    Vector256<float> Amplitudes = Vector256.LoadUnsafe(ref NoteAmplitudes[AmpIndex].Smooth);
                    Vector128<float> Smooth = Sse.Shuffle(Amplitudes.GetLower(), Amplitudes.GetUpper(), 0b10001000);
                    Vector128<float> SmoothShrunk = Sse.Subtract(Smooth, Sse.Multiply(Floor, AmplitudeSumVec));
                    Vector256<float> AmplitudesModified = Avx.Blend(Amplitudes, Avx2.ConvertToVector256Int64(SmoothShrunk.AsUInt32()).AsSingle(), 0b01010101);

                    Vector128<int> AmplitudeMaskInv = Sse2.ShiftRightArithmetic(SmoothShrunk.AsInt32(), 31);
                    Vector128<float> MaskedSmooth = Sse.AndNot(AmplitudeMaskInv.AsSingle(), SmoothShrunk);
                    Vector256<ulong> AmplitudeMaskInvBig = Avx2.ConvertToVector256Int64(AmplitudeMaskInv).AsUInt64();
                    Vector256<float> MaskedAmplitudes = Avx.AndNot(AmplitudeMaskInvBig.AsSingle(), AmplitudesModified);
                    MaskedAmplitudes.StoreUnsafe(ref NoteAmplitudes[AmpIndex].Smooth);
                    SumAdj = Sse.Add(SumAdj, MaskedSmooth);
                    AmpIndex += 4;
                }
                Vector128<float> SumAdjH = Sse3.HorizontalAdd(SumAdj, SumAdj);
                AmplitudeSumAdj = SumAdjH[0] + SumAdjH[1];
            }
            while (AmpIndex < this.NoteCount) // TODO: if NoteCount is divisible by 4, can skip on AVX2-capable
            {
                NoteAmplitudes[AmpIndex].Smooth -= this.LEDFloor * AmplitudeSum;
                if (NoteAmplitudes[AmpIndex].Smooth / AmplitudeSum < 0) // Note too weak, remove it from consideration.
                {
                    NoteAmplitudes[AmpIndex].Smooth = 0;
                    NoteAmplitudes[AmpIndex].Fast = 0;
                }
                AmplitudeSumAdj += NoteAmplitudes[AmpIndex].Smooth;
                AmpIndex++;
            }
            AmplitudeSum = AmplitudeSumAdj;
            // AmplitudeSum now only includes notes that are large enough (relative to others) to be worth displaying.
            
            if (this.IsOrdered) { Array.Sort(NoteChromas, NoteAmplitudes); } // Sort the notes by their location on the scale; map this to the line

            // Convert notes to colour blocks
            float CurrentBlockPosition = 0F;
            this.BlockCount = 0;
            for (int NoteIndex = 0; NoteIndex < this.NoteCount; NoteIndex++)
            {
                if (NoteAmplitudes[NoteIndex].Smooth > 0F)
                {
                    this.BlockLocations[this.BlockCount] = CurrentBlockPosition;
                    this.BlockNoteIDs[this.BlockCount] = (short)NoteIndex;
                    float BlockSize = NoteAmplitudes[NoteIndex].Smooth / AmplitudeSum;
                    CurrentBlockPosition += BlockSize;
                    this.BlockCount++;
                }
            }
            if (this.BlockCount != 0)
            {
                this.BlockLocations[this.BlockCount] = 1F;
                this.BlockNoteIDs[this.BlockCount] = -1;
                this.BlockCount++;
            }

            // Find best offset for circular mode
            float ShiftAverage = 0F;
            if (this.IsCircular)
            {
                for (int BlockIndex = 0; BlockIndex < this.BlockCount - 1; BlockIndex++)
                {
                    if (this.BlockNoteIDs[BlockIndex] == -1) { continue; } // TODO: This shouldn't happen?
                    float ChromaHere = NoteChromas[this.BlockNoteIDs[BlockIndex]];
                    float SizeHere = this.BlockLocations[BlockIndex + 1] - this.BlockLocations[BlockIndex];
                    float CenterHere = this.BlockLocations[BlockIndex] + (SizeHere * 0.5F);

                    int ClosestBlockIndex = 0;
                    float ClosestBlockChromaDiff = float.PositiveInfinity;
                    for (int OtherBlock = 0; OtherBlock < this.PrevBlockCount - 1; OtherBlock++)
                    {
                        float ChromaThere = PrevBlockChromas[OtherBlock];
                        float ChromaDiff = MathF.Abs(ChromaHere - ChromaThere);
                        if (ChromaDiff < ClosestBlockChromaDiff)
                        {
                            ClosestBlockIndex = OtherBlock;
                            ClosestBlockChromaDiff = ChromaDiff;
                        }
                    }

                    float SizeThere = this.PrevBlockLocations[ClosestBlockIndex + 1] - this.PrevBlockLocations[ClosestBlockIndex];
                    float CenterThere = this.PrevBlockLocations[ClosestBlockIndex] + (SizeThere * 0.5F);
                    float DirectDiff = CenterThere - CenterHere;
                    float WrapDiff = ((CenterThere < CenterHere) ? CenterThere + 1F : CenterThere - 1F) - CenterHere;
                    float MinDistance = (MathF.Abs(DirectDiff) < MathF.Abs(WrapDiff)) ? DirectDiff : WrapDiff;
                    ShiftAverage += MinDistance * SizeHere;
                }
                ShiftAverage = (ShiftAverage + 1F) % 1F;
            }

            // Populate LEDs
            if (this.BlockCount == 0) { this.OutputDataDiscrete.AsSpan().Clear(); }
            else
            {
                int LEDShift = (int)MathF.Round(ShiftAverage * this.LEDCount);
                int LEDIndex = 0;
                for (int BlockIndex = 0; BlockIndex < this.BlockCount - 1; BlockIndex++)
                {
                    short NoteIDHere = this.BlockNoteIDs[BlockIndex];
                    float ChromaHere = this.NoteChromas[NoteIDHere];
                    float SizeHere = this.BlockLocations[BlockIndex + 1] - this.BlockLocations[BlockIndex];
                    int LEDCountHere = (int)MathF.Round(SizeHere * this.LEDCount);
                    if (BlockIndex == this.BlockCount - 2) { LEDCountHere = this.LEDCount - LEDIndex; }

                    InternalNoteAmplitude AmplitudesHere = this.NoteAmplitudes[NoteIDHere];
                    float OutSaturation = (this.SteadyBright ? AmplitudesHere.Smooth : AmplitudesHere.Fast) * this.SaturationAmplifier;
                    OutSaturation = MathF.Min(OutSaturation, this.LEDLimit);
                    uint ColourHere = VisualizerTools.CCToRGB(ChromaHere, 1.0F, OutSaturation);

                    this.OutputDataContinuous[BlockIndex].Colour = ChromaHere;
                    this.OutputDataContinuous[BlockIndex].Location = this.BlockLocations[BlockIndex];
                    this.OutputDataContinuous[BlockIndex].Size = SizeHere;
                    this.OutputDataContinuous[BlockIndex].R = (byte)((ColourHere >> 16) & 0xFF);
                    this.OutputDataContinuous[BlockIndex].G = (byte)((ColourHere >> 8) & 0xFF);
                    this.OutputDataContinuous[BlockIndex].B = (byte)(ColourHere & 0xFF);

                    if (LEDCountHere <= 0) { continue; }
                    int StartLED = (LEDIndex + LEDShift) % this.LEDCount;
                    int EndLED = (LEDIndex + LEDShift + LEDCountHere) % this.LEDCount;
                    if (EndLED <= StartLED) // wrap-around
                    {
                        this.OutputDataDiscrete.AsSpan(StartLED).Fill(ColourHere);
                        this.OutputDataDiscrete.AsSpan(0, EndLED).Fill(ColourHere);
                    }
                    else { this.OutputDataDiscrete.AsSpan(StartLED, LEDCountHere).Fill(ColourHere); }
                    LEDIndex += LEDCountHere;
                }
            }
            this.OutputCountContinuous = Math.Max(0, this.BlockCount - 1);
            this.OutputAdvanceContinuous = ShiftAverage;

            // Save off data for next cycle
            this.BlockLocations.AsSpan().CopyTo(this.PrevBlockLocations);
            this.PrevBlockCount = this.BlockCount;
            for (int BlockIndex = 0; BlockIndex < this.PrevBlockChromas.Length; BlockIndex++)
            {
                short NoteID = this.BlockNoteIDs[BlockIndex];
                this.PrevBlockChromas[BlockIndex] = NoteID > 0 ? this.NoteChromas[NoteID] : 0F;
            }
        }
    }
}
