using ColorChord.NET.Visualizers;
using ColorChord.NET.Visualizers.Formats;
using System;
using OpenTK.Graphics.OpenGL4;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace ColorChord.NET.Outputs.Display
{
    public class SmoothCircle : IDisplayMode, IConfigurable
    {
        // TODO: Use only the central square in the window, rather than assuming a 1:1 aspect ratio and rendering an ellipse

        /// <summary> This needs to correspond to NOTE_QTY in the fragment shaders. </summary>
        private const int MAX_COUNT = 12;

        private readonly DisplayOpenGL HostWindow;
        private readonly IContinuous1D DataSource;

        /// <summary> False just renders the ring, true also renders a decaying persistence effect, appearing to go off to infinity. </summary>
        private bool IsInfinity = true;

        /// <summary> Used to create the current ring, with transparent background and no antialiasing. </summary>
        /// <remarks> Used only in infinity mode. </remarks>
        private Shader CircleShader;

        /// <summary> Used to copy the old ring down a level, but slightly smaller. Colour passthrough. </summary>
        /// <remarks> Used only in infinity mode. </remarks>
        private Shader HistoryShader;
        
        /// <summary> Used to render the newest (or only) ring, with antialiasing. </summary>
        private Shader CircleFinishShader;

        /// <summary> Just a full-size rectangle, with only XY info. </summary>
        private static readonly float[] RingShaderGeometry = new float[] // {[X,Y]} x 6
        { // X   Y 
            -1,  1, // Top-Left
            -1, -1, // Bottom-Left
             1, -1, // Bottom-Right
             1, -1, // Bottom-Right
             1,  1, // Top-Right
            -1,  1  // Top-Left
        };

        /// <summary> Size of <see cref="BufferGeometrySmall"/> in each direction, relative to full-window. </summary>
        private const float Nudge = 0.98F;

        /// <summary> This is used to render the buffers into each other, to make them ever so slightly smaller, creating the infinity effect. </summary>
        private static readonly float[] BufferGeometrySmall = new float[] // {[X,Y][U,V]} x 6
        { //   X       Y    U  V
            -Nudge,  Nudge, 0, 1, // Top-Left
            -Nudge, -Nudge, 0, 0, // Bottom-Left
             Nudge, -Nudge, 1, 0, // Bottom-Right
             Nudge, -Nudge, 1, 0, // Bottom-Right
             Nudge,  Nudge, 1, 1, // Top-Right
            -Nudge,  Nudge, 0, 1  // Top-Left
        };

        /// <summary> Holds data for drawing the ring in CircleShader. </summary>
        /// <remarks> Used only in infinity mode. </remarks>
        private int VertexBufferHandle, VertexArrayHandle;

        /// <summary> Holds data for drawing the framebuffers in HistoryShader. </summary>
        /// <remarks> Used only in infinity mode. </remarks>
        private int VertexBufferSmallHandle, VertexArraySmallHandle;

        /// <summary> Holds uniform locations for CircleShader. </summary>
        /// <remarks> Used only in infinity mode. </remarks>
        private int LocationColours, LocationStarts, LocationResolution, LocationAdvance;

        /// <summary> Holds unifrom locations for CircleFinishShader. </summary>
        private int LocationColoursFinish, LocationStartsFinish, LocationResolutionFinish, LocationAdvanceFinish;

        /// <summary> Whether this output is ready to accept data and draw. </summary>
        private bool SetupDone = false;

        /// <summary> The current window and framebuffer resolution. (Width, Height) </summary>
        private Vector2 Resolution = new Vector2(600, 600);

        /// <summary> The framebuffers rendering to each other to achieve the infinity effect. </summary>
        /// <remarks> Used only in infinity mode. </remarks>
        FrameBuffer BufferA, BufferB;

        /// <summary> Which framebuffer needs to be drawn into this frame. Swaps with every frame in infinity mode. </summary>
        /// <remarks> Used only in infinity mode. </remarks>
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
            if (!this.SetupDone) { return; } // Needed as Dispatch() is called on a thread, possibly before we are done setting up.

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
                else // Clear out the colours and lengths for notes not present.
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
            
            if (this.IsInfinity)
            {
                // Bind the current buffer to draw into.
                if (this.CurrentFB) { BufferA.Bind(); }
                else { BufferB.Bind(); }

                // Render the ring into the buffer.
                this.CircleShader.Use();
                GL.BindVertexArray(this.VertexArrayHandle);
                GL.Uniform1(this.LocationStarts, 12, this.Starts);
                GL.Uniform1(this.LocationColours, 36, this.Colours);
                GL.Uniform1(this.LocationAdvance, this.Advance);
                GL.DrawArrays(PrimitiveType.Triangles, 0, RingShaderGeometry.Length / 2);

                // Bind the opposite buffer, and use the one now containing the ring as a texture.
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

                // Draw the buffer containing the newest ring into the other, but slightly smaller to push it in.
                this.HistoryShader.Use();
                GL.BindVertexArray(this.VertexArraySmallHandle);
                GL.DrawArrays(PrimitiveType.Triangles, 0, BufferGeometrySmall.Length / 4);

                // Go back to the default buffer (window), and render the new composite buffer.
                if (this.CurrentFB) { BufferB.Unbind(); }
                else { BufferA.Unbind(); }
                GL.BindVertexArray(this.VertexArraySmallHandle);
                GL.DrawArrays(PrimitiveType.Triangles, 0, BufferGeometrySmall.Length / 4);

                // Switch buffers next time to keep going back and forth.
                this.CurrentFB = !this.CurrentFB;
            }

            // Whether or not we are using infinity mode, we want the newest ring to be rendered on top with antialiasing.
            this.CircleFinishShader.Use();
            GL.BindVertexArray(this.VertexArrayHandle);
            GL.Uniform1(this.LocationStartsFinish, 12, this.Starts);
            GL.Uniform1(this.LocationColoursFinish, 36, this.Colours);
            GL.Uniform1(this.LocationAdvanceFinish, this.Advance);
            GL.DrawArrays(PrimitiveType.Triangles, 0, RingShaderGeometry.Length / 2);

            GL.Disable(EnableCap.Blend);
        }

        public void Resize(int width, int height)
        {
            this.Resolution = new Vector2(width, height);

            if(!this.SetupDone) { return; }
            this.CircleShader.Use();
            GL.Uniform2(this.LocationResolution, ref this.Resolution);

            this.CircleFinishShader.Use();
            GL.Uniform2(this.LocationResolutionFinish, ref this.Resolution);

            this.BufferA.Resize(width, height);
            this.BufferB.Resize(width, height);
        }

        public void ApplyConfig(Dictionary<string, object> options)
        {
            Log.Info("Reading config for SmoothCircle.");// + this.Name + "\".");
            this.IsInfinity = ConfigTools.CheckBool(options, "IsInfinity", false, true);

            ConfigTools.WarnAboutRemainder(options, typeof(IDisplayMode));
        }

        public void Load()
        {
            // Set options
            GL.ClearColor(0.0F, 0.0F, 0.0F, 1.0F);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.DepthFunc(DepthFunction.Lequal);

            // Create objects
            this.CircleShader = new Shader("SmoothCircle.vert", "SmoothCircle.frag");
            this.HistoryShader = new Shader("Passthrough2Textured.vert", "Passthrough2Textured.frag");
            this.CircleFinishShader = new Shader("SmoothCircle.vert", "SmoothCircleFinish.frag");

            this.BufferA = new FrameBuffer(this.HostWindow.Width, this.HostWindow.Height);
            this.BufferB = new FrameBuffer(this.HostWindow.Width, this.HostWindow.Height);

            this.CircleShader.Use();
            this.VertexBufferHandle = GL.GenBuffer();
            this.VertexArrayHandle = GL.GenVertexArray();

            if (this.IsInfinity)
            {
                this.VertexBufferSmallHandle = GL.GenBuffer();
                this.VertexArraySmallHandle = GL.GenVertexArray();
            }

            // Get uniform locations
            if (this.IsInfinity)
            {
                this.LocationColours = this.CircleShader.GetUniformLocation("Colours");
                this.LocationStarts = this.CircleShader.GetUniformLocation("Starts");
                this.LocationResolution = this.CircleShader.GetUniformLocation("Resolution");
                this.LocationAdvance = this.CircleShader.GetUniformLocation("Advance");
                GL.Uniform2(this.LocationResolution, ref this.Resolution);
            }

            this.CircleFinishShader.Use();
            this.LocationColoursFinish = this.CircleFinishShader.GetUniformLocation("Colours");
            this.LocationStartsFinish = this.CircleFinishShader.GetUniformLocation("Starts");
            this.LocationResolutionFinish = this.CircleFinishShader.GetUniformLocation("Resolution");
            this.LocationAdvanceFinish = this.CircleFinishShader.GetUniformLocation("Advance");
            GL.Uniform2(this.LocationResolutionFinish, ref this.Resolution);

            // Prepare and upload vertex data.
            GL.BindVertexArray(this.VertexArraySmallHandle);
            GL.BindBuffer(BufferTarget.ArrayBuffer, this.VertexBufferSmallHandle);
            GL.BufferData(BufferTarget.ArrayBuffer, BufferGeometrySmall.Length * sizeof(float), BufferGeometrySmall, BufferUsageHint.DynamicDraw);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));
            GL.EnableVertexAttribArray(1);

            GL.BindVertexArray(this.VertexArrayHandle);
            GL.BindBuffer(BufferTarget.ArrayBuffer, this.VertexBufferHandle);
            GL.BufferData(BufferTarget.ArrayBuffer, RingShaderGeometry.Length * sizeof(float), RingShaderGeometry, BufferUsageHint.DynamicDraw);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            this.SetupDone = true;
        }

        public void Close()
        {
            if (!this.SetupDone) { return; }
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.DeleteBuffer(this.VertexBufferHandle);
            GL.DeleteBuffer(this.VertexArraySmallHandle);
            this.CircleShader.Dispose();
            this.HistoryShader.Dispose();
            this.CircleFinishShader.Dispose();
        }

        /// <summary> Only <see cref="IContinuous1D"/> visualizers are accepted. </summary>
        public bool SupportsFormat(IVisualizerFormat format) => format is IContinuous1D;
    }
}
