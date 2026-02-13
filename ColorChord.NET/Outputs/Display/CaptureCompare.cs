using ColorChord.NET.API;
using ColorChord.NET.API.Config;
using ColorChord.NET.API.Outputs.Display;
using ColorChord.NET.API.Visualizers;
using ColorChord.NET.API.Visualizers.Formats;
using ColorChord.NET.Config;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace ColorChord.NET.Outputs.Display;

public class CaptureCompare : IDisplayMode, IConfigurableAttr
{
    private readonly DisplayOpenGL HostWindow;
    private readonly IDiscrete1D DataSourceA, DataSourceB;

    [ConfigInt("HistoryLength", 16, 16384, 768)]
    private int TextureHeight = 768;

    private Shader? Shader;

    private bool NewData;
    private int VertexBufferHandle, VertexArrayHandle;
    private int TextureHandleA, TextureHandleB, TextureHandleCaptureA, TextureHandleCaptureB;
    private int TextureWidthA, TextureWidthB;//, TextureLengthA, TextureLengthB;

    private int LocationAdvanceALive, LocationAdvanceBLive, LocationAdvanceACapture, LocationAdvanceBCapture, LocationACaptureBounds, LocationBCaptureBounds;

    private uint[]? NewTextureDataA, NewTextureDataB;
    private int UploadedTextureLocA, UploadedTextureLocB, PopulatedTextureLocA, PopulatedTextureLocB;

    private bool DoCaptureA, DoCaptureB, PushNewBounds;
    private Vector2 CaptureABounds, CaptureBBounds;

    private bool SetupDone = false;

    private static float[] GeometryData = new float[]
    { // X    Y   U   V
       -1F, -1F, 0F, 1F,
        1F, -1F, 1F, 1F,
        1F,  1F, 1F, 0F,
       -1F, -1F, 0F, 1F,
        1F,  1F, 1F, 0F,
       -1F,  1F, 0F, 0F
    };

    public CaptureCompare(DisplayOpenGL parent, IVisualizer visualizer, Dictionary<string, object> config)
    {
        this.HostWindow = parent;
        this.DataSourceA = visualizer as IDiscrete1D ?? throw new Exception($"{nameof(CaptureCompare)} cannot use the provided visualizer, as it doesn't support {nameof(IDiscrete1D)} output mode.");
        
        IDiscrete1D? DataSourceBTemp = null;
        if (config.TryGetValue("SecondaryVisualizer", out object? SecondVizNameObj) && SecondVizNameObj is string SecondVizName) { DataSourceBTemp = Configurer.FindComponentByName(Component.Visualizers, SecondVizName) as IDiscrete1D; }
        if (DataSourceBTemp == null) { Log.Warn($"{nameof(CaptureCompare)} could not find the secondary visualizer, and is instead using the primary one for both inputs. You may set it using \"SecondaryVisualizer\"."); }
        this.DataSourceB = DataSourceBTemp ?? this.DataSourceA;
        config.Remove("SecondaryVisualizer");
        Configurer.Configure(this, config);
        if (this.DataSourceB != this.DataSourceA) { ((IVisualizer)this.DataSourceB).AttachOutput(this.HostWindow); }
    }

