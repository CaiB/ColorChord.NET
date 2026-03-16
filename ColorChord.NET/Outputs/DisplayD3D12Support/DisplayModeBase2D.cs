using ColorChord.NET.API.Config;
using ColorChord.NET.API.NoteFinder;
using ColorChord.NET.API.Visualizers;
using ColorChord.NET.API.Visualizers.Formats;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Vortice.Win32;
using Vortice.Win32.Graphics.Direct3D;
using Vortice.Win32.Graphics.Direct3D12;
using Vortice.Win32.Graphics.Dxgi.Common;
using static ColorChord.NET.Outputs.DisplayD3D12Support.COMUtils;
using static Vortice.Win32.Graphics.Direct3D12.Apis;

namespace ColorChord.NET.Outputs.DisplayD3D12Support;

public abstract class DisplayModeBase2D : ID3D12DisplayMode, IConfigurableAttr
{
    private readonly Vector2[] Vertices =
    [
        new(-1F, 1F),
        new(3F, 1F),
        new(-1F, -3F)
    ];
    private VertexBuffer<Vector2>? VertexBuffer;

    protected readonly DisplayD3D12 Host;
    
    public DisplayModeBase2D(DisplayD3D12 host, IVisualizer visualizer, Dictionary<string, object> config)
    {
        this.Host = host;
    }

    protected unsafe InputElementDescription[] LoadGeometry(ID3D12Device2* device, CommandList copyCommandList)
    {
        Debug.Assert(this.VertexBuffer == null, $"There is no need to call {nameof(LoadGeometry)} more than once.");
        this.VertexBuffer?.Dispose();
        this.VertexBuffer = new(device, copyCommandList, this.Vertices);
        return
        [
            new()
            {
                SemanticName = "POSITION\0"u8.GetPointer(),
                SemanticIndex = 0,
                Format = Format.R32G32Float,
                InputSlot = 0,
                AlignedByteOffset = D3D12_APPEND_ALIGNED_ELEMENT,
                InputSlotClass = InputClassification.PerVertexData,
                InstanceDataStepRate = 0
            }
        ];
    }

    protected unsafe void RenderGeometry(CommandList directCommandList)
    {
        if (this.VertexBuffer == null) { throw new InvalidOperationException($"Cannot call {nameof(RenderGeometry)} before calling {nameof(LoadGeometry)} once."); }
        directCommandList.NativeList->IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        this.VertexBuffer.Use(directCommandList);
        directCommandList.NativeList->DrawInstanced(3, 1, 0, 0);
    }

    public void Close()
    {
        this.VertexBuffer?.Dispose();
    }

    public abstract bool SupportsFormat(IVisualizerFormat format);
    public abstract unsafe void Load(ID3D12Device2* device, CommandList copyCommandList, CommandList directCommandList);
    public abstract unsafe void Dispatch(ID3D12Device2* device, CommandList copyCommandList);
    public abstract void Resize(int width, int height);
    public abstract unsafe void Render(ID3D12Device2* device, CommandList directCommandList);
}
