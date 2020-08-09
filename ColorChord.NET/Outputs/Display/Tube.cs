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
        private const int TUBE_LENGTH = 20;
        private const int TUBE_RESOLUTION = 8;

        private DisplayOpenGL HostWindow;

        private IDiscrete1D DataSource;

        private Shader TubeShader;

        private byte[] TextureData = new byte[TUBE_LENGTH * TUBE_RESOLUTION * 3];
        private int LocationTexture;

        private Matrix4 Projection;
        private int LocationProjection;

        private int VertexBufferHandle, VertexArrayHandle;

        private float[] VertexData = new float[TUBE_LENGTH * TUBE_RESOLUTION * 6 * 5];

        public Tube(DisplayOpenGL parent, IVisualizer visualizer)
        {
            if (!(visualizer is IContinuous1D))
            {
                Log.Error("Tube cannot use the provided visualizer, as it does not output 1D discrete data.");
                throw new InvalidOperationException("Incompatible visualizer. Must implement IDiscrete1D.");
            }
            this.HostWindow = parent;
            this.DataSource = (IDiscrete1D)visualizer;
        }

        public void Close()
        {
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.DeleteBuffer(this.VertexBufferHandle);
            this.TubeShader.Dispose();
        }

        public void Dispatch()
        {
            
        }

        public void Load()
        {
            int DataIndex = 0;

            void AddPoint(float x, float y, float z, int depth, int segment)
            {
                this.VertexData[DataIndex++] = x;
                this.VertexData[DataIndex++] = y;
                this.VertexData[DataIndex++] = z;
                this.VertexData[DataIndex++] = (float)segment / TUBE_RESOLUTION;
                this.VertexData[DataIndex++] = (float)depth / TUBE_LENGTH;

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
                for(int seg = 0; seg < TUBE_RESOLUTION; seg++)
                {
                    this.TextureData[(lvl * (3 * TUBE_RESOLUTION)) + (seg * 3) + 0] = 255;
                    this.TextureData[(lvl * (3 * TUBE_RESOLUTION)) + (seg * 3) + 1] = (byte)(255 - (lvl * 10));
                    this.TextureData[(lvl * (3 * TUBE_RESOLUTION)) + (seg * 3) + 2] = 0;
                }
            }

            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb, TUBE_RESOLUTION, TUBE_LENGTH, 0, PixelFormat.Rgb, PixelType.UnsignedByte, this.TextureData);
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
                for(int seg = 0; seg < TUBE_RESOLUTION; seg++)
                {
                    float SegStartX = (float)Math.Cos(Math.PI * 2 * seg / TUBE_RESOLUTION);
                    float SegStartY = (float)Math.Sin(Math.PI * 2 * seg / TUBE_RESOLUTION);
                    float SegEndX = (float)Math.Cos(Math.PI * 2 * (seg + 1) / TUBE_RESOLUTION);
                    float SegEndY = (float)Math.Sin(Math.PI * 2 * (seg + 1) / TUBE_RESOLUTION);
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

            /*AddPoint(0.7F, 0.7F, -0.7F);
            AddPoint(-0.7F, 0.7F, -0.7F);
            AddPoint(-0.7F, -0.7F, -0.7F);*/

            /*AddPoint(1, 0, 0);
            AddPoint(1, 0, -0.5F);
            AddPoint(0.707F, 0.707F, 0);
            AddPoint(0.707F, 0.707F, 0);
            AddPoint(1, 0, -0.5F);
            AddPoint(0.707F, 0.707F, -0.5F);*/

            GL.BindVertexArray(this.VertexArrayHandle);
            GL.BindBuffer(BufferTarget.ArrayBuffer, this.VertexBufferHandle);
            GL.BufferData(BufferTarget.ArrayBuffer, VertexData.Length * sizeof(float), VertexData, BufferUsageHint.DynamicDraw);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 5 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 5 * sizeof(float), 3 * sizeof(float));
            GL.EnableVertexAttribArray(1);
        }

        public void Render()
        {
            this.TubeShader.Use();
            GL.BindVertexArray(this.VertexArrayHandle);
            GL.DrawArrays(PrimitiveType.Triangles, 0, this.VertexData.Length / 3);
        }

        public void Resize(int width, int height)
        {
            
        }

        public bool SupportsFormat(IVisualizerFormat format) => format is IDiscrete1D;
    }
}
