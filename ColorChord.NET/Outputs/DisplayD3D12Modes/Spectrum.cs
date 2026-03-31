using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using ColorChord.NET.API;
using ColorChord.NET.API.Config;
using ColorChord.NET.API.NoteFinder;
using ColorChord.NET.API.Visualizers;
using ColorChord.NET.API.Visualizers.Formats;
using ColorChord.NET.Config;
using ColorChord.NET.NoteFinder;
using ColorChord.NET.Outputs.DisplayD3D12Support;
using Vortice.Win32;
using Vortice.Win32.Graphics.Direct3D;
using Vortice.Win32.Graphics.Direct3D12;
using Vortice.Win32.Graphics.Dxgi.Common;
using static ColorChord.NET.Outputs.DisplayD3D12Support.COMUtils;
using static Vortice.Win32.Graphics.Direct3D12.Apis;

namespace ColorChord.NET.Outputs.DisplayD3D12Modes;

public class Spectrum : ID3D12DisplayMode, IConfigurableAttr
{
    private struct RootData
    {
        public uint BinCount;
        public uint BinsPerOctave;
        public float ScaleFactor;
        public float Exponent;
        public uint FeatureBits;
        public uint Width;
        public uint Height;

        public uint Pad1;
    };

    private readonly Vector2[] Vertices =
    [
        new(-1F, 1F),
        new(3F, 1F),
        new(-1F, -3F)
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
    private RootData ShaderConfig;

    private DataHeap DataHeap;
    private PingPongBuffer<float> BinValues;

    private bool Ready;

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
            (this.PeakHighlight ? 8 : 0)),
        Width = (uint)this.Host.WindowWidth,
        Height = (uint)this.Host.WindowHeight,
    };

    public bool SupportsFormat(IVisualizerFormat format) => true;
    public unsafe void Load(ID3D12Device2* device, CommandList copyCommandList, CommandList directCommandList)
    {
        this.VertexBuffer = new(device, copyCommandList, this.Vertices);

        long DataSize = Math.Min(65536, (this.NoteFinder.AllBinValues.Length * sizeof(float)) + ((int)MathF.Ceiling(this.NoteFinder.AllBinValues.Length / 32F) * 2));
        this.DataHeap = new(device, (ulong)DataSize);
        this.BinValues = new(name: "Spectrum BinValues");
        this.BinValues.Create(device, this.NoteFinder.AllBinValues.Length);

        ulong FenceValue = copyCommandList.Execute();

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
        RootParameter1.InitAsConstants(out RootParameter1 RootDataParameter, (uint)(sizeof(RootData) / sizeof(float)), 0, visibility: ShaderVisibility.Pixel);
        RootParameter1.InitAsShaderResourceView(out RootParameter1 SRVParam, 1, visibility: ShaderVisibility.Pixel);
        RootParameter1[] RootParameters = [RootDataParameter, SRVParam];

        Span<nint> DescriptorHeaps = [(nint)this.Host.BufferDescriptorHeap.Heap];
        directCommandList.NativeList->SetDescriptorHeaps((uint)DescriptorHeaps.Length, (ID3D12DescriptorHeap**)DescriptorHeaps.GetPointer());
        
        this.Shader = new(device, VertexInputs, "VS_Passthrough2.cso", "PS_Spectrum.cso", rootParameters: RootParameters);

        this.Host.Native.ClearColour = new(0F, 0F, 0F, 1F);

        copyCommandList.WaitForFenceValue(FenceValue);
        this.Ready = true;
    }

    public unsafe void Dispatch(ID3D12Device2* device, CommandList copyCommandList)
    {
        if (!this.Ready) { return; }
        this.NoteFinder.UpdateOutputs();
        lock (this.Host.Interlock)
        {
            if (this.G2NoteFinder != null) { this.BinValues.Load(this.G2NoteFinder.AllBinValuesScaled); }
            else { this.BinValues.Load(this.NoteFinder.AllBinValues); } // TODO: Fix this API so this isn't necessary
            copyCommandList.ExecuteAndWait();
        }
    }

    public unsafe void Render(ID3D12Device2* device, CommandList directCommandList)
    {
        if (!this.Ready) { return; }

        this.Shader.Use(directCommandList);

        this.BinValues.StartRender();
        directCommandList.NativeList->SetGraphicsRootShaderResourceView(1, this.BinValues.RenderBuffer->GetGPUVirtualAddress());

        directCommandList.NativeList->IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        this.VertexBuffer.Use(directCommandList);

        // TODO: this doesn't need to happen every frame
        UpdateConfig();
        fixed (RootData* ConfigPtr = &this.ShaderConfig) { directCommandList.NativeList->SetGraphicsRoot32BitConstants(0, (uint)(sizeof(RootData) / sizeof(float)), ConfigPtr, 0); }
        directCommandList.NativeList->DrawInstanced(3, 1, 0, 0);
    }

    public unsafe void PostRender(ID3D12Device2* device)
    {
        if (!this.Ready) { return; }
        this.BinValues.FinishRender();
    }

    public void Resize(int width, int height) { }

    public void Close()
    {
        this.Shader?.Dispose();
        this.VertexBuffer?.Dispose();
    }
}
