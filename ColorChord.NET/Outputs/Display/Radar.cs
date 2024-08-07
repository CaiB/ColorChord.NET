using ColorChord.NET.API;
using ColorChord.NET.API.Config;
using ColorChord.NET.API.Outputs.Display;
using ColorChord.NET.API.Visualizers;
using ColorChord.NET.API.Visualizers.Formats;
using ColorChord.NET.Config;
using ColorChord.NET.NoteFinder;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;

namespace ColorChord.NET.Outputs.Display
{
    public class Radar : IDisplayMode, IConfigurableAttr
    {
        /// <summary> How many spokes comprise one radar rotation. More means longer history. </summary>
        [ConfigInt("Spokes", 1, 10000, 100)]
        private readonly int Spokes = 150;

        /// <summary> How many pieces comprise one spoke. Equal to the visualizer's data count. </summary>
        private int RadiusResolution = 8;
        // TODO: Handle this changing during runtime.

        /// <summary> Whether to tilt the view and show spikes for beats. </summary>
        [ConfigBool("Is3D", false)]
        private readonly bool Use3DView = false;

        [ConfigFloat("FalloffAfter", 0.0F, 1.0F, 0.9F)]
        private readonly float FalloffAfter = 0.9F;

        /// <summary> How many floats comprise one vertex of data sent to the GPU. </summary>
        private const byte DATA_PER_VERTEX = 9;

        /// <summary> The window we are running in. </summary>
        private readonly DisplayOpenGL HostWindow;

        private readonly BaseNoteFinder BaseNF; // TODO: Generalize this

        /// <summary> Where we are getting the colour data from. </summary>
        private readonly IDiscrete1D DataSource;

        /// <summary> Shader for rending the radar. </summary>
        private Shader? Shader;

        /// <summary> Storage for new colour data to be uploaded to the GPU. </summary>
        private byte[]? TextureData;

        /// <summary> The ID of the texture to store colour data in. </summary>
        private int LocationTexture;

        /// <summary> How many new lines of texture are available to be sent to the GPU. </summary>
        private uint NewLines = 0;

        /// <summary> Perspective projection </summary>
        private Matrix4 Projection;
        private int LocationProjection;

        /// <summary> Location for storing the vertex data. </summary>
        private int VertexBufferHandle, VertexArrayHandle;

        /// <summary> The vertex data to make the basic shape. </summary>
        private float[]? VertexData;

        /// <summary> Where in the texture data the front of the sweep is currently located. </summary>
        private ushort RenderIndex = 0;

        /// <summary> Location of the depth offset uniform (for storing <see cref="RenderIndex"/>). </summary>
        private int LocationFrontOffset;

        /// <summary> Whether we are ready to send data & render frames. </summary>
        private bool SetupDone = false;

        /// <summary> Whether there is new data to be uploaded to the texture. </summary>
        private bool NewData = false;

        public Radar(DisplayOpenGL parent, IVisualizer visualizer, Dictionary<string, object> config)
        {
            if (visualizer is not IDiscrete1D)
            {
                Log.Error("Radar cannot use the provided visualizer, as it does not output 1D discrete data.");
                throw new InvalidOperationException("Incompatible visualizer. Must implement IDiscrete1D.");
            }
            if (parent.NoteFinder is not BaseNoteFinder BaseNoteFinderInst) { throw new Exception($"{nameof(Radar)} currently only supports {nameof(BaseNoteFinder)}."); }
            this.BaseNF = BaseNoteFinderInst;
            Configurer.Configure(this, config);
            this.HostWindow = parent;
            this.DataSource = (IDiscrete1D)visualizer;
            this.RadiusResolution = this.DataSource.GetCountDiscrete();
        }

