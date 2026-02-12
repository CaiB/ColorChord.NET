using ColorChord.NET.API.Config;
using ColorChord.NET.API.NoteFinder;
using ColorChord.NET.API.Outputs.Display;
using ColorChord.NET.API.Visualizers;
using ColorChord.NET.API.Visualizers.Formats;
using ColorChord.NET.Config;
using ColorChord.NET.NoteFinder;
using ColorChord.NET.Outputs;
using ColorChord.NET.Outputs.Display;
using OpenTK.Graphics.OpenGL4;
using System.Reflection;

namespace ColorChord.NET.Extensions.FFTNoteFinder;

public class SpectrumDual : IDisplayMode, IConfigurableAttr
{
    private readonly DisplayOpenGL HostWindow;
    private readonly NoteFinderCommon NoteFinder, NoteFinderBot;
    private readonly Gen2NoteFinder? G2NoteFinder;
    private readonly FFT? FFT;

    private Shader? ShaderTop, ShaderBot;

    const bool OVERLAP = true;

    private float[] GeometryTop = new float[] // {[X,Y]} x 6
    { // X   Y 
        -1,  1, 0,  1, // Top-Left
        -1,  0, 0, (OVERLAP ? 0 : -1), // Bottom-Left
         1,  0, 1, (OVERLAP ? 0 : -1), // Bottom-Right
         1,  0, 1, (OVERLAP ? 0 : -1), // Bottom-Right
         1,  1, 1,  1, // Top-Right
        -1,  1, 0,  1  // Top-Left
    };

    private float[] GeometryBot = new float[] // {[X,Y]} x 6
    { // X   Y 
        -1,  0, 0, (OVERLAP ? 0 : 1), // Top-Left
        -1, -1, 0, -1, // Bottom-Left
         1, -1, 1, -1, // Bottom-Right
         1, -1, 1, -1, // Bottom-Right
         1,  0, 1, (OVERLAP ? 0 : 1), // Top-Right
        -1,  0, 0, (OVERLAP ? 0 : 1)  // Top-Left
    };

    private int VertexBufferHandleTop, VertexArrayHandleTop, TextureHandleRawBinsTop, TextureHandlePeakBitsTop, TextureHandleWidebandBitsTop;
    private int VertexBufferHandleBot, VertexArrayHandleBot, TextureHandleRawBinsBot;
    private int LocationBinCountTop, LocationBPOTop, LocationScaleFactorTop, LocationExponentTop;
    private int LocationBinCountBot, LocationBinFreqSizeBot, LocationScaleFactorBot, LocationExponentBot;

    private float[] RawDataInTop, RawDataInBot;

    [ConfigFloat("ScaleFactor", 0F, 1000F, 4F)]
    private float ScaleFactor = 4F;

    [ConfigFloat("ScaleExponent", 0F, 10F, 3F)]
    private float Exponent = 3F;

    /// <summary> Whether this output is ready to accept data and draw. </summary>
    private bool SetupDone = false;

    public SpectrumDual(DisplayOpenGL parent, IVisualizer visualizer, Dictionary<string, object> config)
    {
        this.HostWindow = parent;
        Configurer.Configure(this, config);
        this.NoteFinder = Configurer.FindNoteFinder(config) ?? throw new Exception($"{nameof(SpectrumDual)} under \"{this.HostWindow.Name}\" could not find the NoteFinder to attach to.");
        if (!(config.TryGetValue("SecondaryNoteFinder", out object? SecondNFNameObj) && SecondNFNameObj is string SecondNFName)) { throw new Exception("cannot find second NF name in config"); }
        this.NoteFinderBot = Configurer.FindComponentByName(API.Component.NoteFinder, SecondNFName) as NoteFinderCommon ?? throw new Exception($"{nameof(SpectrumDual)} under \"{this.HostWindow.Name}\" could not find the NoteFinderBot (secondary) to attach to. Specify with \"NoteFinderSecondary\".");
        this.G2NoteFinder = this.NoteFinder as Gen2NoteFinder;
        this.FFT = this.NoteFinderBot as FFT;
        this.RawDataInTop = new float[this.NoteFinder.AllBinValues.Length * 2];
        this.RawDataInBot = new float[this.NoteFinderBot.AllBinValues.Length];
    }

