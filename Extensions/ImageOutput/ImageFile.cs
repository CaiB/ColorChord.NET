using ColorChord.NET.API;
using ColorChord.NET.API.Config;
using ColorChord.NET.API.Outputs;
using ColorChord.NET.API.Visualizers;
using ColorChord.NET.API.Visualizers.Formats;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ColorChord.NET.Extensions.ImageOutput;

public class ImageFile : IOutput
{
    public string Name { get; private init; }

    private IDiscrete1D Source;

    [ConfigInt("ImageWidth", 128, 64000, 16384)]
    public int ImageWidth = 16384;

    private Image<Rgba32> DataA, DataB;
    private bool WritingToA = true;
    private int WriteHead = 0;
    private int Height = 1;
    private int ImageIndex = 1;

    public ImageFile(string name, Dictionary<string, object> config)
    {
        this.Name = name;
        IVisualizer? Source = ColorChordAPI.Configurer.FindVisualizer(this, config, typeof(IDiscrete1D)) ?? throw new Exception($"{GetType().Name} \"{name}\" could not find requested visualizer.");
        this.Source = (IDiscrete1D)Source;
        ColorChordAPI.Configurer.Configure(this, config);

        this.Height = this.Source.GetCountDiscrete();
        this.DataA = new(this.ImageWidth, this.Height);
        this.DataB = new(this.ImageWidth, this.Height); // TODO: Handle this count changing during runtime

        Source.AttachOutput(this);
    }

    public void Dispatch()
    {
        uint[] NewData = this.Source.GetDataDiscrete();
        if (this.WritingToA)
        {
            for (int y = 0; y < this.Height; y++)
            {
                this.DataA[this.WriteHead, y] = new((byte)(NewData[y] >> 16), (byte)(NewData[y] >> 8), (byte)NewData[y], 0xFF);
            }
            ++this.WriteHead;
            if (this.WriteHead == this.ImageWidth)
            {
                this.WriteHead = 0;
                this.WritingToA = false;
                this.DataA.SaveAsPngAsync($"Output_{this.ImageIndex++}.png");
                this.DataA = new(this.ImageWidth, this.Height); // TODO: Is this safe?
            }
        }
        else
        {
            for (int y = 0; y < this.Height; y++)
            {
                this.DataB[this.WriteHead, y] = new((byte)(NewData[y] >> 16), (byte)(NewData[y] >> 8), (byte)NewData[y], 0xFF);
            }
            ++this.WriteHead;
            if (this.WriteHead == this.ImageWidth)
            {
                this.WriteHead = 0;
                this.WritingToA = true;
                this.DataB.SaveAsPngAsync($"Output_{this.ImageIndex++}.png");
                this.DataB = new(this.ImageWidth, this.Height);
            }
        }
    }

    public void Start() { }

    public void Stop()
    {
        if (this.WritingToA) { this.DataA.SaveAsPng($"Output_{this.ImageIndex++}.png"); }
        else { this.DataB.SaveAsPng($"Output_{this.ImageIndex++}.png"); }
    }
}
