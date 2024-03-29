﻿using ColorChord.NET.API.Outputs;
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
    public List<string> BulbIPs { get; private set; } // TODO: When making this controllable, ensure that re-parsing happens via ParseIPs().

    /// <summary> Where colour data is taken from. </summary>
    private readonly IDiscrete1D Source;

    private UdpClient? UDP;
    private IPEndPoint BroadcastEndpoint = new(IPAddress.Broadcast, PORT_DISCOVERY);
    private bool Stopping = false;
    private bool ReadyForDispatch = false;
    private List<IPEndPoint> BulbIPsParsed = new();

    public Wiz(string name, Dictionary<string, object> config)
    {
        this.Name = name;
        IVisualizer? Source = Configurer.FindVisualizer(this, config, typeof(IDiscrete1D));
        if (Source == null) { throw new Exception($"{GetType().Name} \"{name}\" could not find requested visualizer."); }
        this.Source = (IDiscrete1D)Source;
        Configurer.Configure(this, config);
        ParseIPs();
        Source.AttachOutput(this);
    }

    private void ParseIPs()
    {
        this.BulbIPsParsed.Clear();
        foreach (string IP in this.BulbIPs) { this.BulbIPsParsed.Add(new(IPAddress.Parse(IP), PORT_DISCOVERY)); }
    }

    public void Dispatch()
    {
        if (!this.ReadyForDispatch) { return; }

        int SendCount = Math.Min(this.Source.GetCountDiscrete(), this.BulbIPsParsed.Count);
        uint[] Data = this.Source.GetDataDiscrete();
        for (int i = 0; i < SendCount; i++)
        {
            string CommandJSON = $"{{\"id\":1,\"method\":\"setPilot\",\"params\":{{\"r\":{(byte)(Data[i] >> 16)},\"g\":{(byte)(Data[i] >> 8)},\"b\":{(byte)Data[i]},\"dimming\": 100}}}}";
            byte[] CommandData = Encoding.UTF8.GetBytes(CommandJSON);
            this.UDP!.BeginSend(CommandData, CommandData.Length, this.BulbIPsParsed[i], null, "Dispatch");
        }
    }

    public void Start()
    {
        this.UDP = new(PORT_DISCOVERY);
        if (this.ListDevices) { DoDiscovery(); }
        if (this.BulbIPsParsed.Count != 0)
        {
            int SrcCount = this.Source.GetCountDiscrete();
            if (this.BulbIPsParsed.Count != SrcCount) { Log.Warn($"Wiz sender \"{this.Name}\" has {this.BulbIPsParsed.Count} bulb(s) configured, but the visualizer is outputting {SrcCount} unit(s). It is recommended to change the visualizer settings to match."); }
            ReadyForDispatch = true;

            int BulbCount = Math.Min(this.Source.GetCountDiscrete(), this.BulbIPsParsed.Count);
            ReadOnlySpan<byte> ENABLE_BULB = "{\"id\":1,\"method\":\"setState\",\"params\":{\"state\":true}}"u8;
            ReadOnlySpan<byte> DIM_BULB = "{\"id\":1,\"method\":\"setPilot\",\"params\":{\"r\":10,\"g\":10,\"b\":10,\"dimming\": 10}}"u8;
            for (int i = 0; i < BulbCount; i++)
            {
                this.UDP!.Send(ENABLE_BULB, this.BulbIPsParsed[i]);
                this.UDP!.Send(DIM_BULB, this.BulbIPsParsed[i]);
            }
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

        // Turn off bulbs
        ReadOnlySpan<byte> DISABLE_BULB = "{\"id\":1,\"method\":\"setState\",\"params\":{\"state\":false}}"u8;
        foreach (IPEndPoint Bulb in this.BulbIPsParsed) { this.UDP!.Send(DISABLE_BULB, Bulb); }

        this.UDP?.Close();
    }
}
