using System;
using System.Diagnostics;
using System.Numerics;
using Vortice.Win32;
using Vortice.Win32.Graphics.Direct3D12;
using Vortice.Win32.Graphics.Dxgi.Common;
using static ColorChord.NET.Outputs.DisplayD3D12Support.COMUtils;
using static Vortice.Win32.Graphics.Direct3D12.Apis;
using static Vortice.Win32.Apis;

namespace ColorChord.NET.Outputs.DisplayD3D12Support;

public unsafe class DataBuffer<T> : IDisposable where T : unmanaged
{
    private ID3D12Resource* BF_Buffer;
    private ResourceDescription Description;
    private readonly uint ElementSize;
    private uint Count;
    private string? Name;
    private bool IsDisposed;

    public bool IsByteAddressable { get; private init; }
    public Format Format { get; private init; }
    public ID3D12Resource* Buffer { get => this.BF_Buffer; }
    public GpuDescriptorHandle GPUHandleSRV { get; private set; }
    public GpuDescriptorHandle GPUHandleUAV { get; private set; }

    public DataBuffer(bool isByteAddressable, Format formatOverride = Format.Unknown, uint elementSizeOverride = 0, string? name = null)
    {
        this.IsByteAddressable = isByteAddressable;
        this.Name = name;
        this.ElementSize = elementSizeOverride == 0 ? (uint)sizeof(T) : elementSizeOverride;

        if (isByteAddressable) { this.Format = Format.R32Typeless; }
        else if (formatOverride != Format.Unknown) { this.Format = formatOverride; }
        else
        {
            this.Format = typeof(T) switch
            {
                //Type ByteType when typeof(T) == typeof(byte) => Format.R8Uint,
                //Type ShortType when typeof(T) == typeof(short) => Format.R16Sint,
                //Type UShortType when typeof(T) == typeof(ushort) => Format.R16Uint,
                Type IntType when typeof(T) == typeof(int) => Format.R32Sint,
                Type UIntType when typeof(T) == typeof(uint) => Format.R32Uint,
                Type FloatType when typeof(T) == typeof(float) => Format.R32Float,
                Type Float4Type when typeof(T) == typeof(Vector4) => Format.R32G32B32A32Float,
                _ => throw new Exception($"{nameof(DataBuffer<T>)} could not automatically determine the buffer format for type {typeof(T).FullName}. Override it manually using {nameof(formatOverride)}.")
            };
        }
    }

    /// <summary>Prepares the data buffer, but doesn't create descriptors on a heap. Use this if descriptors are being placed directly into the root signature, rather than on a descriptor heap.</summary>
    public void Create(ID3D12Device2* device, DataHeap dataHeap, uint dataCount)
    {
        if (dataCount == 0) { throw new ArgumentException("Must be greater than 0", nameof(dataCount)); }
        this.Count = dataCount;
        this.Description = new()
        {
            Alignment = 0,
            DepthOrArraySize = 1,
            Dimension = ResourceDimension.Buffer,
            Flags = ResourceFlags.AllowUnorderedAccess,
            Format = Format.Unknown,
            Height = 1,
            Layout = TextureLayout.RowMajor,
            MipLevels = 1,
            SampleDesc = new() { Count = 1, Quality = 0 },
            Width = dataCount * this.ElementSize // TODO: Is this right?
        };
        fixed (ResourceDescription* DescPtr = &this.Description) { this.BF_Buffer = dataHeap.AllocateBuffer(device, DescPtr, this.ElementSize * dataCount); }
        if (this.Name != null) { this.BF_Buffer->SetName(this.Name); }
    }

