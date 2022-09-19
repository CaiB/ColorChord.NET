using ColorChord.NET.API;
using ColorChord.NET.API.Config;
using ColorChord.NET.API.Controllers;
using ColorChord.NET.API.Outputs;
using ColorChord.NET.API.Visualizers;
using ColorChord.NET.API.Visualizers.Formats;
using ColorChord.NET.Config;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Threading;

namespace ColorChord.NET.Outputs;

public class MemoryMap : IOutput, IControllableAttr
{
    /// <summary> Instance name, for identification and attaching controllers. </summary>
    public string Name { get; private init; }

    /// <summary> Where colour data is taken from. </summary>
    private readonly IVisualizer Source;

    /// <summary> The location where we store colour data for other processes to consume. </summary>
    private MemoryMappedFile? Map;

    /// <summary> Used to access the shared memory. </summary>
    private MemoryMappedViewStream? MapView;

    /// <summary> Used to write data into the shared memory. </summary>
    private BinaryWriter? MapWriter;
    
    /// <summary> The mutex to synchronize writes/reads from the shared memory. </summary>
    private Mutex? MapMutex;

    /// <summary> Whether the map is set up and ready for data to be written. </summary>
    private bool Ready;

    [Controllable(ConfigNames.ENABLE)]
    [ConfigBool(ConfigNames.ENABLE, true)]
    private bool Enable = true;

    public MemoryMap(string name, Dictionary<string, object> config)
    {
        this.Name = name;
        IVisualizer? Source = Configurer.FindVisualizer(this, config, typeof(IDiscrete1D));
        if (Source == null) { throw new Exception($"{GetType().Name} \"{name}\" could not find requested visualizer."); }
        this.Source = Source;
        Configurer.Configure(this, config);

        this.Source.AttachOutput(this);
    }

    public void Dispatch()
    {
        if(!this.Ready || !this.Enable) { return; }
        this.MapMutex!.WaitOne();

        IDiscrete1D Source1D = (IDiscrete1D)this.Source;
        this.MapWriter!.Seek(0, SeekOrigin.Begin); // Go to the beginning of the region.
        this.MapWriter.Write((uint)Source1D.GetCountDiscrete());

        for(int LED = 0; LED < Source1D.GetCountDiscrete(); LED++)
        {
            uint LEDData = Source1D.GetDataDiscrete()[LED];
            this.MapWriter.Write((byte)(LEDData >> 16)); // R
            this.MapWriter.Write((byte)(LEDData >> 8)); // G
            this.MapWriter.Write((byte)LEDData); // B
        }
        this.MapWriter.Flush();

        this.MapMutex.ReleaseMutex();
    }

    public void Start()
    {
        if (this.Map != null) { throw new InvalidOperationException("MemoryMap sender cannot be started when already running."); }

        string MapName = "ColorChord.NET-" + this.Name;
        // TODO: Make size configurable, or autoscale to LEDCount?
        this.Map = MemoryMappedFile.CreateNew(MapName, 65536 * 4); // 65536 pixels, 4 bytes each.
        this.MapMutex = new Mutex(false, "ColorChord.NET-Mutex-" + this.Name);
        this.MapView = this.Map.CreateViewStream(0, 0);
        this.MapWriter = new BinaryWriter(this.MapView);
        this.Ready = true;

        Log.Info("Memory map \"" + MapName + "\" ready.");
    }

    public void SettingWillChange(int controlID) { }
    public void SettingChanged(int controlID) { }

    public void Stop()
    {
        this.Ready = false;
        this.MapMutex?.Dispose();
        this.MapMutex = null;
        this.MapWriter?.Close();
        this.MapWriter = null;
        this.MapView?.Dispose();
        this.MapView = null;
        this.Map?.Dispose();
        this.Map = null;
    }
}
