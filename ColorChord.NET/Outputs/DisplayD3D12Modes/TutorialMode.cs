using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using ColorChord.NET.Outputs.DisplayD3D12Support;
using OpenTK.Audio.OpenAL;
using OpenTK.Compute.OpenCL;
using Win32;
using Win32.Graphics.Direct3D;
using Win32.Graphics.Direct3D12;
using Win32.Graphics.Dxgi.Common;
using Win32.Numerics;
using static System.Net.WebRequestMethods;
using static Win32.Apis;
using static Win32.Graphics.Direct3D.Fxc.Apis;
using static Win32.Graphics.Direct3D12.Apis;
using static Win32.Graphics.Dxgi.Apis;

namespace ColorChord.NET.Outputs.DisplayD3D12Modes;

public unsafe class TutorialMode
{
    struct VertexData
    {
        public Vector3 Position;
        public Vector3 Colour;
    }

    private readonly VertexData[] Vertices =
    {
        new() { Position = new(-1.0f, -1.0f, -1.0f), Colour = new(0.0f, 0.0f, 0.0f) }, // 0
        new() { Position = new(-1.0f,  1.0f, -1.0f), Colour = new(0.0f, 1.0f, 0.0f) }, // 1
        new() { Position = new( 1.0f,  1.0f, -1.0f), Colour = new(1.0f, 1.0f, 0.0f) }, // 2
        new() { Position = new( 1.0f, -1.0f, -1.0f), Colour = new(1.0f, 0.0f, 0.0f) }, // 3
        new() { Position = new(-1.0f, -1.0f,  1.0f), Colour = new(0.0f, 0.0f, 1.0f) }, // 4
        new() { Position = new(-1.0f,  1.0f,  1.0f), Colour = new(0.0f, 1.0f, 1.0f) }, // 5
        new() { Position = new( 1.0f,  1.0f,  1.0f), Colour = new(1.0f, 1.0f, 1.0f) }, // 6
        new() { Position = new( 1.0f, -1.0f,  1.0f), Colour = new(1.0f, 0.0f, 1.0f) }  // 7
    };
    private readonly ushort[] CubeIndices =
    {
        0, 1, 2, 0, 2, 3,
        4, 6, 5, 4, 7, 6,
        4, 5, 1, 4, 1, 0,
        3, 2, 6, 3, 6, 7,
        1, 5, 6, 1, 6, 2,
        4, 0, 3, 4, 3, 7
    };
    private readonly Rect ScissorRect = new(0, 0, int.MaxValue, int.MaxValue);
    private Viewport Viewport = new();
    private float FOV = 45F;
    private bool ContentLoaded = false;
    private float Time = 0F;
    private OpenTK.Mathematics.Matrix4 ModelMatrix, ViewMatrix, ProjectionMatrix;

    private ComPtr<ID3D12Resource> GPUVertexBuffer = default;
    private VertexBufferView GPUVertexBufferView;

    private ComPtr<ID3D12Resource> GPUIndexBuffer = default;
    private IndexBufferView GPUIndexBufferView;

    private ComPtr<ID3D12DescriptorHeap> DescriptorHeapDSV = default;
    private ComPtr<ID3D12Resource> DepthBuffer = default;

    private ComPtr<ID3D12RootSignature> RootSignature = default;
    private ComPtr<ID3D12PipelineState> PipelineState = default;

    private readonly Window Window;
    private readonly DisplayD3D12 Host;

    public TutorialMode(Window window, DisplayD3D12 host)
    {
        this.Window = window;
        this.Host = host;

        this.Window.OnResize += OnResize;
    }

