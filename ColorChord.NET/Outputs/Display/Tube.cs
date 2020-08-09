using ColorChord.NET.Visualizers;
using ColorChord.NET.Visualizers.Formats;
using OpenTK;
using OpenTK.Graphics.OpenGL4;
using System;
using System.Collections.Generic;
using System.Text;

namespace ColorChord.NET.Outputs.Display
{
    public class Tube : IDisplayMode
    {
        private const int TUBE_LENGTH = 50;
        private int TubeResolution = 8;

        private DisplayOpenGL HostWindow;

        private IDiscrete1D DataSource;

        private Shader TubeShader;

        private byte[] TextureData;
        private int LocationTexture;

        private Matrix4 Projection;
        private int LocationProjection;

        private int VertexBufferHandle, VertexArrayHandle;

        private float[] VertexData;
        private ushort TubePosition = 0;
        private bool IsForward = true;

        private bool SetupDone = false;
        private bool NewData;

        public Tube(DisplayOpenGL parent, IVisualizer visualizer)
        {
            if (!(visualizer is IContinuous1D))
            {
                Log.Error("Tube cannot use the provided visualizer, as it does not output 1D discrete data.");
                throw new InvalidOperationException("Incompatible visualizer. Must implement IDiscrete1D.");
            }
            this.HostWindow = parent;
            this.DataSource = (IDiscrete1D)visualizer;
            this.TubeResolution = this.DataSource.GetCountDiscrete();
        }

        public void Close()
        {
            if (!this.SetupDone) { return; }
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.DeleteBuffer(this.VertexBufferHandle);
            this.TubeShader.Dispose();
        }

        public void Dispatch()
        {
            if (!this.SetupDone) { return; }

            for (int seg = 0; seg < TubeResolution; seg++)
            {
                this.TextureData[(this.TubePosition * (4 * TubeResolution)) + (seg * 4) + 0] = (byte)(this.DataSource.GetDataDiscrete()[seg] >> 16);
                this.TextureData[(this.TubePosition * (4 * TubeResolution)) + (seg * 4) + 1] = (byte)(this.DataSource.GetDataDiscrete()[seg] >> 8);
                this.TextureData[(this.TubePosition * (4 * TubeResolution)) + (seg * 4) + 2] = (byte)(this.DataSource.GetDataDiscrete()[seg]);
            }
            this.NewData = true;
        }

        public void Load()
        {
            this.TextureData = new byte[TUBE_LENGTH * TubeResolution * 4];
            this.VertexData = new float[TUBE_LENGTH * TubeResolution * 6 * 5];

            int DataIndex = 0;

            void AddPoint(float x, float y, float z, int depth, int segment)
            {
                this.VertexData[DataIndex++] = x;
                this.VertexData[DataIndex++] = y;
                this.VertexData[DataIndex++] = z;
                this.VertexData[DataIndex++] = (float)segment / TubeResolution + (0.5F / TubeResolution);
                this.VertexData[DataIndex++] = (float)depth / TUBE_LENGTH + (0.5F / TUBE_LENGTH);

                //Console.WriteLine("{0:F3}, {1:F3}, {2:F3}", x, y, z);
            }

            GL.ClearColor(0.0F, 0.0F, 0.0F, 1.0F);

            this.TubeShader = new Shader("tube3d.vert", "tube3d.frag");
            this.TubeShader.Use();

            GL.BindTexture(TextureTarget.Texture2D, 0);
            this.LocationTexture = GL.GenTexture();
            GL.Uniform1(this.TubeShader.GetUniformLocation("tex"), 0);

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, this.LocationTexture);

            for (int lvl = 0; lvl < TUBE_LENGTH; lvl++)
            {
                for(int seg = 0; seg < TubeResolution; seg++)
                {
                    this.TextureData[(lvl * (3 * TubeResolution)) + (seg * 3) + 0] = 255;
                    this.TextureData[(lvl * (3 * TubeResolution)) + (seg * 3) + 1] = (byte)(255 - (lvl * 10));
                    this.TextureData[(lvl * (3 * TubeResolution)) + (seg * 3) + 2] = 0;
                }
            }

            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, TubeResolution, TUBE_LENGTH, 0, PixelFormat.Rgba, PixelType.UnsignedByte, this.TextureData);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToBorder);
            // TODO Might need to flip vertically

            this.Projection = Matrix4.CreatePerspectiveFieldOfView((float)(Math.PI / 2), 1, 0.01F, 10F);
            this.LocationProjection = this.TubeShader.GetUniformLocation("projection");
            GL.UniformMatrix4(this.LocationProjection, true, ref this.Projection);

            this.VertexBufferHandle = GL.GenBuffer();
            this.VertexArrayHandle = GL.GenVertexArray();

            for(int i = 0; i < TUBE_LENGTH; i++)
            {
                for(int seg = 0; seg < TubeResolution; seg++)
                {
                    float SegStartX = (float)Math.Cos(Math.PI * 2 * seg / TubeResolution);
                    float SegStartY = (float)Math.Sin(Math.PI * 2 * seg / TubeResolution);
                    float SegEndX = (float)Math.Cos(Math.PI * 2 * (seg + 1) / TubeResolution);
                    float SegEndY = (float)Math.Sin(Math.PI * 2 * (seg + 1) / TubeResolution);
                    float FrontZ = -1 - (float)i / TUBE_LENGTH;
                    float BackZ = -1 - (float)(i + 1) / TUBE_LENGTH;

                    AddPoint(SegStartX, SegStartY, FrontZ, i, seg);
                    AddPoint(SegStartX, SegStartY, BackZ, i, seg);
                    AddPoint(SegEndX, SegEndY, FrontZ, i, seg);

                    AddPoint(SegEndX, SegEndY, FrontZ, i, seg);
                    AddPoint(SegStartX, SegStartY, BackZ, i, seg);
                    AddPoint(SegEndX, SegEndY, BackZ, i, seg);
                }
            }

            GL.BindVertexArray(this.VertexArrayHandle);
            GL.BindBuffer(BufferTarget.ArrayBuffer, this.VertexBufferHandle);
            GL.BufferData(BufferTarget.ArrayBuffer, VertexData.Length * sizeof(float), VertexData, BufferUsageHint.DynamicDraw);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 5 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 5 * sizeof(float), 3 * sizeof(float));
            GL.EnableVertexAttribArray(1);

            this.SetupDone = true;
        }

        public void Render()
        {
            if (!this.SetupDone) { return; }
            this.TubeShader.Use();

            if(this.NewData)
            {
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, TubeResolution, TUBE_LENGTH, 0, PixelFormat.Rgba, PixelType.UnsignedByte, this.TextureData);
                byte[] NewData = new byte[TubeResolution * 4];
                Array.Copy(this.TextureData, 4 * TubeResolution * this.TubePosition, NewData, 0, 4 * TubeResolution);
                GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, this.TubePosition, TubeResolution, 1, PixelFormat.Rgba, PixelType.UnsignedByte, NewData);
                this.TubePosition = (ushort)((this.TubePosition + (this.IsForward ? 1 : -1)) % TUBE_LENGTH);
                if(this.TubePosition == 0 || this.TubePosition == (TUBE_LENGTH - 1)) { this.IsForward = !this.IsForward; }
                this.NewData = false;
            }

            GL.BindVertexArray(this.VertexArrayHandle);
            GL.DrawArrays(PrimitiveType.Triangles, 0, this.VertexData.Length / 3);
        }

        public void Resize(int width, int height)
        {
            if (!this.SetupDone) { return; }

        }

        public bool SupportsFormat(IVisualizerFormat format) => format is IDiscrete1D;
    }
}
