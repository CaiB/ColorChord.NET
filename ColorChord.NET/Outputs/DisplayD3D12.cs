using ColorChord.NET.API;
using ColorChord.NET.API.Config;
using ColorChord.NET.API.Controllers;
using ColorChord.NET.API.Outputs;
using ColorChord.NET.API.Utility;
using ColorChord.NET.Config;
using ColorChord.NET.Outputs.DisplayD3D12Modes;
using ColorChord.NET.Outputs.DisplayD3D12Support;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using Vortice.Win32;
using Vortice.Win32.Graphics.Direct3D;
using Vortice.Win32.Graphics.Direct3D12;
using Vortice.Win32.Graphics.Dxgi;
using Vortice.Win32.Graphics.Dxgi.Common;
using Vortice.Win32.Numerics;
using static ColorChord.NET.Outputs.DisplayD3D12Support.COMUtils;
using static Vortice.Win32.Apis;
using static Vortice.Win32.Graphics.Direct3D12.Apis;
using static Vortice.Win32.Graphics.Dxgi.Apis;

namespace ColorChord.NET.Outputs;

public unsafe class DisplayD3D12 : IOutput, IThreadedInstance
{
    private const int NUM_FRAMEBUFFERS = 2;
    private const bool D3D_DEBUG = true;
    private const bool USE_WARP = false;
    public string Name { get; private init; }

    /// <summary> The width of the window contents, in pixels. </summary>
    [Controllable("WindowWidth")]
    [ConfigInt("WindowWidth", 10, 4000, 1280)]
    public int WindowWidth { get => this.Window.Width; set => this.Window.Width = value; }
    /// <summary> The height of the window contents, in pixels. </summary>
    [Controllable("WindowHeight")]
    [ConfigInt("WindowHeight", 10, 4000, 720)]
    public int WindowHeight { get => this.Window.Height; set => this.Window.Height = value; }

    public bool HasDepth = false;

    public struct NativeResources
    {
        public ID3D12Device2* Device;
        public IDXGISwapChain3* Swapchain;
        public Handle SwapchainWaitHandle;
        public ID3D12DescriptorHeap* DescriptorHeapRTV;
        public ID3D12Resource* BufferRTV0;
        public ID3D12Resource* BufferRTV1;
        public ID3D12Fence* FrameFence;
        public ID3D12CommandQueue* CommandQueue;
        public ID3D12CommandAllocator* CommandAllocator0;
        public ID3D12CommandAllocator* CommandAllocator1;
        public ID3D12GraphicsCommandList* CommandList;
        public Vector4 ClearColour;

        // These are only available if this.HasDepth = true
        public ID3D12DescriptorHeap* DescriptorHeapDSV;
        public ID3D12Resource* DepthBuffer;

        public NativeResources() { }
    }

    public NativeResources Native;

    private readonly Window Window;
    private readonly uint RTVDescriptorSize;
    private readonly CommandQueue CopyQueue;
    private readonly AutoResetEvent FrameFenceEvent = new(false);
    private readonly GCHandle FrameFenceEventHandle;
    private readonly ulong[] FrameFenceValues = new ulong[NUM_FRAMEBUFFERS];
    private ulong FrameFenceValue = 0;
    private readonly Rect ScissorRect = new(0, 0, int.MaxValue, int.MaxValue);
    private Viewport Viewport = new();
    private ClearValue OptimizedDepthClearValue;
    private DepthStencilViewDescription DSVDesc;

    private int PreviousWidth, PreviousHeight;
    private uint CurrentBackBuffer = 0;
    private bool SizeChanged = false;
    private bool KeepGoing = true;

    private TutorialModeNew Mode;

