using ColorChord.NET.Visualizers;
using ColorChord.NET.Visualizers.Formats;
using OpenTK.Graphics.ES30;
using System;

namespace ColorChord.NET.Outputs.Display
{
    public class SmoothStrip : IDisplayMode
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

        private float[] GeometryData = DefaultGeometryData; // {[X,Y,R,G,B] x6} x NoteCount

        /// <summary> True if new data needs to be sent to the GPU. </summary>
        private bool NewData;
        private int VertexBufferHandle, VertexArrayHandle;

        public SmoothStrip(DisplayOpenGL parent, IVisualizer visualizer)
        {
            if (!(visualizer is IContinuous1D))
            {
                Log.Error("SmoothStrip cannot use the provided visualizer, as it does not output 1D continuous data.");
                throw new InvalidOperationException("Incompatible visualizer. Must implement IContinuous1D.");
            }
            this.HostWindow = parent;
            this.DataSource = (IContinuous1D)visualizer;
        }

        public void Dispatch()
        {
            int Count = this.DataSource.GetCountContinuous();
            ContinuousDataUnit[] Data = this.DataSource.GetDataContinuous();

            if (Count == 0 && this.GeometryData != DefaultGeometryData) // We just switched to having no input
            {
                this.GeometryData = DefaultGeometryData;
                this.NewData = true;
                return;
            }
            if (Count == 0) { return; } // We still have no input

            /*if (this.GeometryData.Length != Count * 6 * 5) { */this.GeometryData = new float[Count * 6 * 5];// }
            for (int Block = 0; Block < Count; Block++)
            {
                for (int v = 0; v < 6; v++)
                {
                    switch (v) // X
                    {
                        case 0:
                        case 1:
                        case 5:
                            this.GeometryData[(Block * 5 * 6) + (v * 5) + 0] = (Data[Block].Location * 2F) - 1F; break; // Left
                        case 2:
                        case 3:
                        case 4:
                            this.GeometryData[(Block * 5 * 6) + (v * 5) + 0] = ((Data[Block].Location + Data[Block].Size) * 2F) - 1F; break; // Right
                    }
                    switch(v) // Y
                    {
                        case 0:
                        case 4:
                        case 5:
                            this.GeometryData[(Block * 5 * 6) + (v * 5) + 1] = 1F; break; // Top
                        case 1:
                        case 2:
                        case 3:
                            this.GeometryData[(Block * 5 * 6) + (v * 5) + 1] = -1F; break; // Bottom
                    }
                    this.GeometryData[(Block * 5 * 6) + (v * 5) + 2] = Data[Block].R / 255F; // R
                    this.GeometryData[(Block * 5 * 6) + (v * 5) + 3] = Data[Block].G / 255F; // G
                    this.GeometryData[(Block * 5 * 6) + (v * 5) + 4] = Data[Block].B / 255F; // B
                }
            }
            this.NewData = true;
        }

        public void Render()
        {
            this.Shader.Use();
            GL.BindVertexArray(this.VertexArrayHandle);
            if (this.NewData) { GL.BufferData(BufferTarget.ArrayBuffer, this.GeometryData.Length * sizeof(float), this.GeometryData, BufferUsageHint.DynamicDraw); }
            GL.DrawArrays(PrimitiveType.Triangles, 0, this.GeometryData.Length / 5);
        }

        public void Resize(int width, int height) { }

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
            this.NewData = true;
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
