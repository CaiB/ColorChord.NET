using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using ColorChord.NET.API;
using ColorChord.NET.API.Config;
using ColorChord.NET.API.Controllers;
using ColorChord.NET.API.NoteFinder;
using ColorChord.NET.API.Outputs;
using ColorChord.NET.API.Utility;
using ColorChord.NET.API.Visualizers;
using ColorChord.NET.Config;
using ColorChord.NET.Extensions;
using ColorChord.NET.NoteFinder;
using ColorChord.NET.Outputs.DisplayD3D12Support;
using ColorChord.NET.Sources;
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

    [ConfigBool("ExperimentalLatencyReduction", false)]
    private bool DoLatencyReduction = false;

    public bool HasDepth = false;

    public struct NativeResources
    {
        public ID3D12Device2* Device;
        public IDXGISwapChain3* Swapchain;
        public Handle SwapchainWaitHandle;
        public ID3D12DescriptorHeap* DescriptorHeapRTV;
        public ID3D12Resource* BufferRTV0;
        public ID3D12Resource* BufferRTV1;
        public ID3D12CommandQueue* CommandQueueDirect;
        public ID3D12CommandQueue* CommandQueueCopy;
        public Vector4 ClearColour;

        // These are only available if this.HasDepth = true
        public ID3D12DescriptorHeap* DescriptorHeapDSV;
        public ID3D12Resource* DepthBuffer;

        public NativeResources() { }
    }

    public NativeResources Native;
    public DescriptorHeap BufferDescriptorHeap { get; private init; }
    public readonly object Interlock = new();

    private readonly Window Window;
    private readonly uint RTVDescriptorSize;
    private readonly CommandList CommandListDirect0, CommandListDirect1, CommandListCopy;
    private CommandList CurrentCommandListDirect { get => this.CurrentBackBuffer == 0 ? this.CommandListDirect0 : this.CommandListDirect1; }
    private readonly Rect ScissorRect = new(0, 0, int.MaxValue, int.MaxValue);
    private Viewport Viewport = new();
    private ClearValue OptimizedDepthClearValue;
    private DepthStencilViewDescription DSVDesc;

    private int PreviousWidth, PreviousHeight;
    private uint CurrentBackBuffer = 0;
    private bool SizeChanged = false;
    private bool KeepGoing = true;

    public NoteFinderCommon NoteFinder { get; private init; }
    public IVisualizer Source { get; private init; }
    private readonly Thread? WindowThread;
    private readonly ID3D12DisplayMode? Mode;

    private ulong TimerFrequency;
    private readonly WASAPILoopback? SourceForRenderDelay;
    private bool WaitingOnDispatch = false;
    private readonly AutoResetEvent DispatchRenderGate;

    public DisplayD3D12(string name, Dictionary<string, object> config)
    {
        this.Name = name;
        this.Window = new() { WindowTitle = "ColorChord.NET (D3D12): " + this.Name };

        IVisualizer? Visualizer = Configurer.FindVisualizer(config) ?? throw new InvalidOperationException($"{GetType().Name} cannot find visualizer to attach to");
        this.Source = Visualizer;
        Configurer.Configure(this, config);
        this.NoteFinder = Configurer.FindNoteFinder(config) ?? this.Source.NoteFinder ?? throw new Exception($"{nameof(DisplayD3D12)} {this.Name} could not find NoteFinder to get data from.");
        if (this.DoLatencyReduction)
        {
            if (this.NoteFinder is not Gen2NoteFinder G2NF || G2NF.AudioSource is not WASAPILoopback WASAPI)
            {
                Log.Error($"{nameof(DisplayD3D12)} had 'ExperimentalLatencyReduction' on, but this is only supported when using a {nameof(Gen2NoteFinder)} and {nameof(WASAPILoopback)}.");
                this.DoLatencyReduction = false;
            }
            else { this.SourceForRenderDelay = WASAPI; }
            Win32API.QueryPerformanceFrequency(out this.TimerFrequency);
        }
        this.DispatchRenderGate = new(false);

        AutoResetEvent WindowCreatedEvent = new(false);
        this.WindowThread = new(() =>
        {
            this.Window.Create();
            WindowCreatedEvent.Set();
            this.Window.OnResize += OnResize;
            this.Window.OnClose += (sender, evt) =>
            {
                this.KeepGoing = false;
                ColorChord.Stop();
            };
            this.Window.RunMessageLoop();
        })
        { Name = $"{nameof(DisplayD3D12)} {this.Name} Native Window" };
        this.WindowThread.SetApartmentState(ApartmentState.STA);
        this.WindowThread.Start();

        this.Native = new();
        this.Native.ClearColour = new(0x81 / 255F, 0x14 / 255F, 0x26 / 255F, 1.0F);

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

            nthis->CommandQueueDirect = CommandList.CreateParentQueue(CommandListType.Direct, nthis->Device, "Main Direct Queue");
            nthis->CommandQueueCopy = CommandList.CreateParentQueue(CommandListType.Copy, nthis->Device, "Main Copy Queue");
            this.CommandListDirect0 = new(CommandListType.Direct, nthis->Device, nthis->CommandQueueDirect, "Main Direct List 0");
            this.CommandListDirect1 = new(CommandListType.Direct, nthis->Device, nthis->CommandQueueDirect, "Main Direct List 1");
            this.CommandListCopy = new(CommandListType.Copy, nthis->Device, nthis->CommandQueueCopy, "Main Copy List");

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
                Flags = SwapChainFlags.FrameLatencyWaitableObject | SwapChainFlags.AllowTearing
            };

            WindowCreatedEvent.WaitOne();
            IDXGISwapChain1* Swapchain1 = null;
            ThrowIfFailed(DXGIFactory->CreateSwapChainForHwnd((IUnknown*)nthis->CommandQueueDirect, this.Window.Handle, &SwapChainDesc, null, null, &Swapchain1));
            ThrowIfFailed(DXGIFactory->MakeWindowAssociation(this.Window.Handle, WindowAssociationFlags.NoAltEnter)); // Disable the Alt+Enter fullscreen toggle feature. Switching to fullscreen will be handled manually.
            nthis->Swapchain = COMCastAndReleaseOld<IDXGISwapChain1, IDXGISwapChain3>(&Swapchain1);
            ThrowIfFailed(nthis->Swapchain->SetMaximumFrameLatency(1));
            nthis->SwapchainWaitHandle = nthis->Swapchain->GetFrameLatencyWaitableObject();

            nthis->DescriptorHeapRTV = CreateDescriptorHeap(nthis->Device, DescriptorHeapType.Rtv, NUM_FRAMEBUFFERS);
            this.RTVDescriptorSize = nthis->Device->GetDescriptorHandleIncrementSize(DescriptorHeapType.Rtv);
            UpdateRenderTargetViews(nthis->Device, nthis->Swapchain, nthis->DescriptorHeapRTV, &nthis->BufferRTV0, &nthis->BufferRTV1);

            this.BufferDescriptorHeap = new(nthis->Device, DescriptorHeapType.CbvSrvUav);
        }

        if (config.TryGetValue("Modes", out object? ModesObj)) // Make sure that everything else is configured before creating the modes!
        {
            List<object> ModesList = ModesObj as List<object> ?? throw new Exception("\"Modes\" must be an array of objects");
            for (int i = 0; i < 1/*ModeList.Length*/; i++) // TODO: Add support for multiple modes.
            {
                Dictionary<string, object> ThisMode = ModesList[i] as Dictionary<string, object> ?? throw new Exception($"Mode number {i + 1} was not a valid object");
                if (!ThisMode.TryGetValue(ConfigNames.TYPE, out object? TypeObj) || TypeObj is not string TypeName) { Log.Error($"Mode number {i + 1} is missing a valid \"{ConfigNames.TYPE}\" specification."); continue; }
                this.Mode = CreateMode(TypeName.StartsWith('#') ? TypeName : "ColorChord.NET.Outputs.DisplayD3D12Modes." + TypeName, ThisMode);
                if (this.Mode == null) { Log.Error($"Failed to create display of type \"{TypeName}\" under \"{this.Name}\"."); }
            }
            if (ModesList.Count > 1) { Log.Warn("Config specifies multiple modes. This is not yet supported, so only the first one will be used."); }
            Log.Info($"Finished reading display modes under \"{this.Name}\".");
        }

        fixed (NativeResources* nthis = &this.Native)
        {
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
        }

        HandleResize();

        this.CurrentBackBuffer = this.Native.Swapchain->GetCurrentBackBufferIndex();
        this.CommandListCopy.Reset();
        this.CurrentCommandListDirect.Reset();
        this.Mode?.Load(this.Native.Device, this.CommandListCopy, this.CurrentCommandListDirect);
        this.CurrentCommandListDirect.ExecuteAndWait();

        this.Source.AttachOutput(this);
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

    private ID3D12DisplayMode? CreateMode(string fullName, Dictionary<string, object> config)
    {
        Type? ObjType;
        if (fullName.StartsWith('#'))
        {
            fullName = fullName.Substring(1);
            ObjType = ExtensionHandler.FindType(fullName);
        }
        else { ObjType = Type.GetType(fullName); }
        if (ObjType == null) { Log.Error($"Cannot find display mode type {fullName}!"); return null; }
        if (!typeof(ID3D12DisplayMode).IsAssignableFrom(ObjType)) { Log.Error($"Requested display mode {fullName} is not a valid display mode (must be ID3D12DisplayMode)."); return null; }

        bool IsConfigurable = typeof(IConfigurableAttr).IsAssignableFrom(ObjType);

        object? Instance = null;
        try
        {
            if (IsConfigurable) { Instance = Activator.CreateInstance(ObjType, this, this.Source, config); }
            else { Instance = Activator.CreateInstance(ObjType, this, this.Source); }
        }
        catch (MissingMethodException exc)
        {
            Log.Error("Could not create an instance of \"" + fullName + "\".");
            Console.WriteLine(exc);
        }

        return Instance == null ? null : (ID3D12DisplayMode)Instance;
    }

    public void PauseUntilRenderFinished() => this.CurrentCommandListDirect.Wait();
    public void PauseUntilCopyFinished() => this.CommandListCopy.Wait();

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
        CommandList CommandListD = this.CurrentCommandListDirect;
        ID3D12Resource* BackBuffer = BufferIndex == 0 ? this.Native.BufferRTV0 : this.Native.BufferRTV1;
        CommandListD.Reset();

        CpuDescriptorHandle RTV = this.Native.DescriptorHeapRTV->GetCPUDescriptorHandleForHeapStart();
        RTV.Offset((int)BufferIndex, this.RTVDescriptorSize);
        CpuDescriptorHandle DSV;
        fixed (NativeResources* nthis = &this.Native)
        {
            // Clear the render target.
            {
                ResourceBarrier Barrier = ResourceBarrier.InitTransition(BackBuffer, ResourceStates.Present, ResourceStates.RenderTarget);
                CommandListD.NativeList->ResourceBarrier(1, &Barrier);
                CommandListD.NativeList->ClearRenderTargetView(RTV, (float*)&nthis->ClearColour, 0, null);
                if (this.HasDepth)
                {
                    DSV = this.Native.DescriptorHeapDSV->GetCPUDescriptorHandleForHeapStart();
                    CommandListD.NativeList->ClearDepthStencilView(DSV, ClearFlags.Depth, 1F, 0, 0, null);
                }
            }
            
            // Prep state
            fixed (Viewport* ViewportPtr = &this.Viewport) { CommandListD.NativeList->RSSetViewports(1, ViewportPtr); }
            fixed (Rect* ScissorPtr = &this.ScissorRect) { CommandListD.NativeList->RSSetScissorRects(1, ScissorPtr); }
            CommandListD.NativeList->OMSetRenderTargets(1, &RTV, false, this.HasDepth ? &DSV : null);

            lock (this.Interlock)
            {

                // Actually render
                this.Mode?.Render(nthis->Device, CommandListD);

                // Present
                {
                    ResourceBarrier Barrier = ResourceBarrier.InitTransition(BackBuffer, ResourceStates.RenderTarget, ResourceStates.Present);
                    CommandListD.NativeList->ResourceBarrier(1, &Barrier);
                    ulong RenderFenceValue = CommandListD.Present(nthis->Swapchain);

                    CommandListD.WaitForFenceValue(RenderFenceValue);
                    this.CurrentBackBuffer = nthis->Swapchain->GetCurrentBackBufferIndex();
                }

                this.Mode?.PostRender(nthis->Device);

                DWMTimingInfo TimingInfo = new() { cbSize = (uint)sizeof(DWMTimingInfo) };
                Win32API.QueryPerformanceCounter(out ulong QPC);
                ThrowIfFailed(Win32API.DwmGetCompositionTimingInfo(0, ref TimingInfo));
                //Console.WriteLine($"It is now {QPC}, previous audio was at {(this.SourceForRenderDelay?.LastBufferArrivalTime ?? 0)}, audio is at {this.SourceForRenderDelay?.BufferPeriodTimerTicks ?? 0}, next vsync is at {TimingInfo.qpcCompose}");
                //Console.WriteLine($"It is now {QPC / (double)this.TimerFrequency}, previous audio was at {(this.SourceForRenderDelay?.LastBufferArrivalTime ?? 0) / (double)this.TimerFrequency}, next vsync is at {TimingInfo.qpcCompose / (double)this.TimerFrequency}");
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

        this.CurrentCommandListDirect.Flush();

        // Any references to the back buffers must be released before the swap chain can be resized.
        COMRelease(ref this.Native.BufferRTV0);
        COMRelease(ref this.Native.BufferRTV1);

        this.Viewport = new(0F, 0F, NewWidth, NewHeight);

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
        this.Mode?.Resize(NewWidth, NewHeight);
        this.PreviousHeight = NewHeight;
        this.PreviousWidth = NewWidth;
    }

    public void Dispatch()
    {
        //Win32API.QueryPerformanceCounter(out ulong QPC);
        //Console.WriteLine($"It is now {QPC / (double)this.TimerFrequency}");
        this.CommandListCopy.Reset();
        this.Mode?.Dispatch(this.Native.Device, this.CommandListCopy);
        this.DispatchRenderGate.Set();
    }

    public void Start()
    {
        
    }

    public void Stop()
    {
        this.KeepGoing = false;
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
        //this.WindowThread?.Join();
    }

    public void InstThreadPostInit()
    {
        this.Stopwatch.Start();
        while (this.KeepGoing)
        {
            Win32API.WaitForSingleObjectEx(this.Native.SwapchainWaitHandle, 1000, true);
            if (this.DoLatencyReduction)
            {
                DWMTimingInfo TimingInfo = new() { cbSize = (uint)sizeof(DWMTimingInfo) };
                ThrowIfFailed(Win32API.DwmGetCompositionTimingInfo(0, ref TimingInfo));
                //Win32API.QueryPerformanceCounter(out ulong QPC);
                ulong Margin = 20000;

                this.DispatchRenderGate.Reset();
                ulong NextRenderDeadline = TimingInfo.qpcCompose - Margin;
                ulong NextAudioBuffer = this.SourceForRenderDelay!.LastBufferArrivalTime + this.SourceForRenderDelay.BufferPeriodTimerTicks;
                if (NextRenderDeadline > NextAudioBuffer)
                {
                    //Console.WriteLine($"W {TimingInfo.qpcCompose} - {Margin} > {this.SourceForRenderDelay!.LastBufferArrivalTime} + {this.SourceForRenderDelay.BufferPeriodTimerTicks} (now {QPC})");
                    this.DispatchRenderGate.WaitOne(2);
                    //Win32API.QueryPerformanceCounter(out ulong QPCAfter);
                    //Console.WriteLine($"Waited {(QPCAfter - QPC) / 10000.0}ms");
                }
                //else { Console.WriteLine($"N {TimingInfo.qpcCompose} - {Margin} < {this.SourceForRenderDelay!.LastBufferArrivalTime} + {this.SourceForRenderDelay.BufferPeriodTimerTicks} (now {QPC})"); }
            }
            Update();
            Render();
        }
    }

    internal static void UpdateBufferResource<T>(ID3D12Device2* device, CommandList commandList, ReadOnlySpan<T> data, ResourceFlags flags, out ID3D12Resource* intermediateResource, out ID3D12Resource* destinationResource) where T : unmanaged
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
            UpdateSubresources((ID3D12GraphicsCommandList*)commandList.NativeList, destinationResource, intermediateResource, 0, 0, 1, &Subresource);
        }
    }
}