    [MemberNotNull(nameof(NewTextureDataA), nameof(NewTextureDataB))]
    public void Load()
    {
        // Make shader and geometry storage
        this.Shader = new Shader("Passthrough2Textured.vert", "CaptureCompare.frag");
        this.VertexBufferHandle = GL.GenBuffer();
        this.VertexArrayHandle = GL.GenVertexArray();

        // Activate shader and make texture
        this.Shader.Use();
        GL.BindTexture(TextureTarget.Texture2D, 0);
        this.TextureHandleA = GL.GenTexture();
        this.TextureHandleB = GL.GenTexture();
        this.TextureHandleCaptureA = GL.GenTexture();
        this.TextureHandleCaptureB = GL.GenTexture();
        this.TextureWidthA = this.DataSourceA.GetCountDiscrete();
        this.TextureWidthB = this.DataSourceB.GetCountDiscrete();
        this.NewTextureDataA = new uint[this.TextureWidthA * TextureHeight];
        this.NewTextureDataB = new uint[this.TextureWidthB * TextureHeight];

        // Activate texture
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, this.TextureHandleA);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, this.TextureWidthA, TextureHeight, 0, PixelFormat.Rgba, PixelType.UnsignedByte, this.NewTextureDataA);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);

        GL.ActiveTexture(TextureUnit.Texture1);
        GL.BindTexture(TextureTarget.Texture2D, this.TextureHandleB);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, this.TextureWidthB, TextureHeight, 0, PixelFormat.Rgba, PixelType.UnsignedByte, this.NewTextureDataB);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);

        GL.ActiveTexture(TextureUnit.Texture2);
        GL.BindTexture(TextureTarget.Texture2D, this.TextureHandleCaptureA);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, this.TextureWidthA, TextureHeight, 0, PixelFormat.Rgba, PixelType.UnsignedByte, this.NewTextureDataA);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);

        GL.ActiveTexture(TextureUnit.Texture3);
        GL.BindTexture(TextureTarget.Texture2D, this.TextureHandleCaptureB);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, this.TextureWidthB, TextureHeight, 0, PixelFormat.Rgba, PixelType.UnsignedByte, this.NewTextureDataB);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);

        GL.ActiveTexture(TextureUnit.Texture0);

        // Load geometry
        GL.BindVertexArray(this.VertexArrayHandle);
        GL.BindBuffer(BufferTarget.ArrayBuffer, this.VertexBufferHandle);
        GL.BufferData(BufferTarget.ArrayBuffer, GeometryData.Length * sizeof(float), GeometryData, BufferUsageHint.StaticDraw);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));
        GL.EnableVertexAttribArray(1);

        // Set uniforms
        GL.Uniform1(this.Shader.GetUniformLocation("TextureUnitALive"), 0);
        GL.Uniform1(this.Shader.GetUniformLocation("TextureUnitBLive"), 1);
        GL.Uniform1(this.Shader.GetUniformLocation("TextureUnitACapture"), 2);
        GL.Uniform1(this.Shader.GetUniformLocation("TextureUnitBCapture"), 3);
        GL.Uniform1(this.Shader.GetUniformLocation("HorizontalSplit"), 0.5F);
        this.LocationAdvanceALive = this.Shader.GetUniformLocation("AdvanceALive");
        this.LocationAdvanceBLive = this.Shader.GetUniformLocation("AdvanceBLive");
        this.LocationAdvanceACapture = this.Shader.GetUniformLocation("AdvanceACapture");
        this.LocationAdvanceBCapture = this.Shader.GetUniformLocation("AdvanceBCapture");
        this.LocationACaptureBounds = this.Shader.GetUniformLocation("CaptureABounds");
        this.LocationBCaptureBounds = this.Shader.GetUniformLocation("CaptureBBounds");

        this.SetupDone = true;

        this.HostWindow.KeyDown += OnKeyDown;
        this.HostWindow.MouseDown += OnMouseDown;
        this.HostWindow.MouseUp += OnMouseUp;
    }

    public bool SupportsFormat(IVisualizerFormat format) => format is IDiscrete1D;

    public void Dispatch()
    {
        if (!this.SetupDone) { return; }

        {
            int Count = this.DataSourceA.GetCountDiscrete();
            uint[] Data = this.DataSourceA.GetDataDiscrete();
            if (this.PopulatedTextureLocA >= TextureHeight - 1) { return; } // Drop this data, we are too behind
            lock (this.NewTextureDataA!)
            {
                Array.Copy(Data, 0, this.NewTextureDataA, this.PopulatedTextureLocA * TextureWidthA, Count);
                this.PopulatedTextureLocA++;
            }
        }
        {
            int Count = this.DataSourceB.GetCountDiscrete();
            uint[] Data = this.DataSourceB.GetDataDiscrete();
            if (this.PopulatedTextureLocB >= TextureHeight - 1) { return; } // Drop this data, we are too behind
            lock (this.NewTextureDataB!)
            {
                Array.Copy(Data, 0, this.NewTextureDataB, this.PopulatedTextureLocB * TextureWidthB, Count);
                this.PopulatedTextureLocB++;
            }
        }

        this.NewData = true;
    }

    public void Render()
    {
        if (!this.SetupDone) { return; }
        this.Shader!.Use();

        ((IVisualizer)this.DataSourceB).NoteFinder?.UpdateOutputs(); // TODO: Janky?

        if (this.NewData)
        {
            if (this.PopulatedTextureLocA != 0)
            {
                GL.ActiveTexture(TextureUnit.Texture0);
                lock (this.NewTextureDataA!)
                {
                    if (this.UploadedTextureLocA + this.PopulatedTextureLocA < TextureHeight)
                    {
                        GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, this.UploadedTextureLocA, this.TextureWidthA, this.PopulatedTextureLocA, PixelFormat.Rgba, PixelType.UnsignedByte, this.NewTextureDataA);
                    }
                    else
                    {
                        int BottomCount = TextureHeight - this.UploadedTextureLocA;
                        int TopCount = this.PopulatedTextureLocA - BottomCount;
                        GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, this.UploadedTextureLocA, this.TextureWidthA, BottomCount, PixelFormat.Rgba, PixelType.UnsignedByte, this.NewTextureDataA);
                        GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, this.TextureWidthA, TopCount, PixelFormat.Rgba, PixelType.UnsignedByte, ref this.NewTextureDataA[this.TextureWidthA * BottomCount]);
                    }
                    this.UploadedTextureLocA = (this.UploadedTextureLocA + this.PopulatedTextureLocA) % TextureHeight;
                    GL.Uniform1(this.LocationAdvanceALive, (float)this.UploadedTextureLocA / TextureHeight);
                    this.PopulatedTextureLocA = 0;
                }
            }
            if (this.PopulatedTextureLocB != 0)
            {
                GL.ActiveTexture(TextureUnit.Texture1);
                lock (this.NewTextureDataB!)
                {
                    if (this.UploadedTextureLocB + this.PopulatedTextureLocB < TextureHeight)
                    {
                        GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, this.UploadedTextureLocB, this.TextureWidthB, this.PopulatedTextureLocB, PixelFormat.Rgba, PixelType.UnsignedByte, this.NewTextureDataB);
                    }
                    else
                    {
                        int BottomCount = TextureHeight - this.UploadedTextureLocB;
                        int TopCount = this.PopulatedTextureLocB - BottomCount;
                        GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, this.UploadedTextureLocB, this.TextureWidthB, BottomCount, PixelFormat.Rgba, PixelType.UnsignedByte, this.NewTextureDataB);
                        GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, this.TextureWidthB, TopCount, PixelFormat.Rgba, PixelType.UnsignedByte, ref this.NewTextureDataB[this.TextureWidthB * BottomCount]);
                    }
                    this.UploadedTextureLocB = (this.UploadedTextureLocB + this.PopulatedTextureLocB) % TextureHeight;
                    GL.Uniform1(this.LocationAdvanceBLive, (float)this.UploadedTextureLocB / TextureHeight);
                    this.PopulatedTextureLocB = 0;
                }
            }
        }

        if (this.DoCaptureA)
        {
            GL.CopyImageSubData(this.TextureHandleA, ImageTarget.Texture2D, 0, 0, 0, 0, this.TextureHandleCaptureA, ImageTarget.Texture2D, 0, 0, 0, 0, this.TextureWidthA, TextureHeight, 1);
            GL.Uniform1(this.LocationAdvanceACapture, (float)this.UploadedTextureLocA / TextureHeight);
            this.DoCaptureA = false;
        }
        if (this.DoCaptureB)
        {
            GL.CopyImageSubData(this.TextureHandleB, ImageTarget.Texture2D, 0, 0, 0, 0, this.TextureHandleCaptureB, ImageTarget.Texture2D, 0, 0, 0, 0, this.TextureWidthB, TextureHeight, 1);
            GL.Uniform1(this.LocationAdvanceBCapture, (float)this.UploadedTextureLocB / TextureHeight);
            this.DoCaptureB = false;
        }

        if (this.PushNewBounds)
        {
            GL.Uniform2(this.LocationACaptureBounds, this.CaptureABounds);
            GL.Uniform2(this.LocationBCaptureBounds, this.CaptureBBounds);
            this.PushNewBounds = false;
        }
        
        GL.BindVertexArray(this.VertexArrayHandle);
        GL.DrawArrays(PrimitiveType.Triangles, 0, GeometryData.Length / 4);
    }

    private void OnMouseUp(MouseButtonEventArgs args)
    {
        float HalfWidth = this.HostWindow.Width / 2F;
        float HalfHeight = this.HostWindow.Height / 2F;
        if (this.HostWindow.MousePosition.X <= HalfWidth) // Capture
        {
            float EndPos = this.HostWindow.MousePosition.X / HalfWidth;
            if (this.HostWindow.MousePosition.Y <= HalfHeight)
            {
                this.CaptureABounds = new(MathF.Min(this.CaptureABounds.X, EndPos), MathF.Max(this.CaptureABounds.X, EndPos));
            }
            else
            {
                this.CaptureBBounds = new(MathF.Min(this.CaptureBBounds.X, EndPos), MathF.Max(this.CaptureBBounds.X, EndPos));
            }
            this.PushNewBounds = true;
        }
    }

    private void OnMouseDown(MouseButtonEventArgs args)
    {
        float HalfWidth = this.HostWindow.Width / 2F;
        float HalfHeight = this.HostWindow.Height / 2F;
        if (this.HostWindow.MousePosition.X <= HalfWidth) // Capture
        {
            if (this.HostWindow.MousePosition.Y <= HalfHeight) { this.CaptureABounds = new(this.HostWindow.MousePosition.X / HalfWidth); }
            else { this.CaptureBBounds = new(this.HostWindow.MousePosition.X / HalfWidth); }
        }
    }

    private void OnKeyDown(KeyboardKeyEventArgs evt)
    {
        if (evt.Key == Keys.A)
        {
            this.DoCaptureA = true;
            this.CaptureABounds = new(0F, 1F);
            this.PushNewBounds = true;
        }
        if (evt.Key == Keys.Z)
        {
            this.DoCaptureB = true;
            this.CaptureBBounds = new(0F, 1F);
            this.PushNewBounds = true;
        }
        if (evt.Key == Keys.Q)
        {
            this.CaptureABounds = new(0F, 1F);
            this.CaptureBBounds = new(0F, 1F);
            this.PushNewBounds = true;
        }
    }

    public void Resize(int width, int height) { }

    public void Close()
    {
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        GL.DeleteBuffer(this.VertexBufferHandle);
        this.Shader?.Dispose();
    }
}
