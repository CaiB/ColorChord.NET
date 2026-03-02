using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Win32.Graphics.Direct3D12;
using Win32.Graphics.Dxgi.Common;

namespace ColorChord.NET.Outputs.DisplayD3D12Support;

public class PipelineStateObjectBuilder
{
    private byte[] Buffer;
    private int ContentLength = 0;

    public PipelineStateObjectBuilder()
    {
        this.Buffer = new byte[128];
    }

    private void Expand(int minAvailableBytes)
    {
        byte[] NewBuffer = new byte[Math.Max(this.ContentLength + minAvailableBytes, this.Buffer.Length * 2)];
        Array.Copy(this.Buffer, NewBuffer, this.ContentLength);
        this.Buffer = NewBuffer;
    }

    public unsafe void AppendMember<T>(T member) where T: unmanaged, IPSOMember
    {
        int StructSize = Marshal.SizeOf<T>();
        if (this.Buffer.Length < this.ContentLength + StructSize) { Expand(StructSize); }
        ReadOnlySpan<byte> StructAsBytes = new(&member, StructSize); // TODO: does member need to be pinned for this?
        StructAsBytes.CopyTo(new(this.Buffer, this.ContentLength, this.Buffer.Length - this.ContentLength));
        this.ContentLength += StructSize;
    }

    public byte[] GetResult() => this.Buffer;
    public uint GetResultLength() => (uint)this.ContentLength;
}

public interface IPSOMember;


public unsafe struct PSORootSignature(ID3D12RootSignature* rootSig) : IPSOMember
{
    private readonly PSOSubobjectType Tag = PSOSubobjectType.RootSignature;
    public ID3D12RootSignature* Value = rootSig;
}

public struct PSOInputLayout(InputLayoutDescription desc) : IPSOMember
{
    private readonly PSOSubobjectType Tag = PSOSubobjectType.InputLayout;
    public InputLayoutDescription Value = desc;
}

public struct PSOPrimitiveTopology(PrimitiveTopologyType primitiveTopologyType) : IPSOMember
{
    private readonly PSOSubobjectType Tag = PSOSubobjectType.PrimitiveTopology;
    public PrimitiveTopologyType Value = primitiveTopologyType;
}

public struct PSOVertexShader(ShaderBytecode shaderBytecode) : IPSOMember
{
    private readonly PSOSubobjectType Tag = PSOSubobjectType.VertexShader;
    public ShaderBytecode Value = shaderBytecode;
}

public struct PSOPixelShader(ShaderBytecode shaderBytecode) : IPSOMember
{
    private readonly PSOSubobjectType Tag = PSOSubobjectType.PixelShader;
    public ShaderBytecode Value = shaderBytecode;
}

public struct PSODepthStencilFormat(Format format) : IPSOMember
{
    private readonly PSOSubobjectType Tag = PSOSubobjectType.DepthStencilFormat;
    public Format Value = format;
}

public struct PSORenderTargetFormats(RtFormatArray formatArray) : IPSOMember
{
    private readonly PSOSubobjectType Tag = PSOSubobjectType.RenderTargetFormats;
    public RtFormatArray Value = formatArray;
}

public enum PSOSubobjectType : int
{
    RootSignature = 0,
    VertexShader = 1,
    PixelShader = 2,
    DomainShader = 3,
    HullShader = 4,
    GeometryShader = 5,
    ComputeShader = 6,
    StreamOutput = 7,
    Blend = 8,
    SampleMask = 9,
    Rasterizer = 10,
    DepthStencil = 11,
    InputLayout = 12,
    IBStripCutValue = 13,
    PrimitiveTopology = 14,
    RenderTargetFormats = 15,
    DepthStencilFormat = 16,
    SampleDesc = 17,
    NodeMask = 18,
    CachedPSO = 19,
    Flags = 20,
    DepthStencil1 = 21,
    ViewInstancing = 22,
    // intentionally missing 23
    AS = 24,
    MS = 25,
    DepthStencil2 = 26,
    Rasterizer1 = 27,
    Rasterizer2 = 28,
    SerializedRootSignature = 29,
    MaxValid = 30
}