using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using ColorChord.NET.API;
using ColorChord.NET.API.Visualizers;
using ColorChord.NET.API.Visualizers.Formats;
using ColorChord.NET.Config;
using ColorChord.NET.Outputs.DisplayD3D12Support;
using Vortice.Win32;
using Vortice.Win32.Graphics.Direct3D;
using Vortice.Win32.Graphics.Direct3D12;
using Vortice.Win32.Graphics.Dxgi.Common;
using static Vortice.Win32.Graphics.Direct3D12.Apis;
using static ColorChord.NET.Outputs.DisplayD3D12Support.COMUtils;
using ColorChord.NET.API.Config;

namespace ColorChord.NET.Outputs.DisplayD3D12Modes;

public unsafe class TutorialModeNew : ID3D12DisplayMode, IConfigurableAttr
{
    struct VertexData
    {
        public Vector3 Position;
        public Vector3 Colour;
    }

    private readonly VertexData[] Vertices =
    {
        new() { Position = new(-1.0f, -1.0f, -1.0f), Colour = new(0.0f, 0.0f, 0.0f) }, // 0
        new() { Position = new(-1.0f,  1.0f, -1.0f), Colour = new(0.0f, 1.0f, 0.0f) }, // 1
        new() { Position = new( 1.0f,  1.0f, -1.0f), Colour = new(1.0f, 1.0f, 0.0f) }, // 2
        new() { Position = new( 1.0f, -1.0f, -1.0f), Colour = new(1.0f, 0.0f, 0.0f) }, // 3
        new() { Position = new(-1.0f, -1.0f,  1.0f), Colour = new(0.0f, 0.0f, 1.0f) }, // 4
        new() { Position = new(-1.0f,  1.0f,  1.0f), Colour = new(0.0f, 1.0f, 1.0f) }, // 5
        new() { Position = new( 1.0f,  1.0f,  1.0f), Colour = new(1.0f, 1.0f, 1.0f) }, // 6
        new() { Position = new( 1.0f, -1.0f,  1.0f), Colour = new(1.0f, 0.0f, 1.0f) }  // 7
    };
    private readonly ushort[] CubeIndices =
    {
        0, 1, 2, 0, 2, 3,
        4, 6, 5, 4, 7, 6,
        4, 5, 1, 4, 1, 0,
        3, 2, 6, 3, 6, 7,
        1, 5, 6, 1, 6, 2,
        4, 0, 3, 4, 3, 7
    };

    private readonly DisplayD3D12 Host;
    private readonly IVisualizer Visualizer;

    private VertexBuffer<VertexData> VertexBuffer;
    private IndexBuffer<ushort> IndexBuffer;
    private Shader Shader;
    private bool Ready;
    private float Time;
    private Matrix4x4 ModelMatrix, ViewMatrix, ProjectionMatrix;

    public TutorialModeNew(DisplayD3D12 host, IVisualizer visualizer, Dictionary<string, object> config)
    {
        this.Host = host;
        this.Visualizer = visualizer;
        this.Host.HasDepth = true;
        ColorChordAPI.Configurer.Configure(this, config);
    }

    public bool SupportsFormat(IVisualizerFormat format) => true;

    public void Load(ID3D12Device2* device, CommandList copyCommandList, CommandList directCommandList)
    {
        this.VertexBuffer = new(device, copyCommandList, this.Vertices);
        this.IndexBuffer = new(device, copyCommandList, this.CubeIndices);
        InputElementDescription[] VertexInputs =
        [
            new()
            {
                SemanticName = "POSITION\0"u8.GetPointer(),
                SemanticIndex = 0,
                Format = Format.R32G32B32Float,
                InputSlot = 0,
                AlignedByteOffset = D3D12_APPEND_ALIGNED_ELEMENT,
                InputSlotClass = InputClassification.PerVertexData,
                InstanceDataStepRate = 0
            },
            new()
            {
                SemanticName = "COLOR\0"u8.GetPointer(),
                SemanticIndex = 0,
                Format = Format.R32G32B32Float,
                InputSlot = 0,
                AlignedByteOffset = D3D12_APPEND_ALIGNED_ELEMENT,
                InputSlotClass = InputClassification.PerVertexData,
                InstanceDataStepRate = 0
            }
        ];
        RootParameter1.InitAsConstants(out RootParameter1 MatrixParameter, (uint)(sizeof(Matrix4x4) / sizeof(float)), 0, 0, ShaderVisibility.Vertex);
        RootParameter1[] RootParameters = [MatrixParameter];
        this.Shader = new(device, VertexInputs, "VS_Tutorial.cso", "PS_Tutorial.cso", rootParameters: RootParameters, useDepth: this.Host.HasDepth);

        ulong FenceValue = copyCommandList.Execute();

        float AspectRatio = this.Host.WindowWidth / (float)this.Host.WindowHeight;
        this.ProjectionMatrix = Matrix4x4.CreatePerspectiveFieldOfViewLeftHanded(45F / 180F * MathF.PI, AspectRatio, 0.1F, 100F);

        copyCommandList.WaitForFenceValue(FenceValue);
        this.Ready = true;
    }

    public void Render(ID3D12Device2* device, CommandList directCommandList)
    {
        if (!this.Ready) { return; }
        this.Time += 0.04F;
        this.ModelMatrix = Matrix4x4.CreateRotationY(this.Time) * Matrix4x4.CreateRotationZ(this.Time / 10F) * Matrix4x4.CreateRotationX(this.Time / 100F);
        this.ModelMatrix *= Matrix4x4.CreateTranslation(MathF.Sin(this.Time * 3F) * 5F, MathF.Sin(this.Time * 7F) * 2F, MathF.Tan(this.Time));

        Vector3 EyePosition = new(0, 0, -10);
        Vector3 FocusPoint = new(0, 0, 0);
        Vector3 UpDirection = new(0, 1, 0);
        this.ViewMatrix = Matrix4x4.CreateLookAtLeftHanded(EyePosition, FocusPoint, UpDirection);

        this.Shader.Use(directCommandList);
        directCommandList.NativeList->IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        this.VertexBuffer.Use(directCommandList);
        this.IndexBuffer.Use(directCommandList);

        Matrix4x4 MVPMatrix = this.ModelMatrix * this.ViewMatrix * this.ProjectionMatrix;
        directCommandList.NativeList->SetGraphicsRoot32BitConstants(0, (uint)(sizeof(Matrix4x4) / sizeof(float)), &MVPMatrix, 0);

        directCommandList.NativeList->DrawIndexedInstanced((uint)(this.CubeIndices.Length), 1, 0, 0, 0);
    }

    public void Dispatch(ID3D12Device2* device, CommandList copyCommandList)
    {
        
    }

    public void Resize(int width, int height)
    {
        float AspectRatio = this.Host.WindowWidth / (float)this.Host.WindowHeight;
        this.ProjectionMatrix = Matrix4x4.CreatePerspectiveFieldOfViewLeftHanded(45F / 180F * MathF.PI, AspectRatio, 0.1F, 100F);
    }

    public void Close()
    {
        this.Shader?.Dispose();
        this.VertexBuffer?.Dispose();
        this.IndexBuffer?.Dispose();
    }
}
