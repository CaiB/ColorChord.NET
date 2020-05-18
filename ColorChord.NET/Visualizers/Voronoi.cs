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
    public class Voronoi : IVisualizer
    {
        public string Name { get; private set; }

        public bool Enabled { get; set; }

        public int FramePeriod { get; private set; }

        public VoronoiPoint[] OutputData = new VoronoiPoint[NoteFinder.NoteCount];

        private readonly List<IOutput> Outputs = new List<IOutput>();
        private Thread ProcessThread;        
        private bool KeepGoing = true;

        public Voronoi(string name) { this.Name = name; }

        public void ApplyConfig(Dictionary<string, object> options)
        {
            Log.Info("Reading config for Voronoi \"" + this.Name + "\".");

            this.FramePeriod = 1000 / ConfigTools.CheckInt(options, "frameRate", 0, 1000, 60, true);
            this.Enabled = ConfigTools.CheckBool(options, "enable", true, true);
            ConfigTools.WarnAboutRemainder(options, typeof(IVisualizer));
        }


        public void Start()
        {
            this.KeepGoing = true;
            this.ProcessThread = new Thread(DoProcessing);
            this.ProcessThread.Name = "Voronoi " + this.Name;
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

        private void Update()
        {
            for (byte i = 0; i < NoteFinder.NoteCount; i++)
            {
                NoteFinder.Note Note = NoteFinder.Notes[i];
                this.OutputData[i].Amplitude = Note.AmplitudeFiltered;
                this.OutputData[i].Present = Note.AmplitudeFiltered < 0.05F;
                if (!this.OutputData[i].Present) { continue; } // This one is off, no point calculating the rest.

                const float RADIUS = 0.75F;
                double Angle = (Note.Position / NoteFinder.OctaveBinCount) * Math.PI * 2; // Range 0 ~ 2pi
                this.OutputData[i].X = (float)Math.Cos(Angle) * RADIUS;
                this.OutputData[i].Y = (float)Math.Sin(Angle) * RADIUS;

                uint Colour = VisualizerTools.CCtoHEX(Note.Position / NoteFinder.OctaveBinCount, 1F, 1F);
                this.OutputData[i].R = (byte)((Colour >> 16) & 0xFF);
                this.OutputData[i].G = (byte)((Colour >> 8) & 0xFF);
                this.OutputData[i].B = (byte)(Colour & 0xFF);
            }
        }

        public struct VoronoiPoint
        {
            public bool Present;
            public float X, Y;
            public float Amplitude;
            public byte R, G, B;
        }
    }
}