    public bool SupportsFormat(IVisualizerFormat format) => true;

    public void Load()
    {
        // Set options
        GL.ClearColor(0.0F, 0.0F, 0.0F, 1.0F);
        // TODO: Blending/transparency setup needed?

        // Create objects for top pane
        this.ShaderTop = new("Passthrough2Textured.vert", "Spectrum.frag");
        this.ShaderTop.Use();

        this.VertexBufferHandleTop = GL.GenBuffer();
        this.VertexArrayHandleTop = GL.GenVertexArray();

        this.TextureHandleRawBinsTop = GL.GenTexture();
        GL.Uniform1(this.ShaderTop.GetUniformLocation("TextureRawBins"), 0);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, this.TextureHandleRawBinsTop);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R32f, this.NoteFinder.AllBinValues.Length, 1, 0, PixelFormat.Red, PixelType.Float, new float[this.NoteFinder.AllBinValues.Length]);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToBorder);

        this.TextureHandlePeakBitsTop = GL.GenTexture();
        GL.Uniform1(this.ShaderTop.GetUniformLocation("TexturePeakBits"), 1);
        GL.ActiveTexture(TextureUnit.Texture1);
        GL.BindTexture(TextureTarget.Texture2D, this.TextureHandlePeakBitsTop);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToBorder);

        this.TextureHandleWidebandBitsTop = GL.GenTexture();
        GL.Uniform1(this.ShaderTop.GetUniformLocation("TextureWidebandBits"), 2);
        GL.ActiveTexture(TextureUnit.Texture2);
        GL.BindTexture(TextureTarget.Texture2D, this.TextureHandleWidebandBitsTop);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToBorder);

        this.LocationBinCountTop = this.ShaderTop.GetUniformLocation("BinCount");
        GL.Uniform1(this.LocationBinCountTop, this.NoteFinder.AllBinValues.Length);
        this.LocationBPOTop = this.ShaderTop.GetUniformLocation("BinsPerOctave");
        GL.Uniform1(this.LocationBPOTop, this.NoteFinder.BinsPerOctave);
        this.LocationScaleFactorTop = this.ShaderTop.GetUniformLocation("ScaleFactor");
        GL.Uniform1(this.LocationScaleFactorTop, this.ScaleFactor);
        this.LocationExponentTop = this.ShaderTop.GetUniformLocation("Exponent");
        GL.Uniform1(this.LocationExponentTop, this.Exponent);

        // Prepare and upload vertex data
        GL.BindVertexArray(this.VertexArrayHandleTop);
        GL.BindBuffer(BufferTarget.ArrayBuffer, this.VertexBufferHandleTop);
        GL.BufferData(BufferTarget.ArrayBuffer, GeometryTop.Length * sizeof(float), GeometryTop, BufferUsageHint.DynamicDraw);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));
        GL.EnableVertexAttribArray(1);


        // =========
        // Create objects for bottom pane
        this.ShaderBot = new("Passthrough2Textured.vert", "#ColorChord.NET.Extensions.FFTNoteFinder.SpectrumLinearIn.frag", null, Assembly.GetAssembly(GetType()));
        this.ShaderBot.Use();

        this.VertexBufferHandleBot = GL.GenBuffer();
        this.VertexArrayHandleBot = GL.GenVertexArray();

        this.TextureHandleRawBinsBot = GL.GenTexture();
        GL.Uniform1(this.ShaderBot.GetUniformLocation("TextureRawBins"), 0);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, this.TextureHandleRawBinsBot);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R32f, this.NoteFinderBot.AllBinValues.Length, 1, 0, PixelFormat.Red, PixelType.Float, new float[this.NoteFinderBot.AllBinValues.Length]);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToBorder);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToBorder);


        this.LocationBinCountBot = this.ShaderBot.GetUniformLocation("BinCount");
        GL.Uniform1(this.LocationBinCountBot, this.NoteFinderBot.AllBinValues.Length);
        this.LocationBinFreqSizeBot = this.ShaderBot.GetUniformLocation("BinFreqSize");
        GL.Uniform1(this.LocationBinFreqSizeBot, (float)this.FFT.WindowSize / this.FFT.SampleRate);
        this.LocationScaleFactorBot = this.ShaderBot.GetUniformLocation("ScaleFactor");
        GL.Uniform1(this.LocationScaleFactorBot, this.ScaleFactor);
        this.LocationExponentBot = this.ShaderBot.GetUniformLocation("Exponent");
        GL.Uniform1(this.LocationExponentBot, this.Exponent);

        // Prepare and upload vertex data
        GL.BindVertexArray(this.VertexArrayHandleBot);
        GL.BindBuffer(BufferTarget.ArrayBuffer, this.VertexBufferHandleBot);
        GL.BufferData(BufferTarget.ArrayBuffer, GeometryBot.Length * sizeof(float), GeometryBot, BufferUsageHint.DynamicDraw);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));
        GL.EnableVertexAttribArray(1);

        this.SetupDone = true;
    }

    public void Render()
    {
        if (!this.SetupDone) { return; }
        this.ShaderTop!.Use();

        if (this.RawDataInTop.Length != this.NoteFinder.AllBinValues.Length * 2) { this.RawDataInTop = new float[this.NoteFinder.AllBinValues.Length * 2]; }
        if (this.G2NoteFinder != null)
        {
            this.G2NoteFinder.AllBinValuesScaled.CopyTo(this.RawDataInTop.AsSpan());
            this.G2NoteFinder.RecentBinChanges.CopyTo(this.RawDataInTop, this.NoteFinder.AllBinValues.Length);
            GL.ActiveTexture(TextureUnit.Texture1);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R8ui, this.NoteFinder.AllBinValues.Length / 8, 1, 0, PixelFormat.RedInteger, PixelType.UnsignedByte, this.G2NoteFinder.PeakBits);
            GL.ActiveTexture(TextureUnit.Texture2);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R8ui, this.NoteFinder.AllBinValues.Length / 8, 1, 0, PixelFormat.RedInteger, PixelType.UnsignedByte, this.G2NoteFinder.WidebandBits);
        }
        else { this.NoteFinder.AllBinValues.CopyTo(this.RawDataInTop); }

        GL.ActiveTexture(TextureUnit.Texture0);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R32f, this.NoteFinder.AllBinValues.Length, 2, 0, PixelFormat.Red, PixelType.Float, this.RawDataInTop);
        GL.BindVertexArray(this.VertexArrayHandleTop);
        GL.DrawArrays(PrimitiveType.Triangles, 0, GeometryTop.Length / 2);

        //
        this.NoteFinderBot.UpdateOutputs();
        this.ShaderBot!.Use();
        if (this.RawDataInBot.Length != this.NoteFinderBot.AllBinValues.Length) { this.RawDataInBot = new float[this.NoteFinderBot.AllBinValues.Length]; }
        this.NoteFinderBot.AllBinValues.CopyTo(this.RawDataInBot);
        
        GL.Uniform1(this.LocationBinFreqSizeBot, (float)this.FFT.WindowSize / this.FFT.SampleRate);
        
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.R32f, this.RawDataInBot.Length, 1, 0, PixelFormat.Red, PixelType.Float, this.RawDataInBot);
        GL.BindVertexArray(this.VertexArrayHandleBot);
        GL.DrawArrays(PrimitiveType.Triangles, 0, GeometryBot.Length / 2);
        
    }

    public void Dispatch()
    {

    }

    public void Resize(int width, int height)
    {

    }

    public void Close()
    {
        this.ShaderTop?.Dispose();
        this.ShaderBot?.Dispose();
    }
}
