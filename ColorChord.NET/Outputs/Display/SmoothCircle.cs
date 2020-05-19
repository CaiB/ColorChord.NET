using ColorChord.NET.Visualizers;
using ColorChord.NET.Visualizers.Formats;
using System;
using OpenTK.Graphics.ES30;
using OpenTK;

namespace ColorChord.NET.Outputs.Display
{
    public class SmoothCircle : IDisplayMode
    {
        private const int MAX_COUNT = 12;
        private readonly DisplayOpenGL HostWindow;
        private readonly IContinuous1D DataSource;

        private Shader Shader;

        private static readonly float[] DefaultGeometryData = new float[] // {[X,Y]} x 6
        { // X   Y 
            -1,  1, // Top-Left
            -1, -1, // Bottom-Left
             1, -1, // Bottom-Right
             1, -1, // Bottom-Right
             1,  1, // Top-Right
            -1,  1  // Top-Left
        };

        private int VertexBufferHandle, VertexArrayHandle;
        private int LocationColours, LocationStarts, LocationResolution, LocationAdvance;
        private bool SetupDone = false;
        private Vector2 Resolution = new Vector2(600, 600);

        public SmoothCircle(DisplayOpenGL parent, IVisualizer visualizer)
        {
            if (!(visualizer is IContinuous1D))
            {
                Log.Error("SmoothStrip cannot use the provided visualizer, as it does not output 1D continuous data.");
                throw new InvalidOperationException("Incompatible visualizer. Must implement IContinuous1D.");
            }
            this.HostWindow = parent;
            this.DataSource = (IContinuous1D)visualizer;
        }

        private readonly float[] Starts = new float[MAX_COUNT];
        private readonly float[] Colours = new float[MAX_COUNT * 3];
        private float Advance;

        public void Dispatch()
        {
            if (!this.SetupDone) { return; }
            int Count = this.DataSource.GetCountContinuous();
            ContinuousDataUnit[] Data = this.DataSource.GetDataContinuous();
            this.Advance = this.DataSource.GetAdvanceContinuous();

            for (int i = 0; i < MAX_COUNT; i++)
            {
                if (i < Count)
                {
                    Starts[i] = Data[i].Location;
                    Colours[(i * 3) + 0] = Data[i].R / 255F;
                    Colours[(i * 3) + 1] = Data[i].G / 255F;
                    Colours[(i * 3) + 2] = Data[i].B / 255F;
                }
                else
                {
                    Starts[i] = 1F;
                    Colours[(i * 3) + 0] = 0F;
                    Colours[(i * 3) + 1] = 0F;
                    Colours[(i * 3) + 2] = 0F;
                }
            }
        }

        public void Render()
        {
            if (!this.SetupDone) { return; }
            this.Shader.Use();
            GL.BindVertexArray(this.VertexArrayHandle);
            GL.DrawArrays(PrimitiveType.Triangles, 0, DefaultGeometryData.Length / 2);
            GL.Uniform1(this.LocationStarts, 12, this.Starts);
            GL.Uniform1(this.LocationColours, 36, this.Colours);
            GL.Uniform1(this.LocationAdvance, this.Advance);
            Console.WriteLine(this.Advance);
        }

        public void Resize(int width, int height)
        {
            this.Resolution = new Vector2(width, height);
            GL.Uniform2(this.LocationResolution, ref this.Resolution);
        }

        public void Load()
        {
            this.Shader = new Shader("SmoothCircle.vert", "SmoothCircle.frag");
            this.Shader.Use();
            this.VertexBufferHandle = GL.GenBuffer();
            this.VertexArrayHandle = GL.GenVertexArray();
            this.LocationColours = this.Shader.GetUniformLocation("Colours");
            this.LocationStarts = this.Shader.GetUniformLocation("Starts");
            this.LocationResolution = this.Shader.GetUniformLocation("Resolution");
            this.LocationAdvance = this.Shader.GetUniformLocation("Advance");
            GL.Uniform2(this.LocationResolution, ref this.Resolution);

            GL.BindVertexArray(this.VertexArrayHandle);
            GL.BindBuffer(BufferTarget.ArrayBuffer, this.VertexBufferHandle);
            GL.BufferData(BufferTarget.ArrayBuffer, DefaultGeometryData.Length * sizeof(float), DefaultGeometryData, BufferUsageHint.DynamicDraw);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            this.SetupDone = true;
        }

        public void Close()
        {
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.DeleteBuffer(this.VertexBufferHandle);
            this.Shader.Dispose();
        }

        public bool SupportsFormat(IVisualizerFormat format) => format is IContinuous1D;
    }
}