    public void UpdateBufferResource<T>(ref ComPtr<ID3D12Device2> device, ref ComPtr<ID3D12GraphicsCommandList2> commandList, ReadOnlySpan<T> data, ResourceFlags flags, out ComPtr<ID3D12Resource> intermediateResource, out ComPtr<ID3D12Resource> destinationResource) where T : unmanaged
    {
        ulong BufferSize = (ulong)(sizeof(T) * data.Length);
        HeapProperties HeapPropertiesDest = new() { Type = HeapType.Default };
        ResourceDescription ResourceDesc = ResourceDescription.Buffer(BufferSize, flags);
        destinationResource = default;
        ThrowIfFailed(device.Get()->CreateCommittedResource(&HeapPropertiesDest, HeapFlags.None, &ResourceDesc, ResourceStates.Common, null, __uuidof<ID3D12Resource>(), (void**)destinationResource.GetAddressOf()));
        
        intermediateResource = default;
        if (data.Length > 0)
        {
            HeapProperties HeapPropertiesInt = new() { Type = HeapType.Upload };
            ResourceDescription ResourceDescInt = ResourceDescription.Buffer(BufferSize, flags);
            ThrowIfFailed(device.Get()->CreateCommittedResource(&HeapPropertiesInt, HeapFlags.None, &ResourceDescInt, ResourceStates.GenericRead, null, __uuidof<ID3D12Resource>(), (void**)intermediateResource.GetAddressOf()));
            SubresourceData Subresource = new()
            {
                pData = data.GetPointer(),
                RowPitch = (nint)BufferSize,
                SlicePitch = (nint)BufferSize
            };
            UpdateSubresources((ID3D12GraphicsCommandList*)commandList.Get(), destinationResource.Get(), intermediateResource.Get(), 0, 0, 1, &Subresource);
        }
    }

    private void LoadVertexBuffer<T>(ref ComPtr<ID3D12Device2> device, ref ComPtr<ID3D12GraphicsCommandList2> commandList, ReadOnlySpan<T> data, out ComPtr<ID3D12Resource> vertexBuffer, out VertexBufferView bufferView) where T : unmanaged
    {
        // TODO: for some reason the tutorial completely omits the flags argument???????
        UpdateBufferResource(ref device, ref commandList, data, ResourceFlags.None, out ComPtr<ID3D12Resource> IntermediateVertexBuffer, out vertexBuffer);
        bufferView = new()
        {
            BufferLocation = vertexBuffer.Get()->GetGPUVirtualAddress(),
            SizeInBytes = (uint)(sizeof(T) * data.Length),
            StrideInBytes = (uint)sizeof(T)
        };
    }

    private void LoadIndexBuffer<T>(ref ComPtr<ID3D12Device2> device, ref ComPtr<ID3D12GraphicsCommandList2> commandList, ReadOnlySpan<T> data, out ComPtr<ID3D12Resource> indexBuffer, out IndexBufferView bufferView) where T : unmanaged
    {
        UpdateBufferResource(ref device, ref commandList, data, ResourceFlags.None, out ComPtr<ID3D12Resource> IntermediateVertexBuffer, out indexBuffer);
        bufferView = new()
        {
            BufferLocation = indexBuffer.Get()->GetGPUVirtualAddress(),
            Format = typeof(T) switch
            {
                Type ByteType when typeof(T) == typeof(byte) => Format.R8Uint,
                Type UShortType when typeof(T) == typeof(ushort) => Format.R16Uint,
                Type UIntType when typeof(T) == typeof(uint) => Format.R32Uint,
                _ => throw new Exception($"{nameof(LoadIndexBuffer)} does not support the type {typeof(T).FullName}")
            },
            SizeInBytes = (uint)(sizeof(T) * data.Length)
        };
    }