    public DisplayD3D12(string name, Dictionary<string, object> config)
    {
        this.Name = name;
        this.Window = new() { WindowTitle = this.Name };
        Configurer.Configure(this, config);
        this.Window.Create();
        this.Window.OnResize += OnResize;
        this.Window.OnClose += (sender, evt) => { this.KeepGoing = false; };
        this.Native = new();
        this.Native.ClearColour = new(0x81 / 255F, 0x14 / 255F, 0x26 / 255F, 1.0F);

        this.Mode = new(this, null, new());

        ID3D12Debug* DebugLayer = null;
        if (D3D_DEBUG)
        {
            ThrowIfFailed(D3D12GetDebugInterface(__uuidof<ID3D12Debug>(), (void**)&DebugLayer));
            DebugLayer->EnableDebugLayer();
            COMRelease(&DebugLayer);
        }

        IDXGIFactory2* DXGIFactory2 = null;
        ThrowIfFailed(CreateDXGIFactory2(D3D_DEBUG, __uuidof<IDXGIFactory2>(), (void**)&DXGIFactory2));
        IDXGIFactory6* DXGIFactory = COMCastAndReleaseOld<IDXGIFactory2, IDXGIFactory6>(&DXGIFactory2);

        IDXGIAdapter1* DXGIAdapter = null;
        bool SupportsD3D12 = false;
        if (USE_WARP)
        {
            ThrowIfFailed(DXGIFactory->EnumWarpAdapter(__uuidof<IDXGIAdapter1>(), (void**)&DXGIAdapter));
            SupportsD3D12 = true;
        }
        else
        {
            uint AdapterIndex = 0;
            do
            {
                HResult EnumResult = DXGIFactory->EnumAdapterByGpuPreference(AdapterIndex, GpuPreference.HighPerformance, __uuidof<IDXGIAdapter1>(), (void**)&DXGIAdapter);
                if (EnumResult == 0x887A0002) { break; } // No more adapters
                ThrowIfFailed(EnumResult);

                AdapterDescription1 AdapterDesc = default;
                ThrowIfFailed(DXGIAdapter->GetDesc1(&AdapterDesc));

                if (!AdapterDesc.Flags.HasFlag(AdapterFlags.Software))
                {
                    // Check to see if the adapter supports Direct3D 12, but don't create the actual device yet.
                    if (D3D12CreateDevice((IUnknown*)DXGIAdapter, FeatureLevel.Level_11_0, __uuidof<ID3D12Device>(), null).Success)
                    {
                        Log.Debug($"Found D3D12-capable GPU '" + new string(AdapterDesc.Description) + "'.");
                        SupportsD3D12 = true;
                        break;
                    }
                }

                AdapterIndex++;
                COMRelease(&DXGIAdapter);
            }
            while (true);
        }

        fixed (NativeResources* nthis = &this.Native)
        {
            if (SupportsD3D12)
            {
                ThrowIfFailed(D3D12CreateDevice((IUnknown*)DXGIAdapter, FeatureLevel.Level_11_0, __uuidof<ID3D12Device2>(), (void**)&(nthis->Device)));
                COMRelease(&DXGIAdapter);
            }
            else { throw new Exception("No D3D12-compatible graphics adapters were found."); }

            if (D3D_DEBUG)
            {
                IDXGIInfoQueue* DXGIInfoQueue = default;
                if (DXGIGetDebugInterface1(0, __uuidof<IDXGIInfoQueue>(), (void**)&DXGIInfoQueue).Success)
                {
                    //Span<int> HiddenMessages = stackalloc int[1]
                    //{
                    //    80 /* IDXGISwapChain::GetContainingOutput: The swapchain's adapter does not control the output on which the swapchain's window resides. */
                    //};
                    //
                    //Win32.Graphics.Dxgi.InfoQueueFilter filter = new()
                    //{
                    //    DenyList = new()
                    //    {
                    //        NumIDs = (uint)HiddenMessages.Length,
                    //        pIDList = HiddenMessages.GetPointer()
                    //    }
                    //};
                    //DXGIInfoQueue.Get()->AddStorageFilterEntries(DXGI_DEBUG_DXGI, &filter);
                    COMRelease(&DXGIInfoQueue);
                }

                if (TryCOMCast<ID3D12Device2, ID3D12InfoQueue>(nthis->Device, out ID3D12InfoQueue* D3DInfoQueue))
                {
                    D3DInfoQueue->SetBreakOnSeverity(D3D12_MESSAGE_SEVERITY_CORRUPTION, true);
                    D3DInfoQueue->SetBreakOnSeverity(D3D12_MESSAGE_SEVERITY_ERROR, true);
                    D3DInfoQueue->SetBreakOnSeverity(D3D12_MESSAGE_SEVERITY_WARNING, true);

                    //Span<Win32.Graphics.Direct3D12.MessageId> HiddenMessages = stackalloc Win32.Graphics.Direct3D12.MessageId[4]
                    //{
                    //    D3D12_MESSAGE_ID_MAP_INVALID_NULLRANGE,
                    //    D3D12_MESSAGE_ID_UNMAP_INVALID_NULLRANGE,
                    //    // Workarounds for debug layer issues on hybrid-graphics systems
                    //    D3D12_MESSAGE_ID_EXECUTECOMMANDLISTS_WRONGSWAPCHAINBUFFERREFERENCE,
                    //    D3D12_MESSAGE_ID_RESOURCE_BARRIER_MISMATCHING_COMMAND_LIST_TYPE,
                    //};
                    //Win32.Graphics.Direct3D12.InfoQueueFilter filter = new()
                    //{
                    //    DenyList = new()
                    //    {
                    //        NumIDs = (uint)HiddenMessages.Length,
                    //        pIDList = HiddenMessages.GetPointer()
                    //    }
                    //};
                    //D3DInfoQueue.Get()->AddStorageFilterEntries(&filter);
                    COMRelease(&D3DInfoQueue);
                }
            }

            CommandQueueDescription DirectQueueDesc = new()
            {
                Type = CommandListType.Direct,
                Priority = (int)CommandQueuePriority.Normal,
                Flags = CommandQueueFlags.None,
                NodeMask = 0
            };
            ThrowIfFailed(nthis->Device->CreateCommandQueue(&DirectQueueDesc, __uuidof<ID3D12CommandQueue>(), (void**)&nthis->CommandQueue));
            nthis->CommandAllocator0 = CreateCommandAllocator(nthis->Device, CommandListType.Direct);
            nthis->CommandAllocator1 = CreateCommandAllocator(nthis->Device, CommandListType.Direct);
            nthis->CommandList = CreateCommandList(nthis->Device, this.CurrentBackBuffer == 0 ? nthis->CommandAllocator0 : nthis->CommandAllocator1, CommandListType.Direct);

            SwapChainDescription1 SwapChainDesc = new()
            {
                Width = 0,
                Height = 0,
                Format = Format.R8G8B8A8Unorm, // TODO: if sRGB is wanted, need to set here
                Stereo = false,
                SampleDesc = SampleDescription.Default,
                BufferUsage = Usage.RenderTargetOutput,
                BufferCount = NUM_FRAMEBUFFERS,
                Scaling = Scaling.Stretch,
                SwapEffect = SwapEffect.FlipDiscard,
                AlphaMode = AlphaMode.Unspecified,
                Flags = SwapChainFlags.FrameLatencyWaitableObject
            };

            IDXGISwapChain1* Swapchain1 = null;
            ThrowIfFailed(DXGIFactory->CreateSwapChainForHwnd((IUnknown*)nthis->CommandQueue, this.Window.Handle, &SwapChainDesc, null, null, &Swapchain1));
            ThrowIfFailed(DXGIFactory->MakeWindowAssociation(this.Window.Handle, WindowAssociationFlags.NoAltEnter)); // Disable the Alt+Enter fullscreen toggle feature. Switching to fullscreen will be handled manually.
            nthis->Swapchain = COMCastAndReleaseOld<IDXGISwapChain1, IDXGISwapChain3>(&Swapchain1);
            ThrowIfFailed(nthis->Swapchain->SetMaximumFrameLatency(1));
            nthis->SwapchainWaitHandle = nthis->Swapchain->GetFrameLatencyWaitableObject();

            nthis->DescriptorHeapRTV = CreateDescriptorHeap(nthis->Device, DescriptorHeapType.Rtv, NUM_FRAMEBUFFERS);
            this.RTVDescriptorSize = nthis->Device->GetDescriptorHandleIncrementSize(DescriptorHeapType.Rtv);
            UpdateRenderTargetViews(nthis->Device, nthis->Swapchain, nthis->DescriptorHeapRTV, &nthis->BufferRTV0, &nthis->BufferRTV1);

            if (this.HasDepth)
            {
                this.OptimizedDepthClearValue = new()
                {
                    Format = Format.D32Float,
                    DepthStencil = new() { Depth = 1.0F, Stencil = 0 }
                };
                this.DSVDesc = new()
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

                nthis->DescriptorHeapDSV = CreateDescriptorHeap(nthis->Device, DescriptorHeapType.Dsv, 1);
            }

            ThrowIfFailed(nthis->Device->CreateFence(0, FenceFlags.None, __uuidof<ID3D12Fence>(), (void**)&nthis->FrameFence));
        }

        this.FrameFenceEventHandle = GCHandle.Alloc(this.FrameFenceEvent);
        this.CopyQueue = new(this.Native.Device, CommandListType.Copy);
        HandleResize();
        this.Mode.Load(this.Native.Device, this.CopyQueue);

        COMRelease(&DXGIFactory);
    }

