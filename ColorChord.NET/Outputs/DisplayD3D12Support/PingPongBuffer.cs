using System;
using System.Threading;
using Vortice.Win32.Graphics.Direct3D12;
using Vortice.Win32.Graphics.Dxgi.Common;
using static ColorChord.NET.Outputs.DisplayD3D12Support.COMUtils;
using static Vortice.Win32.Apis;

namespace ColorChord.NET.Outputs.DisplayD3D12Support;

public unsafe class PingPongBuffer<T> : IDisposable where T : unmanaged
{
    // Note: 64KB alignment
    private ID3D12Heap* Heap;
    private ID3D12Resource* BufferA, BufferB;
    private ResourceDescription Description;
    private readonly uint ElementSize;
    private ulong BufferSize;
    private bool CopyingToA = true;
    private readonly string? Name;
    private bool IsDisposed;
    private readonly Lock RenderCopyMutex = new();

    public Format Format { get; private init; }
    public ID3D12Resource* RenderBuffer { get => this.CopyingToA ? this.BufferB : this.BufferA; }

    public PingPongBuffer(Format format = Format.R32Typeless, uint elementSizeOverride = 0, string? name = null)
    {
        this.Name = name;
        this.ElementSize = elementSizeOverride == 0 ? (uint)sizeof(T) : elementSizeOverride;
        this.Format = format;
    }

    public void Create(ID3D12Device2* device, int bufferLength)
    {
        ulong UsedSize = (ulong)(((bufferLength * this.ElementSize) + 65535L) & -65536L);
        this.BufferSize = UsedSize;
        HeapDescription HeapDesc = new()
        {
            Alignment = 0,
            Flags = HeapFlags.AllowOnlyBuffers,
            Properties = new()
            {
                Type = HeapType.Upload,
                CPUPageProperty = CpuPageProperty.Unknown,
                MemoryPoolPreference = MemoryPool.Unknown,
                CreationNodeMask = 0,
                VisibleNodeMask = 0
            },
            SizeInBytes = UsedSize * 2
        };
        ID3D12Heap* ResultHeap;
        ThrowIfFailed(device->CreateHeap(&HeapDesc, __uuidof<ID3D12Heap>(), (void**)&ResultHeap));
        ResultHeap->SetName($"{this.Name} PingPongHeap");
        this.Heap = ResultHeap;

        ResourceDescription BufferDesc = new()
        {
            Alignment = 0,
            DepthOrArraySize = 1,
            Dimension = ResourceDimension.Buffer,
            Flags = ResourceFlags.None,
            Format = Format.Unknown,
            Height = 1,
            Layout = TextureLayout.RowMajor,
            MipLevels = 1,
            SampleDesc = new() { Count = 1, Quality = 0 },
            Width = UsedSize
        };
        this.Description = BufferDesc;
        ID3D12Resource* ResultBuff;
        ThrowIfFailed(device->CreatePlacedResource(this.Heap, 0, &BufferDesc, ResourceStates.GenericRead, null, __uuidof<ID3D12Resource>(), (void**)&ResultBuff));
        ResultBuff->SetName($"{this.Name} PingPongBuffA");
        this.BufferA = ResultBuff;

        ThrowIfFailed(device->CreatePlacedResource(this.Heap, UsedSize, &BufferDesc, ResourceStates.GenericRead, null, __uuidof<ID3D12Resource>(), (void**)&ResultBuff));
        ResultBuff->SetName($"{this.Name} PingPongBuffB");
        this.BufferB = ResultBuff;
    }

    public void Load(ReadOnlySpan<T> data)
    {
        if (this.BufferSize < (uint)data.Length * this.ElementSize) { throw new Exception($"{nameof(PingPongBuffer<T>)} was created with size {this.BufferSize} buffers, but an attempt was made to load {data.Length} items x {this.ElementSize}B, which does not fit into the buffer."); }

        lock (this.RenderCopyMutex)
        {
            ID3D12Resource* CopyTarget = this.CopyingToA ? this.BufferA : this.BufferB;
            byte* UploadBufferPtr;
            ThrowIfFailed(CopyTarget->Map(0, null, (void**)&UploadBufferPtr));
            data.CopyTo(new(UploadBufferPtr, (int)this.BufferSize));
            CopyTarget->Unmap(0, null);
        }
    }

    public void StartRender() => this.RenderCopyMutex.Enter();

    public void FinishRender()
    {
        this.CopyingToA = !this.CopyingToA;
        this.RenderCopyMutex.Exit();
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!this.IsDisposed)
        {
            COMRelease(ref this.BufferA);
            COMRelease(ref this.BufferB);
            COMRelease(ref this.Heap);
            IsDisposed = true;
        }
    }

    ~PingPongBuffer() => Dispose(disposing: false);

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
