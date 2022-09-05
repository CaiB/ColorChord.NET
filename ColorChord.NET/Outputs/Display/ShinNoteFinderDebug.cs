using ColorChord.NET.API.Config;
using ColorChord.NET.API.Outputs.Display;
using ColorChord.NET.API.Visualizers;
using ColorChord.NET.API.Visualizers.Formats;
using ColorChord.NET.Config;
using ColorChord.NET.NoteFinder;
using System;
using System.Collections.Generic;
using System.Text;
using OpenTK.Graphics.ES30;

namespace ColorChord.NET.Outputs.Display
{
    /*
    public class ShinNoteFinderDebug : IDisplayMode
    {
        private readonly DisplayOpenGL HostWindow;
        //private readonly ShinNoteFinderDFT DataSource;

        private Shader BarShader;

        private float[] GeometryData; // {[X,Y,R,G,B] x 6} x BlockCount

        private int VertexBufferHandle, VertexArrayHandle;

        public ShinNoteFinderDebug(DisplayOpenGL parent, IVisualizer visualizer)
        {
            this.HostWindow = parent;
            this.GeometryData = new float[1];
            //this.DataSource = BaseNoteFinder.DFT;
            GenerateGeometry();
            this.DataFilt = new float[BaseNoteFinder.DFT.BinCount];
        }

        private void GenerateGeometry()
        {
            if (this.GeometryData.Length != BaseNoteFinder.DFT.BinCount * 6 * 5) { this.GeometryData = new float[BaseNoteFinder.DFT.BinCount * 6 * 5]; } // 6 vertices per block, with 2+3 floats each.
            float Width = 2F / BaseNoteFinder.DFT.BinCount;
            for (int i = 0; i < BaseNoteFinder.DFT.BinCount; i++)
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
                            this.GeometryData[(i * 6 * 5) + (v * 5) + 1] = 0.75F; break; // Bottom
                    }
                    uint Colour = VisualizerTools.CCtoHEX((float)(i % BaseNoteFinder.DFT.BinsPerOctave) / BaseNoteFinder.DFT.BinsPerOctave, 1F, 1F);
                    this.GeometryData[(i * 6 * 5) + (v * 5) + 2] = ((Colour >> 16) & 0xFF) / 255F;
                    this.GeometryData[(i * 6 * 5) + (v * 5) + 3] = ((Colour >> 8) & 0xFF) / 255F;
                    this.GeometryData[(i * 6 * 5) + (v * 5) + 4] = (Colour & 0xFF) / 255F;
                }
            }
        }

        float[] DataFilt;

        private void UpdateGeometry()
        {
            float[] Data = BaseNoteFinder.DFT.Magnitudes;
            for (int i = 0; i < Data.Length; i++)
            {
                DataFilt[i] = (DataFilt[i] * 0.2F) + (Data[i] * 0.8F);
                float Value = Data[i] / 200F;//DataFilt[i] / 200F;
                this.GeometryData[(i * 6 * 5) + (1 * 5) + 1] = 1F - Value;
                this.GeometryData[(i * 6 * 5) + (2 * 5) + 1] = 1F - Value;
                this.GeometryData[(i * 6 * 5) + (5 * 5) + 1] = 1F - Value;
            }
        }

        /// <summary> Called whenever we want to render a frame. </summary>
        public void Render()
        {
            UpdateGeometry();
            this.BarShader.Use();
            GL.BindVertexArray(this.VertexArrayHandle);
            GL.BufferData(BufferTarget.ArrayBuffer, this.GeometryData.Length * sizeof(float), this.GeometryData, BufferUsageHint.DynamicDraw);
            GL.DrawArrays(PrimitiveType.Triangles, 0, this.GeometryData.Length / 5);
        }

        /// <summary> Called when OpenGL and the window are ready. </summary>
        public void Load()
        {
            this.BarShader = new Shader("Passthrough2Colour.vert", "Passthrough2Colour.frag");
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
            this.BarShader.Dispose();
        }

        public bool SupportsFormat(IVisualizerFormat format) => true;
        public void Dispatch() { } // We gat data every frame since this is not attached to a visualizer
    }
    */
}
