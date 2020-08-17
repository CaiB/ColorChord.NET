using ColorChord.NET.Visualizers;
using ColorChord.NET.Visualizers.Formats;
using OpenTK;
using OpenTK.Graphics.OpenGL4;
using System;

namespace ColorChord.NET.Outputs.Display
{
    public class Radar : IDisplayMode
    {
        private const int SPOKES = 150;

        private int RadiusResolution = 8;

        /// <summary> How many floats comprise one vertex of data sent to the GPU. </summary>
        private const byte DATA_PER_VERTEX = 6;

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

        public void Load()
        {
            this.VertexData = new float[SPOKES * this.RadiusResolution * 6 * DATA_PER_VERTEX];
            this.TextureData = new byte[SPOKES * this.RadiusResolution * 4];

            int DataIndex = 0;
            void AddPoint(float x, float y, float z, int spoke, int seg)
            {
                this.VertexData[DataIndex++] = x;
                this.VertexData[DataIndex++] = y;
                this.VertexData[DataIndex++] = z;
                this.VertexData[DataIndex++] = ((float)seg / this.RadiusResolution) + (0.5F / this.RadiusResolution);
                this.VertexData[DataIndex++] = ((float)spoke / SPOKES) + (0.5F / SPOKES);
                this.VertexData[DataIndex++] = 0;// isLeft ? 0F : 1F;
            }

            GL.ClearColor(0.0F, 0.0F, 0.0F, 1.0F);

            this.Shader = new Shader("Radar.vert", "Radar.frag");
            this.Shader.Use();

            GL.BindTexture(TextureTarget.Texture2D, 0);
            this.LocationTexture = GL.GenTexture();
            GL.Uniform1(this.Shader.GetUniformLocation("tex"), 0);

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, this.LocationTexture);

            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, this.RadiusResolution, SPOKES, 0, PixelFormat.Rgba, PixelType.UnsignedByte, this.TextureData);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToBorder);

            this.Projection = Matrix4.CreatePerspectiveFieldOfView((float)(Math.PI / 2), 1, 0.01F, 10F);
            this.LocationProjection = this.Shader.GetUniformLocation("projection");
            GL.UniformMatrix4(this.LocationProjection, true, ref this.Projection);

            this.VertexBufferHandle = GL.GenBuffer();
            this.VertexArrayHandle = GL.GenVertexArray();

            // Generate geometry
            for(int Spoke = 0; Spoke < SPOKES; Spoke++)
            {
                for(int Seg = 0; Seg < RadiusResolution; Seg++)
                {
                    double RotStart = Math.PI * 2 * Spoke / SPOKES;
                    double RotEnd = Math.PI * 2 * (Spoke + 1) / SPOKES;

                    float RadIn = (float)Seg / RadiusResolution;
                    float RadOut = (float)(Seg + 1) / RadiusResolution;

                    float StartX = (float)Math.Cos(RotStart);
                    float StartY = (float)Math.Sin(RotStart);
                    float EndX = (float)Math.Cos(RotEnd);
                    float EndY = (float)Math.Sin(RotEnd);

                    const float Z = -1;
                    AddPoint(StartX * RadIn, StartY * RadIn, Z, Spoke, Seg); // In bottom
                    AddPoint(StartX * RadOut, StartY * RadOut, Z, Spoke, Seg); // Out bottom
                    AddPoint(EndX * RadOut, EndY * RadOut, Z, Spoke, Seg); // Out top

                    AddPoint(StartX * RadIn, StartY * RadIn, Z, Spoke, Seg); // In bottom
                    AddPoint(EndX * RadOut, EndY * RadOut, Z, Spoke, Seg); // Out top
                    AddPoint(EndX * RadIn, EndY * RadIn, Z, Spoke, Seg); // In top
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

            this.SetupDone = true;
        }

        public void Dispatch()
        {
            if (!this.SetupDone) { return; }
            if (this.NewLines == SPOKES) { return; }

            for (int Seg = 0; Seg < this.RadiusResolution; Seg++)
            {
                this.TextureData[(this.NewLines * (4 * this.RadiusResolution)) + (Seg * 4) + 0] = (byte)(this.DataSource.GetDataDiscrete()[Seg] >> 16); // R
                this.TextureData[(this.NewLines * (4 * this.RadiusResolution)) + (Seg * 4) + 1] = (byte)(this.DataSource.GetDataDiscrete()[Seg] >> 8); // G
                this.TextureData[(this.NewLines * (4 * this.RadiusResolution)) + (Seg * 4) + 2] = (byte)(this.DataSource.GetDataDiscrete()[Seg]); // B
                //this.TextureData[(this.NewLines * (4 * this.RadiusResolution)) + (Seg * 4) + 3] = (byte)(0 * 20); // A
            }
            this.NewLines++;
            this.NewData = true;
        }

        public void Render()
        {
            if (!this.SetupDone) { return; }
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            this.Shader.Use();

            if (this.NewData)
            {
                GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, this.RenderIndex, this.RadiusResolution, (int)this.NewLines, PixelFormat.Rgba, PixelType.UnsignedByte, this.TextureData);
                this.RenderIndex = (ushort)((this.RenderIndex + this.NewLines) % SPOKES);
                this.NewData = false;
                this.NewLines = 0;
                //GL.Uniform1(this.LocationDepthOffset, (float)(this.RenderIndex) / TUBE_LENGTH);
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
