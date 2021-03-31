using ColorChord.NET.Visualizers.Formats;
using System;
using OpenTK.Graphics.OpenGL4;

namespace ColorChord.NET.Outputs.Display
{
    public class BlockMatrix : IDisplayMode
    {
        private readonly DisplayOpenGL HostWindow;
        private readonly IDiscrete2D DataSource;

        private Shader Shader;

        private int CountX, CountY;
        private readonly float[] GeometryData = new float[]
        { // X    Y   U   V
            -1F, -1F, 0F, 1F,
             1F, -1F, 1F, 1F,
             1F,  1F, 1F, 0F,
            -1F, -1F, 0F, 1F,
             1F,  1F, 1F, 0F,
            -1F,  1F, 0F, 0F
        };

        private byte[] TextureData, NextTextureData;

        private bool NewData, SetupDone;
        private int VertexBufferHandle, VertexArrayHandle, TextureHandle;

        public BlockMatrix(DisplayOpenGL parent, IVisualizerFormat visualizer)
        {
            if(visualizer is not IDiscrete2D)
            {
                Log.Error("BlockMatrix cannot use the provided visualizer, as it does not output 2D discrete data.");
                throw new InvalidOperationException("Incompatible visualizer. Must implement IDiscrete2D.");
            }
            this.HostWindow = parent;
            this.DataSource = (IDiscrete2D)visualizer;
            this.CountX = this.DataSource.GetWidth();
            this.CountY = this.DataSource.GetHeight();
            this.TextureData = new byte[CountX * CountY * 4];
            this.NextTextureData = new byte[CountX * CountY * 4];
            this.SetupDone = false;
        }

        public void Load()
        {
            // Make shader and geometry storage
            this.Shader = new Shader("Passthrough2Textured.vert", "Passthrough2Textured.frag");
            this.VertexBufferHandle = GL.GenBuffer();
            this.VertexArrayHandle = GL.GenVertexArray();

            // Activate shader and make texture
            this.Shader.Use();
            GL.BindTexture(TextureTarget.Texture2D, 0);
            this.TextureHandle = GL.GenTexture();

            // Activate texture
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, this.TextureHandle);

            // Configure & pre-load texture
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToBorder);

            // Load geometry
            GL.BindVertexArray(this.VertexArrayHandle);
            GL.BindBuffer(BufferTarget.ArrayBuffer, this.VertexBufferHandle);
            GL.BufferData(BufferTarget.ArrayBuffer, GeometryData.Length * sizeof(float), GeometryData, BufferUsageHint.DynamicDraw);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));
            GL.EnableVertexAttribArray(1);
            this.SetupDone = true;
        }

        public void Dispatch()
        {
            if (!this.SetupDone) { return; }

            uint[,] Data = this.DataSource.GetDataDiscrete();
            for (int x = 0; x < this.CountX; x++)
            {
                for (int y = 0; y < this.CountY; y++)
                {
                    this.TextureData[(y * this.CountX * 4) + (x * 4) + 0] = (byte)(Data[x, y] >> 16); // R
                    this.TextureData[(y * this.CountX * 4) + (x * 4) + 1] = (byte)(Data[x, y] >> 8); // G
                    this.TextureData[(y * this.CountX * 4) + (x * 4) + 2] = (byte)(Data[x, y]); // B 
                }
            }

            this.NewData = true;
        }

        public void Render()
        {
            if (!this.SetupDone) { return; }

            this.Shader.Use();
            if (this.NewData) { GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, this.CountX, this.CountY, 0, PixelFormat.Rgba, PixelType.UnsignedByte, this.TextureData); }
            this.NewData = false;

            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            GL.BindVertexArray(this.VertexArrayHandle);
            GL.DrawArrays(PrimitiveType.Triangles, 0, this.GeometryData.Length / 4);
        }

        public void Resize(int width, int height) { }

        public void Close()
        {
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.DeleteBuffer(this.VertexBufferHandle);
            this.Shader.Dispose();
        }

        public bool SupportsFormat(IVisualizerFormat format) => format is IDiscrete2D;
    }
}
