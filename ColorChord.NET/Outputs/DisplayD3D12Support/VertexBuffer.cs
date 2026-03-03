using System;
using Win32.Graphics.Direct3D12;

namespace ColorChord.NET.Outputs.DisplayD3D12Support;

public unsafe class VertexBuffer<T> where T : unmanaged
{
    private ID3D12Resource* BF_Buffer;
    private VertexBufferView BF_View;
    public ID3D12Resource* Buffer { get => this.BF_Buffer; }
    public VertexBufferView View { get => this.BF_View; }

    public VertexBuffer(ID3D12Device2* device, ID3D12GraphicsCommandList2* copyCommandList, ReadOnlySpan<T> data) => Load(device, copyCommandList, data);

    /// <summary>Copies new vertex data to the GPU, automatically freeing the old data if it was previously loaded</summary>
    public void Load(ID3D12Device2* device, ID3D12GraphicsCommandList2* copyCommandList, ReadOnlySpan<T> data)
    {
        if (this.BF_Buffer != null)
        {
            this.BF_Buffer->Release();
            this.BF_Buffer = null;
        }
        DisplayD3D12.UpdateBufferResource(device, copyCommandList, data, ResourceFlags.None, out ID3D12Resource* IntermediateVertexBuffer, out this.BF_Buffer);
        this.BF_View = new()
        {
            BufferLocation = this.BF_Buffer->GetGPUVirtualAddress(),
            SizeInBytes = (uint)(sizeof(T) * data.Length),
            StrideInBytes = (uint)sizeof(T)
        };
    }

    public void Use(ID3D12GraphicsCommandList* directCommandList)
    {
        fixed (VertexBufferView* VertexViewPtr = &this.BF_View) { directCommandList->IASetVertexBuffers(0, 1, VertexViewPtr); }
    }
}
