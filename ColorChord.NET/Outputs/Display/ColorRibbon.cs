using ColorChord.NET.Config;
using ColorChord.NET.NoteFinder;
using ColorChord.NET.Visualizers;
using ColorChord.NET.Visualizers.Formats;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ColorChord.NET.Outputs.Display
{
    public class ColorRibbon : IDisplayMode, IConfigurableAttr
    {
        private readonly DisplayOpenGL HostWindow;
        private readonly IDiscrete1D DataSource;

        private Shader? RibbonShader, StarShader;

        private int LocationProjection, LocationView, LocationTextureAdvance;
        private int TextureHandle, VertexArrayHandle, VertexBufferHandle;

        /// <summary> Whether this output is ready to accept data and draw. </summary>
        private bool SetupDone = false;

        [ConfigInt("RibbonLength", 2, 10000, 120)]
        private int RibbonLength = 120;

        private int RibbonWidth; // TODO: Handle updating this if the visualizer changes count

        private int NewLines = 0;

        private int LastUploadedPosition = 0;

        private uint[,] TextureData;

        private float[] AmplitudeData;
        private int AmplitudeDataIndex = 0;

        private bool NewTexData = false;

        private float TimeOffset = 0F;

        private float[] Geometry = new float[]
        { // X   Y   Z  U  V
            -1, -1,  0, 0, 0,
             1,  1,  0, 1, 1,
             1, -1,  0, 1, 0,
            -1, -1,  0, 0, 0,
            -1,  1,  0, 0, 1,
             1,  1,  0, 1, 1
        };
        private const int DATA_PER_VERTEX = 5;

        private readonly Vector3 RibbonCenter = new(0F, 0F, 0F);

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
            this.AmplitudeData = new float[this.RibbonLength];
            this.Geometry = new float[this.RibbonLength * 2 * 3 * DATA_PER_VERTEX];
        }

        public void Load()
        {
            this.RibbonShader = new("ColorRibbon.vert", "ColorRibbon.frag");
            this.RibbonShader.Use();

            GL.ClearColor(0.0F, 0.0F, 0.0F, 1.0F);
            GL.Enable(EnableCap.DepthTest);

            this.LocationProjection = this.RibbonShader.GetUniformLocation("Projection");
            this.LocationView = this.RibbonShader.GetUniformLocation("View");
            this.LocationTextureAdvance = this.RibbonShader.GetUniformLocation("TextureAdvance");

            Matrix4 Projection = Matrix4.CreatePerspectiveFieldOfView(MathF.PI / 3F, 1, 0.01F, 10F);
            GL.UniformMatrix4(this.LocationProjection, true, ref Projection);

            GL.BindTexture(TextureTarget.Texture2D, 0);
            this.TextureHandle = GL.GenTexture();
            GL.Uniform1(this.RibbonShader.GetUniformLocation("TextureUnit"), 0);

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, this.TextureHandle);

            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, this.RibbonWidth, this.RibbonLength, 0, PixelFormat.Rgba, PixelType.UnsignedByte, this.TextureData);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToBorder);

            this.VertexBufferHandle = GL.GenBuffer();
            this.VertexArrayHandle = GL.GenVertexArray();

            GL.BindVertexArray(this.VertexArrayHandle);
            GL.BindBuffer(BufferTarget.ArrayBuffer, this.VertexBufferHandle);
            GL.BufferData(BufferTarget.ArrayBuffer, this.Geometry.Length * sizeof(float), this.Geometry, BufferUsageHint.DynamicDraw);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, DATA_PER_VERTEX * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, DATA_PER_VERTEX * sizeof(float), 3 * sizeof(float));
            GL.EnableVertexAttribArray(1);

            this.SetupDone = true;
        }

        public void Dispatch()
        {
            if (!this.SetupDone) { return; }
            lock (this.TextureData)
            {
                uint[] ColourData = this.DataSource.GetDataDiscrete();
                for (int i = 0; i < ColourData.Length; i++)
                {
                    this.TextureData[this.NewLines, i] = ColourData[i];
                }
                this.NewLines = (this.NewLines + 1) % this.RibbonLength;
                this.AmplitudeData[this.AmplitudeDataIndex] = BaseNoteFinder.LastBinSum;
                this.AmplitudeDataIndex = (this.AmplitudeDataIndex + 1) % this.RibbonLength;
                this.NewTexData = true;

                for (int Section = 0; Section < this.RibbonLength; Section++) // TODO: The section that has the ends of the texture on either side interpolated the entire texture into its length. Fix!
                {
                    const float WAVE_PERIOD = 20F;
                    const float RIBBON_LENGTH = 3F;
                    const float BASE_AMPLITUDE = 0.25F;
                    const float AMP_DIV = 4F;

                    int ThisAmplitudeIndex = (this.AmplitudeDataIndex - Section + this.RibbonLength) % this.RibbonLength;
                    int PrevAmplitudeIndex = (this.AmplitudeDataIndex - Section - 1 + this.RibbonLength) % this.RibbonLength;
                    float Top = ((float)Section / this.RibbonLength) * RIBBON_LENGTH;
                    float Bottom = (Section == 0) ? -1F : ((float)(Section - 1) / this.RibbonLength) * RIBBON_LENGTH;
                    float WidthBack = this.AmplitudeData[PrevAmplitudeIndex] / AMP_DIV;
                    float WidthFront = (Section == 0) ? WidthBack : this.AmplitudeData[ThisAmplitudeIndex] / AMP_DIV;
                    float ZTop = MathF.Cos(Section * MathF.PI / WAVE_PERIOD - this.TimeOffset) * (BASE_AMPLITUDE - ((float)Section / this.RibbonLength) * 0.1F);
                    float ZBot = MathF.Cos((Section - 1) * MathF.PI / WAVE_PERIOD - this.TimeOffset) * (BASE_AMPLITUDE - ((float)(Section - 1) / this.RibbonLength) * 0.1F);


                    float[] NewSection = new float[]
                    { //          X       Y     Z   U  V
                        -WidthFront, Bottom,  ZBot, 0, (float)Section / this.RibbonLength,
                          WidthBack,    Top,  ZTop, 1, (float)(Section + 1) / this.RibbonLength,
                         WidthFront, Bottom,  ZBot, 1, (float)Section / this.RibbonLength,
                        -WidthFront, Bottom,  ZBot, 0, (float)Section / this.RibbonLength,
                         -WidthBack,    Top,  ZTop, 0, (float)(Section + 1) / this.RibbonLength,
                          WidthBack,    Top,  ZTop, 1, (float)(Section + 1) / this.RibbonLength,
                    };
                    Array.Copy(NewSection, 0, this.Geometry, Section * 2 * 3 * DATA_PER_VERTEX, NewSection.Length);
                }
            }
        }

        public void Render()
        {
            if (!this.SetupDone) { return; }
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

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
                        GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, this.LastUploadedPosition, this.RibbonWidth, LinesBeforeWrap, PixelFormat.Rgba, PixelType.UnsignedByte, this.TextureData); // Before (at end of texture)
                        GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, this.RibbonWidth, LinesAfterWrap, PixelFormat.Rgba, PixelType.UnsignedByte, TextureDataAfterWrap); // After (at start of texture)
                    }
                    else { GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, this.LastUploadedPosition, this.RibbonWidth, (int)this.NewLines, PixelFormat.Rgba, PixelType.UnsignedByte, this.TextureData); }

                    this.NewTexData = false;
                    this.LastUploadedPosition += this.NewLines;
                    this.LastUploadedPosition %= this.RibbonLength;
                    this.NewLines = 0;
                }
            }

            GL.Uniform1(this.LocationTextureAdvance, (float)this.LastUploadedPosition / this.RibbonLength);

            float ZOffset = MathF.Sin(TimeOffset / 7F) * 0.8F + 2.2F;
            float XOffset = MathF.Sin(TimeOffset / 13F) * 1.7F;
            float YOffset = (MathF.Sin(TimeOffset / 9F) * 1.5F) * (MathF.Abs(XOffset) / 2F) - 2F;

            Vector3 CameraPos = new(XOffset, YOffset, ZOffset);
            Matrix4 Rotations = Matrix4.LookAt(CameraPos, RibbonCenter, Vector3.UnitZ);

            GL.UniformMatrix4(this.LocationView, true, ref Rotations);

            GL.BindVertexArray(this.VertexArrayHandle);
            GL.BindBuffer(BufferTarget.ArrayBuffer, this.VertexBufferHandle);
            GL.BufferData(BufferTarget.ArrayBuffer, this.Geometry.Length * sizeof(float), this.Geometry, BufferUsageHint.DynamicDraw);

            GL.DrawArrays(PrimitiveType.Triangles, 0, this.Geometry.Length / 5);

            this.TimeOffset += 0.05F;
        }

        public void Resize(int width, int height) { }


        public void Close()
        {
            this.RibbonShader?.Dispose();
            this.StarShader?.Dispose();
        }

        public bool SupportsFormat(IVisualizerFormat format) => format is IDiscrete1D;
    }
}
