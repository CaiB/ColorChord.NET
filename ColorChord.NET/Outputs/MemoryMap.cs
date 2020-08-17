using ColorChord.NET.Visualizers;
using ColorChord.NET.Visualizers.Formats;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Threading;

namespace ColorChord.NET.Outputs
{
    public class MemoryMap : IOutput
    {
        /// <summary> Where colour data is taken from. </summary>
        private IVisualizer Source;

        /// <summary> Instance name, for identification and attaching controllers. </summary>
        private readonly string Name;

        /// <summary> The location where we store colour data for other processes to consume. </summary>
        private MemoryMappedFile Map;

        /// <summary> Used to access the shared memory. </summary>
        private MemoryMappedViewStream MapView;

        /// <summary> Used to write data into the shared memory. </summary>
        private BinaryWriter MapWriter;
        
        /// <summary> The mutex to synchronize writes/reads from the shared memory. </summary>
        private Mutex MapMutex;

        /// <summary> Whether the map is set up and ready for data to be written. </summary>
        private bool Ready;

        public MemoryMap(string name)
        {
            this.Name = name;
        }

        public void ApplyConfig(Dictionary<string, object> options)
        {
            Log.Info("Reading config for " + GetType().Name + " \"" + this.Name + "\".");

            if (!options.ContainsKey("VisualizerName") || !ColorChord.VisualizerInsts.ContainsKey((string)options["VisualizerName"])) { Log.Error("Tried to create " + GetType().Name + " with missing or invalid visualizer."); return; }
            this.Source = ColorChord.VisualizerInsts[(string)options["VisualizerName"]];
            if (!(this.Source is IDiscrete1D)) { Log.Error(GetType().Name + " only supports " + nameof(IDiscrete1D) + " visualizers."); }
            this.Source.AttachOutput(this);

            ConfigTools.WarnAboutRemainder(options, typeof(IOutput));
        }

        public void Dispatch()
        {
            if(!this.Ready) { return; }
            this.MapMutex.WaitOne();

            IDiscrete1D Source1D = (IDiscrete1D)this.Source;
            this.MapWriter.Seek(0, SeekOrigin.Begin); // Go to the beginning of the region.
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
            if (Map != null) { throw new InvalidOperationException("MemoryMap sender cannot be started when already running."); }

            string MapName = "ColorChord.NET-" + this.Name;
            // TODO: Make size configurable, or autoscale to LEDCount?
            this.Map = MemoryMappedFile.CreateNew(MapName, 65536 * 4); // 65536 pixels, 4 bytes each.
            this.MapMutex = new Mutex(false, "ColorChord.NET-Mutex-" + this.Name);
            this.MapView = this.Map.CreateViewStream(0, 0);
            this.MapWriter = new BinaryWriter(this.MapView);
            this.Ready = true;

            Log.Info("Memory map \"" + MapName + "\" ready.");
        }

        public void Stop()
        {
            this.Ready = false;
            this.MapMutex.Dispose();
            this.MapMutex = null;
            this.MapWriter.Close();
            this.MapWriter = null;
            this.MapView.Dispose();
            this.MapView = null;
            this.Map.Dispose();
            this.Map = null;
        }
    }
}