    ID3D12DescriptorHeap* CreateDescriptorHeap(ID3D12Device2* device, DescriptorHeapType type, uint numDescriptors)
    {
        ID3D12DescriptorHeap* Result = null;
        DescriptorHeapDescription DescHeapDesc = new() { NumDescriptors = numDescriptors, Type = type, Flags = DescriptorHeapFlags.None };
        ThrowIfFailed(device->CreateDescriptorHeap(&DescHeapDesc, __uuidof<ID3D12DescriptorHeap>(), (void**)&Result));
        return Result;
    }

    void UpdateRenderTargetViews(ID3D12Device2* device, IDXGISwapChain3* swapChain, ID3D12DescriptorHeap* descriptorHeap, ID3D12Resource** bufferRTV0, ID3D12Resource** bufferRTV1)
    {
        COMRelease(bufferRTV0);
        CpuDescriptorHandle RTVHandle = descriptorHeap->GetCPUDescriptorHandleForHeapStart();
        ThrowIfFailed(swapChain->GetBuffer(0, __uuidof<ID3D12Resource>(), (void**)bufferRTV0));
        device->CreateRenderTargetView(*bufferRTV0, null, RTVHandle);

        RTVHandle.Offset(1, this.RTVDescriptorSize);
        COMRelease(bufferRTV1);
        ThrowIfFailed(swapChain->GetBuffer(1, __uuidof<ID3D12Resource>(), (void**)bufferRTV1));
        device->CreateRenderTargetView(*bufferRTV1, null, RTVHandle);
    }

