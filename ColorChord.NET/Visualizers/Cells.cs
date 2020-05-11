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
        public int LEDCount { get; set; }
        public string Name { get; set; }
        public bool Enabled { get; set; }

        public int FramePeriod = 1000 / 90;

        private readonly List<IOutput> Outputs = new List<IOutput>();
        public byte[] OutputData;
        private bool KeepGoing = true;
        private Thread ProcessThread;

        public float LEDFloor { get; set; }
        public float LightSiding { get; set; }
        public float SaturationAmplifier { get; set; }
        public float QtyAmp { get; set; }
        public bool SteadyBright { get; set; }
        public bool TimeBased { get; set; } // Useful for pies, turn off for linear systems.
        public bool Snakey { get; set; } // Advance head for where to get LEDs around.

        public Cells(string name) { this.Name = name; }

        public void ApplyConfig(Dictionary<string, object> options)
        {
            Log.Info("Reading config for Cells \"" + this.Name + "\".");
            if (!options.ContainsKey("ledCount") || !int.TryParse((string)options["ledCount"], out int LEDs) || LEDs <= 0) { Log.Error("Tried to create Linear visualizer with invalid/missing ledCount."); return; }

            this.LEDCount = ConfigTools.CheckInt(options, "ledCount", 1, 100000, 50, true);
            this.FramePeriod = 1000 / ConfigTools.CheckInt(options, "frameRate", 0, 1000, 60, true);

            this.LEDFloor = ConfigTools.CheckFloat(options, "ledFloor", 0, 1, 0.1F, true);
            this.LightSiding = ConfigTools.CheckFloat(options, "lightSiding", 0, 100, 1.9F, true);
            this.SaturationAmplifier = ConfigTools.CheckFloat(options, "saturationAmplifier", 0, 100, 2F, true);
            this.QtyAmp = ConfigTools.CheckFloat(options, "qtyAmp", 0, 100, 20, true);
            this.SteadyBright = ConfigTools.CheckBool(options, "steadyBright", false, true);
            this.TimeBased = ConfigTools.CheckBool(options, "timeBased", false, true);
            this.Snakey = ConfigTools.CheckBool(options, "snakey", false, true);
            this.Enabled = ConfigTools.CheckBool(options, "enable", true, true);
            ConfigTools.WarnAboutRemainder(options, typeof(IVisualizer));

            this.OutputData = new byte[this.LEDCount * 3];

            this.led_note_attached = new int[this.LEDCount];
            this.time_of_change = new double[this.LEDCount];
            this.last_led_pos = new float[this.LEDCount];
            this.last_led_pos_filter = new float[this.LEDCount];
            this.last_led_amp = new float[this.LEDCount];
        }

        public void Start()
        {
            this.KeepGoing = true;
            this.ProcessThread = new Thread(DoProcessing);
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
                foreach (IOutput Output in this.Outputs) { Output.Dispatch(); }
                int WaitTime = (int)(this.FramePeriod - (Timer.ElapsedMilliseconds));
                if (WaitTime > 0) { Thread.Sleep(WaitTime); }
            }
        }

        public int GetCount() => this.LEDCount;
        public byte[] GetData() => this.OutputData;

        #region ColorChord Magic
        private int[] led_note_attached;
        private double[] time_of_change;

        private float[] last_led_pos;
        private float[] last_led_pos_filter;
        private float[] last_led_amp;
        private int snakeyplace = 0;

        private void Update()
        {
            //Step 1: Calculate the quantity of all the LEDs we'll want.
            int totbins = NoteFinder.NoteCount;//nf->dists;
            int i, j;
            float[] binvals = new float[totbins];
            float[] binvalsQ = new float[totbins];
            float[] binpos = new float[totbins];
            float totalbinval = 0;

            float[] qtyHave = new float[totbins];
            float[] qtyWant = new float[totbins];
            float totQtyWant = 0;

            for (i = 0; i < this.LEDCount; i++)
            {
                int l = this.led_note_attached[i];
                if (l >= 0) { qtyHave[l]++; }
            }


            for (i = 0; i < totbins; i++)
            {
                binpos[i] = NoteFinder.Notes[i].Position / NoteFinder.OctaveBinCount;
                binvals[i] = (float)Math.Pow(NoteFinder.Notes[i].AmplitudeFiltered, this.LightSiding); //Slow
                binvalsQ[i] = (float)Math.Pow(NoteFinder.Notes[i].Amplitude, this.LightSiding); //Fast
                totalbinval += binvals[i];

                float want = binvals[i] * this.QtyAmp;
                totQtyWant += want;
                qtyWant[i] = want;
            }

            if (totQtyWant > this.LEDCount)
            {
                float overage = this.LEDCount / totQtyWant;
                for (i = 0; i < totbins; i++) { qtyWant[i] *= overage; }
            }

            float[] qtyDiff = new float[totbins];
            for (i = 0; i < totbins; i++)
            {
                qtyDiff[i] = qtyWant[i] - qtyHave[i];
            }

            //Step 1: Relinquish LEDs
            for (i = 0; i < totbins; i++)
            {
                while (qtyDiff[i] < -0.5)
                {
                    double maxtime = -1.0;
                    int maxindex = -1;

                    //Find the LEDs that have been on least.
                    for (j = 0; j < this.LEDCount; j++)
                    {
                        if (this.led_note_attached[j] != i) continue;
                        if (!this.TimeBased) { maxindex = j; break; }
                        if (this.time_of_change[j] > maxtime)
                        {
                            maxtime = this.time_of_change[j];
                            maxindex = j;
                        }
                    }
                    if (maxindex >= 0)
                    {
                        this.led_note_attached[maxindex] = -1;
                        this.time_of_change[maxindex] = Now();
                    }
                    qtyDiff[i]++;
                }
            }

            //Throw LEDs back in.
            for (i = 0; i < totbins; i++)
            {
                while (qtyDiff[i] > 0.5)
                {
                    double seltime = 1e20;
                    int selindex = -1;


                    //Find the LEDs that haven't been on in a long time.
                    for (j = 0; j < this.LEDCount; j++)
                    {
                        if (this.led_note_attached[j] != -1) continue;
                        if (!this.TimeBased) { selindex = j; break; }

                        if (this.Snakey)
                        {
                            float bias = 0;
                            float timeimp = 1;

                            bias = (j - this.snakeyplace + this.LEDCount) % this.LEDCount;

                            if (bias > this.LEDCount / 2) bias = this.LEDCount - bias + 1;
                            timeimp = 0;

                            float score = (float)(this.time_of_change[j] * timeimp + bias);
                            if (score < seltime)
                            {
                                seltime = score;
                                selindex = j;
                            }
                        }
                        else
                        {
                            if (this.time_of_change[j] < seltime)
                            {
                                seltime = this.time_of_change[j];
                                selindex = j;
                            }
                        }
                    }
                    if (selindex >= 0)
                    {
                        this.led_note_attached[selindex] = i;
                        this.time_of_change[selindex] = Now();
                        this.snakeyplace = selindex;
                    }
                    qtyDiff[i]--;
                }
            }

            //Advance the LEDs to this position when outputting the values.
            for (i = 0; i < this.LEDCount; i++)
            {
                int ia = this.led_note_attached[i];
                if (ia == -1)
                {
                    this.OutputData[i * 3 + 0] = 0;
                    this.OutputData[i * 3 + 1] = 0;
                    this.OutputData[i * 3 + 2] = 0;
                    continue;
                }
                float sat = binvals[ia] * this.SaturationAmplifier;
                float satQ = binvalsQ[ia] * this.SaturationAmplifier;
                if (satQ > 1) satQ = 1;
                float sendsat = (this.SteadyBright ? sat : satQ);
                if (sendsat > 1) sendsat = 1;
                uint r = VisualizerTools.CCtoHEX(binpos[ia], 1.0F, sendsat);

                this.OutputData[i * 3 + 0] = (byte)((r >> 16) & 0xff);
                this.OutputData[i * 3 + 1] = (byte)((r >> 8) & 0xff);
                this.OutputData[i * 3 + 2] = (byte)((r) & 0xff);
            }
        }

        private double Now() => DateTime.UtcNow.Ticks / 10000000D;

        #endregion

    }
}
