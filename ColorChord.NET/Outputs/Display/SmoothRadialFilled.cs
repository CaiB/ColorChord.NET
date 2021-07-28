using System;
using System.Collections.Generic;
using ColorChord.NET.Config;
using ColorChord.NET.NoteFinder;
using ColorChord.NET.Visualizers;
using ColorChord.NET.Visualizers.Formats;
using OpenTK.Graphics.ES30;
using OpenTK.Mathematics;

namespace ColorChord.NET.Outputs.Display
{
    public class SmoothRadialFilled : IDisplayMode, IConfigurableAttr
    {
        private readonly DisplayOpenGL HostWindow;

        private Shader? Shader;

        private static readonly float[] DefaultGeometryData = new float[] // {[X,Y,R,G,B]} x 6
        { // X   Y    R  G  B
            -1,  1,   0, 0, 0, // Top-Left
            -1, -1,   0, 0, 0, // Bottom-Left
             1, -1,   0, 0, 0, // Bottom-Right
             1, -1,   0, 0, 0, // Bottom-Right
             1,  1,   0, 0, 0, // Top-Right
            -1,  1,   0, 0, 0  // Top-Left
        };

        /// <summary> The location to store <see cref="DefaultGeometryData"/> on the GPU. </summary>
        private int VertexBufferHandle, VertexArrayHandle;

        /// <summary> The location of the Resolution uniform in the shaders. </summary>
        private int LocationResolution;

        /// <summary> The location of the note distribution data uniform arrays in the shaders. </summary>
        private int LocationAmplitudes, LocationMeans;
        private Vector2 Resolution = new(600, 600);

        /// <summary> The location of the sigma uniform in the shaders. </summary>
        private int LocationSigma;

        /// <summary> How broad peaks should be on the spectrum. </summary>
        [ConfigFloat("PeakWidth", 0F, 10F, 0.5F)]
        private float PeakWidth = 0.5F;

        /// <summary> The location of the base brightness uniform in the shaders. </summary>
        private int LocationBaseBright;

        /// <summary> How bright colours should be if there is no note at that location. </summary>
        [ConfigFloat("BaseBrightness", 0F, 1F, 0.2F)]
        private float BaseBrightness = 0.2F;

        /// <summary> How much the amplitudes will be amplified. </summary>
        /// <remarks> If width is increased, amplification should also be increased to maintain the same peak brightness. </remarks>
        [ConfigFloat("BrightnessAmp", 0F, 100F, 1.0F)]
        private float Amplify = 1.0F;

        private bool IsReady = false;

        public SmoothRadialFilled(DisplayOpenGL parent, IVisualizer visualizer, Dictionary<string, object> config)
        {
            this.HostWindow = parent;
            if (ColorChord.NoteFinder is not BaseNoteFinder) { throw new Exception("SmoothRadialFilled currently only works with the BaseNoteFinder."); }
            Configurer.Configure(this, config);
        }

        public void Dispatch() { }

        public void Render()
        {
            if (!this.IsReady) { return; }
            this.Shader!.Use();
            GL.BindVertexArray(this.VertexArrayHandle);

            float[] Means = new float[12];
            float[] Ampls = new float[12];

            for (int i = 0; i < 12; i++)
            {
                Means[i] = BaseNoteFinder.NoteDistributions[i].Mean / 2; // TODO: This seems like a bad idea. Refer to the data properly.
                Ampls[i] = BaseNoteFinder.NoteDistributions[i].Amplitude * this.Amplify;
            }

            GL.Uniform1(this.LocationAmplitudes, 12, Ampls);
            GL.Uniform1(this.LocationMeans, 12, Means);

            GL.DrawArrays(PrimitiveType.Triangles, 0, DefaultGeometryData.Length / 5);
        }

        public void Resize(int width, int height)
        {
            this.Resolution = new Vector2(width, height);

            if (!this.IsReady || this.Shader == null) { return; }

            this.Shader.Use();
            GL.Uniform2(this.LocationResolution, ref this.Resolution);
        }

        public void Load()
        {
            this.Shader = new Shader("Passthrough2Colour.vert", "SmoothRadialBright.frag");
            this.VertexBufferHandle = GL.GenBuffer();
            this.VertexArrayHandle = GL.GenVertexArray();

            this.Shader.Use();
            GL.BindVertexArray(this.VertexArrayHandle);
            GL.BindBuffer(BufferTarget.ArrayBuffer, this.VertexBufferHandle);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 5 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 5 * sizeof(float), 2 * sizeof(float));
            GL.EnableVertexAttribArray(1);
            GL.BufferData(BufferTarget.ArrayBuffer, DefaultGeometryData.Length * sizeof(float), DefaultGeometryData, BufferUsageHint.DynamicDraw);

            this.LocationResolution = this.Shader.GetUniformLocation("Resolution");
            this.LocationAmplitudes = this.Shader.GetUniformLocation("Amplitudes");
            this.LocationMeans = this.Shader.GetUniformLocation("Means");
            GL.Uniform2(this.LocationResolution, ref this.Resolution);

            this.LocationSigma = this.Shader.GetUniformLocation("Sigma");
            this.LocationBaseBright = this.Shader.GetUniformLocation("BaseBright");
            GL.Uniform1(this.LocationBaseBright, this.BaseBrightness);
            GL.Uniform1(this.LocationSigma, this.PeakWidth);

            this.IsReady = true;
        }

        public void Close()
        {
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.DeleteBuffer(this.VertexBufferHandle);
            this.Shader?.Dispose();
        }

        public bool SupportsFormat(IVisualizerFormat format) => true;
    }
}
