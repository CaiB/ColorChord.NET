using ColorChord.NET.Visualizers;
using ColorChord.NET.Visualizers.Formats;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace ColorChord.NET.Outputs
{
    public class PacketUDP : IOutput
    {
        private IVisualizer Source;
        private readonly UdpClient Sender = new UdpClient();
        private IPEndPoint Destination;

        /// <summary> Instance name, for identification and attaching controllers. </summary>
        private readonly string Name;

        /// <summary> Number of empty bytes to leave at the front of the packet. </summary>
        public uint FrontPadding { get; set; }

        /// <summary> Number of empty bytes to leave at the end of the packet. </summary>
        public uint BackPadding { get; set; }

        /// <summary> Whether this sender instance is enabled (can send packets). </summary>
        public bool Enabled { get; set; }

        public PacketUDP(string name)
        {
            this.Name = name;
        }

        public void Start() { }
        public void Stop() { }

        public void ApplyConfig(Dictionary<string, object> options)
        {
            Log.Info("Reading config for PacketUDP \"" + this.Name + "\".");

            if (!options.ContainsKey("visualizerName") || !ColorChord.VisualizerInsts.ContainsKey((string)options["visualizerName"])) { Log.Error("Tried to create PacketUDP with missing or invalid visualizer."); return; }
            this.Source = ColorChord.VisualizerInsts[(string)options["visualizerName"]];
            this.Source.AttachOutput(this);

            int Port = ConfigTools.CheckInt(options, "port", 0, 65535, 7777, true);
            string IP = ConfigTools.CheckString(options, "ip", "127.0.0.1", true);
            this.Destination = new IPEndPoint(IPAddress.Parse(IP), Port);
            this.FrontPadding = (uint)ConfigTools.CheckInt(options, "paddingFront", 0, 1000, 0, true);
            this.BackPadding = (uint)ConfigTools.CheckInt(options, "paddingBack", 0, 1000, 0, true);
            this.Enabled = ConfigTools.CheckBool(options, "enable", true, true);

            ConfigTools.WarnAboutRemainder(options, typeof(IOutput));
        }

        public void Dispatch()
        {
            byte[] Output;
            if (this.Source is IDiscrete1D Source1D)
            {
                Output = new byte[Source1D.GetCountDiscrete() * 3 + this.FrontPadding + this.BackPadding];
                byte[] SourceData = Source1D.GetDataDiscrete();
                for (int i = 0; i < SourceData.Length; i++) { Output[i + this.FrontPadding] = SourceData[i]; }
            }
            else { return; }
            this.Sender.Send(Output, Output.Length, this.Destination);
        }
    }
}
