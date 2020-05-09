using ColorChord.NET.Visualizers;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.ES30;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ColorChord.NET.Outputs
{
    public class DisplayOpenGL : GameWindow, IOutput
    {
        public IVisualizer Source { get; private set; }
        private Shader Shader;

        public readonly string Name;

        /// <summary> Empty space in the window on the left side. </summary>
        public float PaddingLeft { get; set; }

        /// <summary> Empty space in the window on the right side. </summary>
        public float PaddingRight { get; set; }

        /// <summary> Empty space in the window on the top. </summary>
        public float PaddingTop { get; set; }

        /// <summary> Empty space in the window on the bottom. </summary>
        public float PaddingBottom { get; set; }

        /// <summary> The width of the window, in pixels. </summary>
        public int WindowWidth
        {
            get => this.Size.Width;
            set => this.Size = new Size(value, this.Size.Height);
        }

        /// <summary> The height of the window, in pixels. </summary>
        public int WindowHeight
        {
            get => this.Size.Height;
            set => this.Size = new Size(this.Size.Width, value);
        }

        // TODO: This can be optimized a ton. The linear output should not be using discrete "LED"s, but instead just drawing a rectangle for each colour, greatly reducing load, and removing the blockiness of the output.
        // Layout: X, Y, Z, R, G, B
        private float[] Vertices = new float[36];

        private int VertexBufferHandle;
        private int VertexArrayHandle;

        public DisplayOpenGL(string name) : base(1280, 150, GraphicsMode.Default, "ColorChord.NET Display Output \"" + name + '"')
        {
            this.Name = name;
        }

        public void Start() { Run(60D); }
        public void Stop() { } // TODO: Stop

        public void ApplyConfig(Dictionary<string, object> options)
        {
            Log.Info("Reading config for DisplayOpenGL \"" + this.Name + "\".");
            if (!options.ContainsKey("visualizerName") || !ColorChord.VisualizerInsts.ContainsKey((string)options["visualizerName"])) { Log.Error("Tried to create DisplayOpenGL with missing or invalid visualizer."); return; }
            this.Source = ColorChord.VisualizerInsts[(string)options["visualizerName"]];
            this.Source.AttachOutput(this);

            this.PaddingLeft = ConfigTools.CheckFloat(options, "paddingLeft", 0, 2, 0, true);
            this.PaddingRight = ConfigTools.CheckFloat(options, "paddingRight", 0, 2, 0, true);
            this.PaddingTop = ConfigTools.CheckFloat(options, "paddingTop", 0, 2, 0, true);
            this.PaddingBottom = ConfigTools.CheckFloat(options, "paddingBottom", 0, 2, 0, true);

            this.WindowWidth = ConfigTools.CheckInt(options, "windowWidth", 10, 4000, 1280, true);
            this.WindowHeight = ConfigTools.CheckInt(options, "windowHeight", 10, 4000, 100, true);
            ConfigTools.WarnAboutRemainder(options, typeof(IOutput));
        }

        protected override void OnLoad(EventArgs evt)
        {
            this.VSync = VSyncMode.On;
            //...

            GL.DebugMessageCallback(DebugCallback, IntPtr.Zero);

            this.Shader = new Shader("shader.vert", "shader.frag");

            this.VertexBufferHandle = GL.GenBuffer();
            this.VertexArrayHandle = GL.GenVertexArray();
            GL.BindVertexArray(this.VertexArrayHandle);
            GL.BindBuffer(BufferTarget.ArrayBuffer, this.VertexBufferHandle);
            GL.BufferData(BufferTarget.ArrayBuffer, this.Vertices.Length * sizeof(float), this.Vertices, BufferUsageHint.StreamDraw);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), 3 * sizeof(float));
            GL.EnableVertexAttribArray(1);
            GL.ClearColor(0.2F, 0.2F, 0.2F, 1.0F);
            base.OnLoad(evt);
        }

        protected override void OnRenderFrame(FrameEventArgs evt)
        {
            GL.Clear(ClearBufferMask.ColorBufferBit);
            
            this.Shader.Use();
            GL.BindVertexArray(this.VertexArrayHandle);
            GL.BindBuffer(BufferTarget.ArrayBuffer, this.VertexBufferHandle);
            GL.BufferData(BufferTarget.ArrayBuffer, this.Vertices.Length * sizeof(float), this.Vertices, BufferUsageHint.StreamDraw);
            GL.DrawArrays(PrimitiveType.Triangles, 0, this.Vertices.Length / 6);

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
            GL.DeleteBuffer(this.VertexBufferHandle);
            this.Shader.Dispose();
            base.OnUnload(evt);
        }

        protected override void OnClosed(EventArgs e)
        {
            Environment.Exit(0);
            base.OnClosed(e);
        }

        protected void DebugCallback(DebugSource source, DebugType type, int id, DebugSeverity severity, int length, IntPtr message, IntPtr userParam)
        {
            Log.Warn("OpenGL Output: Type \"" + type + "\", Severity \"" + severity + "\", Message \"" + Marshal.PtrToStringAnsi(message) + "\".");
        }

        public void Dispatch()
        {
            if (this.Vertices != null)
            {
                if (this.Source is Linear || this.Source is Cells)
                {
                    int Count = (this.Source is Linear) ? ((Linear)this.Source).LEDCount : ((Cells)this.Source).LEDCount;
                    if (this.Vertices.Length != Count * 6 * 6) { this.Vertices = new float[Count * 6 * 6]; } // 6 vertices per "LED", with 6 floats each.
                    float SizeX = (2F - (this.PaddingLeft + this.PaddingRight)) / Count;
                    float SizeY = (2F - (this.PaddingTop + this.PaddingBottom)) / Count; // TODO: Consider moving this out of the loop.
                    for (int i = 0; i < Count; i++)
                    {
                        for (int v = 0; v < 6; v++) // Top-Left, Bottom-Left, Bottom-Right | Top-Left, Top-Right, Bottom-Right
                        {
                            switch (v) // X
                            {
                                case 0:
                                case 1:
                                case 3:
                                    this.Vertices[(i * 6 * 6) + (v * 6) + 0] = -1F + this.PaddingLeft + (SizeX * i); break;
                                case 2:
                                case 4:
                                case 5:
                                    this.Vertices[(i * 6 * 6) + (v * 6) + 0] = -1F + this.PaddingLeft + (SizeX * (i + 1)); break;
                            }
                            switch (v) // Y
                            {
                                case 0:
                                case 3:
                                case 4:
                                    this.Vertices[(i * 6 * 6) + (v * 6) + 1] = 1F - this.PaddingTop; break;
                                case 1:
                                case 2:
                                case 5:
                                    this.Vertices[(i * 6 * 6) + (v * 6) + 1] = -1F + this.PaddingBottom; break;
                            }
                            this.Vertices[(i * 6 * 6) + (v * 6) + 2] = 0F; // Z
                            this.Vertices[(i * 6 * 6) + (v * 6) + 3] = ((this.Source is Linear) ? ((Linear)this.Source).OutputData[(i * 3) + 0] : ((Cells)this.Source).OutputData[(i * 3) + 0]) / 256F; // R
                            this.Vertices[(i * 6 * 6) + (v * 6) + 4] = ((this.Source is Linear) ? ((Linear)this.Source).OutputData[(i * 3) + 1] : ((Cells)this.Source).OutputData[(i * 3) + 1]) / 256F; // G
                            this.Vertices[(i * 6 * 6) + (v * 6) + 5] = ((this.Source is Linear) ? ((Linear)this.Source).OutputData[(i * 3) + 2] : ((Cells)this.Source).OutputData[(i * 3) + 2]) / 256F; // B
                        }
                    }
                }
            }
        }

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
            if (!string.IsNullOrEmpty(LogVertex)) { Log.Error("Vertex Shader Problems Found! Log:\n" + LogVertex); }

            GL.CompileShader(FragmentShaderHandle);
            string LogFragment = GL.GetShaderInfoLog(FragmentShaderHandle);
            if (!string.IsNullOrEmpty(LogFragment)) { Log.Error("Fragment Shader Problems Found! Log:\n" + LogFragment); }

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