    public void Create(ID3D12Device2* device, DataHeap dataHeap, DescriptorHeap descriptorHeap, uint dataCount)
    {
        if (dataCount == 0) { throw new ArgumentException("Must be greater than 0", nameof(dataCount)); }
        Debug.Assert(descriptorHeap.Type == DescriptorHeapType.CbvSrvUav);
        
        this.Count = dataCount;
        this.Description = new()
        {
            Alignment = 0,
            DepthOrArraySize = 1,
            Dimension = ResourceDimension.Buffer,
            Flags = ResourceFlags.AllowUnorderedAccess,
            Format = Format.Unknown,
            Height = 1,
            Layout = TextureLayout.RowMajor,
            MipLevels = 1,
            SampleDesc = new() { Count = 1, Quality = 0 },
            Width = dataCount * this.ElementSize // TODO: Is this right?
        };
        fixed (ResourceDescription* DescPtr = &this.Description) { this.BF_Buffer = dataHeap.AllocateBuffer(device, DescPtr, this.ElementSize * dataCount); }
        if (this.Name != null) { this.BF_Buffer->SetName(this.Name); }

        {
            ShaderResourceViewDescription SRVDesc = new()
            {
                ViewDimension = SrvDimension.Buffer,
                Format = this.Format,
                Shader4ComponentMapping = 0x1688, // no remapping
                Anonymous = new()
                {
                    Buffer = new()
                    {
                        NumElements = dataCount,
                        Flags = this.IsByteAddressable ? BufferSrvFlags.Raw : BufferSrvFlags.None
                    }
                }
            };
            (CpuDescriptorHandle CPUHeapHandleSRV, GpuDescriptorHandle GPUHeapHandleSRV) = descriptorHeap.AllocateObject();
            device->CreateShaderResourceView(this.Buffer, &SRVDesc, CPUHeapHandleSRV);
            this.GPUHandleSRV = GPUHeapHandleSRV;
        }
        {
            UnorderedAccessViewDescription UAVDesc = new()
            {
                ViewDimension = UavDimension.Buffer,
                Format = this.Format,
                Anonymous = new()
                {
                    Buffer = new()
                    {
                        NumElements = dataCount,
                        Flags = this.IsByteAddressable ? BufferUavFlags.Raw : BufferUavFlags.None
                    }
                }
            };
            (CpuDescriptorHandle CPUHeapHandleUAV, GpuDescriptorHandle GPUHeapHandleUAV) = descriptorHeap.AllocateObject();
            device->CreateUnorderedAccessView(this.Buffer, null, &UAVDesc, CPUHeapHandleUAV);
            this.GPUHandleUAV = GPUHeapHandleUAV;
        }
    }

    public void Load(ID3D12Device2* device, CommandList copyCommandList, out ID3D12Resource* intermediateBuffer, ReadOnlySpan<T> data)
    {
        if (this.Count != data.Length) { throw new Exception($"{nameof(DataBuffer<T>)} was created with size {this.Count}, but an attempt was made to load {data.Length} items. These must match."); }

        uint BufferSize = this.ElementSize * (uint)data.Length;
        ID3D12Resource* IntermediateResource = default;
        HeapProperties HeapPropertiesInt = new() { Type = HeapType.Upload };
        ResourceDescription ResourceDescInt = ResourceDescription.Buffer(BufferSize, ResourceFlags.None);
        ThrowIfFailed(device->CreateCommittedResource(&HeapPropertiesInt, HeapFlags.None, &ResourceDescInt, ResourceStates.GenericRead, null, __uuidof<ID3D12Resource>(), (void**)&IntermediateResource));
        SubresourceData Subresource = new()
        {
            pData = data.GetPointer(), // TODO: ??
            RowPitch = (nint)BufferSize,
            SlicePitch = (nint)BufferSize
        };
        UpdateSubresources((ID3D12GraphicsCommandList*)copyCommandList.NativeList, this.Buffer, IntermediateResource, 0, 0, 1, &Subresource);
        intermediateBuffer = IntermediateResource;
    }

    public void Activate(ID3D12GraphicsCommandList* directCommandList) // TODO: DO
    {
        Debug.Assert(this.Buffer != null);
        
    }

    public void Deactivate() // TODO: DO
    {

    }


    protected virtual void Dispose(bool disposing)
    {
        if (!this.IsDisposed)
        {
            COMRelease(ref this.BF_Buffer);
            this.IsDisposed = true;
        }
    }

    ~DataBuffer() => Dispose(disposing: false);

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
