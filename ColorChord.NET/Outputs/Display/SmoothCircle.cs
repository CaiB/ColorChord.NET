﻿using ColorChord.NET.Visualizers;
using ColorChord.NET.Visualizers.Formats;
using System;
using OpenTK.Graphics.OpenGL4;
using OpenTK;

namespace ColorChord.NET.Outputs.Display
{
    public class SmoothCircle : IDisplayMode
    {
        private const int MAX_COUNT = 12;
        private readonly DisplayOpenGL HostWindow;
        private readonly IContinuous1D DataSource;

        private Shader CircleShader, HistoryShader;

        private static readonly float[] DefaultGeometryData = new float[] // {[X,Y]} x 6
        { // X   Y 
            -1,  1, // Top-Left
            -1, -1, // Bottom-Left
             1, -1, // Bottom-Right
             1, -1, // Bottom-Right
             1,  1, // Top-Right
            -1,  1  // Top-Left
        };

        private const float Nudge = 0.98F;
        private static readonly float[] SmallerGeometryData = new float[] // {[X,Y][U,V]} x 6
        { //   X       Y     U   V
            -Nudge,  Nudge, 0, 1, // Top-Left
            -Nudge, -Nudge, 0, 0, // Bottom-Left
             Nudge, -Nudge, 1, 0, // Bottom-Right
             Nudge, -Nudge, 1, 0, // Bottom-Right
             Nudge,  Nudge, 1, 1, // Top-Right
            -Nudge,  Nudge, 0, 1  // Top-Left
        };

        private int VertexBufferHandle, VertexArrayHandle;
        private int VertexBufferHistoryHandle, VertexArrayHistoryHandle;
        private int LocationColours, LocationStarts, LocationResolution, LocationAdvance;
        private bool SetupDone = false;
        private Vector2 Resolution = new Vector2(600, 600);

        FrameBuffer BufferA, BufferB;
        bool CurrentFB;

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
            GL.Enable(EnableCap.Blend);
            
            // Bind the current buffer to draw into.
            if (this.CurrentFB) { BufferA.Bind(); }
            else { BufferB.Bind(); }

            // Render the ring.
            this.CircleShader.Use();
            GL.BindVertexArray(this.VertexArrayHandle);
            GL.Uniform1(this.LocationStarts, 12, this.Starts);
            GL.Uniform1(this.LocationColours, 36, this.Colours);
            GL.Uniform1(this.LocationAdvance, this.Advance);
            GL.DrawArrays(PrimitiveType.Triangles, 0, DefaultGeometryData.Length / 2);

            if (this.CurrentFB)
            {
                BufferA.Unbind();
                BufferA.UseTextureColor();
                BufferB.Bind();
            }
            else
            {
                BufferB.Unbind();
                BufferB.UseTextureColor();
                BufferA.Bind();
            }

            this.HistoryShader.Use();
            GL.BindVertexArray(this.VertexArrayHistoryHandle);
            GL.DrawArrays(PrimitiveType.Triangles, 0, SmallerGeometryData.Length / 4);

            if (this.CurrentFB)
            {
                BufferB.Unbind();
            }
            else
            {
                BufferA.Unbind();
            }
            GL.BindVertexArray(this.VertexArrayHistoryHandle);
            GL.DrawArrays(PrimitiveType.Triangles, 0, SmallerGeometryData.Length / 4);


            GL.Disable(EnableCap.Blend);
            this.CurrentFB = !this.CurrentFB;
        }

        public void Resize(int width, int height)
        {
            this.Resolution = new Vector2(width, height);
            GL.Uniform2(this.LocationResolution, ref this.Resolution);
            this.BufferA.Resize(width, height);
            this.BufferB.Resize(width, height);
        }

        public void Load()
        {
            this.CircleShader = new Shader("SmoothCircle.vert", "SmoothCircle.frag");
            this.HistoryShader = new Shader("Passthrough2Textured.vert", "Passthrough2Textured.frag");
            this.BufferA = new FrameBuffer(this.HostWindow.Width, this.HostWindow.Height);
            this.BufferB = new FrameBuffer(this.HostWindow.Width, this.HostWindow.Height);

            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.DepthFunc(DepthFunction.Lequal);

            this.CircleShader.Use();
            this.VertexBufferHandle = GL.GenBuffer();
            this.VertexArrayHandle = GL.GenVertexArray();
            this.VertexBufferHistoryHandle = GL.GenBuffer();
            this.VertexArrayHistoryHandle = GL.GenVertexArray();
            this.LocationColours = this.CircleShader.GetUniformLocation("Colours");
            this.LocationStarts = this.CircleShader.GetUniformLocation("Starts");
            this.LocationResolution = this.CircleShader.GetUniformLocation("Resolution");
            this.LocationAdvance = this.CircleShader.GetUniformLocation("Advance");
            GL.Uniform2(this.LocationResolution, ref this.Resolution);

            GL.BindVertexArray(this.VertexArrayHistoryHandle);
            GL.BindBuffer(BufferTarget.ArrayBuffer, this.VertexBufferHistoryHandle);
            GL.BufferData(BufferTarget.ArrayBuffer, SmallerGeometryData.Length * sizeof(float), SmallerGeometryData, BufferUsageHint.DynamicDraw);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));
            GL.EnableVertexAttribArray(1);

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
            GL.DeleteBuffer(this.VertexArrayHistoryHandle);
            this.CircleShader.Dispose();
            this.HistoryShader.Dispose();
        }

        public bool SupportsFormat(IVisualizerFormat format) => format is IContinuous1D;
    }
}
