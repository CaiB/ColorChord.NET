using ColorChord.NET.API;
using ColorChord.NET.API.Config;
using ColorChord.NET.API.NoteFinder;
using ColorChord.NET.API.Outputs;
using ColorChord.NET.API.Visualizers;
using ColorChord.NET.API.Visualizers.Formats;

namespace ColorChord.NET.Extensions.FileOutputs;

public class CSVFile : IOutput
{
    public string Name { get; private init; }
    private readonly IDiscrete1D Source;

    [ConfigInt("BufferLength", 1, 1000000, 4096)]
    public int BufferLength = 4096;

    [ConfigBool("Enabled", true)]
    public bool Enabled { get; set; } = true;

    private readonly int Width;
    private readonly float[] Data;

    private int WriteHead = 0;

    private readonly NoteFinderCommon NoteFinder;

    public CSVFile(string name, Dictionary<string, object> config)
    {
        this.Name = name;
        IVisualizer? Source = ColorChordAPI.Configurer.FindVisualizer(this, config, typeof(IDiscrete1D)) ?? throw new Exception($"{GetType().Name} \"{name}\" could not find requested visualizer.");
        this.Source = (IDiscrete1D)Source;
        ColorChordAPI.Configurer.Configure(this, config);

        Source.AttachOutput(this);
        this.NoteFinder = ColorChordAPI.Configurer.FindNoteFinder(new()) ?? throw new Exception("Could not find NoteFinder");
        this.Width = this.NoteFinder.AllBinValues.Length;
        this.Data = new float[this.Width * this.BufferLength];
    }

    public void Dispatch()
    {
        if (!this.Enabled) { return; }
        if (this.WriteHead == this.BufferLength)
        {
            this.Enabled = false;
            Log.Info("CSV file filled");
            return;
        }
        this.NoteFinder.AllBinValues.CopyTo(((Span<float>)this.Data).Slice(this.WriteHead * this.Width));
        this.WriteHead++;
    }

    public void Start() { }

    public void Stop()
    {
        using (StreamWriter Writer = new(new FileStream("G2NFData.csv", FileMode.Create)))
        {
            float[] Buffer = new float[this.Width];
            for (int i = 0; i < this.BufferLength; i++)
            {
                ((Span<float>)this.Data).Slice(i * this.Width, this.Width).CopyTo(Buffer);
                string Line = string.Join(',', Buffer);
                Writer.WriteLine(Line);
            }
        }
    }
}
