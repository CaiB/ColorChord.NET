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
    public class ColorRibbon : IDisplayMode, IConfigurableAttr
    {
        private readonly DisplayOpenGL HostWindow;
        private readonly IDiscrete1D DataSource;

        private Shader? RibbonShader, StarShader;

        private int LocationProjection, LocationView, LocationTextureAdvance;
        private int TextureHandle, VertexArrayHandle, VertexBufferHandle;

        private int LocationStarProjection, LocationStarView, LocationStarModel;
        private int StarVertexArrayHandle, StarVertexBufferHandle, StarInstanceBufferHandle;
        private int StarTextureHandle;

        /// <summary> Whether this output is ready to accept data and draw. </summary>
        private bool SetupDone = false;

        [ConfigInt("RibbonLength", 2, 10000, 120)]
        private int RibbonLength = 120;

        [ConfigFloat("RibbonScale", 0F, 100F, 0.3F)]
        private float RibbonScale = 0.3F;

        [ConfigInt("StarCount", 0, 100000, 1000)]
        private int StarCount = 1000;

        [ConfigFloat("StarSpeed", 0F, 100F, 0.1F)]
        private float StarSpeed = 0.1F;

        [ConfigFloat("StarSize", 0F, 1F, 0.05F)]
        private float StarSize = 0.05F;

        private int RibbonWidth; // TODO: Handle updating this if the visualizer changes count

        private int NewLines = 0;

        private int LastUploadedPosition = 0;

        private uint[,] TextureData;

        private float[] AmplitudeData;
        private int AmplitudeDataIndex = 0;

        private bool NewTexData = false;

        private float TimeOffset = 0F;

        private StarInstance[] StarInstances;
        private const float STAR_WRAP_DIST = 50F;
        private const float STAR_WRAP_LOC = -5F;

        private float[] RibbonGeometry = new float[]
        { // X   Y   Z  U  V
            -1, -1,  0, 0, 0,
             1,  1,  0, 1, 1,
             1, -1,  0, 1, 0,
            -1, -1,  0, 0, 0,
            -1,  1,  0, 0, 1,
             1,  1,  0, 1, 1
        };
        private const int DATA_PER_VERTEX_RIBBON_GEO = 5;

        private float[] StarGeometry;
        private const int DATA_PER_VERTEX_STAR_GEO = 5;

        private uint[] StarTextureData;

        private readonly Vector3 RibbonCenter = new(0F, 0F, 0F);

        private float HistoricalLFData = 0;
        private float CurrentLFData = 0;
        private float SpeedBoost = 0;

        private const float AMPLITUDE_PREK_DETECT_IIR = 0.99F;
        private float RecentPeakTotalAmplitude = 0F;

        public ColorRibbon(DisplayOpenGL hostWindow, IVisualizer visualizer, Dictionary<string, object> config)
        {
            if (visualizer is not IDiscrete1D)
            {
                Log.Error("ColorRibbon cannot use the provided visualizer, as it does not output 1D discrete data.");
                throw new InvalidOperationException("Incompatible visualizer. Must implement IDiscrete1D.");
            }
            this.HostWindow = hostWindow;
            Configurer.Configure(this, config);
            this.DataSource = (IDiscrete1D)visualizer;
            this.RibbonWidth = this.DataSource.GetCountDiscrete();
            this.TextureData = new uint[this.RibbonLength, this.RibbonWidth];
            this.StarTextureData = new uint[this.RibbonWidth];
            this.AmplitudeData = new float[this.RibbonLength];
            this.RibbonGeometry = new float[this.RibbonLength * 2 * 3 * DATA_PER_VERTEX_RIBBON_GEO];
            this.StarInstances = new StarInstance[this.StarCount];

            this.StarGeometry = new float[]
            { //  X    Y    Z  U  V
                -this.StarSize, 0, -this.StarSize, 0, 0,
                 this.StarSize, 0,  this.StarSize, 1, 1,
                 this.StarSize, 0, -this.StarSize, 1, 0,
                -this.StarSize, 0, -this.StarSize, 0, 0,
                -this.StarSize, 0,  this.StarSize, 0, 1,
                 this.StarSize, 0,  this.StarSize, 1, 1
            };
            /*{ //  X      Y    Z  U  V
                -0.1F, -0.1F, 0, 0, 0,
                 0.1F,  0.1F, 0, 1, 1,
                 0.1F, -0.1F, 0, 1, 0,
                -0.1F, -0.1F, 0, 0, 0,
                -0.1F,  0.1F, 0, 0, 1,
                 0.1F,  0.1F, 0, 1, 1
            };*/
            SetupStars();
        }

        private void SetupStars()
        {
            Random Random = new();
            for (int i = 0; i < this.StarInstances.Length; i++)
            {
                float Radius = ((float)Random.NextDouble() * 20F) + 4F;
                float Angle = (float)Random.NextDouble() * MathF.PI * 2;
                float X = Radius * MathF.Cos(Angle);
                float Z = Radius * MathF.Sin(Angle);
                float Y = (float)Random.NextDouble() * -(STAR_WRAP_DIST - STAR_WRAP_LOC);

                this.StarInstances[i] = new(X, Y, Z, Random.Next(this.RibbonWidth));
            }
        }

        public void Load()
        {
            this.RibbonShader = new("ColorRibbon.vert", "ColorRibbon.frag");
            this.StarShader = new("ColorRibbonStars.vert", "ColorRibbonStars.frag");
            this.RibbonShader.Use();

            GL.ClearColor(0.0F, 0.0F, 0.0F, 1.0F);
            GL.Enable(EnableCap.DepthTest);
            GL.Disable(EnableCap.CullFace);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            this.LocationProjection = this.RibbonShader.GetUniformLocation("Projection");
            this.LocationView = this.RibbonShader.GetUniformLocation("View");
            this.LocationTextureAdvance = this.RibbonShader.GetUniformLocation("TextureAdvance");

            Matrix4 Projection = Matrix4.CreatePerspectiveFieldOfView(MathF.PI / 3F, 1, 0.01F, 100F);
            GL.UniformMatrix4(this.LocationProjection, true, ref Projection);

            GL.BindTexture(TextureTarget.Texture2D, 0);
            this.TextureHandle = GL.GenTexture();
            GL.Uniform1(this.RibbonShader.GetUniformLocation("TextureUnit"), 0);

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, this.TextureHandle);

            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, this.RibbonWidth, this.RibbonLength, 0, PixelFormat.Bgra, PixelType.UnsignedByte, this.TextureData);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToBorder);

            this.VertexBufferHandle = GL.GenBuffer();
            this.VertexArrayHandle = GL.GenVertexArray();

            GL.BindVertexArray(this.VertexArrayHandle);
            GL.BindBuffer(BufferTarget.ArrayBuffer, this.VertexBufferHandle);
            GL.BufferData(BufferTarget.ArrayBuffer, this.RibbonGeometry.Length * sizeof(float), this.RibbonGeometry, BufferUsageHint.DynamicDraw);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, DATA_PER_VERTEX_RIBBON_GEO * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, DATA_PER_VERTEX_RIBBON_GEO * sizeof(float), 3 * sizeof(float));
            GL.EnableVertexAttribArray(1);

            this.StarShader.Use();
            this.LocationStarProjection = this.StarShader.GetUniformLocation("Projection");
            this.LocationStarView = this.StarShader.GetUniformLocation("View");
            this.LocationStarModel = this.StarShader.GetUniformLocation("Model");
            GL.UniformMatrix4(this.LocationStarProjection, true, ref Projection);

            this.StarTextureHandle = GL.GenTexture();
            GL.Uniform1(this.StarShader.GetUniformLocation("Texture"), 0);

            int LEDCountLoc = this.StarShader.GetUniformLocation("LEDCount");
            GL.Uniform1(LEDCountLoc, (float)this.RibbonWidth);

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, this.StarTextureHandle);

            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, this.RibbonWidth, 1, 0, PixelFormat.Bgra, PixelType.UnsignedByte, new uint[this.RibbonWidth]);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToBorder);

            this.StarVertexBufferHandle = GL.GenBuffer();
            this.StarInstanceBufferHandle = GL.GenBuffer();
            this.StarVertexArrayHandle = GL.GenVertexArray();

            GL.BindVertexArray(this.StarVertexArrayHandle);
            GL.BindBuffer(BufferTarget.ArrayBuffer, this.StarVertexBufferHandle);
            GL.BufferData(BufferTarget.ArrayBuffer, this.StarGeometry.Length * sizeof(float), this.StarGeometry, BufferUsageHint.StaticDraw);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, DATA_PER_VERTEX_STAR_GEO * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribDivisor(0, 0);
            
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, DATA_PER_VERTEX_STAR_GEO * sizeof(float), 3 * sizeof(float));
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribDivisor(1, 0);

            GL.BindBuffer(BufferTarget.ArrayBuffer, this.StarInstanceBufferHandle);
            GL.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, BYTES_PER_INSTANCE_STAR, 0);
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribDivisor(2, 1);

            GL.VertexAttribIPointer(3, 1, VertexAttribIntegerType.Int, BYTES_PER_INSTANCE_STAR, (IntPtr)(3 * sizeof(float)));
            GL.EnableVertexAttribArray(3);
            GL.VertexAttribDivisor(3, 1);

            GenerateAndUploadProjection(this.HostWindow.Width, this.HostWindow.Height);

            this.SetupDone = true;
        }

        public void Dispatch()
        {
            if (!this.SetupDone) { return; }

            // Beat detection

            // Get newest low-frequency data, and reset the accumulator.
            float LowFreqData = BaseNoteFinder.LastLowFreqSum / (1 + BaseNoteFinder.LastLowFreqCount);
            BaseNoteFinder.LastLowFreqSum = 0;
            BaseNoteFinder.LastLowFreqCount = 0;

            // Strong IIR for keeping an average of the low-frequency content to compare against for finding the beats
            const float IIR_HIST = 0.95F;
            this.HistoricalLFData = Math.Max(LowFreqData, this.HistoricalLFData * IIR_HIST); //(IIR_HIST * this.HistoricalLFData) + ((1F - IIR_HIST) * LowFreqData);

            // The current low-frequency content, filtered lightly, then normalized against the recent average quantity.
            const float IIR_REAL = 0.2F;
            LowFreqData /= this.HistoricalLFData; // Normalize? Maybe

            this.CurrentLFData = (IIR_REAL * CurrentLFData) + ((1F - IIR_REAL) * LowFreqData);
            LowFreqData = this.CurrentLFData;

            // Some non-linearity to make the beats more apparent
            this.SpeedBoost = MathF.Pow(LowFreqData, 3);
            if(float.IsNaN(this.SpeedBoost)) { this.SpeedBoost = 1F; }

            lock (this.TextureData)
            {
                uint[] ColourData = this.DataSource.GetDataDiscrete();
                for (int i = 0; i < ColourData.Length; i++)
                {
                    this.TextureData[this.NewLines, i] = ColourData[i];
                    this.StarTextureData[i] = ColourData[i]; // TODO: Just use array.copy
                }
                this.NewLines = (this.NewLines + 1) % this.RibbonLength;
                float AmplitudeNow = BaseNoteFinder.LastBinSum;
                this.AmplitudeData[this.AmplitudeDataIndex] = AmplitudeNow;
                this.AmplitudeDataIndex = (this.AmplitudeDataIndex + 1) % this.RibbonLength;
                this.NewTexData = true;

                if (this.RecentPeakTotalAmplitude < 0.001F) { this.RecentPeakTotalAmplitude = AmplitudeNow; }
                this.RecentPeakTotalAmplitude = (AMPLITUDE_PREK_DETECT_IIR * this.RecentPeakTotalAmplitude) + ((1F - AMPLITUDE_PREK_DETECT_IIR) * AmplitudeNow);

                for (int Section = 0; Section < this.RibbonLength; Section++) // TODO: The section that has the ends of the texture on either side interpolated the entire texture into its length. Fix!
                {
                    const float WAVE_PERIOD = 6F;
                    const float RIBBON_LENGTH = 3F;
                    const float BASE_AMPLITUDE = 0.25F;

                    int ThisAmplitudeIndex = (this.AmplitudeDataIndex - Section + this.RibbonLength) % this.RibbonLength;
                    int PrevAmplitudeIndex = (this.AmplitudeDataIndex - Section - 1 + this.RibbonLength) % this.RibbonLength;
                    float Top = ((float)Section / this.RibbonLength) * RIBBON_LENGTH;
                    float Bottom = (Section == 0) ? -1F : ((float)(Section - 1) / this.RibbonLength) * RIBBON_LENGTH;
                    float WidthBack = this.AmplitudeData[PrevAmplitudeIndex] * this.RibbonScale / this.RecentPeakTotalAmplitude;
                    float WidthFront = (Section == 0) ? WidthBack : this.AmplitudeData[ThisAmplitudeIndex] * this.RibbonScale / this.RecentPeakTotalAmplitude;
                    float ZTop = MathF.Cos((Section * MathF.PI * WAVE_PERIOD / this.RibbonLength) - this.TimeOffset) * (BASE_AMPLITUDE - ((float)Section / this.RibbonLength) * 0.1F);
                    float ZBot = MathF.Cos(((Section - 1) * MathF.PI * WAVE_PERIOD / this.RibbonLength) - this.TimeOffset) * (BASE_AMPLITUDE - ((float)(Section - 1) / this.RibbonLength) * 0.1F);

                    float[] NewSection = new float[]
                    { //          X       Y     Z   U  V
                        -WidthFront, Bottom,  ZBot, 0, (float)Section / this.RibbonLength,
                          WidthBack,    Top,  ZTop, 1, (float)(Section + 1) / this.RibbonLength,
                         WidthFront, Bottom,  ZBot, 1, (float)Section / this.RibbonLength,
                        -WidthFront, Bottom,  ZBot, 0, (float)Section / this.RibbonLength,
                         -WidthBack,    Top,  ZTop, 0, (float)(Section + 1) / this.RibbonLength,
                          WidthBack,    Top,  ZTop, 1, (float)(Section + 1) / this.RibbonLength,
                    };
                    Array.Copy(NewSection, 0, this.RibbonGeometry, Section * 2 * 3 * DATA_PER_VERTEX_RIBBON_GEO, NewSection.Length);
                }
            }
        }

        public void Render()
        {
            if (!this.SetupDone) { return; }
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            float ZOffset = MathF.Sin(TimeOffset / 7F) * 0.8F + 2.2F;
            float XOffset = MathF.Sin(TimeOffset / 13F) * 1.7F;
            float YOffset = (MathF.Sin(TimeOffset / 9F) * 1.5F) * (MathF.Abs(XOffset) / 2F) - 2F;

            Vector3 CameraPos = new(XOffset, YOffset, ZOffset);
            Matrix4 Rotations = Matrix4.LookAt(CameraPos, RibbonCenter, Vector3.UnitZ);

            this.StarShader!.Use();
            for (int i = 0; i < this.StarInstances.Length; i++)
            {
                this.StarInstances[i].Y += (this.StarSpeed * (this.SpeedBoost) + 0.1F) - STAR_WRAP_LOC;
                this.StarInstances[i].Y %= (STAR_WRAP_DIST - STAR_WRAP_LOC);
                this.StarInstances[i].Y += STAR_WRAP_LOC;
            }
            GL.BindVertexArray(this.StarVertexArrayHandle);
            GL.BindBuffer(BufferTarget.ArrayBuffer, this.StarInstanceBufferHandle);
            GL.BufferData(BufferTarget.ArrayBuffer, BYTES_PER_INSTANCE_STAR * this.StarCount, this.StarInstances, BufferUsageHint.StreamDraw);

            GL.BindTexture(TextureTarget.Texture2D, this.StarTextureHandle);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, this.RibbonWidth, 1, 0, PixelFormat.Bgra, PixelType.UnsignedByte, this.StarTextureData);

            Matrix4 StarModel = Matrix4.LookAt(Vector3.Zero, CameraPos, Vector3.UnitZ);
            GL.UniformMatrix4(this.LocationStarView, true, ref Rotations);
            GL.UniformMatrix4(this.LocationStarModel, true, ref StarModel);

            // None of this works and I don't know why D:
            /*Matrix4 CheatedView = Rotations;
            CheatedView.M11 = 1;
            CheatedView.M12 = 0;
            CheatedView.M13 = 0;
            CheatedView.M21 = 0;
            CheatedView.M22 = 1;
            CheatedView.M23 = 0;
            CheatedView.M31 = 0;
            CheatedView.M32 = 0;
            CheatedView.M33 = 1;
            GL.UniformMatrix4(this.LocationStarView, true, ref Rotations);
            Matrix4 InverseView = Rotations;
            InverseView.Invert();
            Vector3 CamRight = new(Rotations.M11, Rotations.M12, Rotations.M13); //(Vector4.UnitX * InverseView).Xyz; 
            Vector3 CamUp = new(Rotations.M21, Rotations.M22, Rotations.M23); // (Vector4.UnitZ * InverseView).Xyz;
            GL.Uniform3(this.LocationStarCamRight, CamRight);
            GL.Uniform3(this.LocationStarCamUp, CamUp);*/

            GL.DrawArraysInstanced(PrimitiveType.Triangles, 0, 6, this.StarCount);
            
            this.RibbonShader!.Use();
            GL.BindTexture(TextureTarget.Texture2D, this.TextureHandle);

            if (this.NewTexData)
            {
                lock (this.TextureData)
                {
                    if (this.LastUploadedPosition + this.NewLines > this.RibbonLength) // We have more data than remaining space. Split the data into 2, and write to the end & beginning of the texture.
                    {
                        int LinesBeforeWrap = this.RibbonLength - this.LastUploadedPosition;
                        int LinesAfterWrap = (int)(this.NewLines - LinesBeforeWrap);
                        uint[,] TextureDataAfterWrap = new uint[LinesAfterWrap, this.RibbonWidth];
                        Array.Copy(this.TextureData, LinesBeforeWrap * this.RibbonWidth, TextureDataAfterWrap, 0, LinesAfterWrap * this.RibbonWidth);
                        GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, this.LastUploadedPosition, this.RibbonWidth, LinesBeforeWrap, PixelFormat.Bgra, PixelType.UnsignedByte, this.TextureData); // Before (at end of texture)
                        GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, this.RibbonWidth, LinesAfterWrap, PixelFormat.Bgra, PixelType.UnsignedByte, TextureDataAfterWrap); // After (at start of texture)
                    }
                    else { GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, this.LastUploadedPosition, this.RibbonWidth, (int)this.NewLines, PixelFormat.Bgra, PixelType.UnsignedByte, this.TextureData); }

                    this.NewTexData = false;
                    this.LastUploadedPosition += this.NewLines;
                    this.LastUploadedPosition %= this.RibbonLength;
                    this.NewLines = 0;
                }
            }

            GL.Uniform1(this.LocationTextureAdvance, (float)this.LastUploadedPosition / this.RibbonLength);

            GL.UniformMatrix4(this.LocationView, true, ref Rotations);

            GL.BindVertexArray(this.VertexArrayHandle);
            GL.BindBuffer(BufferTarget.ArrayBuffer, this.VertexBufferHandle);
            GL.BufferData(BufferTarget.ArrayBuffer, this.RibbonGeometry.Length * sizeof(float), this.RibbonGeometry, BufferUsageHint.DynamicDraw);

            GL.DrawArrays(PrimitiveType.Triangles, 0, this.RibbonGeometry.Length / 5);
            
            this.TimeOffset += 0.05F;
        }

        private void GenerateAndUploadProjection(int width, int height)
        {
            if (this.RibbonShader is null || this.StarShader is null) { return; }
            Matrix4 Projection = Matrix4.CreatePerspectiveFieldOfView(MathF.PI / 3F, (float)width / height, 0.01F, 100F);

            this.RibbonShader.Use();
            GL.UniformMatrix4(this.LocationProjection, true, ref Projection);

            this.StarShader.Use();
            GL.UniformMatrix4(this.LocationStarProjection, true, ref Projection);

            GL.Viewport(0, 0, width, height);
        }

        public void Resize(int width, int height) => GenerateAndUploadProjection(width, height);


        public void Close()
        {
            this.RibbonShader?.Dispose();
            this.StarShader?.Dispose();
        }

        public bool SupportsFormat(IVisualizerFormat format) => format is IDiscrete1D;

        private struct StarInstance
        {
            public float X { get; set; }
            public float Y { get; set; }
            public float Z { get; set; }
            public int LEDIndex { get; set; }

            public StarInstance(float x, float y, float z, int ledIndex)
            {
                this.X = x;
                this.Y = y;
                this.Z = z;
                this.LEDIndex = ledIndex;
            }
        }
        private const int DATA_PER_INSTANCE_STAR = 4;
        private const int BYTES_PER_INSTANCE_STAR = (3 * sizeof(float)) + (1 * sizeof(int)); // Be careful of struct member alignment issues.
    }
}
