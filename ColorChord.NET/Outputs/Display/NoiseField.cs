using ColorChord.NET.API;
using ColorChord.NET.API.Config;
using ColorChord.NET.API.Outputs.Display;
using ColorChord.NET.API.Visualizers.Formats;
using ColorChord.NET.Config;
using ColorChord.NET.NoteFinder;
using OpenTK.Graphics.OpenGL4;
using System;
using System.Collections.Generic;

namespace ColorChord.NET.Outputs.Display
{
    public class NoiseField : IDisplayMode, IConfigurableAttr
    {
        /// <summary> This needs to correspond to NOTE_QTY in the fragment shaders. </summary>
        private const int MAX_COUNT = 12;

        private readonly DisplayOpenGL HostWindow;
        private readonly IContinuous1D DataSource;

        private Shader? Shader;

        [ConfigFloat("Size1", 0.0F, 10000.0F, 8.0F)]
        public float SizeMult1 { get; set; }

        [ConfigFloat("Size2", 0.0F, 10000.0F, 3.5F)]
        public float SizeMult2 { get; set; }

        [ConfigFloat("Speed", 0.0F, 10000.0F, 5.0F)]
        public float TimeIncr { get; set; }

        private float Time;

        private int LocationSizeMult1, LocationSizeMult2, LocationTime, LocationOffset;
        private int LocationColours, LocationStarts;

        /// <summary> Just a full-size rectangle, with only XY info. </summary>
        private static readonly float[] Geometry = new float[] // {[X,Y]} x 6
        { // X   Y 
            -1,  1, // Top-Left
            -1, -1, // Bottom-Left
             1, -1, // Bottom-Right
             1, -1, // Bottom-Right
             1,  1, // Top-Right
            -1,  1  // Top-Left
        };

        /// <summary> Holds basic geometry data. </summary>
        private int VertexBufferHandle, VertexArrayHandle;

        /// <summary> Whether this output is ready to accept data and draw. </summary>
        private bool SetupDone = false;

        public NoiseField(DisplayOpenGL parent, IVisualizerFormat visualizer, Dictionary<string, object> config)
        {
            if(visualizer is not IContinuous1D)
            {
                Log.Error("NoiseField cannot be used with this visualizer, as it does not output 1D continuous data.");
                throw new InvalidOperationException("Incompatible visualizer. Must implement IContinuous1D.");
            }
            if (ColorChord.NoteFinder is not BaseNoteFinder) { throw new Exception("NoiseField currently only supports BaseNoteFinder."); }
            this.HostWindow = parent;
            this.DataSource = (IContinuous1D)visualizer;
            Configurer.Configure(this, config);
        }

        public void Load()
        {
            GL.ClearColor(0.0F, 0.0F, 0.0F, 1.0F);
            this.Shader = new Shader("Passthrough2.vert", "NoiseField.frag");
            this.Shader.Use();
            this.VertexBufferHandle = GL.GenBuffer();
            this.VertexArrayHandle = GL.GenVertexArray();

            this.LocationColours = this.Shader.GetUniformLocation("Colours");
            this.LocationStarts = this.Shader.GetUniformLocation("Starts");
            this.LocationSizeMult1 = this.Shader.GetUniformLocation("SizeMult1");
            this.LocationSizeMult2 = this.Shader.GetUniformLocation("SizeMult2");
            this.LocationTime = this.Shader.GetUniformLocation("Time");
            this.LocationOffset = this.Shader.GetUniformLocation("Offset");
            GL.Uniform1(this.LocationSizeMult1, this.SizeMult1 * 0.001F);
            GL.Uniform1(this.LocationSizeMult2, this.SizeMult2 * 0.001F);
            GL.Uniform1(this.LocationTime, 0F);

            GL.BindVertexArray(this.VertexArrayHandle);
            GL.BindBuffer(BufferTarget.ArrayBuffer, this.VertexBufferHandle);
            GL.BufferData(BufferTarget.ArrayBuffer, Geometry.Length * sizeof(float), Geometry, BufferUsageHint.StaticDraw);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            this.SetupDone = true;
        }

        private readonly float[] Starts = new float[MAX_COUNT];
        private readonly float[] Colours = new float[MAX_COUNT * 3];

        private float HistoricalLFData = 0;
        private float CurrentLFData = 0;
        private float SizeBoost = 0;
        private float Offset = 0;

        public void Dispatch()
        {
            if (!this.SetupDone) { return; } // Needed as Dispatch() is called on a thread, possibly before we are done setting up.

            // Beat detection

            // Get newest low-frequency data, and reset the accumulator.
            float LowFreqData = BaseNoteFinder.LastLowFreqSum / (1 + BaseNoteFinder.LastLowFreqCount);
            BaseNoteFinder.LastLowFreqSum = 0;
            BaseNoteFinder.LastLowFreqCount = 0;

            // Strong IIR for keeping an average of the low-frequency content to compare against for finding the beats
            const float IIR_HIST = 0.95F;
            this.HistoricalLFData = Math.Max(LowFreqData, this.HistoricalLFData * IIR_HIST); //(IIR_HIST * this.HistoricalLFData) + ((1F - IIR_HIST) * LowFreqData);

            // The current low-frequency content, filtered lightly, then normalized against the recent average quantity.
            const float IIR_REAL = 0.2F;
            LowFreqData /= this.HistoricalLFData; // Normalize? Maybe

            this.CurrentLFData = (IIR_REAL * CurrentLFData) + ((1F - IIR_REAL) * LowFreqData);
            LowFreqData = this.CurrentLFData;

            // Some non-linearity to make the beats more apparent
            this.SizeBoost = MathF.Pow(LowFreqData, 3);

            this.Offset -= 10F;

            // Colours
            int Count = this.DataSource.GetCountContinuous();
            ContinuousDataUnit[] Data = this.DataSource.GetDataContinuous();

            for (int i = 0; i < MAX_COUNT; i++)
            {
                if (i < Count)
                {
                    this.Starts[i] = Data[i].Location;
                    this.Colours[(i * 3) + 0] = Data[i].R / 255F;
                    this.Colours[(i * 3) + 1] = Data[i].G / 255F;
                    this.Colours[(i * 3) + 2] = Data[i].B / 255F;
                }
                else // Clear out the colours and lengths for notes not present.
                {
                    this.Starts[i] = 1F;
                    this.Colours[(i * 3) + 0] = 0F;
                    this.Colours[(i * 3) + 1] = 0F;
                    this.Colours[(i * 3) + 2] = 0F;
                }
            }
        }

        public void Render()
        {
            if (!this.SetupDone) { return; }
            this.Time += (this.TimeIncr * 0.001F);
            this.Shader!.Use();
            GL.BindVertexArray(this.VertexArrayHandle);
            GL.Uniform1(this.LocationStarts, 12, this.Starts);
            GL.Uniform1(this.LocationColours, 36, this.Colours);
            GL.Uniform1(this.LocationTime, this.Time);
            GL.Uniform1(this.LocationSizeMult1, this.SizeMult1 * 0.001F / Math.Max(this.SizeBoost, 0.5F));
            GL.Uniform1(this.LocationOffset, this.Offset);
            GL.DrawArrays(PrimitiveType.Triangles, 0, Geometry.Length / 2);
        }

        public void Resize(int width, int height) { }

        public void Close()
        {
            if (!this.SetupDone) { return; }
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.DeleteBuffer(this.VertexBufferHandle);
            this.Shader?.Dispose();
        }

        public bool SupportsFormat(IVisualizerFormat format) => format is IContinuous1D;
    }
}