    ID3D12CommandAllocator* CreateCommandAllocator(ID3D12Device2* device, CommandListType type)
    {
        ID3D12CommandAllocator* Result;
        ThrowIfFailed(device->CreateCommandAllocator(type, __uuidof<ID3D12CommandAllocator>(), (void**)&Result));
        return Result;
    }

    ID3D12GraphicsCommandList* CreateCommandList(ID3D12Device2* device, ID3D12CommandAllocator* commandAllocator, CommandListType type)
    {
        ID3D12GraphicsCommandList* Result;
        ThrowIfFailed(device->CreateCommandList(0, type, commandAllocator, null, __uuidof<ID3D12GraphicsCommandList>(), (void**)&Result));
        ThrowIfFailed(Result->Close());
        return Result;
    }

    ulong AddSignalToQueue(ID3D12CommandQueue* commandQueue, ID3D12Fence* fence, ref ulong fenceValue)
    {
        ulong FenceValueForSignal = ++fenceValue;
        ThrowIfFailed(commandQueue->Signal(fence, FenceValueForSignal));
        return FenceValueForSignal;
    }

    void WaitForFenceValue(ID3D12Fence* fence, ulong fenceValue, AutoResetEvent fenceEvent)
    {
        while (fence->GetCompletedValue() < fenceValue)
        {
            ThrowIfFailed(fence->SetEventOnCompletion(fenceValue, (Handle)fenceEvent.SafeWaitHandle.DangerousGetHandle())); // TODO: is a GCHandle on the event sufficient to ensure this doesn't explode?
            fenceEvent.WaitOne();
        }
    }

    public void Flush(ID3D12CommandQueue* commandQueue, ID3D12Fence* fence, ref ulong fenceValue, AutoResetEvent fenceEvent)
    {
        ulong fenceValueForSignal = AddSignalToQueue(commandQueue, fence, ref fenceValue);
        WaitForFenceValue(fence, fenceValueForSignal, fenceEvent);
    }

    public void Flush() => Flush(this.Native.CommandQueue, this.Native.FrameFence, ref this.FrameFenceValue, this.FrameFenceEvent);

    private ulong FrameCounterForFPS = 0;
    private readonly Stopwatch Stopwatch = new();
    private void Update()
    {
        this.FrameCounterForFPS++;
        TimeSpan TimeNow = this.Stopwatch.Elapsed;
        if (TimeNow >= TimeSpan.FromSeconds(1))
        {
            Debug.WriteLine($"FPS: {FrameCounterForFPS / TimeNow.TotalSeconds}");
            FrameCounterForFPS = 0;
            this.Stopwatch.Restart();
        }
    }

