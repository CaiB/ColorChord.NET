using System;
using System.Diagnostics;
using Vortice.Win32.Graphics.Direct3D12;
using static ColorChord.NET.Outputs.DisplayD3D12Support.COMUtils;

namespace ColorChord.NET.Outputs.DisplayD3D12Support;

public unsafe class VertexBuffer<T> : IDisposable where T : unmanaged
{
    private ID3D12Resource* BF_Buffer;
    private VertexBufferView BF_View;
    private bool IsDisposed;

    public ID3D12Resource* Buffer { get => this.BF_Buffer; }
    public VertexBufferView View { get => this.BF_View; }

    public VertexBuffer(ID3D12Device2* device, CommandList copyCommandList, ReadOnlySpan<T> data) => Load(device, copyCommandList, data);

    /// <summary>Copies new vertex data to the GPU, automatically freeing the old data if it was previously loaded</summary>
    public void Load(ID3D12Device2* device, CommandList copyCommandList, ReadOnlySpan<T> data)
    {
        COMRelease(ref this.BF_Buffer);
        DisplayD3D12.UpdateBufferResource(device, copyCommandList, data, ResourceFlags.None, out ID3D12Resource* IntermediateVertexBuffer, out this.BF_Buffer);
        this.BF_View = new()
        {
            BufferLocation = this.BF_Buffer->GetGPUVirtualAddress(),
            SizeInBytes = (uint)(sizeof(T) * data.Length),
            StrideInBytes = (uint)sizeof(T)
        };
    }

    public void Use(CommandList directCommandList)
    {
        Debug.Assert(this.Buffer != null);
        fixed (VertexBufferView* VertexViewPtr = &this.BF_View) { directCommandList.NativeList->IASetVertexBuffers(0, 1, VertexViewPtr); }
    }


    protected virtual void Dispose(bool disposing)
    {
        if (!this.IsDisposed)
        {
            COMRelease(ref this.BF_Buffer);
            this.IsDisposed = true;
        }
    }

    ~VertexBuffer() => Dispose(disposing: false);

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
