using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using ColorChord.NET.API.Config;
using ColorChord.NET.API.Controllers;
using ColorChord.NET.API.Outputs;
using ColorChord.NET.API.Utility;
using ColorChord.NET.Config;
using ColorChord.NET.Outputs.DisplayD3D12Support;
using Win32;
using Win32.Graphics.Direct3D;
using Win32.Graphics.Direct3D12;
using Win32.Graphics.Dxgi;
using Win32.Graphics.Dxgi.Common;
using static Win32.Apis;
using static Win32.Graphics.Direct3D12.Apis;
using static Win32.Graphics.Dxgi.Apis;

namespace ColorChord.NET.Outputs;

public unsafe class DisplayD3D12 : IOutput, IThreadedInstance
{
    private const int NUM_FRAMEBUFFERS = 2;
    private const bool D3D_DEBUG = true;
    private const bool USE_WARP = true;
    public string Name { get; private init; }

    /// <summary> The width of the window contents, in pixels. </summary>
    [Controllable("WindowWidth")]
    [ConfigInt("WindowWidth", 10, 4000, 1280)]
    public int WindowWidth { get => this.Window.Width; set => this.Window.Width = value; }
    /// <summary> The height of the window contents, in pixels. </summary>
    [Controllable("WindowHeight")]
    [ConfigInt("WindowHeight", 10, 4000, 720)]
    public int WindowHeight { get => this.Window.Height; set => this.Window.Height = value; }


    private readonly Window Window;
    private ComPtr<ID3D12Debug> DebugLayer = default;
    private ComPtr<ID3D12Device2> Device = default;
    private ComPtr<ID3D12CommandQueue> CommandQueue = default;
    private ComPtr<IDXGISwapChain4> SwapChain4 = default;
    private ComPtr<ID3D12DescriptorHeap> DescriptorHeapRTV = default;
    private uint RTVDescriptorSize;
    private readonly ComPtr<ID3D12Resource>[] BufferRTVs = new ComPtr<ID3D12Resource>[NUM_FRAMEBUFFERS];
    private readonly ComPtr<ID3D12CommandAllocator>[] CommandAllocators = new ComPtr<ID3D12CommandAllocator>[NUM_FRAMEBUFFERS];
    private ComPtr<ID3D12GraphicsCommandList> CommandList = default;
    private ComPtr<ID3D12Fence> FrameFence = default;
    private readonly AutoResetEvent FrameFenceEvent = new(false);
    private GCHandle FrameFenceEventHandle;
    private readonly ulong[] FrameFenceValues = new ulong[NUM_FRAMEBUFFERS];
    private ulong FrameFenceValue = 0;

    private uint CurrentBackBuffer = 0;
    public bool SizeChanged = false;

    public DisplayD3D12(string name, Dictionary<string, object> config)
    {
        this.Name = name;
        this.Window = new() { WindowTitle = this.Name };
        Configurer.Configure(this, config);
        this.Window.Create();
        this.Window.OnResize += OnResize;

        if (D3D_DEBUG)
        {
            ThrowIfFailed(D3D12GetDebugInterface(__uuidof<ID3D12Debug>(), (void**)this.DebugLayer.GetAddressOf()));
            using ComPtr<ID3D12Debug1> Debug1 = default;
            this.DebugLayer.As(&Debug1);
            Debug1.Get()->EnableDebugLayer();
            Debug1.Get()->SetEnableGPUBasedValidation(true);
        }

        using ComPtr<IDXGIFactory4> DXGIFactory4 = default;
        ThrowIfFailed(CreateDXGIFactory2(D3D_DEBUG, __uuidof<IDXGIFactory4>(), (void**)DXGIFactory4.GetAddressOf())); // TODO: debug off if not debug

        using ComPtr<IDXGIAdapter1> DXGIAdapter1 = default;
        bool SupportsD3D12 = false;
        if (USE_WARP)
        {
            ThrowIfFailed(DXGIFactory4.Get()->EnumWarpAdapter(__uuidof<IDXGIAdapter1>(), (void**)DXGIAdapter1.GetAddressOf()));
            SupportsD3D12 = true;
        }
        else
        {
            using ComPtr<IDXGIFactory6> DXGIFactory6 = default;
            if (DXGIFactory4.CopyTo(&DXGIFactory6).Success)
            {
                for (uint adapterIndex = 0; DXGIFactory6.Get()->EnumAdapterByGpuPreference(adapterIndex, GpuPreference.HighPerformance, __uuidof<IDXGIAdapter1>(), (void**)DXGIAdapter1.ReleaseAndGetAddressOf()).Success; adapterIndex++)
                {
                    AdapterDescription1 AdapterDesc = default;
                    ThrowIfFailed(DXGIAdapter1.Get()->GetDesc1(&AdapterDesc));

                    if (!AdapterDesc.Flags.HasFlag(AdapterFlags.Software)) { continue; }

                    // Check to see if the adapter supports Direct3D 12, but don't create the actual device yet.
                    if (D3D12CreateDevice((IUnknown*)DXGIAdapter1.Get(), FeatureLevel.Level_11_0, __uuidof<ID3D12Device>(), null).Success)
                    {
                        SupportsD3D12 = true;
                        break;
                    }

                    break;
                }
            }
            else { } // TODO: Find adapters using a basic method
        }

        if (SupportsD3D12)
        {
            // Create the DX12 API device object.
            ThrowIfFailed(D3D12CreateDevice((IUnknown*)DXGIAdapter1.Get(), FeatureLevel.Level_11_0, __uuidof<ID3D12Device2>(), (void**)Device.GetAddressOf()));
        }
        else { throw new Exception("No D3D12-compatible graphics adapters were found."); }

        using ComPtr<IDXGIInfoQueue> DXGIInfoQueue = default;
        if (DXGIGetDebugInterface1(0, __uuidof<IDXGIInfoQueue>(), (void**)DXGIInfoQueue.GetAddressOf()).Success)
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
        }

