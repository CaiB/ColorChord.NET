using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Win32;
using Win32.Graphics.Direct3D12;
using static Win32.Apis;

namespace ColorChord.NET.Outputs.DisplayD3D12Support;

public sealed unsafe class CommandQueue : IDisposable
{
    public ComPtr<ID3D12CommandQueue> CommandQueueObj { get => this.BF_CommandQueueObj; private set => this.BF_CommandQueueObj = value; }
    public CommandListType CommandListType { get; private init; }

    private ComPtr<ID3D12CommandQueue> BF_CommandQueueObj;
    private readonly ComPtr<ID3D12Device2> Device;
    private readonly ComPtr<ID3D12Fence> Fence;
    private readonly AutoResetEvent FenceEvent; // TODO: Do I need a GCHandle on this? I don't think so?
    private ulong FenceValue = 0;

    private readonly Queue<CommandAllocatorEntry> CommandAllocatorQueue;
    private readonly Queue<ComPtr<ID3D12GraphicsCommandList2>> CommandListQueue;

    private bool IsDisposed = false;

    private struct CommandAllocatorEntry
    {
        public ulong FenceValue;
        public ComPtr<ID3D12CommandAllocator> CommandAllocator;
    };

    public CommandQueue(ref ComPtr<ID3D12Device2> device, CommandListType type)
    {
        this.FenceValue = 0;
        this.CommandListType = type;
        this.Device = device;
        this.CommandAllocatorQueue = new();
        this.CommandListQueue = new();

        CommandQueueDescription QueueDesc = new()
        {
            Type = type,
            Priority = (int)CommandQueuePriority.Normal,
            Flags = CommandQueueFlags.None,
            NodeMask = 0
        };

        fixed (ComPtr<ID3D12CommandQueue>* CommandQueuePtr = &this.BF_CommandQueueObj)
        {
            ThrowIfFailed(device.Get()->CreateCommandQueue(&QueueDesc, __uuidof<ID3D12CommandQueue>(), (void**)(*CommandQueuePtr).GetAddressOf()));
        }
        fixed (ComPtr<ID3D12Fence>* FencePtr = &this.Fence)
        {
            ThrowIfFailed(device.Get()->CreateFence(this.FenceValue, FenceFlags.None, __uuidof<ID3D12Fence>(), (void**)(*FencePtr).GetAddressOf()));
        }

        this.FenceEvent = new(false);
    }

    /// <summary>Get an available command list from the command queue.</summary>
    public ComPtr<ID3D12GraphicsCommandList2> GetCommandList()
    {
        ComPtr<ID3D12CommandAllocator> CommandAllocator;
        ComPtr<ID3D12GraphicsCommandList2> CommandList;

        if (this.CommandAllocatorQueue.Count != 0 && IsFenceComplete(this.CommandAllocatorQueue.Peek().FenceValue))
        {
            CommandAllocator = this.CommandAllocatorQueue.Dequeue().CommandAllocator;
            ThrowIfFailed(CommandAllocator.Get()->Reset());
        }
        else { CommandAllocator = CreateCommandAllocator(); }

        if (this.CommandListQueue.Count != 0)
        {
            CommandList = this.CommandListQueue.Dequeue();
            ThrowIfFailed(CommandList.Get()->Reset(CommandAllocator.Get(), null));
        }
        else { CommandList = CreateCommandList(ref CommandAllocator); }

        ThrowIfFailed(CommandList.Get()->SetPrivateDataInterface(__uuidof<ID3D12CommandAllocator>(), (IUnknown*)CommandAllocator.Get()));
        return CommandList;
    }

    /// <returns>Returns the fence value to wait for for this command list.</returns>
    public ulong ExecuteCommandList(ref ComPtr<ID3D12GraphicsCommandList2> commandList)
    {
        commandList.Get()->Close();

        ID3D12CommandAllocator* CommandAllocator;
        uint DataSize = (uint)IntPtr.Size;
        ThrowIfFailed(commandList.Get()->GetPrivateData(__uuidof<ID3D12CommandAllocator>(), &DataSize, &CommandAllocator));

        ComPtr<ID3D12CommandList> CommandListGeneric;
        commandList.As(&CommandListGeneric);
        ID3D12CommandList* CommandLists = CommandListGeneric.Get();

        this.CommandQueueObj.Get()->ExecuteCommandLists(1, &CommandLists);
        ulong FenceValue = Signal();

        this.CommandAllocatorQueue.Enqueue(new(){ FenceValue = FenceValue, CommandAllocator = CommandAllocator });
        this.CommandListQueue.Enqueue(commandList);

        // The ownership of the command allocator has been transferred to the ComPtr in the command allocator queue. It is safe to release the reference in this temporary COM pointer here.
        // TODO: figure out if this is also true here in C#
        //CommandAllocator->Release();

        return FenceValue;
    }

    public ulong Signal()
    {
        ulong FenceValue = ++this.FenceValue;
        this.CommandQueueObj.Get()->Signal(this.Fence.Get(), FenceValue);
        return FenceValue;
    }

    public bool IsFenceComplete(ulong fenceValue) => this.Fence.Get()->GetCompletedValue() >= fenceValue;

    public void WaitForFenceValue(ulong fenceValue)
    {
        if (!IsFenceComplete(fenceValue))
        {
            this.Fence.Get()->SetEventOnCompletion(fenceValue, (Win32.Handle)this.FenceEvent.SafeWaitHandle.DangerousGetHandle());
            this.FenceEvent.WaitOne();
        }
    }

    public void Flush() => WaitForFenceValue(Signal());

    private ComPtr<ID3D12CommandAllocator> CreateCommandAllocator()
    {
        ComPtr<ID3D12CommandAllocator> Result = default;
        ThrowIfFailed(this.Device.Get()->CreateCommandAllocator(this.CommandListType, __uuidof<ID3D12CommandAllocator>(), (void**)Result.GetAddressOf()));
        return Result;
    }

    private ComPtr<ID3D12GraphicsCommandList2> CreateCommandList(ref ComPtr<ID3D12CommandAllocator> allocator)
    {
        ComPtr<ID3D12GraphicsCommandList2> Result = default;
        ThrowIfFailed(this.Device.Get()->CreateCommandList(0, this.CommandListType, allocator.Get(), null, __uuidof<ID3D12GraphicsCommandList2>(), (void**)Result.GetAddressOf()));
        return Result;
    }


    private void Dispose(bool disposing)
    {
        if (!IsDisposed)
        {
            this.Fence.Get()->Release();
            IsDisposed = true;
        }
    }

    ~CommandQueue() => Dispose(disposing: false);

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