    public bool LoadContent(ref ComPtr<ID3D12Device2> device, CommandQueue copyQueue)
    {
        if (copyQueue.CommandListType != CommandListType.Copy) { throw new Exception($"{nameof(LoadContent)} must be given a copy queue."); }
        ComPtr<ID3D12GraphicsCommandList2> CommandList = copyQueue.GetCommandList();

        LoadVertexBuffer(ref device, ref CommandList, this.Vertices, out this.GPUVertexBuffer, out this.GPUVertexBufferView);
        LoadIndexBuffer(ref device, ref CommandList, this.CubeIndices, out this.GPUIndexBuffer, out this.GPUIndexBufferView);

        DescriptorHeapDescription DSVHeapDesc = new()
        {
            NumDescriptors = 1,
            Type = DescriptorHeapType.Dsv,
            Flags = DescriptorHeapFlags.None
        };

        fixed (ComPtr<ID3D12DescriptorHeap>* DescriptorHeapPtr = &this.DescriptorHeapDSV)
        {
            ThrowIfFailed(device.Get()->CreateDescriptorHeap(&DSVHeapDesc, __uuidof<ID3D12DescriptorHeap>(), (void**)(*DescriptorHeapPtr).GetAddressOf()));
        }

        ComPtr<ID3DBlob> VertexShaderBlob = default;
        Assembly Asm = Assembly.GetExecutingAssembly();
        using (Stream? VertexStream = Asm.GetManifestResourceStream("ColorChord.NET.Outputs.DisplayD3D12Modes.Shaders.Compiled.Tutorial_V.cso"))
        {
            if (VertexStream == null) { throw new Exception($"Could not load vertex shader"); }

            long DataSize = VertexStream.Length;
            ThrowIfFailed(D3DCreateBlob((nuint)DataSize, VertexShaderBlob.GetAddressOf()));

            Span<byte> BlobBuffer = new(VertexShaderBlob.Get()->GetBufferPointer(), (int)DataSize);
            int BytesRead = VertexStream.Read(BlobBuffer);
            if (BytesRead != DataSize) { throw new Exception("Data read from vertex shader object did not match length"); }
        }

        ComPtr<ID3DBlob> PixelShaderBlob = default;
        using (Stream? PixelStream = Asm.GetManifestResourceStream("ColorChord.NET.Outputs.DisplayD3D12Modes.Shaders.Compiled.Tutorial_P.cso"))
        {
            if (PixelStream == null) { throw new Exception($"Could not load pixel shader"); }

            long DataSize = PixelStream.Length;
            ThrowIfFailed(D3DCreateBlob((nuint)DataSize, PixelShaderBlob.GetAddressOf()));

            Span<byte> BlobBuffer = new(PixelShaderBlob.Get()->GetBufferPointer(), (int)DataSize);
            int BytesRead = PixelStream.Read(BlobBuffer);
            if (BytesRead != DataSize) { throw new Exception("Data read from pixel shader object did not match length"); }
        }

        InputElementDescription[] VertexInputs =
        {
            new()
            {
                SemanticName = "POSITION\0"u8.GetPointer(),
                SemanticIndex = 0,
                Format = Format.R32G32B32Float,
                InputSlot = 0,
                AlignedByteOffset = D3D12_APPEND_ALIGNED_ELEMENT,
                InputSlotClass = InputClassification.PerVertexData,
                InstanceDataStepRate = 0
            },
            new()
            {
                SemanticName = "COLOR\0"u8.GetPointer(),
                SemanticIndex = 0,
                Format = Format.R32G32B32Float,
                InputSlot = 0,
                AlignedByteOffset = D3D12_APPEND_ALIGNED_ELEMENT,
                InputSlotClass = InputClassification.PerVertexData,
                InstanceDataStepRate = 0
            }
        };

        FeatureDataRootSignature FeatureData = new() { HighestVersion = RootSignatureVersion.V1_1 };
        if (device.Get()->CheckFeatureSupport(Feature.RootSignature, &FeatureData, sizeof(FeatureDataRootSignature)).Failure) { FeatureData.HighestVersion = RootSignatureVersion.V1_0; }
        RootSignatureFlags RootSignatureFlags =
            RootSignatureFlags.AllowInputAssemblerInputLayout |
            RootSignatureFlags.DenyHullShaderRootAccess | RootSignatureFlags.DenyDomainShaderRootAccess | RootSignatureFlags.DenyGeometryShaderRootAccess | RootSignatureFlags.DenyPixelShaderRootAccess;

        RootParameter1.InitAsConstants(out RootParameter1 MatrixParameter, (uint)(sizeof(Matrix4x4) / sizeof(float)), 0, 0, ShaderVisibility.Vertex);
        RootParameter1[] RootParameters = [MatrixParameter];

        VersionedRootSignatureDescription.Init_1_1(out VersionedRootSignatureDescription RootSignatureDescription, (uint)RootParameters.Length, RootParameters.GetPointer(), 0, null, RootSignatureFlags);

        ComPtr<ID3DBlob> RootSignatureBlob = default, ErrorBlob = default;
        ThrowIfFailed(D3D12SerializeVersionedRootSignature(&RootSignatureDescription, FeatureData.HighestVersion, RootSignatureBlob.GetAddressOf(), ErrorBlob.GetAddressOf()));
        fixed (ComPtr<ID3D12RootSignature>* SigPtr = &RootSignature)
        {
            ThrowIfFailed(device.Get()->CreateRootSignature(0, RootSignatureBlob.Get()->GetBufferPointer(), RootSignatureBlob.Get()->GetBufferSize(), __uuidof<ID3D12RootSignature>(), (void**)(*SigPtr).GetAddressOf()));
        }

        RtFormatArray RTVFormats = new()
        {
            NumRenderTargets = 1,
            RTFormats = { e0 = Format.R8G8B8A8Unorm }
        };

        PipelineStateObjectBuilder PSOBuilder = new();
        //PSOBuilder.AppendMember(new PSORootSignature(this.RootSignature.Get())); // TODO: this requires this.RootSignature to be pinned until ???????
        //PSOBuilder.AppendMember(new PSOInputLayout(new() { NumElements = (uint)VertexInputs.Length, pInputElementDescs = VertexInputs.GetPointer() }));
        PSOBuilder.AppendMember(new PSOPrimitiveTopology(PrimitiveTopologyType.Triangle));
        PSOBuilder.AppendMember(new PSOVertexShader(new ShaderBytecode(VertexShaderBlob.Get()))); // TODO same as ^
        //PSOBuilder.AppendMember(new PSOPixelShader(new ShaderBytecode(PixelShaderBlob.Get()))); // ^
        //PSOBuilder.AppendMember(new PSODepthStencilFormat(Format.D32Float));
        //PSOBuilder.AppendMember(new PSORenderTargetFormats(RTVFormats));
        byte[] PSOStream = PSOBuilder.GetResult();
        uint PSOStreamSize = PSOBuilder.GetResultLength();

        fixed (byte* PSOStreamPtr = PSOStream)
        {
            PipelineStateStreamDescription PipelineStateStreamDesc = new()
            {
                SizeInBytes = PSOStreamSize,
                pPipelineStateSubobjectStream = PSOStreamPtr
            };
            fixed (ComPtr<ID3D12PipelineState>* StatePtr = &this.PipelineState)
            {
                ThrowIfFailed(device.Get()->CreatePipelineState(&PipelineStateStreamDesc, __uuidof<ID3D12PipelineState>(), (void**)(*StatePtr).GetAddressOf()));
            }
        }

        ulong FenceValue = copyQueue.ExecuteCommandList(ref CommandList);
        copyQueue.WaitForFenceValue(FenceValue);
        this.ContentLoaded = true;

        ResizeDepthBuffer(this.Window.Width, this.Window.Height);

        return false;
    }

