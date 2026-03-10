using ColorChord.NET.API.Visualizers.Formats;
using Vortice.Win32.Graphics.Direct3D12;

namespace ColorChord.NET.Outputs.DisplayD3D12Support;

/// <summary>Implemented by display modes for the <see cref="DisplayD3D12"/> output.</summary>
internal unsafe interface ID3D12DisplayMode
{
    bool SupportsFormat(IVisualizerFormat format);

    /// <summary> Called once D3D is ready. </summary>
    void Load(ID3D12Device2* device, CommandQueue copyQueue, ID3D12GraphicsCommandList* directCommandList);

    /// <summary> Called to render a frame on the screen. </summary>
    void Render(ID3D12Device2* device, ID3D12GraphicsCommandList* directCommandList);

    /// <summary> Passed through from the visualizer. </summary>
    void Dispatch();

    /// <summary> Called when the window gets resized. </summary>
    void Resize(int width, int height);

    /// <summary> Called when this display is no longer being shown. </summary>
    void Close();
}
