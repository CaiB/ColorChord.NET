using ColorChord.NET.API.Config;
using ColorChord.NET.API.Outputs.Display;
using ColorChord.NET.API.Visualizers;
using ColorChord.NET.API.Visualizers.Formats;
using ColorChord.NET.Config;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;

namespace ColorChord.NET.Outputs.Display
{
    public class CloudChamber : IDisplayMode, IConfigurableAttr
    {
        private readonly DisplayOpenGL HostWindow;
        private readonly IContinuous1D DataSource;

        private Shader? RingShader, TrailShader, ShadowShader;

        private int LocationResolutionRing, LocationResolutionTrail;
        private int LocationAdvanceRing, LocationAdvanceTrail;

        private int LocationLocationsTrail, LocationAmplitudesTrail;

        private float[] RingShaderGeometry = new float[] // {[X,Y]} x 6
        { // X   Y 
            -1,  1, -1,  1, // Top-Left
            -1, -1, -1, -1, // Bottom-Left
             1, -1,  1, -1, // Bottom-Right
             1, -1,  1, -1, // Bottom-Right
             1,  1,  1,  1, // Top-Right
            -1,  1, -1,  1  // Top-Left
        };

        private int VertexBufferHandleRing, VertexArrayHandleRing;

        [ConfigFloat("RotationSpeed", 0F, 10F, 0.1F)]
        private float RotationSpeed = 0.1F;

        private float Advance = 0F;

        private const int NOTES = 12;
        private readonly float[] Locations, Amplitudes;

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
            this.Locations = new float[NOTES];
            this.Amplitudes = new float[NOTES];
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
            this.RingShader = new("Passthrough2Textured.vert", "CloudChamberRing.frag");
            this.TrailShader = new("Passthrough2Textured.vert", "CloudChamberTrails.frag");
            this.TrailShader.Use();
            this.VertexBufferHandleRing = GL.GenBuffer();
            this.VertexArrayHandleRing = GL.GenVertexArray();

            // Prepare uniforms
            this.RingShader.Use();
            this.LocationResolutionRing = this.RingShader.GetUniformLocation("Resolution");
            this.LocationAdvanceRing = this.RingShader.GetUniformLocation("Advance");
            GL.Uniform2(this.LocationResolutionRing, ref this.Resolution);

            this.TrailShader.Use();
            this.LocationResolutionTrail = this.TrailShader.GetUniformLocation("Resolution");
            this.LocationAdvanceTrail = this.TrailShader.GetUniformLocation("Advance");
            this.LocationAmplitudesTrail = this.TrailShader.GetUniformLocation("Amplitudes");
            this.LocationLocationsTrail = this.TrailShader.GetUniformLocation("Locations");
            //GL.Uniform2(this.LocationResolutionTrail, ref this.Resolution);

            // Prepare and upload vertex data
            this.TrailShader.Use();
            GL.BindVertexArray(this.VertexArrayHandleRing);
            GL.BindBuffer(BufferTarget.ArrayBuffer, this.VertexBufferHandleRing);
            GL.BufferData(BufferTarget.ArrayBuffer, RingShaderGeometry.Length * sizeof(float), RingShaderGeometry, BufferUsageHint.DynamicDraw);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));
            GL.EnableVertexAttribArray(1);

            this.SetupDone = true;
        }

        public void Dispatch()
        {
            ContinuousDataUnit[] Data = this.DataSource.GetDataContinuous();
            int Count = this.DataSource.GetCountContinuous();

            for (int i = 0; i < NOTES; i++)
            {
                this.Locations[i] = Data[i].Colour;
                this.Amplitudes[i] = (i > Count) ? 0 : Data[i].Size;
            }
        }

        public void Render()
        {
            if (!this.SetupDone) { return; }

            //this.RingShader!.Use();
            this.TrailShader!.Use();

            GL.Uniform1(this.LocationLocationsTrail, NOTES, Locations);
            GL.Uniform1(this.LocationAmplitudesTrail, NOTES, Amplitudes);

            GL.BindVertexArray(this.VertexArrayHandleRing);
            //GL.Uniform1(this.LocationAdvanceRing, this.Advance);
            GL.DrawArrays(PrimitiveType.Triangles, 0, RingShaderGeometry.Length / 2);
            //this.Advance += 0.002F;
        }

        public void Resize(int width, int height)
        {
            int MinLength = Math.Min(width, height);
            this.Resolution = new(MinLength, MinLength);

            if (!this.SetupDone) { return; }
            this.RingShader!.Use();
            GL.Uniform2(this.LocationResolutionRing, ref this.Resolution);

            this.TrailShader!.Use();
            //GL.Uniform2(this.LocationResolutionTrail, ref this.Resolution);

            this.RingShaderGeometry = GenGeometry(width, height);
            GL.BindVertexArray(this.VertexArrayHandleRing);
            GL.BindBuffer(BufferTarget.ArrayBuffer, this.VertexBufferHandleRing);
            GL.BufferData(BufferTarget.ArrayBuffer, this.RingShaderGeometry.Length * sizeof(float), this.RingShaderGeometry, BufferUsageHint.DynamicDraw);
        }

        private static float[] GenGeometry(int width, int height)
        {
            float Y = (float)width / Math.Max(width, height);
            float X = (float)height / Math.Max(width, height);
            return new[]
            { // X   Y 
                -X,  Y, -1,  1, // Top-Left
                -X, -Y, -1, -1, // Bottom-Left
                 X, -Y,  1, -1, // Bottom-Right
                 X, -Y,  1, -1, // Bottom-Right
                 X,  Y,  1,  1, // Top-Right
                -X,  Y, -1,  1  // Top-Left
            };
        }

        public void Close()
        {

        }
    }
}
