using ColorChord.NET.Config;
using ColorChord.NET.Outputs;
using ColorChord.NET.Visualizers.Formats;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace ColorChord.NET.Visualizers
{
    public class Prominent : IVisualizer, IDiscrete1D
    {
        /// <summary> A unique name for this visualizer instance, used for referring to it from other components. </summary>
        public string Name { get; private set; }

        /// <summary> The number of discrete points to be output. Usually equals the number of LEDs on a physical system. </summary>
        /// <remarks> All LEDs are assigned the same colour in this visualizer, so 1 works just fine. </remarks>
        [ConfigInt("LEDCount", 1, 100000, 1)]
        public int LEDCount { get; set; }

        /// <summary> Whether or not this visualizer is currently operating. </summary>
        [ConfigBool("Enable", true)]
        public bool Enabled { get; set; }

        /// <summary> How many times per second the output should be updated. </summary>
        [ConfigInt("FrameRate", 0, 1000, 60)]
        public int FrameRate { get; set; } = 60;

        /// <summary> The number of milliseconds to wait between output updates. </summary>
        private int FramePeriod => 1000 / this.FrameRate;

        /// <summary> How much saturation (LED brightness) should be amplified before sending the output data. </summary>
        [ConfigFloat("SaturationAmplifier", 0F, 100F, 1.6F)]
        public float SaturationAmplifier { get; set; }

        /// <summary> All outputs that need to be notified when new data is available. </summary>
        private readonly List<IOutput> Outputs = new();

        /// <summary> Stores the latest data to be given to <see cref="IOutput"/> modules when requested. </summary>
        private uint[] OutputDataDiscrete;

        /// <summary> Whether to continue processing, or stop threads and finish up in preparation for closing the application. </summary>
        private bool KeepGoing = true;

        /// <summary> The thread on which input note data is processed by this visualizer. </summary>
        private Thread ProcessThread;

        /// <summary> A very simple visualizer that simply sets all LEDs to the most prominent note's colour. </summary>
        /// <param name="name"> The unique name for this instance. </param>
        public Prominent(string name, Dictionary<string, object> config)
        {
            this.Name = name;
            Configurer.Configure(this, config);
            this.OutputDataDiscrete = new uint[this.LEDCount];
        }

        public void Start()
        {
            if (this.LEDCount <= 0) { Log.Error("Attempted to start Prominent visualizer \"" + this.Name + "\" with invalid LED count."); return; }
            this.KeepGoing = true;
            this.ProcessThread = new Thread(DoProcessing) { Name = "Prominent " + this.Name };
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
            Stopwatch Timer = new();
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
        public uint[] GetDataDiscrete() => this.OutputDataDiscrete;

        private void Update()
        {
            float OutAmplitude = 0F;
            float OutNote = 0F;

            // Find strongest note
            for(int Bin = 0; Bin < BaseNoteFinder.NoteCount; Bin++)
            {
                float ThisNote = BaseNoteFinder.Notes[Bin].Position / BaseNoteFinder.OctaveBinCount;
                float ThisAmplitude = BaseNoteFinder.Notes[Bin].AmplitudeFiltered * this.SaturationAmplifier;
                if(ThisAmplitude > OutAmplitude)
                {
                    OutAmplitude = ThisAmplitude;
                    OutNote = ThisNote;
                }
            }

            // Assign all LEDs this colour
            if (OutAmplitude > 1F) { OutAmplitude = 1F; }
            uint Colour = VisualizerTools.CCtoHEX(OutNote, 1F, OutAmplitude);
            for (int LED = 0; LED < this.LEDCount; LED++) { this.OutputDataDiscrete[LED] = Colour; }
        }
    }
}
