using ColorChord.NET.Outputs;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ColorChord.NET.Visualizers
{
    public class Linear : IVisualizer
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

        // TODO: Determine what this does.
        public float LightSiding { get; set; }

        // TODO: Determine what this does.
        public bool SteadyBright { get; set; }

        /// <summary> The minimum brightness before LEDs are not outputted. </summary>
        public float LEDFloor { get; set; }

        /// <summary> The maximum brightness. </summary>
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
            if (!options.ContainsKey("ledCount") || !int.TryParse((string)options["ledCount"], out int LEDs) || LEDs <= 0) { Console.WriteLine("[ERR] Tried to create Linear visualizer with invalid/missing ledCount."); return; }

            this.LEDCount = ConfigTools.CheckInt(options, "ledCount", 1, 100000, 50, true);
            this.LightSiding = ConfigTools.CheckFloat(options, "lightSiding", 0, 100, 1, true);
            this.LEDFloor = ConfigTools.CheckFloat(options, "ledFloor", 0, 1, 0.1F, true);
            this.FramePeriod = 1000 / ConfigTools.CheckInt(options, "frameRate", 0, 1000, 60, true);
            this.IsCircular = ConfigTools.CheckBool(options, "isCircular", false, true);
            this.SteadyBright = ConfigTools.CheckBool(options, "steadyBright", false, true);
            this.LEDLimit = ConfigTools.CheckFloat(options, "ledLimit", 0, 1, 1, true);
            this.SaturationAmplifier = ConfigTools.CheckFloat(options, "saturationAmplifier", 0, 100, 1.6F, true);
            this.Enabled = ConfigTools.CheckBool(options, "enable", true, true);
            ConfigTools.WarnAboutRemainder(options);
            Console.WriteLine("[INF] Finished reading config for Linear \"" + this.Name + "\".");
        }

        /// <summary> Used to update internal structures when the number of LEDs changes. </summary>
        private void UpdateSize()
        {
            this.OutputData = new byte[this.LEDCount * 3];
            last_led_pos = new float[this.LEDCount];
            last_led_pos_filter = new float[this.LEDCount];
            last_led_amp = new float[this.LEDCount];
        }

        public void Start()
        {
            if (this.LEDCount <= 0) { Console.WriteLine("[ERR] Attempted to start Linear visualizer \"" + this.Name + "\" with invalid LED count."); return; }
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

        #region ColorChord Magic
        private float[] last_led_pos;
        private float[] last_led_pos_filter;
        private float[] last_led_amp;
        private int lastadvance;

        public void Update()
        {
            //Step 1: Calculate the quantity of all the LEDs we'll want.
            int totbins = NoteFinder.NotePeakCount;//nf->dists;
            int i, j;
            float[] binvals = new float[totbins];
            float[] binvalsQ = new float[totbins];
            float[] binpos = new float[totbins];
            float totalbinval = 0;

            for (i = 0; i < totbins; i++)
            {
                binpos[i] = NoteFinder.NotePositions[i] / NoteFinder.FreqBinCount;
                binvals[i] = (float)Math.Pow(NoteFinder.NoteAmplitudes2[i], this.LightSiding);
                binvalsQ[i] = (float)Math.Pow(NoteFinder.NoteAmplitudes[i], this.LightSiding);
                totalbinval += binvals[i];
            }

            float newtotal = 0;

            for (i = 0; i < totbins; i++)
            {
                binvals[i] -= this.LEDFloor * totalbinval;
                if (binvals[i] / totalbinval < 0) { binvals[i] = binvalsQ[i] = 0; }
                newtotal += binvals[i];
            }
            totalbinval = newtotal;

            float[] rledpos = new float[this.LEDCount];
            float[] rledamp = new float[this.LEDCount];
            float[] rledampQ = new float[this.LEDCount];
            int rbinout = 0;

            for (i = 0; i < totbins; i++)
            {
                int nrleds = (int)((binvals[i] / totalbinval) * this.LEDCount);
                for (j = 0; j < nrleds && rbinout < this.LEDCount; j++)
                {
                    rledpos[rbinout] = binpos[i];
                    rledamp[rbinout] = binvals[i];
                    rledampQ[rbinout] = binvalsQ[i];
                    rbinout++;
                }
            }

            if (rbinout == 0)
            {
                rledpos[0] = 0;
                rledamp[0] = 0;
                rledampQ[0] = 0;
                rbinout++;
            }

            for (; rbinout < this.LEDCount; rbinout++)
            {
                rledpos[rbinout] = rledpos[rbinout - 1];
                rledamp[rbinout] = rledamp[rbinout - 1];
                rledampQ[rbinout] = rledampQ[rbinout - 1];
            }

            //Now we have to minimize "advance".
            int minadvance = 0;

            if (this.IsCircular)
            {
                float mindiff = 1e20F;

                //Uncomment this for a rotationally continuous surface.
                for (i = 0; i < this.LEDCount; i++)
                {
                    float diff = 0;
                    for (j = 0; j < this.LEDCount; j++)
                    {
                        int r = (j + i) % this.LEDCount;
                        float rd = lindiff(last_led_pos_filter[j], rledpos[r]);
                        diff += rd;//*rd;
                    }

                    int advancediff = (lastadvance - i);
                    if (advancediff < 0) advancediff *= -1;
                    if (advancediff > this.LEDCount / 2) advancediff = this.LEDCount - advancediff;

                    float ad = (float)advancediff / (float)this.LEDCount;
                    diff += ad * ad;// * led->this.LEDCount;

                    if (diff < mindiff)
                    {
                        mindiff = diff;
                        minadvance = i;
                    }
                }

            }
            lastadvance = minadvance;

            //Advance the LEDs to this position when outputting the values.
            for (i = 0; i < this.LEDCount; i++)
            {
                int ia = (i + minadvance + this.LEDCount) % this.LEDCount;
                float sat = rledamp[ia] * this.SaturationAmplifier;
                float satQ = rledampQ[ia] * this.SaturationAmplifier;
                if (satQ > 1) satQ = 1;
                last_led_pos[i] = rledpos[ia];
                last_led_amp[i] = sat;
                float sendsat = (this.SteadyBright ? sat : satQ);
                if (sendsat > 1) sendsat = 1;

                if (sendsat > LEDLimit) sendsat = LEDLimit;

                uint r = VisualizerTools.CCtoHEX(last_led_pos[i], 1.0F, sendsat);

                this.OutputData[i * 3 + 0] = (byte)((r >> 16) & 0xff);
                this.OutputData[i * 3 + 1] = (byte)((r >> 8) & 0xff);
                this.OutputData[i * 3 + 2] = (byte)((r) & 0xff);
            }

            if (this.IsCircular)
            {
                for (i = 0; i < this.LEDCount; i++)
                {
                    last_led_pos_filter[i] = last_led_pos_filter[i] * .9F + last_led_pos[i] * .1F;
                }
            }
        }

        private static float lindiff(float a, float b)  //Find the minimum change around a wheel.
        {
            float diff = a - b;
            if (diff < 0) { diff *= -1; }

            float otherdiff = (a < b) ? (a + 1) : (a - 1);
            otherdiff -= b;
            if (otherdiff < 0) { otherdiff *= -1; }

            if (diff < otherdiff) { return diff; }
            else { return otherdiff; }
        }
        #endregion

    }
}