    private void ResizeDepthBuffer(int width, int height)
    {
        if (this.ContentLoaded)
        {
            this.Host.Flush();

            width = Math.Min(1, width);
            height = Math.Min(1, height);

            ref ComPtr<ID3D12Device2> Device = ref this.Host.GetDevice();
            ClearValue OptimizedClearValue = new()
            {
                Format = Format.D32Float,
                DepthStencil = new(){ Depth = 1.0F, Stencil = 0 }
            };

            HeapProperties HeapProps = new(HeapType.Default);
            ResourceDescription DepthResource = ResourceDescription.Tex2D(Format.D32Float, (ulong)width, (uint)height, 1, 0, 1, 0, ResourceFlags.AllowDepthStencil);
            DepthStencilViewDescription DSVDesc = new()
            {
                Format = Format.D32Float,
                ViewDimension = DsvDimension.Texture2D,
                Anonymous = new()
                {
                    Texture2D = new()
                    {
                        MipSlice = 0
                    }
                },
                Flags = DsvFlags.None
            };

            fixed (ComPtr<ID3D12Resource>* DepthBufferPtr = &DepthBuffer)
            {
                ThrowIfFailed(Device.Get()->CreateCommittedResource(&HeapProps, HeapFlags.None, &DepthResource, ResourceStates.DepthWrite, &OptimizedClearValue, __uuidof<ID3D12Resource>(), (void**)(*DepthBufferPtr).GetAddressOf()));
                Device.Get()->CreateDepthStencilView((*DepthBufferPtr).Get(), &DSVDesc, this.DescriptorHeapDSV.Get()->GetCPUDescriptorHandleForHeapStart());
            }
        }
    }

