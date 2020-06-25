using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using ColorChord.NET.Outputs;
using ColorChord.NET.Visualizers.Formats;

namespace ColorChord.NET.Visualizers
{
    /// <summary> Instead of taking data from the NoteFinder, this receives UDP packets and outputs the data. Only meant for testing purposes. </summary>
    /// <remarks> If you actually want to use this, let me know and I'll expand functionality. Currently very minimal, as it was just to test UDP output. </remarks>
    public class UDPReceiver1D : IVisualizer, IDiscrete1D
    {
        private readonly List<IOutput> Outputs = new List<IOutput>();

        /// <summary> The receiver for UDP packets. </summary>
        private UdpClient Receiver;

        /// <summary> If true, we will no longer attempt to listen for new packets. </summary>
        private bool Stopping;

        /// <summary> The data in the most recent packet. </summary>
        private uint[] Data = new uint[0];

        /// <summary> Whether to expect 4 bytes per LED (true), or 3 (false). </summary>
        public bool HasYellowChannel { get; set; }

        /// <summary> The port to listen on. </summary>
        public int Port { get; private set; }

        public string Name { get; private set; }

        public UDPReceiver1D(string name)
        {
            this.Name = name;
        }

        public void ApplyConfig(Dictionary<string, object> options)
        {
            this.HasYellowChannel = ConfigTools.CheckBool(options, "HasYellow", false, true);
            this.Port = ConfigTools.CheckInt(options, "Port", 0, 65535, 7777, true);
            if (this.Port < 1024) { Log.Warn("It is not recommended to use ports below 1024, as they are reserved. UDP listener is operating on port " + this.Port + "."); }

            ConfigTools.WarnAboutRemainder(options, typeof(IOutput));
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
            UdpClient Listener;
            byte[] PacketData = null;
            IPEndPoint Endpoint;

            try
            {
                Listener = (UdpClient)result.AsyncState;
                Endpoint = new IPEndPoint(IPAddress.Any, 0);
                PacketData = Listener.EndReceive(result, ref Endpoint);
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
                    LEDValue |= (uint)(PacketData[(i * Stride) + 0] << 16); // R
                    LEDValue |= (uint)(PacketData[(i * Stride) + 1] << 8); // G
                    LEDValue |= (uint)(PacketData[(i * Stride) + 2]); // B
                    // TODO: Handle Yellow channel if present.

                    this.Data[i] = LEDValue;
                }

                foreach (IOutput output in this.Outputs) { output.Dispatch(); }
            }

            if (!this.Stopping) { this.Receiver.BeginReceive(HandleUDPData, this.Receiver); }
        }

        public int GetCountDiscrete() => this.Data.Length;

        public uint[] GetDataDiscrete() => this.Data;
    }
}
