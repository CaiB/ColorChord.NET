using ColorChord.NET.Visualizers;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace ColorChord.NET.Outputs
{
    public class PacketUDP : IOutput
    {
        private readonly IVisualizer Source;
        private readonly UdpClient Sender = new UdpClient();
        private readonly IPEndPoint Destination;
        private readonly int Padding;

        public PacketUDP(string name) { }

        public PacketUDP(IVisualizer source, string ip, ushort port, int frontPadding = 0)
        {
            this.Source = source;
            this.Destination = new IPEndPoint(IPAddress.Parse(ip), port);
            this.Source.AttachOutput(this);
            this.Padding = frontPadding;
        }

        public void Start() { }
        public void Stop() { }

        public void ApplyConfig(Dictionary<string, object> options)
        {

        }

        public void Dispatch()
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
