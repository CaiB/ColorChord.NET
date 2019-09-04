using ColorChord.NET.Outputs;
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
        public readonly int LEDCount;
        public readonly bool IsCircular;

        public int FramePeriod = 1000 / 90;

        private readonly List<IOutput> Outputs = new List<IOutput>();
        public readonly byte[] OutputData;
        private bool KeepGoing = true;
        private Thread ProcessThread;

        public Linear(int numLEDs, bool circular)
        {
            this.LEDCount = numLEDs;
            this.IsCircular = circular;
            this.OutputData = new byte[this.LEDCount * 3];

            last_led_pos = new float[this.LEDCount];
            last_led_pos_filter = new float[this.LEDCount];
            last_led_amp = new float[this.LEDCount];
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
                foreach(IOutput Output in this.Outputs) { Output.Dispatch(); }
                int WaitTime = (int)(this.FramePeriod - (Timer.ElapsedMilliseconds));
                if (WaitTime > 0) { Thread.Sleep(WaitTime); }
            }
        }

        #region ColorChord Magic
        private static bool did_init;
        private static float light_siding = 1.0F;
        private static float[] last_led_pos;
        private static float[] last_led_pos_filter;
        private static float[] last_led_amp;
        private static bool steady_bright = false;
        private static float led_floor = 0.1F;
        private static float led_limit = 1F; //Maximum brightness
        private static float satamp = 1.6F;
        private static int lastadvance;

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
                binvals[i] = (float)Math.Pow(NoteFinder.NoteAmplitudes2[i], light_siding);
                binvalsQ[i] = (float)Math.Pow(NoteFinder.NoteAmplitudes[i], light_siding);
                totalbinval += binvals[i];
            }

            float newtotal = 0;

            for (i = 0; i < totbins; i++)
            {
                binvals[i] -= led_floor * totalbinval;
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
                    diff = 0;
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
                float sat = rledamp[ia] * satamp;
                float satQ = rledampQ[ia] * satamp;
                if (satQ > 1) satQ = 1;
                last_led_pos[i] = rledpos[ia];
                last_led_amp[i] = sat;
                float sendsat = (steady_bright ? sat : satQ);
                if (sendsat > 1) sendsat = 1;

                if (sendsat > led_limit) sendsat = led_limit;

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
