﻿using ColorChord.NET.Config;
using ColorChord.NET.Outputs;
using ColorChord.NET.Visualizers.Formats;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace ColorChord.NET.Visualizers
{
    public class Voronoi : IVisualizer, IDiscrete2D
    {
        /// <summary> A unique name for this visualizer instance, used for referring to it from other components. </summary>
        public string Name { get; private init; }

        /// <summary> Whether this visualizer is currently active. </summary>
        [ConfigBool("Enable", true)]
        public bool Enabled { get; set; }

        /// <summary> How many times per second the output should be updated. </summary>
        [ConfigInt("FrameRate", 0, 1000, 60)]
        public int FrameRate { get; set; } = 60;

        /// <summary> The number of milliseconds to wait between output updates. </summary>
        private int FramePeriod => 1000 / this.FrameRate;

        /// <summary> How many LEDs/blocks to output for in the X direction (width). </summary>
        [ConfigInt("LEDCountX", 1, 1000000, 64)]
        public int LEDCountX { get; set; }

        /// <summary> How many LEDs/blocks to output for in the Y direction (height). </summary>
        [ConfigInt("LEDCountY", 1, 1000000, 32)]
        public int LEDCountY { get; set; }

        /// <summary> Note amplitude is raised to this power as a preprocessing step. Higher means more differentiation between different peak sizes. </summary>
        [ConfigFloat("AmplifyPower", 0F, 100F, 2.51F)]
        public float AmplifyPower { get; set; }

        /// <summary> Distances from centers to draw the colours up to. Higher numbers means fewer colours will take up a majority of screen space. </summary>
        [ConfigFloat("DistancePower", 0F, 100F, 1.5F)]
        public float DistancePower { get; set; }

        /// <summary> How strong a note's amplitude needs to be for it to be considered in rendering. </summary>
        [ConfigFloat("Cutoff", 0F, 100F, 0.03F)]
        public float NoteCutoff { get; set; }

        /// <summary> Whether to draw the colours in a circle around the edges (true), or to randomly place colours (false). </summary>
        [ConfigBool("CentersFromSides", true)]
        public bool CentersFromSides { get; set; }

        /// <summary> Amplifier on the output colour saturation. </summary>
        [ConfigFloat("SaturationAmplifier", 0, 100, 5F)]
        public float SaturationAmplifier { get; set; }

        /// <summary> Scales the final output saturation curve. </summary>
        [ConfigFloat("OutputGamma", 0F, 1F, 1F)]
        public float OutGamma { get; set; }

        /// <summary> All outputs that need to be notified when new data is available. </summary>
        private readonly List<IOutput> Outputs = new();

        /// <summary> Output data for the continuous voronoi mode. </summary>
        public VoronoiPoint[] OutputData = new VoronoiPoint[BaseNoteFinder.NoteCount];

        /// <summary> Output data for the discrete (pixel matrix) voronoi mode. </summary>
        private readonly uint[,] OutputDataDiscrete;

        private readonly DiscreteVoronoiNote[] DiscreteNotes;

        private Thread ProcessThread;
        private bool KeepGoing = true;

        public Voronoi(string name, Dictionary<string, object> config)
        {
            this.Name = name;
            Configurer.Configure(this, config);
            this.OutputDataDiscrete = new uint[this.LEDCountX, this.LEDCountY];
            this.DiscreteNotes = new DiscreteVoronoiNote[BaseNoteFinder.NoteCount];
        }

        public void Start()
        {
            this.KeepGoing = true;
            this.ProcessThread = new Thread(DoProcessing) { Name = "Voronoi " + this.Name };
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

        private void Update()
        {
            // Smooth Voronoi
            /*
            for (byte i = 0; i < BaseNoteFinder.NoteCount; i++)
            {
                BaseNoteFinder.Note Note = BaseNoteFinder.Notes[i];
                this.OutputData[i].Amplitude = Note.AmplitudeFiltered;
                this.OutputData[i].Present = Note.AmplitudeFiltered < 0.05F;
                if (!this.OutputData[i].Present) { continue; } // This one is off, no point calculating the rest.

                const float RADIUS = 0.75F;
                double Angle = (Note.Position / BaseNoteFinder.OctaveBinCount) * Math.PI * 2; // Range 0 ~ 2pi
                this.OutputData[i].X = (float)Math.Cos(Angle) * RADIUS;
                this.OutputData[i].Y = (float)Math.Sin(Angle) * RADIUS;

                uint Colour = VisualizerTools.CCtoHEX(Note.Position / BaseNoteFinder.OctaveBinCount, 1F, 1F);
                this.OutputData[i].R = (byte)((Colour >> 16) & 0xFF);
                this.OutputData[i].G = (byte)((Colour >> 8) & 0xFF);
                this.OutputData[i].B = (byte)(Colour & 0xFF);
            }*/

            // Discrete Voronoi
            int TotalLEDCount = this.LEDCountX * this.LEDCountY; //tleds
            float AmplitudeSum = 0F; // totalexp

            for (byte i = 0; i < BaseNoteFinder.NoteCount; i++)
            {
                float Amplitude = MathF.Pow(BaseNoteFinder.Notes[i].AmplitudeFiltered, this.AmplifyPower) - this.NoteCutoff;
                if (Amplitude < 0) { Amplitude = 0F; }
                this.DiscreteNotes[i].Value = Amplitude;
                AmplitudeSum += Amplitude;

                if (this.CentersFromSides)
                {
                    float Angle = (BaseNoteFinder.Notes[i].Position / BaseNoteFinder.OctaveBinCount) * MathF.PI * 2;
                    float CenterX = this.LEDCountX / 2F;
                    float CenterY = this.LEDCountY / 2F;
                    float NewX = (MathF.Sin(Angle) * CenterX) + CenterX;
                    float NewY = (MathF.Cos(Angle) * CenterY) + CenterY;
                    bool NotePresent = (BaseNoteFinder.PersistentNoteIDs[i] != 0);
                    this.DiscreteNotes[i].X = NotePresent ? (this.DiscreteNotes[i].X * 0.9F) + (NewX * 0.1F) : CenterX;
                    this.DiscreteNotes[i].Y = NotePresent ? (this.DiscreteNotes[i].Y * 0.9F) + (NewY * 0.1F) : CenterY;
                }
                else
                {
                    // Base colorchord uses a random number generator with the note ID as seed. I didn't see a good way to do that here, so I instead used a simplified hash function from SO.
                    uint RandX = (((uint)BaseNoteFinder.PersistentNoteIDs[i] >> 16) ^ (uint)BaseNoteFinder.PersistentNoteIDs[i]) * 0x45D9F3B;
                    this.DiscreteNotes[i].X = ((RandX >> 16) ^ RandX) % this.LEDCountX;
                    uint RandY = (((uint)BaseNoteFinder.PersistentNoteIDs[i] >> 16) ^ (uint)BaseNoteFinder.PersistentNoteIDs[i]) * 0x119DE1F3;
                    this.DiscreteNotes[i].Y = ((RandY >> 16) ^ RandY) % this.LEDCountY;
                }
            }

            // Determine number of LEDs/blocks for each note
            for (byte i = 0; i < BaseNoteFinder.NoteCount; i++)
            {
                this.DiscreteNotes[i].LEDCount = this.DiscreteNotes[i].Value * TotalLEDCount / AmplitudeSum;
                this.DiscreteNotes[i].Value /= AmplitudeSum;
            }

            for (int X = 0; X < this.LEDCountX; X++)
            {
                for (int Y = 0; Y < this.LEDCountY; Y++)
                {
                    float OffsetX = X + 0.5F;
                    float OffsetY = Y + 0.5F;

                    // Find the note that is closest to this LED, based on its amplitude and location.
                    int BestMatch = -1;
                    float BestMatchValue = 0F;

                    for (byte PeakInd = 0; PeakInd < BaseNoteFinder.NoteCount; PeakInd++)
                    {
                        float DistX = OffsetX - this.DiscreteNotes[PeakInd].X;
                        float DistY = OffsetY - this.DiscreteNotes[PeakInd].Y;
                        float DistanceSquared = (DistX * DistX) + (DistY * DistY);
                        float Distance;
                        if (this.DistancePower == 1F) { Distance = DistanceSquared; }
                        else if (this.DistancePower == 2F) { Distance = MathF.Sqrt(DistanceSquared); }
                        else { Distance = DistanceSquared; } // TODO: Pretty sure this is a bug in base ColorChord.

                        float Suitability = this.DiscreteNotes[PeakInd].Value / Distance;
                        if (Suitability > BestMatchValue)
                        {
                            BestMatch = PeakInd;
                            BestMatchValue = Suitability;
                        }
                    }

                    uint Colour = 0;
                    if (BestMatch != -1)
                    {
                        float Saturation = BaseNoteFinder.Notes[BestMatch].AmplitudeFinal * this.SaturationAmplifier;
                        if (Saturation > 1F) { Saturation = 1F; }
                        float Note = BaseNoteFinder.Notes[BestMatch].Position / BaseNoteFinder.OctaveBinCount;
                        Colour = VisualizerTools.CCtoHEX(Note, 1F, MathF.Pow(Saturation, this.OutGamma));
                    }
                    this.OutputDataDiscrete[X, Y] = Colour;
                }
            }
        }

        private struct DiscreteVoronoiNote
        {
            public float X, Y;
            public float Value;
            public float LEDCount;
        }

        public struct VoronoiPoint
        {
            public bool Present;
            public float X, Y;
            public float Amplitude;
            public byte R, G, B;
        }

        // Discrete
        public int GetWidth() => this.LEDCountX;
        public int GetHeight() => this.LEDCountY;
        public uint[,] GetDataDiscrete() => this.OutputDataDiscrete;
    }
}
