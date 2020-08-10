using ColorChord.NET.Visualizers;
using ColorChord.NET.Visualizers.Formats;
using OpenTK;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Input;
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
        private uint NewLines = 0;

        private Matrix4 Projection;
        private int LocationProjection;

        private Matrix4 TubeTransform = Matrix4.Identity;
        private int LocationTransform;

        private int VertexBufferHandle, VertexArrayHandle;

        private float[] VertexData;

        private ushort TubePosition = 0;
        private int LocationDepthOffset;

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
            this.HostWindow.MouseMove += MouseMove;
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
            if (this.NewLines == TUBE_LENGTH) { return; }

            for (int seg = 0; seg < TubeResolution; seg++)
            {
                this.TextureData[(this.NewLines * (4 * TubeResolution)) + (seg * 4) + 0] = (byte)(this.DataSource.GetDataDiscrete()[seg] >> 16);
                this.TextureData[(this.NewLines * (4 * TubeResolution)) + (seg * 4) + 1] = (byte)(this.DataSource.GetDataDiscrete()[seg] >> 8);
                this.TextureData[(this.NewLines * (4 * TubeResolution)) + (seg * 4) + 2] = (byte)(this.DataSource.GetDataDiscrete()[seg]);
            }
            this.NewLines++;
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

            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, TubeResolution, TUBE_LENGTH, 0, PixelFormat.Rgba, PixelType.UnsignedByte, this.TextureData);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToBorder);
            // TODO Might need to flip vertically

            this.Projection = Matrix4.CreatePerspectiveFieldOfView((float)(Math.PI / 2), 1, 0.01F, 10F);
            this.LocationProjection = this.TubeShader.GetUniformLocation("projection");
            GL.UniformMatrix4(this.LocationProjection, true, ref this.Projection);

            this.LocationDepthOffset = this.TubeShader.GetUniformLocation("depthOffset");

            this.LocationTransform = this.TubeShader.GetUniformLocation("transform");
            GL.UniformMatrix4(this.LocationTransform, true, ref this.TubeTransform);

            this.VertexBufferHandle = GL.GenBuffer();
            this.VertexArrayHandle = GL.GenVertexArray();

            for(int i = 0; i < TUBE_LENGTH; i++)
            {
                for(int seg = 0; seg < TubeResolution; seg++)
                {
                    // Turn on the commented out lines for crazy effects :)
                    //float SegStartX = (float)(Math.Cos(Math.PI * 2 * seg / TubeResolution) * (1 - ((float)i / TUBE_LENGTH)) * (1 - Math.Abs(Math.Sin(Frame / 10F)) * 0.2));
                    float SegStartX = (float)Math.Cos(Math.PI * 2 * seg / TubeResolution);
                    float SegStartY = (float)Math.Sin(Math.PI * 2 * seg / TubeResolution);
                    float SegEndX = (float)Math.Cos(Math.PI * 2 * (seg + 1) / TubeResolution);
                    //float SegEndY = (float)(Math.Sin(Math.PI * 2 * (seg + 1) / TubeResolution) * (1 - ((float)(i + 1) / TUBE_LENGTH)) * (1 - Math.Abs(Math.Cos(Frame / 10F)) * 0.2));
                    float SegEndY = (float)Math.Sin(Math.PI * 2 * (seg + 1) / TubeResolution);
                    float FrontZ = -1 - (float)i / TUBE_LENGTH;
                    float BackZ = -1 - (float)(i + 1 /*(i * 2)*/) / TUBE_LENGTH;

                    float OutMult = 1 - (i / (TUBE_LENGTH * 1.02F));
                    float InMult = 1 - ((i + 1) / (TUBE_LENGTH * 1.02F));

                    AddPoint(SegStartX * OutMult, SegStartY * OutMult, FrontZ, i, seg); // Out right
                    AddPoint(SegStartX * InMult, SegStartY * InMult, BackZ, i, seg); // In right 
                    AddPoint(SegEndX * OutMult, SegEndY * OutMult, FrontZ, i, seg); // Out left

                    AddPoint(SegEndX * OutMult, SegEndY * OutMult, FrontZ, i, seg); // Out left
                    AddPoint(SegStartX * InMult, SegStartY * InMult, BackZ, i, seg); // In right
                    AddPoint(SegEndX * InMult, SegEndY * InMult, BackZ, i, seg); // In left
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

        private Random Random = new Random();
        private int Frame = 0;

        public void Render()
        {
            if (!this.SetupDone) { return; }
            this.TubeShader.Use();

            if(this.NewData)
            {
                GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, this.TubePosition, TubeResolution, (int)this.NewLines, PixelFormat.Rgba, PixelType.UnsignedByte, this.TextureData);
                this.TubePosition = (ushort)((this.TubePosition + this.NewLines) % TUBE_LENGTH);
                this.NewData = false;
                this.NewLines = 0;
                GL.Uniform1(this.LocationDepthOffset, (float)(this.TubePosition) / TUBE_LENGTH);
                Matrix4.CreateTranslation((float)((this.Random.NextDouble() - 0.5) / 10), (float)((this.Random.NextDouble() - 0.5) / 10), 0, out this.TubeTransform);
                this.TubeTransform = Matrix4.CreateRotationZ(Frame / 30F);
                Matrix4 Rot = Matrix4.CreateRotationZ(Frame / 60F) * Matrix4.CreateRotationX((float)(Math.Sin(Frame / 100F) / 3F)) * Matrix4.CreateRotationY((float)(Math.Sin(Frame / 50F) / 5F));
                //Matrix4.CreateTranslation((float)(Math.Sin(Frame / 100F) / 3F), (float)(-Math.Sin(Frame / 50F) / 5F), 0, out this.TubeTransform);
                Matrix4 Combined = Rot;// * this.TubeTransform;
                GL.UniformMatrix4(this.LocationTransform, true, ref Combined);
            }
            Frame++;

            GL.BindVertexArray(this.VertexArrayHandle);
            GL.DrawArrays(PrimitiveType.Triangles, 0, this.VertexData.Length / 3);
        }

        public void MouseMove(object sender, MouseMoveEventArgs evt)
        {
            //Matrix4.CreateTranslation((evt.Position.X * 2F / this.HostWindow.Width) - 1, (evt.Position.Y * -2F / this.HostWindow.Height) + 1, 0, out this.TubeTransform);
        }

        public void Resize(int width, int height)
        {
            if (!this.SetupDone) { return; }

            this.TubeShader.Use();
            this.Projection = Matrix4.CreatePerspectiveFieldOfView((float)(Math.PI / 2), (float)this.HostWindow.Width / this.HostWindow.Height, 0.01F, 10F);
            GL.UniformMatrix4(this.LocationProjection, true, ref this.Projection);
        }

        public bool SupportsFormat(IVisualizerFormat format) => format is IDiscrete1D;
    }
}
