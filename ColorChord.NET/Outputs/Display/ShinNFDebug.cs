using ColorChord.NET.API.Config;
using ColorChord.NET.API.NoteFinder;
using ColorChord.NET.API.Outputs.Display;
using ColorChord.NET.API.Visualizers;
using ColorChord.NET.API.Visualizers.Formats;
using ColorChord.NET.Config;
using ColorChord.NET.NoteFinder;
using OpenTK.Graphics.OpenGL4;
using System;
using System.Collections.Generic;

namespace ColorChord.NET.Outputs.Display;

public class ShinNFDebug : IDisplayMode, IConfigurableAttr
{
    private readonly DisplayOpenGL HostWindow;

    private Shader? Shader;

    private float[] Geometry = new float[] // {[X,Y]} x 6
    { // X   Y 
        -1,  1, 0,  1, // Top-Left
        -1, -1, 0, -1, // Bottom-Left
         1, -1, 1, -1, // Bottom-Right
         1, -1, 1, -1, // Bottom-Right
         1,  1, 1,  1, // Top-Right
        -1,  1, 0,  1  // Top-Left
    };

    private int VertexBufferHandle, VertexArrayHandle, TextureHandle;
    private int LocationBinCount, LocationScaleFactor;

    private float ScaleFactor = 3F;

    /// <summary> Whether this output is ready to accept data and draw. </summary>
    private bool SetupDone = false;

    public ShinNFDebug(DisplayOpenGL parent, IVisualizer visualizer, Dictionary<string, object> config)
    {
        this.HostWindow = parent;
        Configurer.Configure(this, config);
    }

    public bool SupportsFormat(IVisualizerFormat format) => true;

    public void Load()
    {
        // Set options
        GL.ClearColor(0.0F, 0.0F, 0.0F, 1.0F);
        // TODO: Blending/transparency setup needed?

        // Create objects
        this.Shader = new("Passthrough2Textured.vert", "ShinNFDebug.frag");
        this.Shader.Use();

        this.VertexBufferHandle = GL.GenBuffer();
        this.VertexArrayHandle = GL.GenVertexArray();

        this.TextureHandle = GL.GenTexture();
        GL.Uniform1(this.Shader.GetUniformLocation("Texture"), 0);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, this.TextureHandle);

        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R32f, ShinNoteFinderDFT.BinCount, 1, 0, PixelFormat.Red, PixelType.Float, new float[ShinNoteFinderDFT.BinCount]);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToBorder);

        this.LocationBinCount = this.Shader.GetUniformLocation("BinCount");
        GL.Uniform1(this.LocationBinCount, ShinNoteFinderDFT.BinCount);
        this.LocationScaleFactor = this.Shader.GetUniformLocation("ScaleFactor");
        GL.Uniform1(this.LocationScaleFactor, this.ScaleFactor);

        // Prepare and upload vertex data
        GL.BindVertexArray(this.VertexArrayHandle);
        GL.BindBuffer(BufferTarget.ArrayBuffer, this.VertexBufferHandle);
        GL.BufferData(BufferTarget.ArrayBuffer, Geometry.Length * sizeof(float), Geometry, BufferUsageHint.DynamicDraw);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));
        GL.EnableVertexAttribArray(1);

        this.SetupDone = true;
    }

    public void Render()
    {
        if (!this.SetupDone) { return; }
        this.Shader!.Use();

        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R32f, ShinNoteFinderDFT.BinCount, 1, 0, PixelFormat.Red, PixelType.Float, NoteFinderCommon.AllBinValues);
        GL.BindVertexArray(this.VertexArrayHandle);
        GL.DrawArrays(PrimitiveType.Triangles, 0, Geometry.Length / 2);
    }

    public void Dispatch()
    {
        
    }

    public void Resize(int width, int height)
    {
        
    }

    public void Close()
    {
        this.Shader?.Dispose();
    }
}
