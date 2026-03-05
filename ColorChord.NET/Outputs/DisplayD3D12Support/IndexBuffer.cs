using System;
using System.Diagnostics;
using Win32.Graphics.Direct3D12;
using Win32.Graphics.Dxgi.Common;
using static ColorChord.NET.Outputs.DisplayD3D12Support.COMUtils;

namespace ColorChord.NET.Outputs.DisplayD3D12Support;

public unsafe class IndexBuffer<T> : IDisposable where T : unmanaged
{
    private ID3D12Resource* BF_Buffer;
    private IndexBufferView BF_View;
    private bool IsDisposed;

    public ID3D12Resource* Buffer { get => this.BF_Buffer; }
    public IndexBufferView View { get => this.BF_View; }

    public IndexBuffer(ID3D12Device2* device, ID3D12GraphicsCommandList2* copyCommandList, ReadOnlySpan<T> data) => Load(device, copyCommandList, data);

    /// <summary>Copies new index data to the GPU, automatically freeing the old data if it was previously loaded</summary>
    public void Load(ID3D12Device2* device, ID3D12GraphicsCommandList2* copyCommandList, ReadOnlySpan<T> data)
    {
        COMRelease(ref this.BF_Buffer);
        DisplayD3D12.UpdateBufferResource(device, copyCommandList, data, ResourceFlags.None, out ID3D12Resource* IntermediateIndexBuffer, out this.BF_Buffer);
        this.BF_View = new()
        {
            BufferLocation = this.BF_Buffer->GetGPUVirtualAddress(),
            Format = typeof(T) switch
            {
                Type ByteType when typeof(T) == typeof(byte) => Format.R8Uint,
                Type UShortType when typeof(T) == typeof(ushort) => Format.R16Uint,
                Type UIntType when typeof(T) == typeof(uint) => Format.R32Uint,
                _ => throw new Exception($"{nameof(IndexBuffer<>)} does not support the type {typeof(T).FullName}")
            },
            SizeInBytes = (uint)(sizeof(T) * data.Length)
        };
    }

    public void Use(ID3D12GraphicsCommandList* directCommandList)
    {
        Debug.Assert(this.Buffer != null);
        fixed (IndexBufferView* IndexViewPtr = &this.BF_View) { directCommandList->IASetIndexBuffer(IndexViewPtr); }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!this.IsDisposed)
        {
            COMRelease(ref this.BF_Buffer);
            this.IsDisposed = true;
        }
    }

    ~IndexBuffer() => Dispose(disposing: false);

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
