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

    [ConfigInt("Repetitions", 1, 1000, 1)]
    public uint Repetitions;

    [ConfigFloat("Advance", 0.0F, 1.0F, 0.0F)]
    public float Advance;

    private struct RootData
    {
        public uint Width;
        public uint Height;
        public uint PoleCount;
        public float Advance;
        public float ScaleFactor;
        public float Exponent;
        public float CenterBlank;
        public uint Repetitions;
    };

    private Shader? Shader;
    private RootData ShaderConfig;
    private PingPongBuffer<float>? BinValues;
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
        Advance = -(this.Advance * MathF.Tau) - MathF.PI / 2,
        ScaleFactor = this.ScaleFactor,
        Exponent = this.ScaleExponent,
        CenterBlank = this.CenterSize,
        Repetitions = this.Repetitions
    };

    public override unsafe void Load(ID3D12Device2* device, CommandList copyCommandList, CommandList directCommandList)
    {
        InputElementDescription[] VertexInputs = LoadGeometry(device, copyCommandList);
        ulong CopyFenceValue = copyCommandList.Execute();

        this.BinValues = new(name: $"{nameof(RadialPoles)} {nameof(BinValues)}");
        this.BinValues.Create(device, this.RawDataIn.Length);

        RootParameter1.InitAsConstants(out RootParameter1 RootDataParameter, (uint)(sizeof(RootData) / sizeof(float)), 0, visibility: ShaderVisibility.Pixel);
        RootParameter1.InitAsShaderResourceView(out RootParameter1 SRVParam, 1, visibility: ShaderVisibility.Pixel);
        RootParameter1[] RootParameters = [RootDataParameter, SRVParam];

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
        this.Host.NoteFinder.UpdateOutputs();
        this.Host.NoteFinder.OctaveBinValues.CopyTo(this.RawDataIn);

        ID3D12Resource* IntermediateCopyBuffer = default;
        lock (this.Host.Interlock)
        {
            this.BinValues!.Load(this.RawDataIn);
            copyCommandList.ExecuteAndWait();
        }
        COMRelease(&IntermediateCopyBuffer);
    }

    public override unsafe void Render(ID3D12Device2* device, CommandList directCommandList)
    {
        if (!this.Ready) { return; }
        this.Shader!.Use(directCommandList);
        this.BinValues!.StartRender();
        directCommandList.NativeList->SetGraphicsRootShaderResourceView(1, this.BinValues.RenderBuffer->GetGPUVirtualAddress());
        fixed (RootData* ConfigPtr = &this.ShaderConfig) { directCommandList.NativeList->SetGraphicsRoot32BitConstants(0, (uint)(sizeof(RootData) / sizeof(float)), ConfigPtr, 0); }

        RenderGeometry(directCommandList);
    }

    public override unsafe void PostRender(ID3D12Device2* device)
    {
        if (!this.Ready) { return; }
        this.BinValues!.FinishRender();
    }

    public override void Resize(int width, int height) => UpdateConfig();
}
