using ColorChord.NET.Visualizers.Formats;

namespace ColorChord.NET.Outputs.Display
{
    public interface IDisplayMode
    {
        bool SupportsFormat(IVisualizerFormat format);

        /// <summary> Called once OpenGL is ready. </summary>
        void Load();

        /// <summary> Called to render a frame on the screen. </summary>
        void Render();

        /// <summary> Passed through from the visualizer. </summary>
        void Dispatch();

        /// <summary> Called when this display is no longer being shown. </summary>
        void Close();
    }
}