    private void Render()
    {
        if (this.SizeChanged) { HandleResize(); }

        uint BufferIndex = this.CurrentBackBuffer;
        ID3D12CommandAllocator* CommandAllocator = BufferIndex == 0 ? this.Native.CommandAllocator0 : this.Native.CommandAllocator1;
        ID3D12Resource* BackBuffer = BufferIndex == 0 ? this.Native.BufferRTV0 : this.Native.BufferRTV1;
        ID3D12GraphicsCommandList* CommandList = this.Native.CommandList;
        CommandAllocator->Reset();
        CommandList->Reset(CommandAllocator, null);

        CpuDescriptorHandle RTV = this.Native.DescriptorHeapRTV->GetCPUDescriptorHandleForHeapStart();
        RTV.Offset((int)BufferIndex, this.RTVDescriptorSize);
        CpuDescriptorHandle DSV;
        fixed (NativeResources* nthis = &this.Native)
        {
            // Clear the render target.
            {
                ResourceBarrier Barrier = ResourceBarrier.InitTransition(BackBuffer, ResourceStates.Present, ResourceStates.RenderTarget);
                CommandList->ResourceBarrier(1, &Barrier);
                CommandList->ClearRenderTargetView(RTV, (float*)&nthis->ClearColour, 0, null);
            }
            if (this.HasDepth)
            {
                DSV = this.Native.DescriptorHeapDSV->GetCPUDescriptorHandleForHeapStart();
                CommandList->ClearDepthStencilView(DSV, ClearFlags.Depth, 1F, 0, 0, null);
            }
            
            fixed (Viewport* ViewportPtr = &this.Viewport) { CommandList->RSSetViewports(1, ViewportPtr); }
            fixed (Rect* ScissorPtr = &this.ScissorRect) { CommandList->RSSetScissorRects(1, ScissorPtr); }
            CommandList->OMSetRenderTargets(1, &RTV, false, this.HasDepth ? &DSV : null);

            this.Mode.Render(CommandList);

            // Present
            {
                ResourceBarrier Barrier = ResourceBarrier.InitTransition(BackBuffer, ResourceStates.RenderTarget, ResourceStates.Present);
                CommandList->ResourceBarrier(1, &Barrier);
                ThrowIfFailed(CommandList->Close());

                ID3D12CommandList* CommandListGeneric = COMCast<ID3D12GraphicsCommandList, ID3D12CommandList>(CommandList);
                nthis->CommandQueue->ExecuteCommandLists(1, &CommandListGeneric);
                COMRelease(&CommandListGeneric);

                ThrowIfFailed(nthis->Swapchain->Present(1, 0));

                // TODO: Here https://www.3dgep.com/learning-directx-12-1/#present it's after the Present
                // But here https://github.com/jpvanoosten/LearningDirectX12/blob/v0.0.1/Tutorial1/src/main.cpp#L516 it's before the Present
                // Which one is correct???
                this.FrameFenceValues[BufferIndex] = AddSignalToQueue(nthis->CommandQueue, nthis->FrameFence, ref this.FrameFenceValue);
                this.CurrentBackBuffer = nthis->Swapchain->GetCurrentBackBufferIndex();

                WaitForFenceValue(nthis->FrameFence, this.FrameFenceValues[BufferIndex], this.FrameFenceEvent);
            }
        }
    }

    public void OnResize(object? sender, EventArgs evt) { this.SizeChanged = true; }

