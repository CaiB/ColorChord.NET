﻿using ColorChord.NET.API.Config;
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

public class Spectrum : IDisplayMode, IConfigurableAttr
{
    private readonly DisplayOpenGL HostWindow;
    private readonly NoteFinderCommon NoteFinder;
    private readonly Gen2NoteFinder? G2NoteFinder;

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

    private int VertexBufferHandle, VertexArrayHandle, TextureHandleRawBins, TextureHandlePeakBits, TextureHandleWidebandBits;
    private int LocationBinCount, LocationBPO, LocationScaleFactor, LocationExponent;

    private float[] RawDataIn;

    [ConfigFloat("ScaleFactor", 0F, 1000F, 4F)]
    private float ScaleFactor = 4F;

    [ConfigFloat("ScaleExponent", 0F, 10F, 3F)]
    private float Exponent = 3F;

    /// <summary> Whether this output is ready to accept data and draw. </summary>
    private bool SetupDone = false;

    public Spectrum(DisplayOpenGL parent, IVisualizer visualizer, Dictionary<string, object> config)
    {
        this.HostWindow = parent;
        Configurer.Configure(this, config);
        this.NoteFinder = Configurer.FindNoteFinder(config) ?? throw new Exception($"{nameof(Spectrum)} under \"{this.HostWindow.Name}\" could not find the NoteFinder to attach to.");
        this.G2NoteFinder = this.NoteFinder as Gen2NoteFinder;
        this.RawDataIn = new float[this.NoteFinder.AllBinValues.Length * 2];
    }

    public bool SupportsFormat(IVisualizerFormat format) => true;

    public void Load()
    {
        // Set options
        GL.ClearColor(0.0F, 0.0F, 0.0F, 1.0F);
        // TODO: Blending/transparency setup needed?

        // Create objects
        this.Shader = new("Passthrough2Textured.vert", "Spectrum.frag");
        this.Shader.Use();

        this.VertexBufferHandle = GL.GenBuffer();
        this.VertexArrayHandle = GL.GenVertexArray();

        this.TextureHandleRawBins = GL.GenTexture();
        GL.Uniform1(this.Shader.GetUniformLocation("TextureRawBins"), 0);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, this.TextureHandleRawBins);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R32f, this.NoteFinder.AllBinValues.Length, 1, 0, PixelFormat.Red, PixelType.Float, new float[this.NoteFinder.AllBinValues.Length]);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToBorder);

        this.TextureHandlePeakBits = GL.GenTexture();
        GL.Uniform1(this.Shader.GetUniformLocation("TexturePeakBits"), 1);
        GL.ActiveTexture(TextureUnit.Texture1);
        GL.BindTexture(TextureTarget.Texture2D, this.TextureHandlePeakBits);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToBorder);

        this.TextureHandleWidebandBits = GL.GenTexture();
        GL.Uniform1(this.Shader.GetUniformLocation("TextureWidebandBits"), 2);
        GL.ActiveTexture(TextureUnit.Texture2);
        GL.BindTexture(TextureTarget.Texture2D, this.TextureHandleWidebandBits);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToBorder);

        this.LocationBinCount = this.Shader.GetUniformLocation("BinCount");
        GL.Uniform1(this.LocationBinCount, this.NoteFinder.AllBinValues.Length);
        this.LocationBPO = this.Shader.GetUniformLocation("BinsPerOctave");
        GL.Uniform1(this.LocationBPO, this.NoteFinder.BinsPerOctave);
        this.LocationScaleFactor = this.Shader.GetUniformLocation("ScaleFactor");
        GL.Uniform1(this.LocationScaleFactor, this.ScaleFactor);
        this.LocationExponent = this.Shader.GetUniformLocation("Exponent");
        GL.Uniform1(this.LocationExponent, this.Exponent);

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

        if (this.RawDataIn.Length != this.NoteFinder.AllBinValues.Length * 2) { this.RawDataIn = new float[this.NoteFinder.AllBinValues.Length * 2]; }
        if (this.G2NoteFinder != null)
        {
            this.G2NoteFinder.AllBinValuesScaled.CopyTo(this.RawDataIn.AsSpan());
            this.G2NoteFinder.RecentBinChanges.CopyTo(this.RawDataIn, this.NoteFinder.AllBinValues.Length);
            GL.ActiveTexture(TextureUnit.Texture1);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R8ui, this.NoteFinder.AllBinValues.Length / 8, 1, 0, PixelFormat.RedInteger, PixelType.UnsignedByte, this.G2NoteFinder.PeakBits);
            GL.ActiveTexture(TextureUnit.Texture2);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R8ui, this.NoteFinder.AllBinValues.Length / 8, 1, 0, PixelFormat.RedInteger, PixelType.UnsignedByte, this.G2NoteFinder.WidebandBits);
        }
        else { this.NoteFinder.AllBinValues.CopyTo(this.RawDataIn); }

        GL.ActiveTexture(TextureUnit.Texture0);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R32f, this.NoteFinder.AllBinValues.Length, 2, 0, PixelFormat.Red, PixelType.Float, this.RawDataIn);
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
