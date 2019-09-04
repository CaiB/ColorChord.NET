using ColorChord.NET.Outputs;
using ColorChord.NET.Sources;
using ColorChord.NET.Visualizers;

namespace ColorChord.NET
{
    public class ColorChord
    {

        public static void Main(string[] args)
        {
            bool DoLinear = true;

            NoteFinder.Start();

            WASAPILoopback LoopbackSrc = new WASAPILoopback();
            LoopbackSrc.Start();

            Linear Linear = new Linear(50, false);
            if (DoLinear) { Linear.Start(); }

            Cells Cells = new Cells(50);
            if (!DoLinear) { Cells.Start(); }

            //PacketUDP NetworkLin = new PacketUDP(Linear, "192.168.0.60", 7777, 1);
            //PacketUDP NetworkCel = new PacketUDP(Cells, "192.168.0.60", 7777, 1);
            DisplayOpenGL TestDisp = new DisplayOpenGL(Linear);
        }

    }
}