        public void Load()
        {
            this.VertexData = new float[this.Spokes * this.RadiusResolution * 6 * DATA_PER_VERTEX];
            this.TextureData = new byte[this.Spokes * this.RadiusResolution * 4];

            int DataIndex = 0;
            void AddPoint(Vector3 point, int spoke, int seg, bool isStart, Vector3 normal)
            {
                this.VertexData[DataIndex++] = point.X;
                this.VertexData[DataIndex++] = point.Y;
                this.VertexData[DataIndex++] = point.Z;
                this.VertexData[DataIndex++] = ((float)seg / this.RadiusResolution) + (0.5F / this.RadiusResolution);
                this.VertexData[DataIndex++] = ((float)spoke / this.Spokes) + (0.5F / this.Spokes);
                this.VertexData[DataIndex++] = isStart ? 0F : 1F;
                this.VertexData[DataIndex++] = normal.X;
                this.VertexData[DataIndex++] = normal.Y;
                this.VertexData[DataIndex++] = normal.Z;
            }

            GL.ClearColor(0.0F, 0.0F, 0.0F, 1.0F);
            GL.Enable(EnableCap.DepthTest);

            this.Shader = new Shader("Radar.vert", "Radar.frag");
            this.Shader.Use();

            GL.BindTexture(TextureTarget.Texture2D, 0);
            this.LocationTexture = GL.GenTexture();
            GL.Uniform1(this.Shader.GetUniformLocation("tex"), 0);

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, this.LocationTexture);

            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, this.RadiusResolution, this.Spokes, 0, PixelFormat.Rgba, PixelType.UnsignedByte, this.TextureData);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToBorder);

            this.Projection = Matrix4.CreatePerspectiveFieldOfView(MathF.PI / 2, 1, 0.01F, 10F);
            this.LocationProjection = this.Shader.GetUniformLocation("projection");
            GL.UniformMatrix4(this.LocationProjection, true, ref this.Projection);

            this.LocationFrontOffset = this.Shader.GetUniformLocation("frontOffset");

            GL.Uniform1(this.Shader.GetUniformLocation("falloffAfter"), this.FalloffAfter); // only set once

            this.VertexBufferHandle = GL.GenBuffer();
            this.VertexArrayHandle = GL.GenVertexArray();

            Matrix3 ViewRotation = this.Use3DView ? Matrix3.CreateRotationX(MathF.PI * -0.3F) : Matrix3.Identity;
            Vector3 ViewOffset = this.Use3DView ? ((Vector3.UnitZ * -1.3F) + (Vector3.UnitY * 0.3F)) : (Vector3.UnitZ * -1);
            
            Vector3 NormalVec = new(0, 2, 0);

            // Generate geometry
            for(int Spoke = 0; Spoke < this.Spokes; Spoke++)
            {
                for(int Seg = 0; Seg < RadiusResolution; Seg++)
                {
                    float RotStart = MathF.PI * 2 * Spoke / this.Spokes;
                    float RotEnd = MathF.PI * 2 * (Spoke + 1) / this.Spokes;

                    float RadIn = ((float)Seg / RadiusResolution * 0.9F) + 0.1F;
                    float RadOut = ((float)(Seg + 1) / RadiusResolution * 0.9F) + 0.1F;

                    float StartX = MathF.Cos(RotStart);
                    float StartY = MathF.Sin(RotStart);
                    float EndX = MathF.Cos(RotEnd);
                    float EndY = MathF.Sin(RotEnd);
                    const float Z = 0F;

                    // RadIn/RadOut is in range [0..1] premade for linear interpolation
                    Vector3 InnerNormal = NormalVec * RadIn;
                    Vector3 OuterNormal = NormalVec * RadOut;

                    AddPoint((new Vector3(StartX * RadIn, StartY * RadIn, Z) * ViewRotation) + ViewOffset, Spoke, Seg, true, InnerNormal); // In bottom
                    AddPoint((new Vector3(StartX * RadOut, StartY * RadOut, Z) * ViewRotation) + ViewOffset, Spoke, Seg, true, OuterNormal); // Out bottom
                    AddPoint((new Vector3(EndX * RadOut, EndY * RadOut, Z) * ViewRotation) + ViewOffset, Spoke, Seg, false, OuterNormal); // Out top

                    AddPoint((new Vector3(StartX * RadIn, StartY * RadIn, Z) * ViewRotation) + ViewOffset, Spoke, Seg, true, InnerNormal); // In bottom
                    AddPoint((new Vector3(EndX * RadOut, EndY * RadOut, Z) * ViewRotation) + ViewOffset, Spoke, Seg, false, OuterNormal); // Out top
                    AddPoint((new Vector3(EndX * RadIn, EndY * RadIn, Z) * ViewRotation) + ViewOffset, Spoke, Seg, false, InnerNormal); // In top
                }
            }

            GL.BindVertexArray(this.VertexArrayHandle);
            GL.BindBuffer(BufferTarget.ArrayBuffer, this.VertexBufferHandle);
            GL.BufferData(BufferTarget.ArrayBuffer, VertexData.Length * sizeof(float), VertexData, BufferUsageHint.DynamicDraw);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, DATA_PER_VERTEX * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, DATA_PER_VERTEX * sizeof(float), 3 * sizeof(float));
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, DATA_PER_VERTEX * sizeof(float), 5 * sizeof(float));
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(3, 3, VertexAttribPointerType.Float, false, DATA_PER_VERTEX * sizeof(float), 6 * sizeof(float));
            GL.EnableVertexAttribArray(3);

            this.SetupDone = true;
        }

        private float HistoricalLFData;
        private float CurrentFLData;

        public void Dispatch()
        {
            if (!this.SetupDone) { return; }
            if (this.NewLines == this.Spokes) { return; }

            // TODO: MOVE THIS OUT INTO COMMON-USE OBJECT (Visualizer?)

            // Get newest low-frequency data, and reset the accumulator.
            float LowFreqData = this.BaseNF.LastLowFreqSum / (1 + this.BaseNF.LastLowFreqCount);
            this.BaseNF.LastLowFreqSum = 0;
            this.BaseNF.LastLowFreqCount = 0;

            // Strong IIR for keeping an average of the low-frequency content to compare against for finding the beats
            const float IIR_HIST = 0.9F;
            this.HistoricalLFData = (IIR_HIST * this.HistoricalLFData) + ((1F - IIR_HIST) * LowFreqData);

            // The current low-frequency content, filtered lightly, then normalized against the recent average quantity.
            const float IIR_REAL = 0.2F;
            LowFreqData /= this.HistoricalLFData; // Normalize? Maybe

            this.CurrentFLData = (IIR_REAL * CurrentFLData) + ((1F - IIR_REAL) * LowFreqData);
            LowFreqData = this.CurrentFLData;

            // Some non-linearity to make the beats more apparent
            LowFreqData = MathF.Pow(LowFreqData, 5);

            lock (this.TextureData!)
            {
                for (int Seg = 0; Seg < this.RadiusResolution; Seg++)
                {
                    this.TextureData[(this.NewLines * (4 * this.RadiusResolution)) + (Seg * 4) + 0] = (byte)(this.DataSource.GetDataDiscrete()[Seg] >> 16); // R
                    this.TextureData[(this.NewLines * (4 * this.RadiusResolution)) + (Seg * 4) + 1] = (byte)(this.DataSource.GetDataDiscrete()[Seg] >> 8); // G
                    this.TextureData[(this.NewLines * (4 * this.RadiusResolution)) + (Seg * 4) + 2] = (byte)(this.DataSource.GetDataDiscrete()[Seg]); // B
                    this.TextureData[(this.NewLines * (4 * this.RadiusResolution)) + (Seg * 4) + 3] = this.Use3DView ? (byte)(LowFreqData * 20) : (byte)0; // A
                }
                this.NewLines++;
                this.NewData = true;
            }
        }

        public void Render()
        {
            if (!this.SetupDone) { return; }
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            this.Shader!.Use();

            if (this.NewData)
            {
                lock (this.TextureData!)
                {
                    if (this.RenderIndex + this.NewLines > this.Spokes) // We have more data than remaining space. Split the data into 2, and write to the end & beginning of the texture.
                    {
                        int LinesBeforeWrap = this.Spokes - this.RenderIndex;
                        int LinesAfterWrap = (int)(this.NewLines - LinesBeforeWrap);
                        byte[] TextureDataAfterWrap = new byte[LinesAfterWrap * this.RadiusResolution * 4];
                        Array.Copy(this.TextureData, LinesBeforeWrap * this.RadiusResolution * 4, TextureDataAfterWrap, 0, LinesAfterWrap * this.RadiusResolution * 4);
                        GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, this.RenderIndex, this.RadiusResolution, LinesBeforeWrap, PixelFormat.Rgba, PixelType.UnsignedByte, this.TextureData); // Before (at end of texture)
                        GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, this.RadiusResolution, LinesAfterWrap, PixelFormat.Rgba, PixelType.UnsignedByte, TextureDataAfterWrap); // After (at start of texture)
                    }
                    else { GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, this.RenderIndex, this.RadiusResolution, (int)this.NewLines, PixelFormat.Rgba, PixelType.UnsignedByte, this.TextureData); }
                    this.RenderIndex = (ushort)((this.RenderIndex + this.NewLines) % this.Spokes);
                    this.NewData = false;
                    this.NewLines = 0;
                }
                GL.Uniform1(this.LocationFrontOffset, (float)this.RenderIndex / this.Spokes);
            }

            GL.BindVertexArray(this.VertexArrayHandle);
            GL.DrawArrays(PrimitiveType.Triangles, 0, this.VertexData!.Length / 3);
        }

        public void Resize(int width, int height)
        {
            if (!this.SetupDone) { return; }

            this.Shader!.Use();
            this.Projection = Matrix4.CreatePerspectiveFieldOfView(MathF.PI / 2, (float)this.HostWindow.Width / this.HostWindow.Height, 0.01F, 10F);
            GL.UniformMatrix4(this.LocationProjection, true, ref this.Projection);
        }

        public void Close()
        {
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.DeleteBuffer(this.VertexBufferHandle);
            this.Shader?.Dispose();
        }

        public bool SupportsFormat(IVisualizerFormat format) => format is IDiscrete1D;

    }
}
