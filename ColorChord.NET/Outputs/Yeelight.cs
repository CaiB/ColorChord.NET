﻿using ColorChord.NET.API;
using ColorChord.NET.API.Config;
using ColorChord.NET.API.Outputs;
using ColorChord.NET.API.Visualizers;
using ColorChord.NET.API.Visualizers.Formats;
using ColorChord.NET.Config;
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
        /// <summary> A unique name for this visualizer instance, used for referring to it from other components. </summary>
        public string Name { get; private init; }
        
        [ConfigBool("Enable", true)]
        public bool Enabled { get; set; }

        [ConfigInt("Port", 0, 65535, 14887)]
        private readonly int Port = 14887;

        [ConfigBool("ListDevices", false)]
        private readonly bool DoDeviceListing = false;

        private readonly IVisualizer Source;

        private TcpListener? TCPServer;

        private Thread? ClientAcceptor;
        
        private readonly List<TcpClient> Clients = new();

        private bool Stopping = false;

        //private ManualResetEventSlim ResetEvent;

        // Command to start music mode:
        // {"id":1,"method":"set_music","params":[1,"MYIP",PORT]}

        // Command to turn on:
        // {"id":1,"method":"set_power","params":["on","smooth",500,2]}

        // Command to set RGB:
        // {"id":1,"method":"set_rgb","params":[uint32,"sudden",0]}

        // Discovery:
        // 239.255.255.250:1982 multicast

        private readonly string DiscoverRequest = "M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1982\r\nMAN: \"ssdp:discover\"\r\nST: wifi_bulb\r\n";

        public Yeelight(string name, Dictionary<string, object> config)
        {
            this.Name = name;
            IVisualizer? Source = Configurer.FindVisualizer(config);
            if (Source == null) { throw new Exception($"{GetType().Name} \"{name}\" could not find requested visualizer."); }
            this.Source = Source;
            Configurer.Configure(this, config);

            this.Source.AttachOutput(this);
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

            // Send discovery request to find clients
            if(this.DoDeviceListing) { DoDiscovery(); }

            // Handles incoming TCP connections and adds the clients to the list
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

        private void DoDiscovery()
        {
            List<string> Responses = new();
            IPEndPoint Endpoint = new(IPAddress.Parse("239.255.255.250"), 1982);
            UdpClient UDPClient = new() { ExclusiveAddressUse = false };
            UDPClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            UDPClient.Client.Bind(Endpoint);

            foreach (string Line in Responses) { Log.Info(Line); }
        }

        private static string ParseDiscoverResponse()
        {
            string Response = "HTTP/1.1 200 OK\r\nCache-Control: max-age=3600\r\nDate: \r\nLocation: yeelight://192.168.1.239:55443\r\nid: 0x000000000015243f\r\nmodel: color \r\nfw_ver: 18 \r\nsupport: get_prop set_default set_power toggle set_bright start_cf stop_cf set_scene cron_add cron_get cron_del set_ct_abx set_rgb\r\nname: lolwut\r\n";
            string[] Lines = Response.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            ulong ID = 0;
            string? ControlAddress = null;
            string? Model = null;
            string? FirmwareVersion = null;
            string? Support = null;
            string? Name = null;

            foreach(string Line in Lines)
            {
                if (Line.StartsWith("LOCATION", StringComparison.InvariantCultureIgnoreCase)) { ControlAddress = Line.Substring(Line.IndexOf("yeelight://") + 11).Trim(); }
                if (Line.StartsWith("ID", StringComparison.InvariantCultureIgnoreCase)) { ID = Convert.ToUInt64(Line.Substring(Line.IndexOf("0x") + 2).Trim(), 16); }
                if (Line.StartsWith("MODEL", StringComparison.InvariantCultureIgnoreCase)) { Model = Line.Substring(Line.IndexOf(':') + 1).Trim(); }
                if (Line.StartsWith("FW_VER", StringComparison.InvariantCultureIgnoreCase)) { FirmwareVersion = Line.Substring(Line.IndexOf(':') + 1).Trim(); }
                if (Line.StartsWith("SUPPORT", StringComparison.InvariantCultureIgnoreCase)) { Support = Line.Substring(Line.IndexOf(':') + 1).Trim(); }
                if (Line.StartsWith("NAME", StringComparison.InvariantCultureIgnoreCase)) { Name = Line.Substring(Line.IndexOf(':') + 1).Trim(); }
            }
            bool Usable = Support != null && Support.Contains("set_power") && Support.Contains("set_rgb");

            return string.Format("  [{0}] \"{1}\" at {2}, model {3} with firmware {4}. Usable? {5}", ID, Name, ControlAddress, Model, FirmwareVersion, Usable ? "Yes" : "NO");
        }

        public void Stop()
        {
            this.Stopping = true; // TODO: This probably won't stop until a client joins. Interrupt the thread?
            this.ClientAcceptor?.Join();
        }
    }
}
