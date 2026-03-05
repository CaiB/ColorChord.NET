using System;
using System.IO;
using System.Reflection;
using Win32.Graphics.Direct3D;
using Win32.Graphics.Direct3D12;
using Win32.Graphics.Dxgi.Common;
using static Win32.Apis;
using static Win32.Graphics.Direct3D.Fxc.Apis;
using static Win32.Graphics.Direct3D12.Apis;
using static ColorChord.NET.Outputs.DisplayD3D12Support.COMUtils;

namespace ColorChord.NET.Outputs.DisplayD3D12Support;

public sealed unsafe class Shader : IDisposable
{
    private const string PATH_PREFIX = "ColorChord.NET.Outputs.DisplayD3D12Modes.Shaders.Compiled.";
    private ID3D12RootSignature* RootSignature = null;
    private ID3D12PipelineState* PipelineState = null;
    private bool IsDisposed;

    public Shader(ID3D12Device2* device, InputElementDescription[] vertexInputs, string vertexPath, string pixelPath, Assembly? vertexAssembly = null, Assembly? pixelAssembly = null, RootParameter1[]? rootParameters = null)
    {
        string VertexPath = (vertexPath.StartsWith('#') ? vertexPath.Substring(1) : PATH_PREFIX + vertexPath);
        string PixelPath = (pixelPath.StartsWith('#') ? pixelPath.Substring(1) : PATH_PREFIX + pixelPath);
        Assembly Asm = Assembly.GetExecutingAssembly();

        ID3DBlob* VertexShaderBlob = null;
        using (Stream? VertexStream = (vertexAssembly ?? Asm).GetManifestResourceStream(VertexPath))
        {
            if (VertexStream == null) { throw new Exception($"Could not load vertex shader \"{VertexPath}\""); }

            long DataSize = VertexStream.Length;
            ThrowIfFailed(D3DCreateBlob((nuint)DataSize, &VertexShaderBlob));

            Span<byte> BlobBuffer = new(VertexShaderBlob->GetBufferPointer(), (int)DataSize);
            int BytesRead = VertexStream.Read(BlobBuffer);
            if (BytesRead != DataSize) { throw new Exception("Data read from vertex shader object did not match length"); }
        }

        ID3DBlob* PixelShaderBlob = null;
        using (Stream? PixelStream = (pixelAssembly ?? Asm).GetManifestResourceStream(PixelPath))
        {
            if (PixelStream == null) { throw new Exception($"Could not load pixel shader \"{PixelPath}\""); }

            long DataSize = PixelStream.Length;
            ThrowIfFailed(D3DCreateBlob((nuint)DataSize, &PixelShaderBlob));

            Span<byte> BlobBuffer = new(PixelShaderBlob->GetBufferPointer(), (int)DataSize);
            int BytesRead = PixelStream.Read(BlobBuffer);
            if (BytesRead != DataSize) { throw new Exception("Data read from pixel shader object did not match length"); }
        }

        FeatureDataRootSignature FeatureData = new() { HighestVersion = RootSignatureVersion.V1_1 };
        if (device->CheckFeatureSupport(Feature.RootSignature, &FeatureData, sizeof(FeatureDataRootSignature)).Failure) { FeatureData.HighestVersion = RootSignatureVersion.V1_0; }
        RootSignatureFlags RootSignatureFlags =
            RootSignatureFlags.AllowInputAssemblerInputLayout |
            RootSignatureFlags.DenyHullShaderRootAccess | RootSignatureFlags.DenyDomainShaderRootAccess | RootSignatureFlags.DenyGeometryShaderRootAccess | RootSignatureFlags.DenyPixelShaderRootAccess;

        VersionedRootSignatureDescription RootSignatureDescription;
        if (rootParameters == null || rootParameters.Length == 0) { VersionedRootSignatureDescription.Init_1_1(out RootSignatureDescription, 0, null, 0, null, RootSignatureFlags); }
        else
        {
            fixed (RootParameter1* RootParametersPtr = &rootParameters[0]) { VersionedRootSignatureDescription.Init_1_1(out RootSignatureDescription, (uint)rootParameters.Length, RootParametersPtr, 0, null, RootSignatureFlags); }
        }

        ID3DBlob* RootSignatureBlob = default, ErrorBlob = default;
        ThrowIfFailed(D3D12SerializeVersionedRootSignature(&RootSignatureDescription, FeatureData.HighestVersion, &RootSignatureBlob, &ErrorBlob));

        ID3D12RootSignature* RootSignature = null;
        ThrowIfFailed(device->CreateRootSignature(0, RootSignatureBlob->GetBufferPointer(), RootSignatureBlob->GetBufferSize(), __uuidof<ID3D12RootSignature>(), (void**)&RootSignature));
        this.RootSignature = RootSignature;

        RtFormatArray RTVFormats = new()
        {
            NumRenderTargets = 1,
            RTFormats = { e0 = Format.R8G8B8A8Unorm }
        };

        fixed (InputElementDescription* VertexInputsPtr = &vertexInputs[0])
        {
            PipelineStateObjectBuilder PSOBuilder = new();
            PSOBuilder.AppendMember(new PSORootSignature(RootSignature)); // TODO: this requires this.RootSignature to be pinned until ???????
            PSOBuilder.AppendMember(new PSOInputLayout(new() { NumElements = (uint)vertexInputs.Length, pInputElementDescs = VertexInputsPtr }));
            PSOBuilder.AppendMember(new PSOPrimitiveTopology(PrimitiveTopologyType.Triangle));
            PSOBuilder.AppendMember(new PSOVertexShader(new ShaderBytecode(VertexShaderBlob))); // TODO same as ^
            PSOBuilder.AppendMember(new PSOPixelShader(new ShaderBytecode(PixelShaderBlob))); // ^
            PSOBuilder.AppendMember(new PSODepthStencilFormat(Format.D32Float));
            PSOBuilder.AppendMember(new PSORenderTargetFormats(RTVFormats));
            byte[] PSOStream = PSOBuilder.GetResult();
            uint PSOStreamSize = PSOBuilder.GetResultLength();

            fixed (byte* PSOStreamPtr = PSOStream)
            {
                PipelineStateStreamDescription PipelineStateStreamDesc = new()
                {
                    SizeInBytes = PSOStreamSize,
                    pPipelineStateSubobjectStream = PSOStreamPtr
                };
                ID3D12PipelineState* PipelineState = null;
                ThrowIfFailed(device->CreatePipelineState(&PipelineStateStreamDesc, __uuidof<ID3D12PipelineState>(), (void**)&PipelineState));
                this.PipelineState = PipelineState;
            }
        }

        COMRelease(&VertexShaderBlob);
        COMRelease(&PixelShaderBlob);
        COMRelease(&RootSignatureBlob);
        COMRelease(&ErrorBlob);
    }

    public void Use(ID3D12GraphicsCommandList* commandList)
    {
        commandList->SetPipelineState(this.PipelineState);
        commandList->SetGraphicsRootSignature(this.RootSignature);
    }

    private void Dispose(bool disposing) // TODO: a bunch of things are created in the constructor which I don't currently clean up. Figure out when they can be cleaned and do so
    {
        if (!this.IsDisposed)
        {
            COMRelease(ref this.PipelineState);
            COMRelease(ref this.RootSignature);
            this.IsDisposed = true;
        }
    }

    ~Shader() => Dispose(disposing: false);

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
