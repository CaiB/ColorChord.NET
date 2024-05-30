using ColorChord.NET.API;
using ColorChord.NET.API.Outputs.Display;
using ColorChord.NET.API.Visualizers;
using ColorChord.NET.API.Visualizers.Formats;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System;

namespace ColorChord.NET.Outputs.Display;

public class CaptureCompare : IDisplayMode
{
    private readonly DisplayOpenGL HostWindow;
    private readonly IDiscrete1D DataSourceA, DataSourceB;

    private const int TEX_HEIGHT = 128;

    private Shader? Shader;

    private bool NewData;
    private int VertexBufferHandle, VertexArrayHandle;
    private int TextureHandleA, TextureHandleB, TextureHandleCaptureA, TextureHandleCaptureB;
    private int TextureWidthA, TextureWidthB;//, TextureLengthA, TextureLengthB;

    private int LocationAdvanceALive, LocationAdvanceBLive, LocationAdvanceACapture, LocationAdvanceBCapture;

    private uint[] NewTextureDataA, NewTextureDataB;
    private int UploadedTextureLocA, UploadedTextureLocB, PopulatedTextureLocA, PopulatedTextureLocB;

    private bool DoCaptureA, DoCaptureB;

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

    public CaptureCompare(DisplayOpenGL parent, IVisualizer visualizer)
    {
        this.HostWindow = parent;
        this.DataSourceA = visualizer as IDiscrete1D ?? throw new Exception($"{nameof(CaptureCompare)} cannot use the provided visualizer, as it doesn't support {nameof(IDiscrete1D)} output mode.");
        this.DataSourceB = this.DataSourceA; // TODO: Actually read the second one
    }

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
        this.NewTextureDataA = new uint[this.TextureWidthA * TEX_HEIGHT];
        this.NewTextureDataB = new uint[this.TextureWidthB * TEX_HEIGHT];

        // Activate texture
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, this.TextureHandleA);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, this.TextureWidthA, TEX_HEIGHT, 0, PixelFormat.Rgba, PixelType.UnsignedByte, this.NewTextureDataA);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);

        GL.ActiveTexture(TextureUnit.Texture1);
        GL.BindTexture(TextureTarget.Texture2D, this.TextureHandleB);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, this.TextureWidthB, TEX_HEIGHT, 0, PixelFormat.Rgba, PixelType.UnsignedByte, this.NewTextureDataB);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);

        GL.ActiveTexture(TextureUnit.Texture2);
        GL.BindTexture(TextureTarget.Texture2D, this.TextureHandleCaptureA);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, this.TextureWidthA, TEX_HEIGHT, 0, PixelFormat.Rgba, PixelType.UnsignedByte, this.NewTextureDataA);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);

        GL.ActiveTexture(TextureUnit.Texture3);
        GL.BindTexture(TextureTarget.Texture2D, this.TextureHandleCaptureB);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, this.TextureWidthB, TEX_HEIGHT, 0, PixelFormat.Rgba, PixelType.UnsignedByte, this.NewTextureDataB);
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

        this.SetupDone = true;

        this.HostWindow.UpdateFrame += UpdateFrame;
    }

    public bool SupportsFormat(IVisualizerFormat format) => format is IDiscrete1D;

    public void Dispatch()
    {
        if (!this.SetupDone) { return; }
        bool IsFromA = true;
        IDiscrete1D Source = IsFromA ? this.DataSourceA : this.DataSourceB;

        int Count = Source.GetCountDiscrete();
        uint[] Data = Source.GetDataDiscrete();
        if (PopulatedTextureLocA == TEX_HEIGHT - 1) { return; } // Drop this data, we are too behind
        lock (NewTextureDataA)
        {
            Array.Copy(Data, 0, NewTextureDataA, PopulatedTextureLocA * TextureWidthA, Count);
            PopulatedTextureLocA++;
        }
        this.NewData = true;
    }

    public void Render()
    {
        if (!this.SetupDone) { return; }
        this.Shader!.Use();

        if (this.NewData)
        {
            if (this.PopulatedTextureLocA != 0)
            {
                GL.ActiveTexture(TextureUnit.Texture0);
                lock (NewTextureDataA)
                {
                    GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, this.UploadedTextureLocA, this.TextureWidthA, this.PopulatedTextureLocA, PixelFormat.Rgba, PixelType.UnsignedByte, this.NewTextureDataA);
                    this.UploadedTextureLocA = (this.UploadedTextureLocA + this.PopulatedTextureLocA) % TEX_HEIGHT;
                    GL.Uniform1(this.LocationAdvanceALive, (float)this.UploadedTextureLocA / TEX_HEIGHT);
                    this.PopulatedTextureLocA = 0;
                }
            }
            if (this.PopulatedTextureLocB != 0)
            {

            }
        }

        if (this.DoCaptureA)
        {
            GL.CopyImageSubData(this.TextureHandleA, ImageTarget.Texture2D, 0, 0, 0, 0, this.TextureHandleCaptureA, ImageTarget.Texture2D, 0, 0, 0, 0, this.TextureWidthA, TEX_HEIGHT, 1);
            GL.Uniform1(this.LocationAdvanceACapture, (float)this.UploadedTextureLocA / TEX_HEIGHT);
            this.DoCaptureA = false;
        }
        if (this.DoCaptureB)
        {
            GL.CopyImageSubData(this.TextureHandleA, ImageTarget.Texture2D, 0, 0, 0, 0, this.TextureHandleCaptureB, ImageTarget.Texture2D, 0, 0, 0, 0, this.TextureWidthA, TEX_HEIGHT, 1);
            GL.Uniform1(this.LocationAdvanceBCapture, (float)this.UploadedTextureLocA / TEX_HEIGHT);
            this.DoCaptureB = false;
        }
        

        GL.BindVertexArray(this.VertexArrayHandle);
        GL.DrawArrays(PrimitiveType.Triangles, 0, GeometryData.Length / 4);


    }

    public void UpdateFrame(FrameEventArgs evt)
    {
        KeyboardState KeyState = this.HostWindow.KeyboardState;
        if (KeyState.IsKeyDown(Keys.A)) { this.DoCaptureA = true; }
        if (KeyState.IsKeyDown(Keys.Z)) { this.DoCaptureB = true; }
    }

    public void Resize(int width, int height) { }

    public void Close()
    {
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        GL.DeleteBuffer(this.VertexBufferHandle);
        this.Shader?.Dispose();
    }
}
