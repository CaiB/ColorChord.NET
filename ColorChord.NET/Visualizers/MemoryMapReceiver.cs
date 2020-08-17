using ColorChord.NET.Outputs;
using ColorChord.NET.Visualizers.Formats;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace ColorChord.NET.Visualizers
{
    /// <summary> Instead of taking data from the NoteFinder, this reads from shared memory and outputs the data. Only meant for testing purposes. </summary>
    /// <remarks> If you actually want to use this, let me know and I'll expand functionality. Currently very minimal, as it was just to test memory-mapped output. </remarks>
    public class MemoryMapReceiver : IVisualizer, IDiscrete1D
    {
        private readonly List<IOutput> Outputs = new List<IOutput>();

        /// <summary> How many LEDs are in the source data. </summary>
        private int LEDCount;

        /// <summary> The colour data from the source. </summary>
        private uint[] LEDData;

        /// <summary> The name of the memory-mapped file to look for. </summary>
        private string MapName;

        /// <summary> The name of the map mutex to look for. </summary>
        private string MapMutexName;

        /// <summary> The shared memory to read colour data from. </summary>
        private MemoryMappedFile Map;

        /// <summary> Used to read from the shared memory. </summary>
        private MemoryMappedViewStream MapView;

        /// <summary> Reads colour data from the shared memory. </summary>
        private BinaryReader MapReader;

        /// <summary> Used to synchronize writes/reads. </summary>
        private Mutex MapMutex;

        /// <summary> The thread for receiving data from the shared memory. </summary>
        private Thread ReceiveThread;

        private bool Stopping = false;

        public int FramePeriod = 1000 / 90;

        public string Name { get; private set; }

        public MemoryMapReceiver(string name) { this.Name = name; }

        public void ApplyConfig(Dictionary<string, object> options)
        {
            Log.Info("Reading config for " + GetType().Name + " \"" + this.Name + "\".");
            this.MapName = ConfigTools.CheckString(options, "MapName", "UnconfiguredMapName", true);
            this.MapMutexName = ConfigTools.CheckString(options, "MutexName", "UnconfiguredMutexName", true);
            this.FramePeriod = 1000 / ConfigTools.CheckInt(options, "FrameRate", 0, 1000, 60, true);
            ConfigTools.WarnAboutRemainder(options, typeof(IOutput));
        }

        public void Start()
        {
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
            catch(FileNotFoundException)
            {
                Log.Error("Could not find memory map by name \"" + this.MapName + "\".");
            }
        }

        private void DoReceive()
        {
            Stopwatch Timer = new Stopwatch();
            while (!this.Stopping)
            {
                Timer.Restart();
                this.MapMutex.WaitOne();
                this.MapReader.BaseStream.Seek(0, SeekOrigin.Begin);
                
                this.LEDCount = (int)this.MapReader.ReadUInt32();
                if (this.LEDData == null || this.LEDData.Length != this.LEDCount) { this.LEDData = new uint[this.LEDCount]; }
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
            this.ReceiveThread.Join();
            this.MapReader.Close();
            this.MapReader = null;
            this.MapView.Dispose();
            this.MapView = null;
            this.MapMutex.Dispose();
            this.MapMutex = null;
            this.Map.Dispose();
            this.Map = null;
        }

        public void AttachOutput(IOutput output) { if (output != null) { this.Outputs.Add(output); } }

        public int GetCountDiscrete() => this.LEDCount;
        public uint[] GetDataDiscrete() => this.LEDData;
    }
}
