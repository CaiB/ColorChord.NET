using ColorChord.NET.API.Visualizers.Formats;

namespace ColorChord.NET.API.Outputs.Display;

/// <summary>Implemented by display modes for the DisplayOpenGL output.</summary>
public interface IDisplayMode
{
    bool SupportsFormat(IVisualizerFormat format);

    /// <summary> Called once OpenGL is ready. </summary>
    void Load();

    /// <summary> Called to render a frame on the screen. </summary>
    void Render();

    /// <summary> Passed through from the visualizer. </summary>
    void Dispatch();

    /// <summary> Called when the window gets resized. </summary>
    void Resize(int width, int height);

    /// <summary> Called when this display is no longer being shown. </summary>
    void Close();
}
