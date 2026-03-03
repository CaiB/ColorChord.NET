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
    public ID3D12CommandQueue* CommandQueueObj { get => this.BF_CommandQueueObj; private set => this.BF_CommandQueueObj = value; }
    public CommandListType CommandListType { get; private init; }

    private ID3D12CommandQueue* BF_CommandQueueObj;
    private readonly ID3D12Device2* Device;
    private readonly ID3D12Fence* Fence;
    private readonly AutoResetEvent FenceEvent; // TODO: Do I need a GCHandle on this? I don't think so?
    private ulong FenceValue = 0;

    private readonly Queue<CommandAllocatorEntry> CommandAllocatorQueue;
    private readonly Queue<IntPtr> CommandListQueue;

    private bool IsDisposed = false;

    private struct CommandAllocatorEntry
    {
        public ulong FenceValue;
        public ID3D12CommandAllocator* CommandAllocator;
    };

    public CommandQueue(ID3D12Device2* device, CommandListType type)
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

        fixed (ID3D12CommandQueue** CommandQueuePtr = &this.BF_CommandQueueObj)
        {
            ThrowIfFailed(device->CreateCommandQueue(&QueueDesc, __uuidof<ID3D12CommandQueue>(), (void**)CommandQueuePtr));
        }
        fixed (ID3D12Fence** FencePtr = &this.Fence)
        {
            ThrowIfFailed(device->CreateFence(this.FenceValue, FenceFlags.None, __uuidof<ID3D12Fence>(), (void**)FencePtr));
        }

        this.FenceEvent = new(false);
    }

    /// <summary>Get an available command list from the command queue.</summary>
    public ID3D12GraphicsCommandList2* GetCommandList()
    {
        ID3D12CommandAllocator* CommandAllocator;
        ID3D12GraphicsCommandList2* CommandList;

        if (this.CommandAllocatorQueue.Count != 0 && IsFenceComplete(this.CommandAllocatorQueue.Peek().FenceValue))
        {
            CommandAllocator = this.CommandAllocatorQueue.Dequeue().CommandAllocator;
            ThrowIfFailed(CommandAllocator->Reset());
        }
        else { CommandAllocator = CreateCommandAllocator(); }

        if (this.CommandListQueue.Count != 0)
        {
            CommandList = (ID3D12GraphicsCommandList2*)this.CommandListQueue.Dequeue();
            ThrowIfFailed(CommandList->Reset(CommandAllocator, null));
        }
        else { CommandList = CreateCommandList(CommandAllocator); }

        ThrowIfFailed(CommandList->SetPrivateDataInterface(__uuidof<ID3D12CommandAllocator>(), (IUnknown*)CommandAllocator));
        return CommandList;
    }

    /// <returns>Returns the fence value to wait for for this command list.</returns>
    public ulong ExecuteCommandList(ID3D12GraphicsCommandList2* commandList)
    {
        commandList->Close();

        ID3D12CommandAllocator* CommandAllocator;
        uint DataSize = (uint)IntPtr.Size;
        ThrowIfFailed(commandList->GetPrivateData(__uuidof<ID3D12CommandAllocator>(), &DataSize, &CommandAllocator));

        ID3D12CommandList* CommandListGeneric;
        commandList->QueryInterface(__uuidof<ID3D12CommandList>(), (void**)&CommandListGeneric);

        this.CommandQueueObj->ExecuteCommandLists(1, &CommandListGeneric);
        ulong FenceValue = Signal();

        this.CommandAllocatorQueue.Enqueue(new(){ FenceValue = FenceValue, CommandAllocator = CommandAllocator });
        this.CommandListQueue.Enqueue((IntPtr)commandList);

        // The ownership of the command allocator has been transferred to the ComPtr in the command allocator queue. It is safe to release the reference in this temporary COM pointer here.
        // TODO: figure out if this is also true here in C#
        //CommandAllocator->Release();

        return FenceValue;
    }

    public ulong Signal()
    {
        ulong FenceValue = ++this.FenceValue;
        this.CommandQueueObj->Signal(this.Fence, FenceValue);
        return FenceValue;
    }

    public bool IsFenceComplete(ulong fenceValue) => this.Fence->GetCompletedValue() >= fenceValue;

    public void WaitForFenceValue(ulong fenceValue)
    {
        if (!IsFenceComplete(fenceValue))
        {
            this.Fence->SetEventOnCompletion(fenceValue, (Win32.Handle)this.FenceEvent.SafeWaitHandle.DangerousGetHandle());
            this.FenceEvent.WaitOne();
        }
    }

    public void Flush() => WaitForFenceValue(Signal());

    private ID3D12CommandAllocator* CreateCommandAllocator()
    {
        ID3D12CommandAllocator* Result = default;
        ThrowIfFailed(this.Device->CreateCommandAllocator(this.CommandListType, __uuidof<ID3D12CommandAllocator>(), (void**)&Result));
        return Result;
    }

    private ID3D12GraphicsCommandList2* CreateCommandList(ID3D12CommandAllocator* allocator)
    {
        ID3D12GraphicsCommandList2* Result = default;
        ThrowIfFailed(this.Device->CreateCommandList(0, this.CommandListType, allocator, null, __uuidof<ID3D12GraphicsCommandList2>(), (void**)&Result));
        return Result;
    }


    private void Dispose(bool disposing)
    {
        if (!IsDisposed)
        {
            if (this.Fence != null) { this.Fence->Release(); }
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
