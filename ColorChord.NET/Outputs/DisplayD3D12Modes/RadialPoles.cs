using ColorChord.NET.API;
using ColorChord.NET.API.Config;
using ColorChord.NET.API.Visualizers;
using ColorChord.NET.API.Visualizers.Formats;
using ColorChord.NET.Outputs.DisplayD3D12Support;
using System;
using System.Collections.Generic;
using Vortice.Win32.Graphics.Direct3D12;
using static ColorChord.NET.Outputs.DisplayD3D12Support.COMUtils;

namespace ColorChord.NET.Outputs.DisplayD3D12Modes;

public class RadialPoles : DisplayModeBase2D
{
    private float[] RawDataIn;

    [ConfigFloat("CenterSize", 0.0F, 1.0F, 0.2F)]
    public float CenterSize; // TODO: Make these controllable

    [ConfigFloat("ScaleFactor", 0.0F, 100.0F, 0.7F)]
    public float ScaleFactor;

    [ConfigFloat("ScaleExponent", 0.0F, 10.0F, 1.6F)]
    public float ScaleExponent;

    private struct RootData
    {
        public uint Width;
        public uint Height;
        public uint PoleCount;
        public float Advance;
        public float ScaleFactor;
        public float Exponent;
        public float CenterBlank;
        
        public uint FeatureBits; // unused
    };

    private Shader? Shader;
    private RootData ShaderConfig;
    private DataHeap? DataHeap;
    private DataBuffer<float>? BinValues;
    private bool Ready;
    private uint InterQueueFenceValue;

    public RadialPoles(DisplayD3D12 host, IVisualizer visualizer, Dictionary<string, object> config) : base(host, visualizer, config)
    {
        ColorChordAPI.Configurer.Configure(this, config);
        this.RawDataIn = new float[this.Host.NoteFinder.OctaveBinValues.Length];
        UpdateConfig();
    }

    public override bool SupportsFormat(IVisualizerFormat format) => true;

    private void UpdateConfig() => this.ShaderConfig = new()
    {
        Width = (uint)this.Host.WindowWidth,
        Height = (uint)this.Host.WindowHeight,
        PoleCount = (uint)this.RawDataIn.Length,
        Advance = 0F,
        ScaleFactor = this.ScaleFactor,
        Exponent = this.ScaleExponent,
        FeatureBits = 0U
    };

    public override unsafe void Load(ID3D12Device2* device, CommandList copyCommandList, CommandList directCommandList)
    {
        InputElementDescription[] VertexInputs = LoadGeometry(device, copyCommandList);
        ulong CopyFenceValue = copyCommandList.Execute();

        long DataSize = Math.Min(65536, (this.RawDataIn.Length * sizeof(float)) + ((int)MathF.Ceiling(this.RawDataIn.Length / 32F) * 2));
        this.DataHeap = new(device, (ulong)DataSize);
        this.BinValues = new(true, name: $"{nameof(RadialPoles)} {nameof(BinValues)}");
        this.BinValues.Create(device, this.DataHeap, (uint)this.RawDataIn.Length);

        RootParameter1.InitAsConstants(out RootParameter1 RootDataParameter, (uint)(sizeof(RootData) / sizeof(float)), 0, visibility: ShaderVisibility.Pixel);
        RootParameter1.InitAsShaderResourceView(out RootParameter1 SRVParam, 1, visibility: ShaderVisibility.Pixel);
        //RootParameter1.InitAsUnorderedAccessView(out RootParameter1 UAVParam, 1, visibility: ShaderVisibility.Pixel);
        RootParameter1[] RootParameters = [RootDataParameter, SRVParam];//, UAVParam];

        this.Shader = new(device, VertexInputs, "VS_Passthrough2.cso", "PS_RadialPoles.cso", rootParameters: RootParameters);

        this.Host.Native.ClearColour = new(0F, 0F, 0F, 1F);

        copyCommandList.WaitForFenceValue(CopyFenceValue);
        this.Ready = true;
    }

    public override unsafe void Dispatch(ID3D12Device2* device, CommandList copyCommandList)
    {
        if (!this.Ready) { return; }
        if (this.Host.NoteFinder.OctaveBinValues.Length != this.RawDataIn.Length)
        {
            // TODO: Handle size change
            throw new Exception("can't handle size change");
        }
        this.Host.NoteFinder.OctaveBinValues.CopyTo(this.RawDataIn);

        ID3D12Resource* IntermediateCopyBuffer = default;
        lock (this.Host.Interlock)
        {
            fixed (float* DataPtr = &this.RawDataIn[0]) { this.BinValues.Load(device, copyCommandList, out IntermediateCopyBuffer, new(DataPtr, this.RawDataIn.Length)); }
            copyCommandList.ExecuteAndWait();
        }
        COMRelease(&IntermediateCopyBuffer);
    }

    public override unsafe void Render(ID3D12Device2* device, CommandList directCommandList)
    {
        if (!this.Ready) { return; }
        this.Shader.Use(directCommandList);
        //directCommandList.NativeList->ResourceBarrierTransition(this.BinValues.Buffer, ResourceStates.PixelShaderResource, ResourceStates.PixelShaderResource);
        directCommandList.NativeList->SetGraphicsRootShaderResourceView(1, this.BinValues.Buffer->GetGPUVirtualAddress());
        //directCommandList.NativeList->SetGraphicsRootUnorderedAccessView(1, this.BinValues.Buffer->GetGPUVirtualAddress());
        fixed (RootData* ConfigPtr = &this.ShaderConfig) { directCommandList.NativeList->SetGraphicsRoot32BitConstants(0, (uint)(sizeof(RootData) / sizeof(float)), ConfigPtr, 0); }

        RenderGeometry(directCommandList);
        //directCommandList.NativeList->ResourceBarrierTransition(this.BinValues.Buffer, ResourceStates.UnorderedAccess, ResourceStates.PixelShaderResource);
    }

    public override void Resize(int width, int height) => UpdateConfig();
}
