using ColorChord.NET.Outputs;
using ColorChord.NET.Visualizers.Formats;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace ColorChord.NET.Visualizers
{
    public class Cells : IVisualizer, IDiscrete1D
    {
        /// <summary> The number of discrete points to be output. Usually equals the number of LEDs on a physical system. </summary>
        public int LEDCount { get; set; }

        /// <summary> A unique name for this visualizer instance, used for referring to it from other components. </summary>
        public string Name { get; set; }

        /// <summary> Whether or not this visualizer is currently operating. </summary>
        public bool Enabled { get; set; }

        public int FramePeriod = 1000 / 90;

        /// <summary> All outputs that need to be notified when new data is available. </summary>
        private readonly List<IOutput> Outputs = new List<IOutput>();

        /// <summary> Stores the latest data to be given to <see cref="IOutput"/> modules when requested. </summary>
        private uint[] OutputData;

        /// <summary> Whether to continue processing, or stop threads and finish up in preparation for closing the application. </summary>
        private bool KeepGoing = true;

        /// <summary> The thread on which input note data is processed by this visualizer. </summary>
        private Thread ProcessThread;

        /// <summary> How strongly notes should be amplified before processing. </summary>
        public float LightSiding { get; set; }

        /// <summary> How much saturation (LED brightness) should be amplified before sending the output data. </summary>
        public float SaturationAmplifier { get; set; }

        /// <summary> A multiplier applied to input note strengths to determine how many LEDs should become that colour. </summary>
        /// <remarks> This should be proportionally increased/decreased if the number of LEDs changes to keep the same overall percentage of LEDs on the same. </remarks>
        public float QtyAmp { get; set; }

        /// <summary> Whether to use smoothed input data, reducing flicker, or only the latest data. </summary>
        public bool SteadyBright { get; set; }

        /// <summary> Whether LEDs turning on/off should be based on how long since their last state change (true), or based solely on position (false). </summary>
        /// <remarks> Useful for pies, turn off for linear systems. </remarks>
        public bool TimeBased { get; set; }

        // TODO: Understand & document this feature once it works in base ColorChord.
        public bool Snakey { get; set; } // Advance head for where to get LEDs around.

        public Cells(string name) { this.Name = name; }

        public void ApplyConfig(Dictionary<string, object> options)
        {
            Log.Info("Reading config for Cells \"" + this.Name + "\".");
            if (!options.ContainsKey("LEDCount") || !int.TryParse((string)options["LEDCount"], out int LEDs) || LEDs <= 0) { Log.Error("Tried to create Linear visualizer with invalid/missing LEDCount."); return; }

            this.LEDCount = ConfigTools.CheckInt(options, "LEDCount", 1, 100000, 50, true);
            this.FramePeriod = 1000 / ConfigTools.CheckInt(options, "FrameRate", 0, 1000, 60, true);

            this.LightSiding = ConfigTools.CheckFloat(options, "LightSiding", 0, 100, 1.9F, true);
            this.SaturationAmplifier = ConfigTools.CheckFloat(options, "SaturationAmplifier", 0, 100, 2F, true);
            this.QtyAmp = ConfigTools.CheckFloat(options, "QtyAmp", 0, 100, 20, true);
            this.SteadyBright = ConfigTools.CheckBool(options, "SteadyBright", false, true);
            this.TimeBased = ConfigTools.CheckBool(options, "TimeBased", false, true);
            this.Snakey = ConfigTools.CheckBool(options, "Snakey", false, true);
            this.Enabled = ConfigTools.CheckBool(options, "Enable", true, true);
            ConfigTools.WarnAboutRemainder(options, typeof(IVisualizer));

            this.OutputData = new uint[this.LEDCount];
            this.LEDBinMapping = new int[this.LEDCount];
            this.LastChangeTime = new double[this.LEDCount];
        }

        public void Start()
        {
            this.KeepGoing = true;
            this.ProcessThread = new Thread(DoProcessing);
            this.ProcessThread.Start();
            BaseNoteFinder.AdjustOutputSpeed((uint)this.FramePeriod);
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
                foreach (IOutput Output in this.Outputs) { Output.Dispatch(); }
                int WaitTime = (int)(this.FramePeriod - Timer.ElapsedMilliseconds);
                if (WaitTime > 0) { Thread.Sleep(WaitTime); }
            }
        }

        public int GetCountDiscrete() => this.LEDCount;
        public uint[] GetDataDiscrete() => this.OutputData;

        // These are used between frames of output data. Only used in Update().
        private int[] LEDBinMapping; // Contains each LED's note bin ID.
        private double[] LastChangeTime; // When each LED last changed state.
        private int LastChangeIndex = 0; // Where we last made changes, only used in snakey mode.

        private void Update()
        {
            const int BinCount = BaseNoteFinder.NoteCount;

            // Determine how many LEDs of each colour there currently are on from the previous cycle.
            float[] LEDsPerBin = new float[BinCount];
            for (int i = 0; i < this.LEDCount; i++)
            {
                int Bin = this.LEDBinMapping[i];
                if (Bin >= 0) { LEDsPerBin[Bin]++; }
            }

            // Determine the colour and value for each note present in new input data, then figure out how many LEDs should be on in that colour.
            float[] BinColours = new float[BinCount]; // Range of 0-1, colour of each bin
            float[] BinValuesSlow = new float[BinCount]; // Note amplitude, slightly smoothed to reduce flicker.
            float[] BinValues = new float[BinCount]; // Note amplitude, not smoothed.
            float BinValuesSum = 0;
            float[] LEDsDesired = new float[BinCount];
            float TotalDesiredCount = 0; // How many LEDs we'd want on, taking into account the quantity for each colour
            for (int i = 0; i < BinCount; i++)
            {
                BinColours[i] = BaseNoteFinder.Notes[i].Position / BaseNoteFinder.OctaveBinCount;
                BinValuesSlow[i] = (float)Math.Pow(BaseNoteFinder.Notes[i].AmplitudeFiltered, this.LightSiding);
                BinValues[i] = (float)Math.Pow(BaseNoteFinder.Notes[i].Amplitude, this.LightSiding);
                BinValuesSum += BinValuesSlow[i];

                float DesiredCount = BinValuesSlow[i] * this.QtyAmp; // How many LEDs we'd like on for this colour
                TotalDesiredCount += DesiredCount;
                LEDsDesired[i] = DesiredCount;
            }

            // We want more LEDs on than we have. Scale all counts down to fit our output size.
            if (TotalDesiredCount > this.LEDCount)
            {
                float ScaleFactor = this.LEDCount / TotalDesiredCount;
                for (int i = 0; i < BinCount; i++) { LEDsDesired[i] *= ScaleFactor; }
            }

            // Determine how many LEDs we'd like to turn on (+) or off (-) based on how many we want on, and how many are on.
            float[] DesiredChange = new float[BinCount];
            for (int i = 0; i < BinCount; i++) { DesiredChange[i] = LEDsDesired[i] - LEDsPerBin[i]; }

            // Find LEDs to turn off.
            for (int BinInd = 0; BinInd < BinCount; BinInd++)
            {
                while (DesiredChange[BinInd] < -0.5) // We want to reduce LED count of this colour.
                {
                    double MostRecentChange = -1.0; // When the youngest LED of this colour turned on.
                    int MostRecentChangeIndex = -1; // The index of the above LED.

                    // Finds the LEDs that have been on for the shortest amount of time.
                    for (int LEDInd = 0; LEDInd < this.LEDCount; LEDInd++)
                    {
                        if (this.LEDBinMapping[LEDInd] != BinInd) { continue; } // This LED is a different colour, or already off.
                        if (!this.TimeBased) { MostRecentChangeIndex = LEDInd; break; }
                        if (this.LastChangeTime[LEDInd] > MostRecentChange)
                        {
                            MostRecentChange = this.LastChangeTime[LEDInd];
                            MostRecentChangeIndex = LEDInd;
                        }
                    }
                    // Turn off the LED that has been on the shortest.
                    if (MostRecentChangeIndex >= 0)
                    {
                        this.LEDBinMapping[MostRecentChangeIndex] = -1;
                        this.LastChangeTime[MostRecentChangeIndex] = Now();
                    }
                    DesiredChange[BinInd]++;
                }
            }

            // Find LEDs to turn on. Pattern depends on whether snakey is on.
            for (int BinInd = 0; BinInd < BinCount; BinInd++)
            {
                while (DesiredChange[BinInd] > 0.5) // We want to increase LED count of this colour.
                {
                    double LeastRecentChange = 1E20;
                    int LeastRecentChangeIndex = -1;

                    // Find an LED suitable to turn on.
                    for (int LEDInd = 0; LEDInd < this.LEDCount; LEDInd++)
                    {
                        if (this.LEDBinMapping[LEDInd] != -1) { continue; } // This LED is already on.
                        if (!this.TimeBased) { LeastRecentChangeIndex = LEDInd; break; }

                        if (this.Snakey) // TODO: Finish rewriting this section. Base ColorChord's functionality here does not currently work, so waiting on that to be fixed.
                        {
                            //float timeimp = 1;

                            float DistanceFromLast = (LEDInd - this.LastChangeIndex + this.LEDCount) % this.LEDCount;

                            if (DistanceFromLast > this.LEDCount / 2) { DistanceFromLast = this.LEDCount - DistanceFromLast + 1; }
                            //timeimp = 0;

                            float score = (float)(/*this.LastChangeTime[LEDInd] * timeimp + */DistanceFromLast);
                            if (score < LeastRecentChange)
                            {
                                LeastRecentChange = score;
                                LeastRecentChangeIndex = LEDInd;
                            }
                        }
                        else // In non-snakey mode, find the LED that has been off the longest.
                        {
                            if (this.LastChangeTime[LEDInd] < LeastRecentChange)
                            {
                                LeastRecentChange = this.LastChangeTime[LEDInd];
                                LeastRecentChangeIndex = LEDInd;
                            }
                        }
                    }
                    if (LeastRecentChangeIndex >= 0) // We found a suitable LED, turn it on.
                    {
                        this.LEDBinMapping[LeastRecentChangeIndex] = BinInd;
                        this.LastChangeTime[LeastRecentChangeIndex] = Now();
                        this.LastChangeIndex = LeastRecentChangeIndex;
                    }
                    DesiredChange[BinInd]--;
                }
            }

            // Update the output array with the new colours.
            for (int LEDInd = 0; LEDInd < this.LEDCount; LEDInd++)
            {
                int BinID = this.LEDBinMapping[LEDInd]; // Which bin this LED is using.

                if (BinID == -1) { this.OutputData[LEDInd] = 0; } // This LED is off.
                else
                {
                    float SaturationSlow = BinValuesSlow[BinID] * this.SaturationAmplifier;
                    float Saturation = BinValues[BinID] * this.SaturationAmplifier;
                    float SaturationUsed = (this.SteadyBright ? SaturationSlow : Saturation);
                    if (SaturationUsed > 1) { SaturationUsed = 1; }
                    this.OutputData[LEDInd] = VisualizerTools.CCtoHEX(BinColours[BinID], 1.0F, SaturationUsed);
                }
            }
        }

        private double Now() => DateTime.UtcNow.Ticks / 10000000D;

    }
}
