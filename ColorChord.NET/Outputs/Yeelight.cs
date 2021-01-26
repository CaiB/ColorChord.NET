using ColorChord.NET.Visualizers;
using ColorChord.NET.Visualizers.Formats;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace ColorChord.NET.Outputs
{
    public class Yeelight : IOutput
    {
        private IVisualizer Source;
        private readonly string Name;
        private bool Enabled;
        private int Port;
        private bool Stopping = false;

        private TcpListener TCPServer;

        private Thread ClientAcceptor;
        
        private readonly List<TcpClient> Clients = new List<TcpClient>();

        //private ManualResetEventSlim ResetEvent;

        // Command to start music mode:
        // {"id":1,"method":"set_music","params":[1,"MYIP",PORT]}
        
        // Command to turn on:
        // {"id":1,"method":"set_power","params":["on","smooth",500,2]}

        // Command to set RGB:
        // {"id":1,"method":"set_rgb","params":[uint32,"sudden",0]}

        public Yeelight(string name) { this.Name = name; }

        public void ApplyConfig(Dictionary<string, object> options)
        {
            Log.Info("Reading config for Yeelight \"" + this.Name + "\"");

            if (!options.ContainsKey("VisualizerName") || !ColorChord.VisualizerInsts.ContainsKey((string)options["VisualizerName"])) { Log.Error("Tried to create Yeelight with missing or invalid visualizer."); return; }
            this.Source = ColorChord.VisualizerInsts[(string)options["VisualizerName"]];
            this.Source.AttachOutput(this);

            this.Port = ConfigTools.CheckInt(options, "Port", 0, 65535, 14887, true);
            this.Enabled = ConfigTools.CheckBool(options, "Enable", true, true);

            ConfigTools.WarnAboutRemainder(options, typeof(IOutput));
        }

        public void Start()
        {
            if (this.ClientAcceptor != null) { throw new Exception("Cannot start Yeelight TCP server if already started"); }
            this.ClientAcceptor = new Thread(WaitForClients);
            this.ClientAcceptor.Start();
        }

        private void WaitForClients()
        {
            this.TCPServer = new TcpListener(new IPEndPoint(IPAddress.Any, this.Port));
            this.TCPServer.Start();
            Log.Info("Waiting for Yeelight clients");
            while (!this.Stopping)
            {
                if(!this.TCPServer.Pending()) // No clients want to connect
                {
                    Thread.Sleep(500);
                    continue;
                }
                TcpClient Client = this.TCPServer.AcceptTcpClient();
                Log.Info("Yeelight client connecting");

                byte[] TurnOn = Encoding.ASCII.GetBytes("{\"id\":1,\"method\":\"set_power\",\"params\":[\"on\",\"smooth\",500,2]}\r\n");
                if (!Client.GetStream().CanWrite) { continue; }
                Client.GetStream().BeginWrite(TurnOn, 0, TurnOn.Length, null, null);

                this.Clients.Add(Client);
            }
            this.TCPServer.Stop();
        }

        public void Dispatch()
        {
            if(!this.Enabled) { return; }
            string PacketContent = "{\"id\":1,\"method\":\"set_rgb\",\"params\":[" + ((IDiscrete1D)this.Source).GetDataDiscrete()[0] + ",\"sudden\",0]}\r\n";
            byte[] PacketData = Encoding.ASCII.GetBytes(PacketContent);
            for (int i = 0; i < this.Clients.Count; i++)
            {
                TcpClient Client = this.Clients[i];
                if (!Client.GetStream().CanWrite) { continue; }
                Client.GetStream().BeginWrite(PacketData, 0, PacketData.Length, null, null);
            }
        }

        public void Stop()
        {
            this.Stopping = true; // TODO: This probably won't stop until a client joins. Interrupt the thread?
            this.ClientAcceptor.Join();
        }
    }
}
