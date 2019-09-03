using ColorChord.NET.Visualizers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace ColorChord.NET.Outputs
{
    public class PacketUDP : IOutput
    {
        private readonly IVisualizer Source;
        private readonly UdpClient Sender = new UdpClient();
        private readonly IPEndPoint Destination;
        private readonly int Padding;

        public PacketUDP(IVisualizer source, string ip, ushort port, int frontPadding = 0)
        {
            this.Source = source;
            this.Destination = new IPEndPoint(IPAddress.Parse(ip), port);
            this.Source.AttachOutput(this);
            this.Padding = frontPadding;
        }

        public void Dispatch()
        {
            if (this.Source is Linear SourceLin)
            {
                byte[] Output = new byte[SourceLin.OutputData.Length + this.Padding];
                for (int i = 0; i < SourceLin.OutputData.Length; i++) { Output[i + this.Padding] = SourceLin.OutputData[i]; }
                this.Sender.Send(Output, Output.Length, Destination);
            }
        }
    }
}
