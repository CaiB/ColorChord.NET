using System;
using Win32.Graphics.Direct3D12;
using Win32.Graphics.Dxgi.Common;

namespace ColorChord.NET.Outputs.DisplayD3D12Support;

public unsafe class IndexBuffer<T> where T : unmanaged
{
    private ID3D12Resource* BF_Buffer;
    private IndexBufferView BF_View;
    public ID3D12Resource* Buffer { get => this.BF_Buffer; }
    public IndexBufferView View { get => this.BF_View; }

    public IndexBuffer(ID3D12Device2* device, ID3D12GraphicsCommandList2* copyCommandList, ReadOnlySpan<T> data) => Load(device, copyCommandList, data);

    public void Load(ID3D12Device2* device, ID3D12GraphicsCommandList2* copyCommandList, ReadOnlySpan<T> data)
    {
        if (this.BF_Buffer != null)
        {
            this.BF_Buffer->Release();
            this.BF_Buffer = null;
        }
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
        fixed (IndexBufferView* IndexViewPtr = &this.BF_View) { directCommandList->IASetIndexBuffer(IndexViewPtr); }
    }
}
