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

        private const int NOTE_COUNT = 12;

        private readonly float[] GeometryData = new float[] // {[X,Y]} x 6
        {
            -1, 1, // Top-Left
            -1, -1, // Bottom-Left
            1, -1, // Bottom-Right
            1, -1, // Bottom-Right
            1, 1, // Top-Right
            -1, 1 // Top-Left
        };

        private int VertexBufferHandle, VertexArrayHandle;

        public SmoothStrip(DisplayOpenGL parent, IVisualizer visualizer)
        {
            if (!(visualizer is IContinuous1D))
            {
                Log.Error("SmmothStrip cannot use the provided visualizer, as it does not output 1D continuous data.");
                throw new InvalidOperationException("Incompatible visualizer. Must implement IContinuous1D.");
            }
            this.HostWindow = parent;
            this.DataSource = (IContinuous1D)visualizer;
            if (this.DataSource.MaxPossibleUnits > NOTE_COUNT)
            {
                Log.Error("SmoothStrip cannot use the provided visualizer, as it outputs too much data.");
                throw new InvalidOperationException("Incompatible visualizer. MaxPossibleUnits exceeds this shader's capabilities.");
            }
        }

        public void Dispatch()
        {
            int Count = this.DataSource.GetCountContinuous();
            ContinuousDataUnit[] Data = this.DataSource.GetDataContinuous();

            // TODO: Upload new data.
        }

        public void Render()
        {
            this.Shader.Use();
            GL.BindVertexArray(this.VertexArrayHandle);
            GL.DrawArrays(PrimitiveType.Triangles, 0, this.GeometryData.Length / 2);
        }

        public void Load()
        {
            this.Shader = new Shader("SmoothStrip.vert", "SmoothStrip.frag");
            this.VertexBufferHandle = GL.GenBuffer();
            this.VertexArrayHandle = GL.GenVertexArray();

            GL.BindVertexArray(this.VertexArrayHandle);
            GL.BindBuffer(BufferTarget.ArrayBuffer, this.VertexBufferHandle);
            GL.BufferData(BufferTarget.ArrayBuffer, this.GeometryData.Length * sizeof(float), this.GeometryData, BufferUsageHint.StaticDraw);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
        }

        public void Close()
        {
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.DeleteBuffer(this.VertexBufferHandle);
            this.Shader.Dispose();
        }

        public bool SupportsFormat(IVisualizerFormat format) => (format is IContinuous1D) && (((IContinuous1D)format).MaxPossibleUnits <= NOTE_COUNT);
    }
}
