using ColorChord.NET.Visualizers;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.ES30;
using System;
using System.IO;
using System.Text;

namespace ColorChord.NET.Outputs
{
    public class DisplayOpenGL : GameWindow, IOutput
    {
        private readonly IVisualizer Source;
        private Shader Shader;

        private float[] Vertices = {
        -0.5f, -0.5f, 0.0f, //Bottom-left vertex
        0.5f, -0.5f, 0.0f, //Bottom-right vertex
         0.0f,  0.5f, 0.0f  //Top vertex
        };

        private int VertexBufferHandle;
        private int VertexArrayHandle;

        public DisplayOpenGL(IVisualizer source) : base(1280, 720, GraphicsMode.Default, "ColorChord.NET Display Output")
        {
            this.Source = source;
            Run(60D);
        }

        protected override void OnLoad(EventArgs evt)
        {
            if (this.Source is Linear Linear)
            {
                if (!Linear.IsCircular) { this.Size = new System.Drawing.Size(this.Size.Width, 60); }
            }
            else if (this.Source is Cells Cells)
            {

            }
            //...

            this.Shader = new Shader("shader.vert", "shader.frag");

            this.VertexBufferHandle = GL.GenBuffer();
            this.VertexArrayHandle = GL.GenVertexArray();
            GL.BindVertexArray(this.VertexArrayHandle);
            GL.BindBuffer(BufferTarget.ArrayBuffer, this.VertexBufferHandle);
            GL.BufferData(BufferTarget.ArrayBuffer, this.Vertices.Length * sizeof(float), this.Vertices, BufferUsageHint.StaticDraw);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);

            GL.ClearColor(0.2F, 0.2F, 0.2F, 1.0F);
            base.OnLoad(evt);
        }

        protected override void OnRenderFrame(FrameEventArgs evt)
        {
            GL.Clear(ClearBufferMask.ColorBufferBit);

            this.Shader.Use();
            GL.BindVertexArray(this.VertexArrayHandle);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

            this.Context.SwapBuffers();
            base.OnRenderFrame(evt);
        }

        protected override void OnResize(EventArgs evt)
        {
            GL.Viewport(0, 0, this.Width, this.Height);
            base.OnResize(evt);
        }

        protected override void OnUnload(EventArgs evt)
        {
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.DeleteBuffer(VertexBufferHandle);
            this.Shader.Dispose();
            base.OnUnload(evt);
        }

        public void Dispatch() { }

    }

    class Shader : IDisposable
    {
        private readonly int ShaderHandle;
        private bool IsDisposed = false;

        public Shader(string vertexPath, string fragmentPath)
        {
            int VertexShaderHandle, FragmentShaderHandle;

            string VertexShaderSource, FragmentShaderSource;

            using (StreamReader reader = new StreamReader(vertexPath, Encoding.UTF8)) { VertexShaderSource = reader.ReadToEnd(); }
            using (StreamReader reader = new StreamReader(fragmentPath, Encoding.UTF8)) { FragmentShaderSource = reader.ReadToEnd(); }

            VertexShaderHandle = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(VertexShaderHandle, VertexShaderSource);

            FragmentShaderHandle = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(FragmentShaderHandle, FragmentShaderSource);

            GL.CompileShader(VertexShaderHandle);
            string LogVertex = GL.GetShaderInfoLog(VertexShaderHandle);
            if (!string.IsNullOrEmpty(LogVertex)) { Console.WriteLine("Vertex Shader Problems Found! Log:\n" + LogVertex); }

            GL.CompileShader(FragmentShaderHandle);
            string LogFragment = GL.GetShaderInfoLog(FragmentShaderHandle);
            if (!string.IsNullOrEmpty(LogFragment)) { Console.WriteLine("Vertex Shader Problems Found! Log:\n" + LogFragment); }

            this.ShaderHandle = GL.CreateProgram();
            GL.AttachShader(this.ShaderHandle, VertexShaderHandle);
            GL.AttachShader(this.ShaderHandle, FragmentShaderHandle);
            GL.LinkProgram(this.ShaderHandle);

            GL.DetachShader(this.ShaderHandle, VertexShaderHandle);
            GL.DetachShader(this.ShaderHandle, FragmentShaderHandle);
            GL.DeleteShader(VertexShaderHandle);
            GL.DeleteShader(FragmentShaderHandle);
        }

        public void Use() => GL.UseProgram(this.ShaderHandle);

        protected virtual void Dispose(bool disposing)
        {
            if (!this.IsDisposed)
            {
                GL.DeleteProgram(this.ShaderHandle);
                this.IsDisposed = true;
            }
        }

        ~Shader() { GL.DeleteProgram(this.ShaderHandle); } // Don't rely on this.

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }

}
