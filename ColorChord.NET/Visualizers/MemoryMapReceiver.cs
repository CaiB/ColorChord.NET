using ColorChord.NET.API.Config;
using ColorChord.NET.API.Outputs;
using ColorChord.NET.API.Visualizers;
using ColorChord.NET.API.Visualizers.Formats;
using ColorChord.NET.Config;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Threading;

namespace ColorChord.NET.Visualizers
{
    /// <summary> Instead of taking data from the NoteFinder, this reads from shared memory and outputs the data. Only meant for testing purposes. </summary>
    /// <remarks> If you actually want to use this, let me know and I'll expand functionality. Currently very minimal, as it was just to test memory-mapped output. </remarks>
    public class MemoryMapReceiver : IVisualizer, IDiscrete1D
    {
        /// <summary> A unique name for this visualizer instance, used for referring to it from other components. </summary>
        public string Name { get; private init; }

        /// <summary> The name of the memory-mapped file to look for. </summary>
        [ConfigString("MapName", "UnconfiguredMapName")]
        private readonly string MapName = "UnconfiguredMapName";

        /// <summary> The name of the map mutex to look for. </summary>
        [ConfigString("MutexName", "UnconfiguredMutexName")]
        private readonly string MapMutexName = "UnconfiguredMutexName";

        /// <summary> How many times per second the output should be updated. </summary>
        [ConfigInt("FrameRate", 0, 1000, 60)]
        public int FrameRate { get; set; } = 60;

        /// <summary> The number of milliseconds to wait between output updates. </summary>
        private int FramePeriod => 1000 / this.FrameRate;

        /// <summary> All outputs that need to be notified when new data is available. </summary>
        private readonly List<IOutput> Outputs = new();

        /// <summary> The shared memory to read colour data from. </summary>
        private MemoryMappedFile? Map;

        /// <summary> Used to read from the shared memory. </summary>
        private MemoryMappedViewStream? MapView;

        /// <summary> Reads colour data from the shared memory. </summary>
        private BinaryReader? MapReader;

        /// <summary> Used to synchronize writes/reads. </summary>
        private Mutex? MapMutex;

        /// <summary> How many LEDs are in the source data. </summary>
        private int LEDCount;

        /// <summary> The colour data from the source. </summary>
        private uint[] LEDData = Array.Empty<uint>();

        /// <summary> The thread for receiving data from the shared memory. </summary>
        private Thread? ReceiveThread;

        private bool Stopping = false;

        public MemoryMapReceiver(string name, Dictionary<string, object> config)
        {
            this.Name = name;
            Configurer.Configure(this, config);
        }

        public void Start()
        {
            if (!OperatingSystem.IsWindows()) { throw new InvalidOperationException("MemoryMapReceiver is only supported on Windows."); }
            if (this.Map != null) { throw new InvalidOperationException("Cannot start already receiving memory map receiver."); }
            try
            {
                this.Map = MemoryMappedFile.OpenExisting(this.MapName);
                this.MapMutex = Mutex.OpenExisting(this.MapMutexName);
                this.MapView = this.Map.CreateViewStream(0, 0);
                this.MapReader = new BinaryReader(this.MapView);
                this.Stopping = false;
                this.ReceiveThread = new Thread(DoReceive);
                this.ReceiveThread.Start();
            }
            catch(FileNotFoundException) { Log.Error("Could not find memory map by name \"" + this.MapName + "\"."); }
        }

        private void DoReceive()
        {
            if (this.MapMutex == null || this.MapReader == null) { Log.Warn("MemoryMapReceiver does not have a valid source to get data from."); return; }
            Stopwatch Timer = new();
            while (!this.Stopping)
            {
                Timer.Restart();
                this.MapMutex.WaitOne();
                this.MapReader.BaseStream.Seek(0, SeekOrigin.Begin);
                
                this.LEDCount = (int)this.MapReader.ReadUInt32();
                if (this.LEDData.Length != this.LEDCount) { this.LEDData = new uint[this.LEDCount]; }
                for (int i = 0; i < this.LEDCount; i++)
                {
                    uint Data = 0;
                    Data |= (uint)(this.MapReader.ReadByte() << 16);
                    Data |= (uint)(this.MapReader.ReadByte() << 8);
                    Data |= this.MapReader.ReadByte();
                    this.LEDData[i] = Data;
                }
                this.MapMutex.ReleaseMutex();
                foreach (IOutput output in this.Outputs) { output.Dispatch(); }
                int WaitTime = (int)(this.FramePeriod - Timer.ElapsedMilliseconds);
                if (WaitTime > 0) { Thread.Sleep(WaitTime); }
            }
        }

        public void Stop()
        {
            this.Stopping = true;
            this.ReceiveThread?.Join();
            this.MapReader?.Close();
            this.MapReader = null;
            this.MapView?.Dispose();
            this.MapView = null;
            this.MapMutex?.Dispose();
            this.MapMutex = null;
            this.Map?.Dispose();
            this.Map = null;
        }

        public void AttachOutput(IOutput output) { if (output != null) { this.Outputs.Add(output); } }

        public int GetCountDiscrete() => this.LEDCount;
        public uint[] GetDataDiscrete() => this.LEDData;
    }
}
