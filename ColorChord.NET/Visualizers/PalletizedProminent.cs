using ColorChord.NET.API;
using ColorChord.NET.API.Config;
using ColorChord.NET.API.NoteFinder;
using ColorChord.NET.API.Outputs;
using ColorChord.NET.API.Sources;
using ColorChord.NET.API.Utility;
using ColorChord.NET.API.Visualizers;
using ColorChord.NET.API.Visualizers.Formats;
using ColorChord.NET.Config;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace ColorChord.NET.Visualizers;

public class PalletizedProminent : IVisualizer, IDiscrete1D
{
    public string Name { get; private init; }

    [ConfigStringList("Palette")]
    private List<string> PaletteHexCodes { get; set; } = new();

    public List<Vector3> PaletteHSV { get; private set; } = new();

    public List<uint> PaletteRGB { get; private set; } = new();

    [ConfigString("TimeSource", "this")]
    private string TimeSource { get; set; } = "this";

    [ConfigFloat("TimePeriod", -1000000, 1000000, 100)]
    private float TimePeriod = 100F;

    private bool IsOwnTimeSource = true;
    private ITimingSource? ConnectedTimingSource { get; set; } = null;

    private uint[] Data = new uint[1];

    private List<IOutput> Outputs = new();

    public PalletizedProminent(string name, Dictionary<string, object> config)
    {
        this.Name = name;
        Configurer.Configure(this, config);
        ParsePalette();
    }

    private void ParsePalette()
    {
        this.PaletteHSV = new(this.PaletteHexCodes.Count);
        this.PaletteRGB = new(this.PaletteHexCodes.Count);
        for (int i = 0; i < this.PaletteHexCodes.Count; i++)
        {
            string HexCode = this.PaletteHexCodes[i];
            if (HexCode.Length != 6) { goto InvalidItem; }
            bool InvalidChar = false;
            int Red = (ParseHexChar(HexCode[0], ref InvalidChar) << 4) | ParseHexChar(HexCode[1], ref InvalidChar);
            int Green = (ParseHexChar(HexCode[2], ref InvalidChar) << 4) | ParseHexChar(HexCode[3], ref InvalidChar);
            int Blue = (ParseHexChar(HexCode[4], ref InvalidChar) << 4) | ParseHexChar(HexCode[5], ref InvalidChar);
            if (InvalidChar) { goto InvalidItem; }

            this.PaletteHSV.Add(VisualizerTools.RGBToHSV(new(Red / 255F, Green / 255F, Blue / 255F)));
            this.PaletteRGB.Add((uint)((Red << 16) | (Green << 8) | Blue));
            continue;

            InvalidItem:
            Log.Warn($"<{nameof(PalletizedProminent)}> \"{this.Name}\" Item {i + 1} in the Palette list was invalid. It will be omitted.");
        }
        this.PaletteHSV.Sort((a, b) => a.X.CompareTo(b.X));
    }

    private static int ParseHexChar(char c, ref bool invalidChar)
    {
        if (c >= 48 && c <= 57) { return c - 48; }
        else if (c >= 65 && c <= 70) { return c - 55; }
        else if (c >= 97 && c <= 102) { return c - 87; }
        else
        {
            invalidChar = true;
            return 0;
        }
    }

    private void HookTimeSource()
    {
        ITimingSource? NewSource;
        if (this.TimeSource.ToLower() == "this") { NewSource = new GenericTimingSourceSingle(); }
        else { NewSource = ColorChord.GetInstanceFromPath(this.TimeSource) as ITimingSource; }
        this.IsOwnTimeSource = this.TimeSource.ToLower() == "this";

        this.ConnectedTimingSource?.RemoveTimingReceiver(Update);
        NewSource?.AddTimingReceiver(Update, this.TimePeriod);
        this.ConnectedTimingSource = NewSource;
    }

    private void UnhookTimeSource()
    {
        this.ConnectedTimingSource?.RemoveTimingReceiver(Update);
        this.ConnectedTimingSource = null;
    }

    public void Start() => HookTimeSource();

    public void AttachOutput(IOutput output) => this.Outputs.Add(output);

    public int GetCountDiscrete() => 1;

    public uint[] GetDataDiscrete() => this.Data;

    public void Update()
    {
        if (ColorChord.NoteFinder.OctaveBinValues == null) { return; }
        if (this.IsOwnTimeSource) { ColorChord.NoteFinder?.UpdateOutputs(); }

        float MaxValue = 0F;
        int MaxIndex = 0;
        for (int i = 0; i < ColorChord.NoteFinder.OctaveBinValues.Length; i++)
        {
            if (MaxValue < ColorChord.NoteFinder.OctaveBinValues[i])
            {
                MaxValue = ColorChord.NoteFinder.OctaveBinValues[i];
                MaxIndex = i;
            }
        }

        if (MaxValue == 0F || this.PaletteHSV.Count == 0) { this.Data[0] = 0; }
        else
        {
            float NoteHue = VisualizerTools.CCToHue((float)MaxIndex / ColorChord.NoteFinder.OctaveBinValues.Length);
            int UpperColour = -1;
            for (int i = 0; i < this.PaletteHSV.Count; i++) // TODO: Maybe replace with binary search
            {
                if (this.PaletteHSV[i].X > NoteHue) { UpperColour = i; break; }
            }

            if (UpperColour == -1) // Current note is higher hue than any palette colours, so wrap around and check both ends
            {
                float UpperHue = this.PaletteHSV[0].X + 360F;
                float LowerHue = this.PaletteHSV[^1].X;
                this.Data[0] = this.PaletteRGB[(UpperHue - NoteHue < NoteHue - LowerHue) ? 0 : ^1];
            }
            else if (UpperColour == 0) // Current note is lower hue than any palette colours, so wrap around and check both ends
            {
                float UpperHue = this.PaletteHSV[0].X;
                float LowerHue = this.PaletteHSV[^1].X - 360F;
                this.Data[0] = this.PaletteRGB[(UpperHue - NoteHue < NoteHue - LowerHue) ? 0 : ^1];
            }
            else
            {
                float UpperHue = this.PaletteHSV[UpperColour].X;
                float LowerHue = this.PaletteHSV[UpperColour - 1].X;
                this.Data[0] = this.PaletteRGB[(UpperHue - NoteHue < NoteHue - LowerHue) ? UpperColour : UpperColour - 1];
            }
        }

        foreach (IOutput Output in this.Outputs) { Output.Dispatch(); }
    }

    public void Stop() => UnhookTimeSource();
}
