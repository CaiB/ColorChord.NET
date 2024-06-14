using ColorChord.NET.API;
using System;
using System.IO;
using System.Reflection;
using System.Text;
using OpenTK.Graphics.ES30;

namespace ColorChord.NET.Outputs.Display
{
    public class Shader : IDisposable
    {
        private const string PATH_PREFIX = "ColorChord.NET.Outputs.Display.Shaders.";

        private readonly int ShaderHandle;
        private bool IsDisposed = false;

        public Shader(string vertexPath, string fragmentPath)
        {
            int VertexShaderHandle, FragmentShaderHandle;

            string VertexShaderSource, FragmentShaderSource;

            Assembly Asm = Assembly.GetExecutingAssembly();
            using (Stream? VertexStream = Asm.GetManifestResourceStream(PATH_PREFIX + vertexPath))
            {
                if (VertexStream == null) { throw new Exception($"Could not load vertex shader \"{PATH_PREFIX}{vertexPath}\""); }
                using (StreamReader VertexReader = new(VertexStream, Encoding.UTF8)) { VertexShaderSource = VertexReader.ReadToEnd(); }
            }
            using (Stream? FragmentStream = Asm.GetManifestResourceStream(PATH_PREFIX + fragmentPath))
            {
                if (FragmentStream == null) { throw new Exception($"Could not load fragment shader \"{PATH_PREFIX}{fragmentPath}\""); }
                using (StreamReader FragmentReader = new(FragmentStream, Encoding.UTF8)) { FragmentShaderSource = FragmentReader.ReadToEnd(); }
            }

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

        public int GetUniformLocation(string name) => GL.GetUniformLocation(this.ShaderHandle, name);

        protected virtual void Dispose(bool disposing)
        {
            if (!this.IsDisposed)
            {
                GL.DeleteProgram(this.ShaderHandle);
                this.IsDisposed = true;
            }
        }

        ~Shader() { Dispose(false); } // Don't rely on this.

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
