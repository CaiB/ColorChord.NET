using System;
using Vortice.Win32.Graphics.Direct3D12;
using static Vortice.Win32.Apis;

namespace ColorChord.NET.Outputs.DisplayD3D12Support;

public unsafe class DescriptorHeap : IDisposable
{
    private const uint HEAP_CAPACITY = 32;

    public DescriptorHeapType Type { get; private init; }
    private ID3D12DescriptorHeap* BF_Heap;
    public ID3D12DescriptorHeap* Heap { get => this.BF_Heap; }
    public GpuDescriptorHandle GPUHandle { get; private set; }

    private readonly uint HandleSize;
    private CpuDescriptorHandle CPUHandle;
    private uint RemainingHandleCount;
    private bool IsDisposed;

    public DescriptorHeap(ID3D12Device2* device, DescriptorHeapType type) // TODO: Look into making thread safe
    {
        this.Type = type;
        DescriptorHeapDescription Desc = new()
        {
            Type = type,
            NumDescriptors = HEAP_CAPACITY,
            Flags = DescriptorHeapFlags.ShaderVisible,
            NodeMask = 0
        };
        ID3D12DescriptorHeap* ResultHeap;
        ThrowIfFailed(device->CreateDescriptorHeap(&Desc, __uuidof<ID3D12DescriptorHeap>(), (void**)&ResultHeap));
        this.BF_Heap = ResultHeap;
        this.RemainingHandleCount = HEAP_CAPACITY;
        this.CPUHandle = ResultHeap->GetCPUDescriptorHandleForHeapStart();
        this.GPUHandle = ResultHeap->GetGPUDescriptorHandleForHeapStart();
        this.HandleSize = device->GetDescriptorHandleIncrementSize(type);
    }

    public (CpuDescriptorHandle, GpuDescriptorHandle) AllocateObject()
    {
        if (this.RemainingHandleCount <= 0) { throw new Exception($"{nameof(DescriptorHeap)} is full"); }
        CpuDescriptorHandle ResultCPU = this.CPUHandle;
        GpuDescriptorHandle ResultGPU = this.GPUHandle;
        this.CPUHandle.Offset(1, this.HandleSize);
        this.GPUHandle.Offset(1, this.HandleSize);
        this.RemainingHandleCount--;
        return (ResultCPU, ResultGPU);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!IsDisposed)
        {
            COMUtils.COMRelease(ref this.BF_Heap);
            IsDisposed = true;
        }
    }

    ~DescriptorHeap() => Dispose(disposing: false);

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
