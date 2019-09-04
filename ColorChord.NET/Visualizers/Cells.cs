using ColorChord.NET.Outputs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace ColorChord.NET.Visualizers
{
    public class Cells : IVisualizer
    {
        private readonly int LEDCount;

        public int FramePeriod = 1000 / 90;

        private readonly List<IOutput> Outputs = new List<IOutput>();
        public readonly byte[] OutputData;
        private bool KeepGoing = true;
        private Thread ProcessThread;

        public Cells(int numLEDs)
        {
            this.LEDCount = numLEDs;
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

        #region ColorChord Magic
        private int[] led_note_attached;
        private double[] time_of_change;

        private float[] last_led_pos;
        private float[] last_led_pos_filter;
        private float[] last_led_amp;
        private float led_floor = 0.1F;
        private float light_siding = 1.9F;
        private float satamp = 2;
        private float qtyamp = 8;
        private bool steady_bright = false;
        private bool timebased = false; // Useful for pies, turn off for linear systems.
        private bool snakey = false; // Advance head for where to get LEDs around.
        private int snakeyplace = 0;

        private void Update()
        {
            //Step 1: Calculate the quantity of all the LEDs we'll want.
            int totbins = NoteFinder.NotePeakCount;//nf->dists;
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
                binpos[i] = NoteFinder.NotePositions[i] / NoteFinder.FreqBinCount;
                binvals[i] = (float)Math.Pow(NoteFinder.NoteAmplitudes2[i], this.light_siding); //Slow
                binvalsQ[i] = (float)Math.Pow(NoteFinder.NoteAmplitudes[i], this.light_siding); //Fast
                totalbinval += binvals[i];

                float want = binvals[i] * this.qtyamp;
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
                        if (!this.timebased) { maxindex = j; break; }
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
                        if (!this.timebased) { selindex = j; break; }

                        if (this.snakey)
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
                float sat = binvals[ia] * this.satamp;
                float satQ = binvalsQ[ia] * this.satamp;
                if (satQ > 1) satQ = 1;
                float sendsat = (this.steady_bright ? sat : satQ);
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