        using ComPtr<ID3D12InfoQueue> D3DInfoQueue = default;
        if (this.Device.CopyTo(&D3DInfoQueue).Success)
        {
            D3DInfoQueue.Get()->SetBreakOnSeverity(D3D12_MESSAGE_SEVERITY_CORRUPTION, true);
            D3DInfoQueue.Get()->SetBreakOnSeverity(D3D12_MESSAGE_SEVERITY_ERROR, true);
            D3DInfoQueue.Get()->SetBreakOnSeverity(D3D12_MESSAGE_SEVERITY_WARNING, true);
            // TODO: filter out some messages

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

        }
        //else { throw new Exception("no info queue"); }

        CommandQueueDescription QueueDesc = new()
        {
            Type = CommandListType.Direct,
            Priority = (int)CommandQueuePriority.Normal,
            Flags = CommandQueueFlags.None,
            NodeMask = 0
        };

        ThrowIfFailed(this.Device.Get()->CreateCommandQueue(&QueueDesc, __uuidof<ID3D12CommandQueue>(), (void**)this.CommandQueue.GetAddressOf()));

        // TODO: Maybe support tearing for VRR displays?

        SwapChainDescription1 SwapChainDesc = new()
        {
            Width = 0, //width,
            Height = 0, //height,
            Format = Format.R8G8B8A8Unorm, // TODO: if sRGB is wanted, need to set here
            Stereo = false,
            SampleDesc = SampleDescription.Default,
            BufferUsage = Usage.RenderTargetOutput,
            BufferCount = NUM_FRAMEBUFFERS,
            Scaling = Scaling.None,
            SwapEffect = SwapEffect.FlipDiscard,
            AlphaMode = AlphaMode.Unspecified,
            Flags = 0 // TODO: if tearing is implemented later, this needs to be changed
        };

        using ComPtr<IDXGISwapChain1> SwapChain1 = default;
        ThrowIfFailed(DXGIFactory4.Get()->CreateSwapChainForHwnd((IUnknown*)this.CommandQueue.Get(), this.Window.Handle, &SwapChainDesc, null, null, SwapChain1.GetAddressOf()));
        // Disable the Alt+Enter fullscreen toggle feature. Switching to fullscreen will be handled manually.
        ThrowIfFailed(DXGIFactory4.Get()->MakeWindowAssociation(this.Window.Handle, WindowAssociationFlags.NoAltEnter));
        fixed (ComPtr<IDXGISwapChain4>* Swap4 = &this.SwapChain4) { ThrowIfFailed(SwapChain1.As(Swap4)); }

        DescriptorHeapDescription desc = new() { NumDescriptors = NUM_FRAMEBUFFERS, Type = DescriptorHeapType.Rtv };
        CreateDescriptorHeap(ref this.Device, DescriptorHeapType.Rtv, NUM_FRAMEBUFFERS, out this.DescriptorHeapRTV);

        this.RTVDescriptorSize = this.Device.Get()->GetDescriptorHandleIncrementSize(DescriptorHeapType.Rtv);
        UpdateRenderTargetViews(ref this.Device, ref this.SwapChain4, ref this.DescriptorHeapRTV);

        for (uint i = 0; i < NUM_FRAMEBUFFERS; ++i)
        {
            CreateCommandAllocator(ref this.Device, CommandListType.Direct, out this.CommandAllocators[i]);
        }

