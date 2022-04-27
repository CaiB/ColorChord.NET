using ColorChord.NET.Config;
using ColorChord.NET.Visualizers;
using ColorChord.NET.Visualizers.Formats;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ColorChord.NET.Outputs.Display
{
    public class CloudChamber : IDisplayMode, IConfigurableAttr
    {
        private readonly DisplayOpenGL HostWindow;
        private readonly IContinuous1D DataSource;

        private Shader? RingShader, ArcShader, ShadowShader;

        private int LocationResolutionRing;
        private int LocationAdvanceRing;

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

        private int VertexBufferHandleRing, VertexArrayHandleRing;

        [ConfigFloat("PeakWidth", 0F, 10F, 0.1F)]
        private float RotationSpeed = 0.1F;

        private float Advance = 0F;

        /// <summary> The current window and framebuffer resolution. (Width, Height) </summary>
        private Vector2 Resolution = new(600, 600);

        /// <summary> Whether this output is ready to accept data and draw. </summary>
        private bool SetupDone = false;

        public CloudChamber(DisplayOpenGL parent, IVisualizer visualizer, Dictionary<string, object> config)
        {
            if (visualizer is not IContinuous1D)
            {
                Log.Error("CloudChamber cannot use the provided visualizer, as it does not output 1D continuous data.");
                throw new InvalidOperationException("Incompatible visualizer. Must implement IContinuous1D.");
            }
            this.HostWindow = parent;
            Configurer.Configure(this, config);
            this.DataSource = (IContinuous1D)visualizer;
        }

        public bool SupportsFormat(IVisualizerFormat format) => format is IContinuous1D;

        public void Load()
        {
            // Set options
            GL.ClearColor(0.0F, 0.0F, 0.0F, 1.0F);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.DepthFunc(DepthFunction.Lequal);
            GL.Enable(EnableCap.Blend);

            // Create objects
            this.RingShader = new("Passthrough2.vert", "CloudChamberRing.frag");
            this.RingShader.Use();
            this.VertexBufferHandleRing = GL.GenBuffer();
            this.VertexArrayHandleRing = GL.GenVertexArray();

            // Prepare uniforms
            this.LocationResolutionRing = this.RingShader.GetUniformLocation("Resolution");
            this.LocationAdvanceRing = this.RingShader.GetUniformLocation("Advance");
            GL.Uniform2(this.LocationResolutionRing, ref this.Resolution);

            // Prepare and upload vertex data
            GL.BindVertexArray(this.VertexArrayHandleRing);
            GL.BindBuffer(BufferTarget.ArrayBuffer, this.VertexBufferHandleRing);
            GL.BufferData(BufferTarget.ArrayBuffer, RingShaderGeometry.Length * sizeof(float), RingShaderGeometry, BufferUsageHint.DynamicDraw);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);

            this.SetupDone = true;
        }

        public void Dispatch()
        {

        }

        public void Render()
        {
            if (!this.SetupDone) { return; }

            this.RingShader!.Use();
            GL.BindVertexArray(this.VertexArrayHandleRing);
            GL.Uniform1(this.LocationAdvanceRing, this.Advance);
            GL.DrawArrays(PrimitiveType.Triangles, 0, RingShaderGeometry.Length / 2);
            this.Advance += 0.002F;
        }

        public void Resize(int width, int height)
        {
            this.Resolution = new Vector2(width, height);

            if (!this.SetupDone) { return; }
            this.RingShader!.Use();
            GL.Uniform2(this.LocationResolutionRing, ref this.Resolution);
        }

        public void Close()
        {

        }
    }
}
