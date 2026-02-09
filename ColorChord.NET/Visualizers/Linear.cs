using ColorChord.NET.API;
using ColorChord.NET.API.Config;
using ColorChord.NET.API.Controllers;
using ColorChord.NET.API.NoteFinder;
using ColorChord.NET.API.Outputs;
using ColorChord.NET.API.Visualizers;
using ColorChord.NET.API.Visualizers.Formats;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace ColorChord.NET.Visualizers;

public class Linear : IVisualizer, IDiscrete1D, IContinuous1D, IControllableAttr
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

    /// <summary> How many times per second the output should be updated. </summary>
    [Controllable("FrameRate", 2)]
    [ConfigInt("FrameRate", 0, 1000, 60)]
    public int FrameRate { get; set; } = 60;

    /// <summary> The number of milliseconds to wait between output updates. </summary>
    private int FramePeriod => 1000 / this.FrameRate;

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
    private readonly List<IOutput> Outputs = new();

    private uint[] OutputDataDiscrete = Array.Empty<uint>();

    private readonly ContinuousDataUnit[] OutputDataContinuous;
    private int OutputCountContinuous;
    private float OutputAdvanceContinuous;

    /// <summary> Whether to continue processing, or stop threads and finish up in preparation for closing the application. </summary>
    private bool KeepGoing = true;

    /// <summary>Used to make sure a controller changing a setting does not happen in the middle of a processing cycle.</summary>
    private readonly object SettingUpdateLock = new();

    /// <summary> The thread on which input note data is processed by this visualizer. </summary>
    private Thread? ProcessThread;

    private readonly int NoteCount, BinsPerOctave;

    public Linear(string name, Dictionary<string, object> config)
    {
        this.Name = name;
        ColorChordAPI.Configurer.Configure(this, config);
        this.NoteFinder = ColorChordAPI.Configurer.FindNoteFinder(config) ?? throw new Exception($"{nameof(Linear)} \"{name}\" could not find the NoteFinder to attach to.");
        this.NoteCount = this.NoteFinder.NoteCount;
        this.BinsPerOctave = this.NoteFinder.BinsPerOctave;
        this.OutputDataContinuous = new ContinuousDataUnit[this.NoteCount];
        this.LastVectorCenters = new float[this.NoteCount];
        for (int i = 0; i < this.OutputDataContinuous.Length; i++) { this.OutputDataContinuous[i] = new ContinuousDataUnit(); }
    }

    /// <summary> Used to update internal structures when the number of LEDs changes. </summary>
    private void UpdateSize()
    {
        this.OutputDataDiscrete = new uint[this.LEDCount];
        this.LastLEDColours = new float[this.LEDCount];
        this.LastLEDPositionsFiltered = new float[this.LEDCount];
        this.LastLEDSaturations = new float[this.LEDCount];
    }

    public void Start()
    {
        if (this.LEDCount <= 0) { Log.Error("Attempted to start Linear visualizer \"" + this.Name + "\" with invalid LED count."); return; }
        this.KeepGoing = true;
        this.ProcessThread = new Thread(DoProcessing) { Name = "Linear " + this.Name };
        this.ProcessThread.Start();
        this.NoteFinder.AdjustOutputSpeed((uint)this.FramePeriod);
    }

    public void Stop()
    {
        this.KeepGoing = false;
        this.ProcessThread?.Join();
    }

    public void AttachOutput(IOutput output) { if (output != null) { this.Outputs.Add(output); } }

    private void DoProcessing()
    {
        Stopwatch Timer = new();
        while (this.KeepGoing)
        {
            Timer.Restart();
            Update();
            foreach(IOutput Output in this.Outputs) { Output.Dispatch(); } // TODO: If an output gets added while this is running, this crashes.
            int WaitTime = (int)(this.FramePeriod - Timer.ElapsedMilliseconds);
            if (WaitTime > 0) { Thread.Sleep(WaitTime); }
        }
    }

    public void SettingWillChange(int controlID)
    {
        if (controlID == 1) { Monitor.Enter(this.SettingUpdateLock); }
    }

    public void SettingChanged(int controlID)
    {
        if (controlID == 1)
        {
            UpdateSize();
            Monitor.Exit(this.SettingUpdateLock);
        }
        else if (controlID == 2) { this.NoteFinder.AdjustOutputSpeed((uint)this.FramePeriod); }
    }

    public int GetCountDiscrete() => this.LEDCount;
    public uint[] GetDataDiscrete() => this.OutputDataDiscrete;

    public int GetCountContinuous() => this.OutputCountContinuous;
    public ContinuousDataUnit[] GetDataContinuous() => this.OutputDataContinuous;
    public float GetAdvanceContinuous() => this.OutputAdvanceContinuous;
    public int MaxPossibleUnits { get => this.NoteCount; }

    // These variables are only used to keep inter-frame info for Update(). Do not touch.
    private float[] LastLEDColours = Array.Empty<float>();
    private float[] LastLEDPositionsFiltered = Array.Empty<float>(); // Only used when IsCircular is true.
    private float[] LastLEDSaturations = Array.Empty<float>();
    private int PrevAdvance;
    private readonly float[] LastVectorCenters; // Where the center-point of each block was last frame

    private struct InternalNote : IComparable<InternalNote>
    {
        /// <summary>The amplitudes of each note, time-smoothed</summary>
        public float AmplitudeSmooth { get; set; }
        /// <summary>The amplitudes of each note, with minimal time-smoothing</summary>
        public float AmplitudeFast { get; set; }
        /// <summary>The locations of the notes on the scale, range 0 ~ 1</summary>
        public float Position { get; set; }

        public int CompareTo(InternalNote other) => this.Position.CompareTo(other.Position);
    }

    public void Update()
    {

        lock (this.SettingUpdateLock)
        {
            InternalNote[] Notes = new InternalNote[this.NoteCount];
            float AmplitudeSum = 0;

            if (this.OutputDataDiscrete.Length != this.LEDCount) { UpdateSize(); }

            // Populate data from the NoteFinder.
            for (int i = 0; i < this.NoteCount; i++)
            {
                Notes[i].Position = this.NoteFinder.Notes[i].Position / this.BinsPerOctave;
                Notes[i].AmplitudeSmooth = MathF.Pow(this.NoteFinder.Notes[i].AmplitudeFiltered, this.LightSiding);
                Notes[i].AmplitudeFast = MathF.Pow(this.NoteFinder.Notes[i].Amplitude, this.LightSiding);
                AmplitudeSum += Notes[i].AmplitudeSmooth;
            }

            // Adjust AmplitudeSum to remove notes that are too weak to be included.
            float AmplitudeSumAdj = 0;
            for (int i = 0; i < this.NoteCount; i++)
            {
                Notes[i].AmplitudeSmooth -= this.LEDFloor * AmplitudeSum;
                if (Notes[i].AmplitudeSmooth / AmplitudeSum < 0) // Note too weak, remove it from consideration.
                {
                    Notes[i].AmplitudeSmooth = 0;
                    Notes[i].AmplitudeFast = 0;
                }
                AmplitudeSumAdj += Notes[i].AmplitudeSmooth;
            }
            AmplitudeSum = AmplitudeSumAdj;
            // AmplitudeSum now only includes notes that are large enough (relative to others) to be worth displaying.

            float[] LEDColours = new float[this.LEDCount]; // The colour (range 0 ~ 1) of each LED in the chain.
            float[] LEDAmplitudes = new float[this.LEDCount]; // The amplitude (time-smoothed) of each LED in the chain.
            float[] LEDAmplitudesFast = new float[this.LEDCount]; // The amplitude (fast-updating) of each LED in the chain.

            int LEDsFilled = 0; // How many LEDs have been assigned a colour.
            float VectorPosition = 0; // Where in the continuous line we are (continuous equivalent of LEDsFilled).
            this.OutputCountContinuous = 0;
            float[] VectorCenters = new float[this.NoteCount];

            if (this.IsOrdered) { Array.Sort(Notes); } // Sort the notes by their location on the scale; map this to the line

            // Fill the LED slots with available notes.
            for (int NoteIndex = 0; NoteIndex < this.NoteCount; NoteIndex++)
            {
                // How many of the LEDs should be taken up by this colour.
                int LEDCountColour = (int)((Notes[NoteIndex].AmplitudeSmooth / AmplitudeSum) * this.LEDCount);
                // Fill those LEDs with this note's data.
                for (int LEDIndex = 0; LEDIndex < LEDCountColour && LEDsFilled < this.LEDCount; LEDIndex++)
                {
                    LEDColours[LEDsFilled] = Notes[NoteIndex].Position;
                    LEDAmplitudes[LEDsFilled] = Notes[NoteIndex].AmplitudeSmooth;
                    LEDAmplitudesFast[LEDsFilled] = Notes[NoteIndex].AmplitudeFast;
                    LEDsFilled++;
                }

                // For continuous outputs
                float VectorSizeColour = (Notes[NoteIndex].AmplitudeSmooth / AmplitudeSum);
                if (VectorSizeColour == 0 || float.IsNaN(VectorSizeColour)) { continue; }
                this.OutputDataContinuous[this.OutputCountContinuous].Location = VectorPosition;
                this.OutputDataContinuous[this.OutputCountContinuous].Size = VectorSizeColour;
                VectorCenters[NoteIndex] = VectorPosition + (VectorSizeColour / 2);

                float OutSaturation = (this.SteadyBright ? Notes[NoteIndex].AmplitudeSmooth : Notes[NoteIndex].AmplitudeFast) * this.SaturationAmplifier;
                if (OutSaturation > 1) { OutSaturation = 1; }
                if (OutSaturation > LEDLimit) { OutSaturation = LEDLimit; }

                uint Colour = VisualizerTools.CCToRGB(Notes[NoteIndex].Position, 1.0F, OutSaturation);
                this.OutputDataContinuous[this.OutputCountContinuous].R = (byte)((Colour >> 16) & 0xff);
                this.OutputDataContinuous[this.OutputCountContinuous].G = (byte)((Colour >> 8) & 0xff);
                this.OutputDataContinuous[this.OutputCountContinuous].B = (byte)((Colour) & 0xff);
                this.OutputDataContinuous[this.OutputCountContinuous].Colour = Notes[NoteIndex].Position;

                VectorPosition += VectorSizeColour;
                this.OutputCountContinuous++;
            }

            // If there are no notes to display, set the first to 0.
            if (LEDsFilled == 0)
            {
                LEDColours[0] = 0;
                LEDAmplitudes[0] = 0;
                LEDAmplitudesFast[0] = 0;
                LEDsFilled++;
            }

            // Fill the remaining LEDs at the end with the last present colour.
            // If there are no notes to display, fills the strip with 0s.
            // If there are notes, this should only fill the last few in case of rounding errors earlier.
            for (; LEDsFilled < this.LEDCount; LEDsFilled++)
            {
                LEDColours[LEDsFilled] = LEDColours[LEDsFilled - 1];
                LEDAmplitudes[LEDsFilled] = LEDAmplitudes[LEDsFilled - 1];
                LEDAmplitudesFast[LEDsFilled] = LEDAmplitudesFast[LEDsFilled - 1];
            }

            // In case of a circular display, we need to try and keep the colours in the same locations between frames.
            int Advance = 0; // How many LEDs to shift the output by to achieve minimal movement.

            // Advance is not used in non-circular displays.
            if (this.IsCircular)
            {
                // Used to compare inter-frame difference for different Advance values.
                float MinDifference = 1e20F;

                // Check every potential Advance value to find the best for this frame.
                for (int ShiftQty = 0; ShiftQty < this.LEDCount; ShiftQty++)
                {
                    float ThisDistance = 0;

                    // Check how different the colours are at each LED compared to last frame.
                    for (int LEDIndex = 0; LEDIndex < this.LEDCount; LEDIndex++)
                    {
                        int NewIndex = (LEDIndex + ShiftQty) % this.LEDCount;
                        float ColourDifference = MinCircleDistance(LastLEDPositionsFiltered[LEDIndex], LEDColours[NewIndex]);
                        ThisDistance += ColourDifference;
                    }

                    // Compare the Advance value of this and last frame if we were to use ShiftQty as the new Advance.
                    int AdvanceDifference = Math.Abs(PrevAdvance - ShiftQty);
                    if (AdvanceDifference > this.LEDCount / 2) AdvanceDifference = this.LEDCount - AdvanceDifference;

                    float NormAdvance = (float)AdvanceDifference / this.LEDCount; // Normalized advance difference (range 0 ~ 1)
                    ThisDistance += NormAdvance * NormAdvance;

                    if (ThisDistance < MinDifference) // We found a better shift distance.
                    {
                        MinDifference = ThisDistance;
                        Advance = ShiftQty;
                    }
                }

                // For continuous output mode
                float VectorCenterOffset = 0;
                for (int NoteIndex = 0; NoteIndex < this.NoteCount; NoteIndex++)
                {
                    VectorCenterOffset += (VectorCenters[NoteIndex] - LastVectorCenters[NoteIndex]) * Notes[NoteIndex].AmplitudeSmooth; // TODO: CONSIDER USINGBLOCK SIZE INSTEAD OF AMPLITUDE
                }
                //VectorCenterOffset /= (this.OutputCountContinuous + 2); // This is now an average offset.
                const float IIR = 0.6F;
                this.OutputAdvanceContinuous = (VectorCenterOffset * IIR) + (this.OutputAdvanceContinuous * (1 - IIR));
                if (float.IsNaN(this.OutputAdvanceContinuous)) { this.OutputAdvanceContinuous = 0; }
            }
            this.PrevAdvance = Advance;

            // Shift the LEDs by Advance, then output.
            for (int LEDIndex = 0; LEDIndex < this.LEDCount; LEDIndex++)
            {
                // The index, shifted by Advance.
                int ShiftedIndex = (LEDIndex + Advance + this.LEDCount) % this.LEDCount;

                float Saturation = LEDAmplitudes[ShiftedIndex] * this.SaturationAmplifier;
                float SaturationFast = LEDAmplitudesFast[ShiftedIndex] * this.SaturationAmplifier;
                if (SaturationFast > 1) { SaturationFast = 1; }

                LastLEDColours[LEDIndex] = LEDColours[ShiftedIndex];
                LastLEDSaturations[LEDIndex] = Saturation;

                float OutSaturation = (this.SteadyBright ? Saturation : SaturationFast);
                if (OutSaturation > 1) { OutSaturation = 1; }
                if (OutSaturation > LEDLimit) { OutSaturation = LEDLimit; }

                uint Colour = VisualizerTools.CCToRGB(LastLEDColours[LEDIndex], 1.0F, OutSaturation);

                this.OutputDataDiscrete[LEDIndex] = Colour;
            }

            if (this.IsCircular)
            {
                for (int i = 0; i < this.LEDCount; i++)
                {
                    LastLEDPositionsFiltered[i] = (LastLEDPositionsFiltered[i] * 0.9F) + (LastLEDColours[i] * 0.1F);
                }
                for (int i = 0; i < this.NoteCount; i++) { LastVectorCenters[i] = VectorCenters[i]; }
            }
        }
    }

    /// <summary> Gets the shortest distance of two points around the circumference of a circle, where the circumference is 1.0. </summary>
    /// <param name="a"> Location of point A. </param>
    /// <param name="b"> Location of point B. </param>
    /// <returns> The (positive) distance between the two points using the more direct route. </returns>
    private static float MinCircleDistance(float a, float b)
    {
        // The distance by just going straight.
        float DirectDiff = Math.Abs(a - b);

        // The distance if we wrap around the "ends" of the circle.
        float WrapDiff = (a < b) ? (a + 1) : (a - 1);
        WrapDiff -= b;
        WrapDiff = Math.Abs(WrapDiff);

        return Math.Min(DirectDiff, WrapDiff);
    }
}
