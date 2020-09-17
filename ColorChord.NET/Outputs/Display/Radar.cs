using ColorChord.NET.Visualizers;
using ColorChord.NET.Visualizers.Formats;
using OpenTK;
using OpenTK.Graphics.OpenGL4;
using System;
using System.Collections.Generic;
using System.Threading;

namespace ColorChord.NET.Outputs.Display
{
    public class Radar : IDisplayMode, IConfigurable
    {
        /// <summary> How many spokes comprise one radar rotation. More means longer history. </summary>
        private int Spokes = 150;

        /// <summary> How many pieces comprise one spoke. Equal to the visualizer's data count. </summary>
        private int RadiusResolution = 8;
        // TODO: Handle this changing during runtime.

        /// <summary> Whether to tilt the view and show spikes for beats. </summary>
        private bool Use3DView = false;

        /// <summary> How many floats comprise one vertex of data sent to the GPU. </summary>
        private const byte DATA_PER_VERTEX = 9;

        /// <summary> The window we are running in. </summary>
        private readonly DisplayOpenGL HostWindow;

        /// <summary> Where we are getting the colour data from. </summary>
        private readonly IDiscrete1D DataSource;

        /// <summary> Shader for rending the radar. </summary>
        private Shader Shader;

        /// <summary> Storage for new colour data to be uploaded to the GPU. </summary>
        private byte[] TextureData;

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
        private float[] VertexData;

        /// <summary> Where in the texture data the front of the sweep is currently located. </summary>
        private ushort RenderIndex = 0;

        /// <summary> Whether we are ready to send data & render frames. </summary>
        private bool SetupDone = false;

        /// <summary> Whether there is new data to be uploaded to the texture. </summary>
        private bool NewData = false;

        public Radar(DisplayOpenGL parent, IVisualizer visualizer)
        {
            if (!(visualizer is IDiscrete1D))
            {
                Log.Error("Radar cannot use the provided visualizer, as it does not output 1D discrete data.");
                throw new InvalidOperationException("Incompatible visualizer. Must implement IDiscrete1D.");
            }
            this.HostWindow = parent;
            this.DataSource = (IDiscrete1D)visualizer;
            this.RadiusResolution = this.DataSource.GetCountDiscrete();
        }

        public void ApplyConfig(Dictionary<string, object> options)
        {
            Log.Info("Reading config for Radar.");
            this.Spokes = ConfigTools.CheckInt(options, "Spokes", 1, 10000, 100, true);
            this.Use3DView = ConfigTools.CheckBool(options, "Is3D", false, true);

            ConfigTools.WarnAboutRemainder(options, typeof(IDisplayMode));
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

            this.Projection = Matrix4.CreatePerspectiveFieldOfView((float)(Math.PI / 2), 1, 0.01F, 10F);
            this.LocationProjection = this.Shader.GetUniformLocation("projection");
            GL.UniformMatrix4(this.LocationProjection, true, ref this.Projection);

            this.VertexBufferHandle = GL.GenBuffer();
            this.VertexArrayHandle = GL.GenVertexArray();

            Matrix3 ViewRotation = this.Use3DView ? Matrix3.CreateRotationX((float)Math.PI * -0.3F) : Matrix3.Identity;
            Vector3 ViewOffset = this.Use3DView ? ((Vector3.UnitZ * -1.3F) + (Vector3.UnitY * 0.3F)) : (Vector3.UnitZ * -1);
            
            Vector3 NormalVec = new Vector3(0, 2, 0);

            // Generate geometry
            for(int Spoke = 0; Spoke < this.Spokes; Spoke++)
            {
                for(int Seg = 0; Seg < RadiusResolution; Seg++)
                {
                    double RotStart = Math.PI * 2 * Spoke / this.Spokes;
                    double RotEnd = Math.PI * 2 * (Spoke + 1) / this.Spokes;

                    float RadIn = (float)Seg / RadiusResolution;
                    float RadOut = (float)(Seg + 1) / RadiusResolution;

                    float StartX = (float)Math.Cos(RotStart);
                    float StartY = (float)Math.Sin(RotStart);
                    float EndX = (float)Math.Cos(RotEnd);
                    float EndY = (float)Math.Sin(RotEnd);
                    const float Z = 0f;

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
            float LowFreqData = BaseNoteFinder.LastLowFreqSum / (1 + BaseNoteFinder.LastLowFreqCount);
            BaseNoteFinder.LastLowFreqSum = 0;
            BaseNoteFinder.LastLowFreqCount = 0;

            // Strong IIR for keeping an average of the low-frequency content to compare against for finding the beats
            const float IIR_HIST = 0.9F;
            this.HistoricalLFData = (IIR_HIST * this.HistoricalLFData) + ((1F - IIR_HIST) * LowFreqData);

            // The current low-frequency content, filtered lightly, then normalized against the recent average quantity.
            const float IIR_REAL = 0.2F;
            LowFreqData /= this.HistoricalLFData; // Normalize? Maybe

            this.CurrentFLData = (IIR_REAL * CurrentFLData) + ((1F - IIR_REAL) * LowFreqData);
            LowFreqData = this.CurrentFLData;

            // Some non-linearity to make the beats more apparent
            LowFreqData = (float)Math.Pow(LowFreqData, 5);

            lock (this.TextureData)
            {
                for (int Seg = 0; Seg < this.RadiusResolution; Seg++)
                {
                    this.TextureData[(this.NewLines * (4 * this.RadiusResolution)) + (Seg * 4) + 0] = (byte)(this.DataSource.GetDataDiscrete()[Seg] >> 16); // R
                    this.TextureData[(this.NewLines * (4 * this.RadiusResolution)) + (Seg * 4) + 1] = (byte)(this.DataSource.GetDataDiscrete()[Seg] >> 8); // G
                    this.TextureData[(this.NewLines * (4 * this.RadiusResolution)) + (Seg * 4) + 2] = (byte)(this.DataSource.GetDataDiscrete()[Seg]); // B
                    this.TextureData[(this.NewLines * (4 * this.RadiusResolution)) + (Seg * 4) + 3] = (byte)(LowFreqData * 20); // A
                }
                this.NewLines++;
                this.NewData = true;
            }
        }

        public void Render()
        {
            if (!this.SetupDone) { return; }
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            this.Shader.Use();

            if (this.NewData)
            {
                lock (this.TextureData)
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
                    //GL.Uniform1(this.LocationDepthOffset, (float)(this.RenderIndex) / this.Spokes);
                }
            }

            GL.BindVertexArray(this.VertexArrayHandle);
            GL.DrawArrays(PrimitiveType.Triangles, 0, this.VertexData.Length / 3);
        }

        public void Resize(int width, int height)
        {
            if (!this.SetupDone) { return; }

            this.Shader.Use();
            this.Projection = Matrix4.CreatePerspectiveFieldOfView((float)(Math.PI / 2), (float)this.HostWindow.Width / this.HostWindow.Height, 0.01F, 10F);
            GL.UniformMatrix4(this.LocationProjection, true, ref this.Projection);
        }

        public void Close()
        {
            if (!this.SetupDone) { return; }
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.DeleteBuffer(this.VertexBufferHandle);
            this.Shader.Dispose();
        }

        public bool SupportsFormat(IVisualizerFormat format) => format is IDiscrete1D;

    }
}
