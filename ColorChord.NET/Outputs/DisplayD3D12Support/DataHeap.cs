using System;
using System.Collections.Generic;
using System.Text;
using Vortice.Win32.Graphics.Direct3D12;
using static Vortice.Win32.Apis;

namespace ColorChord.NET.Outputs.DisplayD3D12Support;

public unsafe class DataHeap : IDisposable
{
    // TODO: It sounds like by querying and properly handling UMA vs NUMA, memory access can be made significantly more efficient on integrated graphics.
    // This could be a potential future optimization avenue.

    private ID3D12Heap* BF_Heap;
    public ID3D12Heap* Heap { get => this.BF_Heap; private set => this.BF_Heap = value; }
    private readonly ulong Capacity = 0;
    private ulong CurrentOffset = 0;
    private bool IsDisposed;

    public DataHeap(ID3D12Device2* device, ulong sizeInBytes)
    {
        HeapDescription HeapDesc = new()
        {
            Alignment = 0,
            Flags = HeapFlags.AllowOnlyBuffers,
            Properties = new()
            {
                Type = HeapType.Default,
                CPUPageProperty = CpuPageProperty.Unknown,
                MemoryPoolPreference = MemoryPool.Unknown,
                CreationNodeMask = 0,
                VisibleNodeMask = 0
            },
            SizeInBytes = sizeInBytes
        };
        ID3D12Heap* ResultHeap;
        ThrowIfFailed(device->CreateHeap(&HeapDesc, __uuidof<ID3D12Heap>(), (void**)&ResultHeap));
        this.Heap = ResultHeap;
        this.Capacity = sizeInBytes;
    }

    public ID3D12Resource* AllocateBuffer(ID3D12Device2* device, ResourceDescription* resourceDesc, uint sizeInBytes)
    {
        if (this.CurrentOffset + sizeInBytes > this.Capacity) { throw new Exception($"{nameof(DataHeap)} is size {this.Capacity}, and {this.CurrentOffset} bytes are used. There is insufficient space for an allocation of size {sizeInBytes}."); }
        ID3D12Resource* Result;
        ThrowIfFailed(device->CreatePlacedResource(this.Heap, this.CurrentOffset, resourceDesc, ResourceStates.UnorderedAccess, null, __uuidof<ID3D12Resource>(), (void**)&Result));
        this.CurrentOffset += sizeInBytes;
        return Result;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!IsDisposed)
        {
            COMUtils.COMRelease(ref this.BF_Heap);
            IsDisposed = true;
        }
    }

    ~DataHeap() => Dispose(disposing: false);

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
