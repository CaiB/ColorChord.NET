using ColorChord.NET.API.Config;
using ColorChord.NET.API.Outputs.Display;
using ColorChord.NET.API.Visualizers;
using ColorChord.NET.API.Visualizers.Formats;
using ColorChord.NET.Config;
using ColorChord.NET.NoteFinder;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;

namespace ColorChord.NET.Outputs.Display;

public class RadialPoles : IDisplayMode, IConfigurableAttr
{
    private readonly DisplayOpenGL HostWindow;

    [ConfigFloat("CenterSize", 0.0F, 1.0F, 0.2F)]
    public float CenterSize; // TODO: Make these controllable

    [ConfigFloat("ScaleFactor", 0.0F, 100.0F, 0.7F)]
    public float ScaleFactor;

    [ConfigFloat("ScaleExponent", 0.0F, 10.0F, 1.6F)]
    public float ScaleExponent;

    private Shader? Shader;

    private float[] Geometry = new float[] // {[X,Y]} x 6
        { // X   Y 
            -1,  1, -1,  1, // Top-Left
            -1, -1, -1, -1, // Bottom-Left
             1, -1,  1, -1, // Bottom-Right
             1, -1,  1, -1, // Bottom-Right
             1,  1,  1,  1, // Top-Right
            -1,  1, -1,  1  // Top-Left
        };

    private int TextureHandle, VertexBufferHandle, VertexArrayHandle;
    private int LocationResolution, LocationPoleCount, LocationScaleFactor, LocationExponent, LocationIsConnected, LocationAdvance, LocationCenterBlank, LocationWidthOverride;

    private int LastPoleCount;

    /// <summary> The current window and framebuffer resolution. (Width, Height) </summary>
    private Vector2 Resolution = new(600, 600);

    /// <summary> Whether this output is ready to accept data and draw. </summary>
    private bool SetupDone = false;

    public RadialPoles(DisplayOpenGL parent, IVisualizer visualizer, Dictionary<string, object> config)
    {
        this.HostWindow = parent;
        Configurer.Configure(this, config);
    }

    public bool SupportsFormat(IVisualizerFormat format) => true;

    public int PoleCount => BaseNoteFinder.OctaveBinValues.Length;

    public void Load()
    {
        // Set options
        GL.ClearColor(0.0F, 0.0F, 0.0F, 1.0F);
        // TODO: Blending/transparency setup needed?

        // Create objects
        this.Shader = new("Passthrough2Textured.vert", "RadialPoles.frag");
        this.Shader.Use();
        this.VertexBufferHandle = GL.GenBuffer();
        this.VertexArrayHandle = GL.GenVertexArray();

        GL.BindTexture(TextureTarget.Texture2D, 0);
        this.TextureHandle = GL.GenTexture();
        GL.Uniform1(this.Shader.GetUniformLocation("TextureUnit"), 0);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, this.TextureHandle);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToBorder);

        int PoleCount = this.PoleCount;
        this.LastPoleCount = PoleCount;
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R32f, PoleCount, 1, 0, PixelFormat.Red, PixelType.Float, new float[PoleCount]);

        this.LocationResolution = this.Shader.GetUniformLocation("Resolution");
        GL.Uniform2(this.LocationResolution, ref this.Resolution);
        this.LocationPoleCount = this.Shader.GetUniformLocation("PoleCount");
        GL.Uniform1(this.LocationPoleCount, PoleCount);
        this.LocationIsConnected = this.Shader.GetUniformLocation("IsConnected");
        this.LocationScaleFactor = this.Shader.GetUniformLocation("ScaleFactor");
        GL.Uniform1(this.LocationScaleFactor, this.ScaleFactor);
        this.LocationExponent = this.Shader.GetUniformLocation("Exponent");
        GL.Uniform1(this.LocationExponent, this.ScaleExponent);
        this.LocationCenterBlank = this.Shader.GetUniformLocation("CenterBlank");
        GL.Uniform1(this.LocationCenterBlank, this.CenterSize);
        this.LocationWidthOverride = this.Shader.GetUniformLocation("WidthOverride");
        this.LocationAdvance = this.Shader.GetUniformLocation("Advance");

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

    public void Dispatch()
    {
        
    }

    public void Render()
    {
        if (!this.SetupDone) { return; }
        this.Shader!.Use();

        // TODO: Set uniforms

        float[] NoteData = BaseNoteFinder.OctaveBinValues;
        if (this.LastPoleCount != NoteData.Length) { GL.Uniform1(this.LocationPoleCount, NoteData.Length); }
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R32f, NoteData.Length, 1, 0, PixelFormat.Red, PixelType.Float, NoteData);
        this.LastPoleCount = NoteData.Length;

        GL.BindVertexArray(this.VertexArrayHandle);
        GL.DrawArrays(PrimitiveType.Triangles, 0, Geometry.Length / 2);
    }

    public void Resize(int width, int height)
    {
        int MinLength = Math.Min(width, height);
        this.Resolution = new(MinLength, MinLength);

        if (!this.SetupDone) { return; }
        this.Shader!.Use();
        GL.Uniform2(this.LocationResolution, ref this.Resolution);

        this.Geometry = GenGeometry(width, height);
        GL.BindVertexArray(this.VertexArrayHandle);
        GL.BindBuffer(BufferTarget.ArrayBuffer, this.VertexBufferHandle);
        GL.BufferData(BufferTarget.ArrayBuffer, this.Geometry.Length * sizeof(float), this.Geometry, BufferUsageHint.DynamicDraw);
    }

    private static float[] GenGeometry(int width, int height)
    {
        float Y = (float)width / Math.Max(width, height);
        float X = (float)height / Math.Max(width, height);
        return new[]
        { // X   Y 
            -X,  Y, -1,  1, // Top-Left
            -X, -Y, -1, -1, // Bottom-Left
             X, -Y,  1, -1, // Bottom-Right
             X, -Y,  1, -1, // Bottom-Right
             X,  Y,  1,  1, // Top-Right
            -X,  Y, -1,  1  // Top-Left
        };
    }

    public void Close() { }
}
