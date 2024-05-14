using ColorChord.NET.API;
using ColorChord.NET.API.Config;
using ColorChord.NET.API.Outputs;
using ColorChord.NET.API.Visualizers;
using ColorChord.NET.API.Visualizers.Formats;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ColorChord.NET.Extensions.ImageOutput;

/// <summary>Writes the output of an <see cref="IDiscrete1D"/> visualizer to image files.</summary>
public class ImageFile : IOutput
{
    public string Name { get; private init; }

    private readonly IDiscrete1D Source;

    /// <summary> The width that output images should be limited to. If more data is received, it will be split into subsequent images. </summary>
    /// <remarks> Some applications may not like reading images wider than 65535 pixels, hence the limit. </remarks>
    [ConfigInt("ImageWidth", 128, 64000, 16384)]
    public int ImageWidth = 16384;

    /// <summary> Pixel buffers to store data for the current image, as well as the next one. </summary>
    /// <remarks> These are swapped in between, to avoid allocating more buffers during runtime. </remarks>
    private Rgba32[] DataA, DataB;

    /// <summary>Which buffer is currently being written to.</summary>
    private bool WritingToA = true;

    /// <summary>The X position in the buffer that will be written to next.</summary>
    private int WriteHead = 0;

    /// <summary>The height of the output images, correposnding to the number of data elements of the attached visualizer.</summary>
    private int Height = 1;

    /// <summary>The index of the image currently being written. Starts at 1 and increments every time an image file is finished.</summary>
    private int ImageIndex = 1;

    public ImageFile(string name, Dictionary<string, object> config)
    {
        this.Name = name;
        IVisualizer? Source = ColorChordAPI.Configurer.FindVisualizer(this, config, typeof(IDiscrete1D)) ?? throw new Exception($"{GetType().Name} \"{name}\" could not find requested visualizer.");
        this.Source = (IDiscrete1D)Source;
        ColorChordAPI.Configurer.Configure(this, config);

        this.Height = this.Source.GetCountDiscrete();
        this.DataA = new Rgba32[this.ImageWidth * this.Height];
        this.DataB = new Rgba32[this.ImageWidth * this.Height]; // TODO: Handle this count changing during runtime

        Source.AttachOutput(this);
    }

    public void Dispatch()
    {
        uint[] NewData = this.Source.GetDataDiscrete();
        Rgba32[] CurrentData = this.WritingToA ? this.DataA : this.DataB;
        for (int y = 0; y < this.Height; y++)
        {
            CurrentData[(this.ImageWidth * y) + this.WriteHead] = new((byte)(NewData[y] >> 16), (byte)(NewData[y] >> 8), (byte)NewData[y], 0xFF);
        }
        ++this.WriteHead;
        if (this.WriteHead == this.ImageWidth)
        {
            Image<Rgba32> OutputImg = Image.LoadPixelData<Rgba32>(CurrentData, this.ImageWidth, this.Height);
            OutputImg.SaveAsPngAsync($"Output_{this.ImageIndex++}.png"); // TODO: Is this safe?
            //if (this.WritingToA) { this.DataA = new Rgba32[this.ImageWidth]; }
            //else { this.DataB = new Rgba32[this.ImageWidth]; }
            this.WriteHead = 0;
            this.WritingToA = !this.WritingToA;
        }
    }

    public void Start() { }

    public void Stop()
    {
        Rgba32[] Source = this.WritingToA ? this.DataA : this.DataB;
        Image<Rgba32> OutputImage = new(this.WriteHead, this.Height);
        OutputImage.ProcessPixelRows(ImageContent =>
        {
            for (int y = 0; y < ImageContent.Height; y++)
            {
                Span<Rgba32> ImageRow = ImageContent.GetRowSpan(y);
                Source.AsSpan().Slice(y * this.ImageWidth, this.WriteHead).CopyTo(ImageRow);
            }
        });
        OutputImage.SaveAsPng($"Output_{this.ImageIndex++}.png");
    }
}