    public void HandleResize()
    {
        this.SizeChanged = false;
        int NewWidth = Math.Max(1, this.Window.Width);
        int NewHeight = Math.Max(1, this.Window.Height);
        if (NewHeight == this.PreviousHeight && NewWidth == this.PreviousWidth) { return; }

        Flush(this.Native.CommandQueue, this.Native.FrameFence, ref this.FrameFenceValue, this.FrameFenceEvent);
        this.Viewport = new(0F, 0F, NewWidth, NewHeight);

        // Any references to the back buffers must be released before the swap chain can be resized.
        COMRelease(ref this.Native.BufferRTV0);
        COMRelease(ref this.Native.BufferRTV1);
        this.FrameFenceValues[0] = this.FrameFenceValues[this.CurrentBackBuffer];
        this.FrameFenceValues[1] = this.FrameFenceValues[this.CurrentBackBuffer];

        SwapChainDescription1 SwapchainDesc = default;
        ThrowIfFailed(this.Native.Swapchain->GetDesc1(&SwapchainDesc));
        ThrowIfFailed(this.Native.Swapchain->ResizeBuffers(NUM_FRAMEBUFFERS, (uint)NewWidth, (uint)NewHeight, SwapchainDesc.Format, SwapchainDesc.Flags));
        this.CurrentBackBuffer = this.Native.Swapchain->GetCurrentBackBufferIndex();

        fixed (NativeResources* nthis = &this.Native)
        {
            UpdateRenderTargetViews(nthis->Device, nthis->Swapchain, nthis->DescriptorHeapRTV, &nthis->BufferRTV0, &nthis->BufferRTV1);
        }

        if (this.HasDepth)
        {
            ResourceDescription DepthResource = ResourceDescription.Tex2D(Format.D32Float, (ulong)NewWidth, (uint)NewHeight, 1, 0, 1, 0, ResourceFlags.AllowDepthStencil);
            HeapProperties HeapProps = new(HeapType.Default);
            
            COMRelease(ref this.Native.DepthBuffer);
            ID3D12Resource* NewDepthBuffer;
            fixed (ClearValue* DepthClearPtr = &this.OptimizedDepthClearValue) { ThrowIfFailed(this.Native.Device->CreateCommittedResource(&HeapProps, HeapFlags.None, &DepthResource, ResourceStates.DepthWrite, DepthClearPtr, __uuidof<ID3D12Resource>(), (void**)&NewDepthBuffer)); }
            fixed (DepthStencilViewDescription* DSVDescPtr = &this.DSVDesc) { this.Native.Device->CreateDepthStencilView(NewDepthBuffer, DSVDescPtr, this.Native.DescriptorHeapDSV->GetCPUDescriptorHandleForHeapStart()); }
            this.Native.DepthBuffer = NewDepthBuffer;
        }
        this.Mode.Resize(NewWidth, NewHeight);
        this.PreviousHeight = NewHeight;
        this.PreviousWidth = NewWidth;
    }

    public void Dispatch()
    {
        
    }

    public void Start()
    {
        this.Stopwatch.Start();
        while (this.KeepGoing)
        {
            Win32API.WaitForSingleObjectEx(this.Native.SwapchainWaitHandle, 1000, true);
            Update();
            Render();
        }
        ColorChord.Stop();
    }

    public void Stop()
    {
        if (D3D_DEBUG)
        {
            Debug.WriteLine("Live object report:");
            IDXGIDebug1* DXGIDebug;
            if (DXGIGetDebugInterface1(0, __uuidof<IDXGIDebug1>(), (void**)&DXGIDebug).Success)
            {
                DXGIDebug->ReportLiveObjects(DXGI_DEBUG_DX, ReportLiveObjectFlags.All);
                COMRelease(&DXGIDebug);
            }
        }
    }

    public void InstThreadPostInit()
    {
        this.Window.RunMessageLoop();
    }

    internal static void UpdateBufferResource<T>(ID3D12Device2* device, ID3D12GraphicsCommandList2* commandList, ReadOnlySpan<T> data, ResourceFlags flags, out ID3D12Resource* intermediateResource, out ID3D12Resource* destinationResource) where T : unmanaged
    {
        ulong BufferSize = (ulong)(sizeof(T) * data.Length);
        HeapProperties HeapPropertiesDest = new() { Type = HeapType.Default };
        ResourceDescription ResourceDesc = ResourceDescription.Buffer(BufferSize, flags);
        destinationResource = default;
        fixed (ID3D12Resource** DestPtr = &destinationResource) { ThrowIfFailed(device->CreateCommittedResource(&HeapPropertiesDest, HeapFlags.None, &ResourceDesc, ResourceStates.Common, null, __uuidof<ID3D12Resource>(), (void**)DestPtr)); }

        intermediateResource = default;
        if (data.Length > 0)
        {
            HeapProperties HeapPropertiesInt = new() { Type = HeapType.Upload };
            ResourceDescription ResourceDescInt = ResourceDescription.Buffer(BufferSize, flags);
            fixed (ID3D12Resource** IntermediatePtr = &intermediateResource) { ThrowIfFailed(device->CreateCommittedResource(&HeapPropertiesInt, HeapFlags.None, &ResourceDescInt, ResourceStates.GenericRead, null, __uuidof<ID3D12Resource>(), (void**)IntermediatePtr)); }
            SubresourceData Subresource = new()
            {
                pData = data.GetPointer(), // TODO: ??
                RowPitch = (nint)BufferSize,
                SlicePitch = (nint)BufferSize
            };
            UpdateSubresources((ID3D12GraphicsCommandList*)commandList, destinationResource, intermediateResource, 0, 0, 1, &Subresource);
        }
    }
}
