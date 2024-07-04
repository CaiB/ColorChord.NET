using ColorChord.NET.API;
using ColorChord.NET.API.Config;
using ColorChord.NET.API.NoteFinder;
using ColorChord.NET.API.Outputs;
using ColorChord.NET.API.Visualizers;
using ColorChord.NET.API.Visualizers.Formats;
using ColorChord.NET.Config;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace ColorChord.NET.Visualizers
{
    /// <summary> Instead of taking data from the NoteFinder, this receives UDP packets and outputs the data. Only meant for testing purposes. </summary>
    /// <remarks> If you actually want to use this, let me know and I'll expand functionality. Currently very minimal, as it was just to test UDP output. </remarks>
    public class UDPReceiver1D : IVisualizer, IDiscrete1D
    {
        /// <summary> A unique name for this visualizer instance, used for referring to it from other components. </summary>
        public string Name { get; private init; }

        public NoteFinderCommon? NoteFinder => null;

        /// <summary> The port to listen on. </summary>
        [ConfigInt("Port", 0, 65535, 7777)]
        public int Port { get; private set; }

        /// <summary> Whether to expect 4 bytes per LED (true), or 3 (false). </summary>
        [ConfigBool("HasYellow", false)]
        public bool HasYellowChannel { get; set; }

        /// <summary> All outputs that need to be notified when new data is available. </summary>
        private readonly List<IOutput> Outputs = new();

        /// <summary> The receiver for UDP packets. </summary>
        private UdpClient? Receiver;

        /// <summary> If true, we will no longer attempt to listen for new packets. </summary>
        private bool Stopping;

        /// <summary> The data in the most recent packet. </summary>
        private uint[] Data = Array.Empty<uint>();

        public UDPReceiver1D(string name, Dictionary<string, object> config)
        {
            this.Name = name;
            Configurer.Configure(this, config);
            if (this.Port < 1024) { Log.Warn("It is not recommended to use ports below 1024, as they are reserved. UDP listener is operating on port " + this.Port + "."); }
        }

        public void AttachOutput(IOutput output) { if (output != null) { this.Outputs.Add(output); } }

        public void Start()
        {
            this.Receiver = new UdpClient(this.Port);
            this.Stopping = false;
            this.Receiver.BeginReceive(HandleUDPData, this.Receiver);
        }

        public void Stop() => this.Stopping = true;

        private void HandleUDPData(IAsyncResult result)
        {
            UdpClient? Listener;
            byte[]? PacketData = null;
            IPEndPoint? Endpoint;

            try
            {
                Listener = (UdpClient?)result.AsyncState;
                Endpoint = new IPEndPoint(IPAddress.Any, 0);
                PacketData = Listener?.EndReceive(result, ref Endpoint);
            }
            catch(Exception e)
            {
                Log.Error("Error during UDP data handling.");
                Log.Error(e.ToString());
            }

            if (PacketData != null && this.Outputs.Count > 0)
            {
                byte Stride = (byte)(this.HasYellowChannel ? 4 : 3);
                int LEDCount = PacketData.Length / Stride;
                if (this.Data == null || this.Data.Length != LEDCount) { this.Data = new uint[LEDCount]; }

                for (int i = 0; i < LEDCount; i++)
                {
                    uint LEDValue = 0;

                    if (this.HasYellowChannel) // If Y is present, add to R and G first.
                    {
                        byte Yellow = PacketData[(i * Stride) + 3];
                        PacketData[(i * Stride) + 0] += Yellow;
                        PacketData[(i * Stride) + 1] += Yellow;
                    }

                    LEDValue |= (uint)(PacketData[(i * Stride) + 0] << 16); // R
                    LEDValue |= (uint)(PacketData[(i * Stride) + 1] << 8); // G
                    LEDValue |= (uint)(PacketData[(i * Stride) + 2]); // B

                    this.Data[i] = LEDValue;
                }

                foreach (IOutput output in this.Outputs) { output.Dispatch(); }
            }

            if (!this.Stopping) { this.Receiver?.BeginReceive(HandleUDPData, this.Receiver); }
        }

        public int GetCountDiscrete() => this.Data.Length;

        public uint[] GetDataDiscrete() => this.Data;
    }
}
