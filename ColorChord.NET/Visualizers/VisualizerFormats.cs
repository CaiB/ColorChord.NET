namespace ColorChord.NET.Visualizers.Formats
{
    /// <summary> Just used as a parent category, provides no direct functionality. </summary>
    public interface IVisualizerFormat { }

    // Discrete is like a raster image, it has a specified resolution and colour data for each unit. Continuous is like a vector image, where colour boundaries are defined, and the final rendering is left to the data consumer.

    /// <summary> Used by visualizers to indicate that they output a 1D constant-size array of individual colour data points, like LEDs. </summary>
    public interface IDiscrete1D : IVisualizerFormat
    {
        int GetCount();
        byte[] GetData();
    }

    /// <summary> Used by visualizers to indicate that they output a 2D constant-size array of individual colour data points, like a screen. </summary>
    public interface IDiscrete2D : IVisualizerFormat
    {
        int GetWidth();
        int GetHeight();
        byte[,] GetData();
    }

    /// <summary> Used by visualizers to indicate that they output a 1D variable-size array of colour boundaries with no specified resolution. </summary>
    /// <remarks> NOT YET IMPLEMENTED. </remarks>
    public interface IContinuous1D : IVisualizerFormat { }

    /// <summary> Used to distinguish formats that don't fit other descriptions. </summary>
    public interface IOtherFormat : IVisualizerFormat { }
}