using System;
using System.Collections.Generic;
using System.Numerics;
using ColorChord.NET.API;
using ColorChord.NET.API.Config;
using ColorChord.NET.API.NoteFinder;
using ColorChord.NET.API.Visualizers;
using ColorChord.NET.API.Visualizers.Formats;
using ColorChord.NET.Config;
using ColorChord.NET.NoteFinder;
using ColorChord.NET.Outputs.DisplayD3D12Support;
using Win32;
using Win32.Graphics.Direct3D;
using Win32.Graphics.Direct3D12;
using Win32.Graphics.Dxgi.Common;
using static ColorChord.NET.Outputs.DisplayD3D12Support.COMUtils;
using static Win32.Graphics.Direct3D12.Apis;

namespace ColorChord.NET.Outputs.DisplayD3D12Modes;

public class Spectrum : ID3D12DisplayMode
{
    private struct RootData
    {
        public uint BinCount;
        public uint BinsPerOctave;
        public float ScaleFactor;
        public float Exponent;
        public uint FeatureBits;
    };

    private readonly Vector2[] Vertices =
    [
        new(-1F, 1F),
        new(2F, 1F),
        new(-1F, -2F)
    ];

    private readonly DisplayD3D12 Host;
    private readonly NoteFinderCommon NoteFinder;
    private readonly Gen2NoteFinder? G2NoteFinder;

    [ConfigFloat("ScaleFactor", 0F, 1000F, 4F)]
    private float ScaleFactor = 4F;

    [ConfigFloat("ScaleExponent", 0F, 10F, 3F)]
    private float Exponent = 3F;

    [ConfigBool("PeakHighlight", true)]
    private bool PeakHighlight = true;

    [ConfigBool("HideWideband", true)]
    private bool HideWideband = true;

    [ConfigBool("EnableColour", true)]
    private bool EnableColour = true;

    [ConfigBool("StartInCenter", true)]
    private bool StartInCenter = true;

    private VertexBuffer<Vector2> VertexBuffer;
    private Shader Shader;
    private bool Ready;
    private RootData ShaderConfig;

    public Spectrum(DisplayD3D12 host, IVisualizer visualizer, Dictionary<string, object> config)
    {
        this.Host = host;
        ColorChordAPI.Configurer.Configure(this, config);
        this.NoteFinder = Configurer.FindNoteFinder(config) ?? throw new Exception($"{nameof(Spectrum)} under \"{this.Host.Name}\" could not find the NoteFinder to attach to.");
        this.G2NoteFinder = this.NoteFinder as Gen2NoteFinder;
        UpdateConfig();
    }

    private void UpdateConfig() => this.ShaderConfig = new()
    {
        BinCount = (uint)this.NoteFinder.AllBinValues.Length,
        BinsPerOctave = (uint)this.NoteFinder.BinsPerOctave,
        ScaleFactor = this.ScaleFactor,
        Exponent = this.Exponent,
        FeatureBits = (uint)(
            (this.StartInCenter ? 1 : 0) |
            (this.EnableColour ? 2 : 0) |
            (this.HideWideband ? 4 : 0) |
            (this.PeakHighlight ? 8 : 0))
    };

    public bool SupportsFormat(IVisualizerFormat format) => true;
    public unsafe void Load(ID3D12Device2* device, CommandQueue copyQueue)
    {
        ID3D12GraphicsCommandList2* CopyCommandList = copyQueue.GetCommandList();
        this.VertexBuffer = new(device, CopyCommandList, this.Vertices);

        ulong FenceValue = copyQueue.ExecuteCommandList(CopyCommandList);

        InputElementDescription[] VertexInputs =
        [
            new()
            {
                SemanticName = "POSITION\0"u8.GetPointer(),
                SemanticIndex = 0,
                Format = Format.R32G32Float,
                InputSlot = 0,
                AlignedByteOffset = D3D12_APPEND_ALIGNED_ELEMENT,
                InputSlotClass = InputClassification.PerVertexData,
                InstanceDataStepRate = 0
            }
        ];
        RootParameter1.InitAsConstants(out RootParameter1 RootDataParameter, (uint)(sizeof(RootData) / sizeof(float)), 0, 0, ShaderVisibility.Pixel);
        RootParameter1[] RootParameters = [RootDataParameter];
        this.Shader = new(device, VertexInputs, "VS_Passthrough2.cso", "PS_Spectrum.cso", rootParameters: RootParameters);

        this.Host.Native.ClearColour = new(0F, 0F, 0F, 1F);

        copyQueue.WaitForFenceValue(FenceValue);
        this.Ready = true;
        COMRelease(&CopyCommandList);
    }

    public void Dispatch()
    {
        
    }

    public unsafe void Render(ID3D12GraphicsCommandList* directCommandList)
    {
        if (!this.Ready) { return; }
        this.Shader.Use(directCommandList);
        directCommandList->IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        this.VertexBuffer.Use(directCommandList);

        // TODO: this doesn't need to happen every frame
        fixed (RootData* ConfigPtr = &this.ShaderConfig) { directCommandList->SetGraphicsRoot32BitConstants(0, (uint)(sizeof(RootData) / sizeof(float)), ConfigPtr, 0); }
        directCommandList->DrawInstanced(3, 1, 0, 0);
    }

    public void Resize(int width, int height) { }

    public void Close()
    {
        this.Shader?.Dispose();
        this.VertexBuffer?.Dispose();
    }
}
