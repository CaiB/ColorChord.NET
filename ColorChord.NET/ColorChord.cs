using ColorChord.NET.Outputs;
using ColorChord.NET.Sources;
using ColorChord.NET.Visualizers;

namespace ColorChord.NET
{
    public class ColorChord
    {

        public static void Main(string[] args)
        {
            NoteFinder.Start();

            WASAPILoopback LoopbackSrc = new WASAPILoopback();
            LoopbackSrc.Start();

            Linear Linear = new Linear(50, false);
            Linear.Start();

            PacketUDP Network = new PacketUDP(Linear, "192.168.0.60", 7777, 1);
        }

    }
}
