using ColorChord.NET.Outputs;
using ColorChord.NET.Visualizers.Formats;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ColorChord.NET.Visualizers
{
    public class Linear : IVisualizer, IDiscrete1D
    {
        /// <summary> The number of discrete elements outputted by this visualizer. </summary>
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

        /// <summary> The name of this instance. Used to refer to the instance. </summary>
        public string Name { get; private set; }

        /// <summary> Whether the visualizer is currently processing/outputting. </summary>
        public bool Enabled { get; set; }

        public int FramePeriod = 1000 / 90;

        /// <summary> Whether the output should be treated as a line with ends, or a continuous circle. </summary>
        public bool IsCircular { get; set; }

        /// <summary> Exponent used to convert raw note amplitudes to strength. </summary>
        public float LightSiding { get; set; }

        /// <summary> Applies inter-frame smoothing to the LED brightnesses to prevent fast flickering. </summary>
        public bool SteadyBright { get; set; }

        /// <summary> The minimum relative amplitude of a note required to consider it for output. </summary>
        public float LEDFloor { get; set; }

        /// <summary> The maximum brightness of all output LEDs. </summary>
        /// <remarks> Caps the brightness at this, doesn't scale brightnesses below. </remarks>
        public float LEDLimit { get; set; }

        /// <summary> How intense to make the colours. </summary>
        public float SaturationAmplifier { get; set; }

        private readonly List<IOutput> Outputs = new List<IOutput>();
        public byte[] OutputData;
        private bool KeepGoing = true;
        private Thread ProcessThread;

        public Linear(string name) { this.Name = name; }

        public void ApplyConfig(Dictionary<string, object> options)
        {
            Log.Info("Reading config for Linear \"" + this.Name + "\".");
            if (!options.ContainsKey("ledCount") || !int.TryParse((string)options["ledCount"], out int LEDs) || LEDs <= 0) { Log.Error("Tried to create Linear visualizer with invalid/missing ledCount."); return; }

            this.LEDCount = ConfigTools.CheckInt(options, "ledCount", 1, 100000, 50, true);
            this.LightSiding = ConfigTools.CheckFloat(options, "lightSiding", 0, 100, 1, true);
            this.LEDFloor = ConfigTools.CheckFloat(options, "ledFloor", 0, 1, 0.1F, true);
            this.FramePeriod = 1000 / ConfigTools.CheckInt(options, "frameRate", 0, 1000, 60, true);
            this.IsCircular = ConfigTools.CheckBool(options, "isCircular", false, true);
            this.SteadyBright = ConfigTools.CheckBool(options, "steadyBright", false, true);
            this.LEDLimit = ConfigTools.CheckFloat(options, "ledLimit", 0, 1, 1, true);
            this.SaturationAmplifier = ConfigTools.CheckFloat(options, "saturationAmplifier", 0, 100, 1.6F, true);
            this.Enabled = ConfigTools.CheckBool(options, "enable", true, true);
            ConfigTools.WarnAboutRemainder(options, typeof(IVisualizer));
        }

        /// <summary> Used to update internal structures when the number of LEDs changes. </summary>
        private void UpdateSize()
        {
            this.OutputData = new byte[this.LEDCount * 3];
            LastLEDColours = new float[this.LEDCount];
            LastLEDPositionsFiltered = new float[this.LEDCount];
            LastLEDSaturations = new float[this.LEDCount];
        }

        public void Start()
        {
            if (this.LEDCount <= 0) { Log.Error("Attempted to start Linear visualizer \"" + this.Name + "\" with invalid LED count."); return; }
            this.KeepGoing = true;
            this.ProcessThread = new Thread(DoProcessing);
            this.ProcessThread.Name = "Linear " + this.Name;
            this.ProcessThread.Start();
            NoteFinder.AdjustOutputSpeed((uint)this.FramePeriod);
        }

        public void Stop()
        {
            this.KeepGoing = false;
            this.ProcessThread.Join();
        }

        public void AttachOutput(IOutput output) { if (output != null) { this.Outputs.Add(output); } }

        private void DoProcessing()
        {
            Stopwatch Timer = new Stopwatch();
            while (this.KeepGoing)
            {
                Timer.Restart();
                Update();
                foreach(IOutput Output in this.Outputs) { Output.Dispatch(); }
                int WaitTime = (int)(this.FramePeriod - (Timer.ElapsedMilliseconds));
                if (WaitTime > 0) { Thread.Sleep(WaitTime); }
            }
        }

        public int GetCountDiscrete() => this.LEDCount;
        public byte[] GetDataDiscrete() => this.OutputData;


        // These variables are only used to keep inter-frame info for Update(). Do not touch.
        private float[] LastLEDColours;
        private float[] LastLEDPositionsFiltered; // Only used when IsCirculr is true.
        private float[] LastLEDSaturations;
        private int PrevAdvance;

        public void Update()
        {
            const int BIN_QTY = NoteFinder.NoteCount; // Number of bins present
            float[] NoteAmplitudes = new float[BIN_QTY]; // The amplitudes of each note, time-smoothed
            float[] NoteAmplitudesFast = new float[BIN_QTY]; // The amplitudes of each note, with minimal time-smoothing
            float[] NotePositions = new float[BIN_QTY]; // The locations of the notes, range 0 ~ 1.
            float AmplitudeSum = 0;

            // Populate data from the NoteFinder.
            for (int i = 0; i < BIN_QTY; i++)
            {
                NotePositions[i] = NoteFinder.Notes[i].Position / NoteFinder.OctaveBinCount;
                NoteAmplitudes[i] = (float)Math.Pow(NoteFinder.Notes[i].AmplitudeFiltered, this.LightSiding);
                NoteAmplitudesFast[i] = (float)Math.Pow(NoteFinder.Notes[i].Amplitude, this.LightSiding);
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
                // GRAB VECTOR DATA HERE
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

                this.OutputData[LEDIndex * 3 + 0] = (byte)((Colour >> 16) & 0xff);
                this.OutputData[LEDIndex * 3 + 1] = (byte)((Colour >> 8) & 0xff);
                this.OutputData[LEDIndex * 3 + 2] = (byte)((Colour) & 0xff);
            }

            if (this.IsCircular)
            {
                for (int i = 0; i < this.LEDCount; i++)
                {
                    LastLEDPositionsFiltered[i] = (LastLEDPositionsFiltered[i] * 0.9F) + (LastLEDColours[i] * 0.1F);
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
}