        CreateCommandList(ref this.Device, ref this.CommandAllocators[this.CurrentBackBuffer], CommandListType.Direct, out this.CommandList);

        fixed (ComPtr<ID3D12Fence>* FixedFence = &this.FrameFence) { ThrowIfFailed(this.Device.Get()->CreateFence(0, FenceFlags.None, __uuidof<ID3D12Fence>(), (void**)this.FrameFence.GetAddressOf())); }

        this.FrameFenceEventHandle = GCHandle.Alloc(this.FrameFenceEvent);


        Console.WriteLine("We got here");
    }

    void CreateDescriptorHeap(ref ComPtr<ID3D12Device2> device, DescriptorHeapType type, uint numDescriptors, out ComPtr<ID3D12DescriptorHeap> result)
    {
        DescriptorHeapDescription desc = new() { NumDescriptors = numDescriptors, Type = type };
        result = default;
        fixed (ComPtr<ID3D12DescriptorHeap>* ResultFixed = &result)
        {
            HResult MyThing = device.Get()->CreateDescriptorHeap(&desc, __uuidof<ID3D12DescriptorHeap>(), (void**)result.GetAddressOf());
            ThrowIfFailed(MyThing);
        }
    }

    void UpdateRenderTargetViews(ref ComPtr<ID3D12Device2> device, ref ComPtr<IDXGISwapChain4> swapChain, ref ComPtr<ID3D12DescriptorHeap> descriptorHeap)
    {
        CpuDescriptorHandle RTVHandle = descriptorHeap.Get()->GetCPUDescriptorHandleForHeapStart();
        for (uint i = 0; i < NUM_FRAMEBUFFERS; ++i)
        {
            fixed (ComPtr<ID3D12Resource>* BufferRTV = &this.BufferRTVs[i]) { ThrowIfFailed(swapChain.Get()->GetBuffer(i, __uuidof<ID3D12Resource>(), (void**)this.BufferRTVs[i].GetAddressOf())); }
            device.Get()->CreateRenderTargetView(this.BufferRTVs[i].Get(), null, RTVHandle);
            RTVHandle.Offset(1, this.RTVDescriptorSize);
        }
    }

    void CreateCommandAllocator(ref ComPtr<ID3D12Device2> device, CommandListType type, out ComPtr<ID3D12CommandAllocator> result)
    {
        result = default;
        fixed (ComPtr<ID3D12CommandAllocator>* CommandAllocator = &result) { ThrowIfFailed(device.Get()->CreateCommandAllocator(type, __uuidof<ID3D12CommandAllocator>(), (void**)result.GetAddressOf())); }
    }

    void CreateCommandList(ref ComPtr<ID3D12Device2> device, ref ComPtr<ID3D12CommandAllocator> commandAllocator, CommandListType type, out ComPtr<ID3D12GraphicsCommandList> result)
    {
        result = default;
        ThrowIfFailed(device.Get()->CreateCommandList(0, type, commandAllocator.Get(), null, __uuidof<ID3D12GraphicsCommandList>(), (void**)result.GetAddressOf()));
        ThrowIfFailed(result.Get()->Close());
    }

    ulong AddSignalToQueue(ref ComPtr<ID3D12CommandQueue> commandQueue, ref ComPtr<ID3D12Fence> fence, ref ulong fenceValue)
    {
        ulong fenceValueForSignal = ++fenceValue;
        ThrowIfFailed(commandQueue.Get()->Signal(fence.Get(), fenceValueForSignal));
        return fenceValueForSignal;
    }

    void WaitForFenceValue(ref ComPtr<ID3D12Fence> fence, ulong fenceValue, AutoResetEvent fenceEvent)
    {
        while (fence.Get()->GetCompletedValue() < fenceValue)
        {
            ThrowIfFailed(fence.Get()->SetEventOnCompletion(fenceValue, (Win32.Handle)fenceEvent.SafeWaitHandle.DangerousGetHandle()));
            fenceEvent.WaitOne();
        }
    }

    void Flush(ref ComPtr<ID3D12CommandQueue> commandQueue, ref ComPtr<ID3D12Fence> fence, ref ulong fenceValue, AutoResetEvent fenceEvent)
    {
        ulong fenceValueForSignal = AddSignalToQueue(ref commandQueue, ref fence, ref fenceValue);
        WaitForFenceValue(ref fence, fenceValueForSignal, fenceEvent);
    }

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
        ref ComPtr<ID3D12CommandAllocator> CommandAllocator = ref this.CommandAllocators[this.CurrentBackBuffer];
        ref ComPtr<ID3D12Resource> BackBuffer = ref this.BufferRTVs[this.CurrentBackBuffer];
        CommandAllocator.Get()->Reset();
        this.CommandList.Get()->Reset(CommandAllocator.Get(), null);

        // Clear the render target.
        {
            ResourceBarrier Barrier = ResourceBarrier.InitTransition(BackBuffer.Get(), ResourceStates.Present, ResourceStates.RenderTarget);
            this.CommandList.Get()->ResourceBarrier(1, &Barrier);
            Span<float> ClearColour = [0x81 / 255F, 0x14 / 255F, 0x26 / 255F, 1.0F];
            CpuDescriptorHandle RTV = this.DescriptorHeapRTV.Get()->GetCPUDescriptorHandleForHeapStart();
            RTV.Offset((int)this.CurrentBackBuffer, this.RTVDescriptorSize);
            this.CommandList.Get()->ClearRenderTargetView(RTV, ClearColour.GetPointer(), 0, null);
        }

        // Present
        {
            ResourceBarrier Barrier = ResourceBarrier.InitTransition(BackBuffer.Get(), ResourceStates.RenderTarget, ResourceStates.Present);
            this.CommandList.Get()->ResourceBarrier(1, &Barrier);

            ThrowIfFailed(this.CommandList.Get()->Close());

            //Span<nint> CommandLists = stackalloc nint[1];
            ComPtr<ID3D12CommandList> CommandListGeneric;
            this.CommandList.As(&CommandListGeneric);

            ID3D12CommandList* CommandLists = CommandListGeneric.Get();

            //CommandLists[0] = (nint)CommandListGeneric.Get();
            this.CommandQueue.Get()->ExecuteCommandLists(1, &CommandLists);


            //UINT syncInterval = g_VSync ? 1 : 0;
            //UINT presentFlags = g_TearingSupported && !g_VSync ? DXGI_PRESENT_ALLOW_TEARING : 0;
            ThrowIfFailed(this.SwapChain4.Get()->Present(1, 0)); // TODO: maybe support VRR

            // TODO: Here https://www.3dgep.com/learning-directx-12-1/#present it's after the Present
            // But here https://github.com/jpvanoosten/LearningDirectX12/blob/v0.0.1/Tutorial1/src/main.cpp#L516 it's before the Present
            // Which one is correct???
            this.FrameFenceValues[this.CurrentBackBuffer] = AddSignalToQueue(ref this.CommandQueue, ref this.FrameFence, ref this.FrameFenceValue);
            
            this.CurrentBackBuffer = this.SwapChain4.Get()->GetCurrentBackBufferIndex();

            WaitForFenceValue(ref this.FrameFence, this.FrameFenceValues[this.CurrentBackBuffer], this.FrameFenceEvent);
            if (this.SizeChanged) { HandleResize(); }
            //Console.WriteLine("Frame rendered!!!!!!!!!!");
        }
        //Thread.Sleep(100);
    }

    public void OnResize(object? sender, EventArgs evt) { this.SizeChanged = true; }

    public void HandleResize()
    {
        this.SizeChanged = false;
        // TODO: Check if size actually changed first?
        Flush(ref this.CommandQueue, ref this.FrameFence, ref this.FrameFenceValue, this.FrameFenceEvent);
        int NewWidth = Math.Max(1, this.Window.Width);
        int NewHeight = Math.Max(1, this.Window.Height);

        for (int i = 0; i < NUM_FRAMEBUFFERS; ++i)
        {
            // Any references to the back buffers must be released before the swap chain can be resized.
            this.BufferRTVs[i].Reset(); // TODO: sohuld this be released instead? .Get()->Release();
            this.FrameFenceValues[i] = this.FrameFenceValues[this.CurrentBackBuffer];
        }

        SwapChainDescription swapChainDesc = default;
        ThrowIfFailed(this.SwapChain4.Get()->GetDesc(&swapChainDesc));
        ThrowIfFailed(this.SwapChain4.Get()->ResizeBuffers(NUM_FRAMEBUFFERS, (uint)NewWidth, (uint)NewHeight, swapChainDesc.BufferDesc.Format, swapChainDesc.Flags));

        this.CurrentBackBuffer = this.SwapChain4.Get()->GetCurrentBackBufferIndex();

        UpdateRenderTargetViews(ref this.Device, ref this.SwapChain4, ref this.DescriptorHeapRTV);
    }

    public void Dispatch()
    {
        
    }

    public void Start()
    {
        this.Stopwatch.Start();
        while (true)
        {
            Update();
            Render();
        }
    }

    public void Stop()
    {
        
    }

    public void InstThreadPostInit()
    {
        this.Window.RunMessageLoop();
    }
}