    public void OnResize(object? sender, EventArgs evt)
    {
        this.Viewport = new(0F, 0F, this.Window.Width, this.Window.Height); // TODO: make better
        ResizeDepthBuffer(this.Window.Width, this.Window.Height);
    }

    public void Update()
    {
        this.Time += 0.05F;
        float Angle = this.Time * 90F;
        this.ModelMatrix = OpenTK.Mathematics.Matrix4.CreateRotationX(Angle);

        OpenTK.Mathematics.Vector3 EyePosition = new(0, 0, -10);
        OpenTK.Mathematics.Vector3 FocusPoint = new(0, 0, 0);
        OpenTK.Mathematics.Vector3 UpDirection = new(0, 1, 0);
        this.ViewMatrix = OpenTK.Mathematics.Matrix4.LookAt(EyePosition, FocusPoint, UpDirection);

        float AspectRatio = this.Window.Width / (float)this.Window.Height;
        this.ProjectionMatrix = OpenTK.Mathematics.Matrix4.CreatePerspectiveFieldOfView(this.FOV / 180F * MathF.PI, AspectRatio, 0.1F, 100F);
    }

    public void Render(ref ComPtr<ID3D12GraphicsCommandList> commandList, ref CpuDescriptorHandle rtv)
    {

        CpuDescriptorHandle DSV = this.DescriptorHeapDSV.Get()->GetCPUDescriptorHandleForHeapStart();
        CpuDescriptorHandle RTV = rtv;

        // Clear depth
        commandList.Get()->ClearDepthStencilView(DSV, ClearFlags.Depth, 1F, 0, 0, null);

        // Pipeline state
        fixed (ComPtr<ID3D12PipelineState>* PipelineStatePtr = &this.PipelineState) { commandList.Get()->SetPipelineState((*PipelineStatePtr).Get()); }
        fixed (ComPtr<ID3D12RootSignature>* RootSignaturePtr = &this.RootSignature) { commandList.Get()->SetGraphicsRootSignature((*RootSignaturePtr).Get()); }

        commandList.Get()->IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        fixed (VertexBufferView* VertexViewPtr = &this.GPUVertexBufferView) { commandList.Get()->IASetVertexBuffers(0, 1, VertexViewPtr); }
        fixed (IndexBufferView* IndexViewPtr = &this.GPUIndexBufferView) { commandList.Get()->IASetIndexBuffer(IndexViewPtr); }

        fixed (Viewport* ViewportPtr = &this.Viewport) { commandList.Get()->RSSetViewports(1, ViewportPtr); }
        fixed (Rect* ScissorPtr = &this.ScissorRect) { commandList.Get()->RSSetScissorRects(1, ScissorPtr); }

        commandList.Get()->OMSetRenderTargets(1, &RTV, false, &DSV);

        // Push matrix
        OpenTK.Mathematics.Matrix4 MVPMatrix = this.ModelMatrix * this.ViewMatrix * this.ProjectionMatrix;
        commandList.Get()->SetGraphicsRoot32BitConstants(0, (uint)(sizeof(OpenTK.Mathematics.Matrix4) / sizeof(float)), &MVPMatrix, 0);

        commandList.Get()->DrawIndexedInstanced((uint)(this.CubeIndices.Length), 1, 0, 0, 0);


    }
}
