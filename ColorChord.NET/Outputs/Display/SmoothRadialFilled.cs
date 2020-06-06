using System;
using System.Collections.Generic;
using System.Text;
using ColorChord.NET.Visualizers;
using ColorChord.NET.Visualizers.Formats;
using OpenTK;
using OpenTK.Graphics.ES30;

namespace ColorChord.NET.Outputs.Display
{
    public class SmoothRadialFilled : IDisplayMode
    {
        private readonly DisplayOpenGL HostWindow;
        private readonly IContinuous1D DataSource;

        private Shader Shader;

        private static readonly float[] DefaultGeometryData = new float[] // {[X,Y,R,G,B]} x 6
        { // X   Y    R  G  B
            -1,  1,   0, 0, 0, // Top-Left
            -1, -1,   0, 0, 0, // Bottom-Left
             1, -1,   0, 0, 0, // Bottom-Right
             1, -1,   0, 0, 0, // Bottom-Right
             1,  1,   0, 0, 0, // Top-Right
            -1,  1,   0, 0, 0  // Top-Left
        };

        private int VertexBufferHandle, VertexArrayHandle;

        private int LocationResolution;

        private int LocationAmplitudes, LocationMeans;

        private Vector2 Resolution = new Vector2(600, 600);

        public SmoothRadialFilled(DisplayOpenGL parent, IVisualizer visualizer)
        {
            this.HostWindow = parent;
        }

        public void Dispatch() { }

        public void Render()
        {
            this.Shader.Use();
            GL.BindVertexArray(this.VertexArrayHandle);

            float[] Means = new float[12];
            float[] Ampls = new float[12];

            for (int i = 0; i < 12; i++)
            {
                Means[i] = BaseNoteFinder.NoteDistributions[i].Mean / 2;
                Ampls[i] = BaseNoteFinder.NoteDistributions[i].Amplitude;
            }

            GL.Uniform1(this.LocationAmplitudes, 12, Ampls);
            GL.Uniform1(this.LocationMeans, 12, Means);

            //if (this.NewData) { GL.BufferData(BufferTarget.ArrayBuffer, this.GeometryData.Length * sizeof(float), this.GeometryData, BufferUsageHint.DynamicDraw); }
            GL.DrawArrays(PrimitiveType.Triangles, 0, DefaultGeometryData.Length / 5);
        }

        public void Resize(int width, int height)
        {
            this.Resolution = new Vector2(width, height);

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
            //this.NewData = true;
            GL.BufferData(BufferTarget.ArrayBuffer, DefaultGeometryData.Length * sizeof(float), DefaultGeometryData, BufferUsageHint.DynamicDraw);
            this.LocationResolution = this.Shader.GetUniformLocation("Resolution");
            this.LocationAmplitudes = this.Shader.GetUniformLocation("Amplitudes");
            this.LocationMeans = this.Shader.GetUniformLocation("Means");
            GL.Uniform2(this.LocationResolution, ref this.Resolution);
        }

        public void Close()
        {
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.DeleteBuffer(this.VertexBufferHandle);
            this.Shader.Dispose();
        }

        public bool SupportsFormat(IVisualizerFormat format) => true;
    }
}
