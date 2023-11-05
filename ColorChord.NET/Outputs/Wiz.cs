using ColorChord.NET.API.Outputs;
using ColorChord.NET.API.Visualizers.Formats;
using ColorChord.NET.API.Visualizers;
using ColorChord.NET.Config;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Net;
using ColorChord.NET.API.Config;
using ColorChord.NET.API;
using System.Text;

namespace ColorChord.NET.Outputs;

// Note: In order for UDP communication to work, apparently there is a toggle in the settings on the mobile app that needs to be enabled, called "Allow local communication".
public class Wiz : IOutput
{
    private const ushort PORT_DISCOVERY = 38899;

    /// <summary> Instance name, for identification and attaching controllers. </summary>
    public string Name { get; private init; }

    /// <summary>Whether bulb discovery should be done at initialization time.</summary>
    [ConfigBool("ListDevices", true)]
    public bool ListDevices { get; private set; }

    [ConfigStringList("BulbIPs")]
    public List<string> BulbIPs { get; private set; }

    /// <summary> Where colour data is taken from. </summary>
    private readonly IDiscrete1D Source;

    private UdpClient? UDP;
    private IPEndPoint BroadcastEndpoint = new(IPAddress.Broadcast, PORT_DISCOVERY);
    private bool Stopping = false;
    private bool ReadyForDispatch = false;

    public Wiz(string name, Dictionary<string, object> config)
    {
        this.Name = name;
        IVisualizer? Source = Configurer.FindVisualizer(this, config, typeof(IDiscrete1D));
        if (Source == null) { throw new Exception($"{GetType().Name} \"{name}\" could not find requested visualizer."); }
        this.Source = (IDiscrete1D)Source;
        Configurer.Configure(this, config);
    }

    public void Dispatch()
    {
        if (!this.ReadyForDispatch) { return; }

        int SendCount = Math.Min(this.Source.GetCountDiscrete(), this.BulbIPs.Count);
        uint[] Data = this.Source.GetDataDiscrete();
        for (int i = 0; i < SendCount; i++)
        {
            string CommandJSON = $"{{\"id\":1,\"method\":\"setPilot\",\"params\":{{\"r\":{(byte)(Data[i] >> 16)},\"g\":{(byte)(Data[i] >> 8)},\"b\":{(byte)Data[i]},\"dimming\": 100}}}}";
            byte[] CommandData = Encoding.UTF8.GetBytes(CommandJSON);
            this.UDP!.SendAsync(CommandData, CommandData.Length, new IPEndPoint(IPAddress.Parse(BulbIPs[i]), PORT_DISCOVERY)); // TODO: Don't parse the IP every time, this is hideously inefficient
        }
    }

    public void Start()
    {
        this.UDP = new(PORT_DISCOVERY);
        if (this.ListDevices) { DoDiscovery(); }
        if (this.BulbIPs.Count != 0)
        {
            int SrcCount = this.Source.GetCountDiscrete();
            if (this.BulbIPs.Count != SrcCount) { Log.Warn($"Wiz sender \"{this.Name}\" has {this.BulbIPs.Count} bulb(s) configured, but the visualizer is outputting {SrcCount} unit(s). It is recommended to change the visualizer settings to match."); }
            ReadyForDispatch = true;
        }
        else { Log.Warn($"Wiz sender \"{this.Name}\" does not have any bulbs configured. Specify at least one IP address in config entry \"BulbIPs\"."); }
    }

    private void DoDiscovery()
    {
        try
        {
            this.UDP!.BeginReceive(new(UDPReceiveCallback), "Discovery");
            ReadOnlySpan<byte> STATE_REQ = "{\"method\":\"getSystemConfig\",\"params\":{}}"u8;
            Log.Debug("Sending Wiz bulb discovery broadcast");
            this.UDP.Send(STATE_REQ, this.BroadcastEndpoint);
        }
        catch(Exception exc)
        {
            Log.Error("Failed to send Wiz bulb discovery boradcast:");
            Log.Error(exc.ToString());
        }
    }

    private void UDPReceiveCallback(IAsyncResult result)
    {
        if (this.Stopping) { return; }

        IPEndPoint? RemoteEndpoint = new(IPAddress.Any, PORT_DISCOVERY);
        byte[] Data = this.UDP!.EndReceive(result, ref RemoteEndpoint);

        string JSONData = Encoding.UTF8.GetString(Data);
        Log.Info($"Got Wiz Discovery Response from {RemoteEndpoint?.Address}: \"{JSONData}\"");

        this.UDP.BeginReceive(new(UDPReceiveCallback), "Discovery");
    }

    public void Stop()
    {
        this.Stopping = true;
        this.ReadyForDispatch = false;
        this.UDP?.Close();
    }
}
