using ColorChord.NET.Visualizers;
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
        private int Padding;
        public string Name;

        public PacketUDP(string name)
        {
            this.Name = name;
        }

        public void Start() { }
        public void Stop() { }

        public void ApplyConfig(Dictionary<string, object> options)
        {
            if (!options.ContainsKey("visualizerName") || !ColorChord.VisualizerInsts.ContainsKey((string)options["visualizerName"])) { Console.WriteLine("[ERR] Tried to create PacketUDP with missing or invalid visualizer."); return; }
            this.Source = ColorChord.VisualizerInsts[(string)options["visualizerName"]];
            this.Source.AttachOutput(this);

            int Port = ConfigTools.CheckInt(options, "port", 0, 65535, 7777, true);
            this.Destination = new IPEndPoint(IPAddress.Parse(ConfigTools.CheckString(options, "saturationAmplifier", "127.0.0.1", true)), Port);
            this.Padding = ConfigTools.CheckInt(options, "frontPadding", 0, 1000, 0, true);
            ConfigTools.WarnAboutRemainder(options);
            Console.WriteLine("[INF] Finished reading config for PacketUDP \"" + this.Name + "\".");
        }

        public void Dispatch() // TODO: Make modular.
        {
            if (this.Source is Linear SourceLin)
            {
                byte[] Output = new byte[SourceLin.OutputData.Length + this.Padding];
                for (int i = 0; i < SourceLin.OutputData.Length; i++) { Output[i + this.Padding] = SourceLin.OutputData[i]; }
                this.Sender.Send(Output, Output.Length, this.Destination);
            }
            else if (this.Source is Cells SourceCells)
            {
                byte[] Output = new byte[SourceCells.OutputData.Length + this.Padding];
                for (int i = 0; i < SourceCells.OutputData.Length; i++) { Output[i + this.Padding] = SourceCells.OutputData[i]; }
                this.Sender.Send(Output, Output.Length, this.Destination);
            }
        }
    }
}
