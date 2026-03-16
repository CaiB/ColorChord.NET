using System;
using System.Diagnostics;
using System.Threading;
using Vortice.Win32;
using Vortice.Win32.Graphics.Direct3D12;
using Vortice.Win32.Graphics.Dxgi;
using static ColorChord.NET.Outputs.DisplayD3D12Support.COMUtils;
using static Vortice.Win32.Apis;

namespace ColorChord.NET.Outputs.DisplayD3D12Support;

public unsafe sealed class CommandList : IDisposable
{
    private readonly ID3D12Device2* Device;
    private readonly ID3D12CommandQueue* ParentQueue;
    private ID3D12CommandAllocator* CommandStorage;
    private ID3D12Fence* Fence;
    private readonly AutoResetEvent FenceEvent;
    private ulong FenceValue;
    private bool IsDisposed;

    public CommandListType Type { get; private init; }
    private ID3D12GraphicsCommandList1* BF_NativeList;
    public ID3D12GraphicsCommandList1* NativeList { get => this.BF_NativeList; private set => this.BF_NativeList = value; }

    public CommandList(CommandListType type, ID3D12Device2* device, ID3D12CommandQueue* parentQueue, string? name = null)
    {
        this.Type = type;
        this.Device = device;
        this.ParentQueue = parentQueue;

        ID3D12CommandAllocator* Allocator = default;
        ThrowIfFailed(device->CreateCommandAllocator(this.Type, __uuidof<ID3D12CommandAllocator>(), (void**)&Allocator));
        this.CommandStorage = Allocator;

        ID3D12GraphicsCommandList1* List = default;
        ThrowIfFailed(device->CreateCommandList(0, type, Allocator, null, __uuidof<ID3D12GraphicsCommandList2>(), (void**)&List));
        if (name != null) { List->SetName(name); }
        List->Close();
        this.NativeList = List;

        ID3D12Fence* Fence = default;
        ThrowIfFailed(device->CreateFence(FenceValue, FenceFlags.Shared, __uuidof<ID3D12Fence>(), (void**)&Fence));
        this.Fence = Fence;
        this.FenceEvent = new(false);
        //Reset();
    }

    internal static ID3D12CommandQueue* CreateParentQueue(CommandListType type, ID3D12Device2* device, string? name = null)
    {
        CommandQueueDescription QueueDesc = new()
        {
            Type = type,
            Priority = (int)CommandQueuePriority.Normal,
            Flags = CommandQueueFlags.None,
            NodeMask = 0
        };
        ID3D12CommandQueue* Result = default;
        ThrowIfFailed(device->CreateCommandQueue(&QueueDesc, __uuidof<ID3D12CommandQueue>(), (void**)&Result));
        if (name != null) { Result->SetName(name); }
        return Result;
    }

    public void Reset()
    {
        Debug.Assert(IsFenceComplete(FenceValue));
        ThrowIfFailed(this.CommandStorage->Reset());
        ThrowIfFailed(this.NativeList->Reset(this.CommandStorage, null));
    }

    public ulong InsertFenceSignal(ref ulong fenceValue)
    {
        ulong NewFenceValue = ++fenceValue;
        ThrowIfFailed(this.ParentQueue->Signal(this.Fence, NewFenceValue));
        return NewFenceValue;
    }

    public bool IsFenceComplete(ulong fenceValue) => this.Fence->GetCompletedValue() >= fenceValue;

    public void WaitForFenceValue(ulong fenceValue)
    {
        if (!IsFenceComplete(fenceValue))
        {
            this.Fence->SetEventOnCompletion(fenceValue, (Handle)this.FenceEvent.SafeWaitHandle.DangerousGetHandle());
            this.FenceEvent.WaitOne();
        }
    }

    public void Wait() => WaitForFenceValue(FenceValue);

    public void Flush()
    {
        ulong NewFenceValue = InsertFenceSignal(ref FenceValue);
        WaitForFenceValue(NewFenceValue);
    }

    internal ulong Present(IDXGISwapChain3* swapchain)
    {
        Debug.Assert(this.Type == CommandListType.Direct);
        ThrowIfFailed(this.NativeList->Close());
        ID3D12CommandList* CommandListGeneric = COMCast<ID3D12GraphicsCommandList1, ID3D12CommandList>(this.NativeList);
        this.ParentQueue->ExecuteCommandLists(1, &CommandListGeneric);
        ThrowIfFailed(swapchain->Present(1, PresentFlags.None));
        ulong NewFenceValue = InsertFenceSignal(ref FenceValue);
        COMRelease(&CommandListGeneric);
        return NewFenceValue;
    }

    public ulong Execute()
    {
        Debug.Assert(IsFenceComplete(FenceValue));
        ThrowIfFailed(this.NativeList->Close());
        ID3D12CommandList* CommandListGeneric = COMCast<ID3D12GraphicsCommandList1, ID3D12CommandList>(this.NativeList);
        this.ParentQueue->ExecuteCommandLists(1, &CommandListGeneric);
        ulong NewFenceValue = InsertFenceSignal(ref FenceValue);
        COMRelease(&CommandListGeneric);
        return NewFenceValue;
    }

    public void ExecuteAndWait() => WaitForFenceValue(Execute());

    private void Dispose(bool disposing)
    {
        if (!this.IsDisposed)
        {
            if (disposing) { this.FenceEvent.Dispose(); }
            COMRelease(ref this.Fence);
            COMRelease(ref this.CommandStorage);
            COMRelease(ref this.BF_NativeList);
            this.IsDisposed = true;
        }
    }

    ~CommandList() => Dispose(disposing: false);

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}