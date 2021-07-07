using ColorChord.NET.Config;
using ColorChord.NET.Outputs;
using ColorChord.NET.Visualizers.Formats;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace ColorChord.NET.Visualizers
{
    public class Linear : IVisualizer, IDiscrete1D, IContinuous1D
    {
        /// <summary> A unique name for this visualizer instance, used for referring to it from other components. </summary>
        public string Name { get; private init; }

        /// <summary> The number of discrete elements outputted by this visualizer. </summary>
        /// <remarks> If only using continuous mode, set this to 12 or 24. </remarks>
        [ConfigInt("LEDCount", 1, 100000, 50)]
        public int LEDCount
        {
            get => this.P_LEDCount;
            set
            {
                this.P_LEDCount = value;
                UpdateSize();
            }
        }
        private int P_LEDCount;

        /// <summary> Whether the visualizer is currently processing/outputting. </summary>
        [ConfigBool("Enable", true)]
        public bool Enabled { get; set; }

        /// <summary> How many times per second the output should be updated. </summary>
        [ConfigInt("FrameRate", 0, 1000, 60)]
        public int FrameRate { get; set; } = 60;

        /// <summary> The number of milliseconds to wait between output updates. </summary>
        private int FramePeriod => 1000 / this.FrameRate;

        /// <summary> Whether the output should be treated as a line with ends, or a continuous circle with the ends joined. </summary>
        [ConfigBool("IsCircular", false)]
        public bool IsCircular { get; set; }

        /// <summary> Exponent used to convert raw note amplitudes to strength. </summary>
        [ConfigFloat("LightSiding", 0F, 100F, 1F)]
        public float LightSiding { get; set; }

        /// <summary> Whether to use smoothed input data (to reduce flicker at the cost of higher response time), or only the latest data. </summary>
        [ConfigBool("SteadyBright", false)]
        public bool SteadyBright { get; set; }

        /// <summary> The minimum relative amplitude of a note required to consider it for output. </summary>
        [ConfigFloat("LEDFloor", 0F, 1F, 0.1F)]
        public float LEDFloor { get; set; }

        /// <summary> The maximum brightness to cap output at. </summary>
        /// <remarks> Useful to limit current consumption in physical systems. </remarks>
        [ConfigFloat("LEDLimit", 0F, 1F, 1F)]
        public float LEDLimit { get; set; }

        /// <summary> How much to amplify saturation (LED brightness) after processing. </summary>
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

        /// <summary> The thread on which input note data is processed by this visualizer. </summary>
        private Thread? ProcessThread;

        public Linear(string name, Dictionary<string, object> config)
        {
            this.Name = name;
            Configurer.Configure(this, config);
            this.OutputDataContinuous = new ContinuousDataUnit[BaseNoteFinder.NoteCount];
            for (int i = 0; i < this.OutputDataContinuous.Length; i++) { this.OutputDataContinuous[i] = new ContinuousDataUnit(); }
        }

        /// <summary> Used to update internal structures when the number of LEDs changes. </summary>
        private void UpdateSize()
        {
            this.OutputDataDiscrete = new uint[this.LEDCount];
            LastLEDColours = new float[this.LEDCount];
            LastLEDPositionsFiltered = new float[this.LEDCount];
            LastLEDSaturations = new float[this.LEDCount];
        }

        public void Start()
        {
            if (this.LEDCount <= 0) { Log.Error("Attempted to start Linear visualizer \"" + this.Name + "\" with invalid LED count."); return; }
            this.KeepGoing = true;
            this.ProcessThread = new Thread(DoProcessing) { Name = "Linear " + this.Name };
            this.ProcessThread.Start();
            BaseNoteFinder.AdjustOutputSpeed((uint)this.FramePeriod);
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
                foreach(IOutput Output in this.Outputs) { Output.Dispatch(); }
                int WaitTime = (int)(this.FramePeriod - Timer.ElapsedMilliseconds);
                if (WaitTime > 0) { Thread.Sleep(WaitTime); }
            }
        }

        public int GetCountDiscrete() => this.LEDCount;
        public uint[] GetDataDiscrete() => this.OutputDataDiscrete;

        public int GetCountContinuous() => this.OutputCountContinuous;
        public ContinuousDataUnit[] GetDataContinuous() => this.OutputDataContinuous;
        public float GetAdvanceContinuous() => this.OutputAdvanceContinuous;
        public int MaxPossibleUnits { get => BaseNoteFinder.NoteCount; }

        // These variables are only used to keep inter-frame info for Update(). Do not touch.
        private float[] LastLEDColours = Array.Empty<float>();
        private float[] LastLEDPositionsFiltered = Array.Empty<float>(); // Only used when IsCircular is true.
        private float[] LastLEDSaturations = Array.Empty<float>();
        private int PrevAdvance;
        private readonly float[] LastVectorCenters = new float[BaseNoteFinder.NoteCount]; // Where the center-point of each block was last frame

        public void Update()
        {
            const int BIN_QTY = BaseNoteFinder.NoteCount; // Number of bins present
            float[] NoteAmplitudes = new float[BIN_QTY]; // The amplitudes of each note, time-smoothed
            float[] NoteAmplitudesFast = new float[BIN_QTY]; // The amplitudes of each note, with minimal time-smoothing
            float[] NotePositions = new float[BIN_QTY]; // The locations of the notes, range 0 ~ 1.
            float AmplitudeSum = 0;

            if (this.OutputDataDiscrete.Length != this.LEDCount) { UpdateSize(); }

            // Populate data from the NoteFinder.
            for (int i = 0; i < BIN_QTY; i++)
            {
                NotePositions[i] = BaseNoteFinder.Notes[i].Position / BaseNoteFinder.OctaveBinCount;
                NoteAmplitudes[i] = MathF.Pow(BaseNoteFinder.Notes[i].AmplitudeFiltered, this.LightSiding);
                NoteAmplitudesFast[i] = MathF.Pow(BaseNoteFinder.Notes[i].Amplitude, this.LightSiding);
                AmplitudeSum += NoteAmplitudes[i];
            }

            // Adjust AmplitudeSum to remove notes that are too weak to be included.
            float AmplitudeSumAdj = 0;
            for (int i = 0; i < BIN_QTY; i++)
            {
                NoteAmplitudes[i] -= this.LEDFloor * AmplitudeSum;
                if (NoteAmplitudes[i] / AmplitudeSum < 0) // Note too weak, remove it from consideration.
                {
                    NoteAmplitudes[i] = 0;
                    NoteAmplitudesFast[i] = 0;
                }
                AmplitudeSumAdj += NoteAmplitudes[i];
            }
            AmplitudeSum = AmplitudeSumAdj;
            // AmplitudeSum now only includes notes that are large enough (relative to others) to be worth displaying.

            float[] LEDColours = new float[this.LEDCount]; // The colour (range 0 ~ 1) of each LED in the chain.
            float[] LEDAmplitudes = new float[this.LEDCount]; // The amplitude (time-smoothed) of each LED in the chain.
            float[] LEDAmplitudesFast = new float[this.LEDCount]; // The amplitude (fast-updating) of each LED in the chain.
            
            int LEDsFilled = 0; // How many LEDs have been assigned a colour.
            float VectorPosition = 0; // Where in the continuous line we are (continuous equivalent of LEDsFilled).
            this.OutputCountContinuous = 0;
            float[] VectorCenters = new float[BIN_QTY];

            // Fill the LED slots with available notes.
            for (int NoteIndex = 0; NoteIndex < BIN_QTY; NoteIndex++)
            {
                // How many of the LEDs should be taken up by this colour.
                int LEDCountColour = (int)((NoteAmplitudes[NoteIndex] / AmplitudeSum) * this.LEDCount);
                // Fill those LEDs with this note's data.
                for (int LEDIndex = 0; LEDIndex < LEDCountColour && LEDsFilled < this.LEDCount; LEDIndex++)
                {
                    LEDColours[LEDsFilled] = NotePositions[NoteIndex];
                    LEDAmplitudes[LEDsFilled] = NoteAmplitudes[NoteIndex];
                    LEDAmplitudesFast[LEDsFilled] = NoteAmplitudesFast[NoteIndex];
                    LEDsFilled++;
                }

                // For continuous outputs
                float VectorSizeColour = (NoteAmplitudes[NoteIndex] / AmplitudeSum);
                if (VectorSizeColour == 0 || float.IsNaN(VectorSizeColour)) { continue; }
                this.OutputDataContinuous[this.OutputCountContinuous].Location = VectorPosition;
                this.OutputDataContinuous[this.OutputCountContinuous].Size = VectorSizeColour;
                VectorCenters[NoteIndex] = VectorPosition + (VectorSizeColour / 2);

                float OutSaturation = (this.SteadyBright ? NoteAmplitudes[NoteIndex] : NoteAmplitudesFast[NoteIndex]) * this.SaturationAmplifier;
                if (OutSaturation > 1) { OutSaturation = 1; }
                if (OutSaturation > LEDLimit) { OutSaturation = LEDLimit; }

                uint Colour = VisualizerTools.CCtoHEX(NotePositions[NoteIndex], 1.0F, OutSaturation);
                this.OutputDataContinuous[this.OutputCountContinuous].R = (byte)((Colour >> 16) & 0xff);
                this.OutputDataContinuous[this.OutputCountContinuous].G = (byte)((Colour >> 8) & 0xff);
                this.OutputDataContinuous[this.OutputCountContinuous].B = (byte)((Colour) & 0xff);

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
                for(int NoteIndex = 0; NoteIndex < BIN_QTY; NoteIndex++)
                {
                    VectorCenterOffset += (VectorCenters[NoteIndex] - LastVectorCenters[NoteIndex]) * NoteAmplitudes[NoteIndex]; // TODO: CONSIDER USINGBLOCK SIZE INSTEAD OF AMPLITUDE
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

                uint Colour = VisualizerTools.CCtoHEX(LastLEDColours[LEDIndex], 1.0F, OutSaturation);

                this.OutputDataDiscrete[LEDIndex] = Colour;
            }

            if (this.IsCircular)
            {
                for (int i = 0; i < this.LEDCount; i++)
                {
                    LastLEDPositionsFiltered[i] = (LastLEDPositionsFiltered[i] * 0.9F) + (LastLEDColours[i] * 0.1F);
                }
                for (int i = 0; i < BIN_QTY; i++) { LastVectorCenters[i] = VectorCenters[i]; }
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
}
