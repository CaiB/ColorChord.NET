using ColorChord.NET.API.Outputs.Display;
using ColorChord.NET.API.Visualizers;
using ColorChord.NET.API.Visualizers.Formats;
using System;
using OpenTK.Graphics.ES30;

namespace ColorChord.NET.Outputs.Display
{
    public class BlockStrip : IDisplayMode
    {
        private readonly DisplayOpenGL HostWindow;
        private readonly IDiscrete1D DataSource;

        private Shader? Shader;

        private int BlockCount;
        private float[] GeometryData; // {[X,Y,R,G,B] x 6} x BlockCount

        /// <summary> True if new data is waiting to be sent to the GPU. </summary>
        private bool NewData;
        private int VertexBufferHandle, VertexArrayHandle;

        public BlockStrip(DisplayOpenGL parent, IVisualizer visualizer)
        {
            if (!(visualizer is IDiscrete1D))
            {
                Log.Error("BlockStrip cannot use the provided visualizer, as it does not output 1D discrete data.");
                throw new InvalidOperationException("Incompatible visualizer. Must implement IDiscrete1D.");
            }
            this.HostWindow = parent;
            this.DataSource = (IDiscrete1D)visualizer;
            this.BlockCount = this.DataSource.GetCountDiscrete();
            this.GeometryData = new float[1];
            GenerateGeometry();
        }

        /// <summary> Updates the geometry data. Used at startup and whenever <see cref="BlockCount"/> changes. </summary>
        private void GenerateGeometry()
        {
            if (this.GeometryData.Length != this.BlockCount * 6 * 5) { this.GeometryData = new float[this.BlockCount * 6 * 5]; } // 6 vertices per block, with 2+3 floats each.
            float Width = 2F / this.BlockCount;
            for (int i = 0; i < this.BlockCount; i++)
            { // TODO: This generates triangles with opposing directions.
                for (int v = 0; v < 6; v++) // Top-Left, Bottom-Left, Bottom-Right | Top-Left, Top-Right, Bottom-Right
                { // This inner block runs once per vertex, per block.
                    switch (v) // X
                    {
                        case 0:
                        case 1:
                        case 3:
                            this.GeometryData[(i * 6 * 5) + (v * 5) + 0] = -1F + (Width * i); break; // Left
                        case 2:
                        case 4:
                        case 5:
                            this.GeometryData[(i * 6 * 5) + (v * 5) + 0] = -1F + (Width * (i + 1)); break; // Right
                    }
                    switch (v) // Y
                    {
                        case 0:
                        case 3:
                        case 4:
                            this.GeometryData[(i * 6 * 5) + (v * 5) + 1] = 1F; break; // Top
                        case 1:
                        case 2:
                        case 5:
                            this.GeometryData[(i * 6 * 5) + (v * 5) + 1] = -1F; break; // Bottom
                    }
                    // +2, +3, and +4 are RGB data which is handled separately.
                }
            }
            this.NewData = true;
        }

        /// <summary> Called by the visualizer when new data is available. </summary>
        public void Dispatch()
        {
            uint[] Data = this.DataSource.GetDataDiscrete();
            if (Data.Length != this.BlockCount) // The number of blocks has changed!
            {
                this.BlockCount = Data.Length;
                GenerateGeometry();
            }
            for (int i = 0; i < this.BlockCount; i++) // Every block
            {
                for (byte v = 0; v < 6; v++) // Every vertex in every block
                {
                    this.GeometryData[(i * 6 * 5) + (v * 5) + 2] = (byte)((Data[i] >> 16) & 0xFF) / 255F; // R
                    this.GeometryData[(i * 6 * 5) + (v * 5) + 3] = (byte)((Data[i] >> 8) & 0xFF) / 255F; // G
                    this.GeometryData[(i * 6 * 5) + (v * 5) + 4] = (byte)(Data[i] & 0xFF) / 255F; // B
                }
            }
            this.NewData = true;
        }

        /// <summary> Called whenever we want to render a frame. </summary>
        public void Render()
        {
            this.Shader!.Use();
            GL.BindVertexArray(this.VertexArrayHandle);
            if (this.NewData) { GL.BufferData(BufferTarget.ArrayBuffer, this.GeometryData.Length * sizeof(float), this.GeometryData, BufferUsageHint.DynamicDraw); }
            GL.DrawArrays(PrimitiveType.Triangles, 0, this.GeometryData.Length / 5);
        }

        /// <summary> Called when OpenGL and the window are ready. </summary>
        public void Load()
        {
            this.Shader = new Shader("Passthrough2Colour.vert", "Passthrough2Colour.frag");
            this.VertexBufferHandle = GL.GenBuffer();
            this.VertexArrayHandle = GL.GenVertexArray();

            GL.BindVertexArray(this.VertexArrayHandle);
            GL.BindBuffer(BufferTarget.ArrayBuffer, this.VertexBufferHandle);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 5 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 5 * sizeof(float), 2 * sizeof(float));
            GL.EnableVertexAttribArray(1);
        }

        public void Resize(int width, int height) { }

        public void Close()
        {
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.DeleteBuffer(this.VertexBufferHandle);
            this.Shader?.Dispose();
        }

        public bool SupportsFormat(IVisualizerFormat format) => format is IDiscrete1D;
    }
}
