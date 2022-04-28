using System;

namespace ColorChord.NET.Visualizers.Formats
{
    /// <summary> Just used as a parent category, provides no direct functionality. </summary>
    public interface IVisualizerFormat { }

    // Discrete is like a raster image, it has a specified resolution and colour data for each unit. Continuous is like a vector image, where colour boundaries are defined, and the final rendering is left to the data consumer.

    /// <summary> Used by visualizers to indicate that they output a 1D constant-size array of individual colour data points, like LEDs. </summary>
    public interface IDiscrete1D : IVisualizerFormat
    {
        int GetCountDiscrete();
        uint[] GetDataDiscrete();
    }

    /// <summary> Used by visualizers to indicate that they output a 2D constant-size array of individual colour data points, like a screen. </summary>
    public interface IDiscrete2D : IVisualizerFormat
    {
        int GetWidth();
        int GetHeight();
        uint[,] GetDataDiscrete();
    }

    public class ContinuousDataUnit : IComparable<ContinuousDataUnit>
    {
        /// <summary>Where this section of output begins. The first one will always be 0.</summary>
        public float Location;

        /// <summary>How large this chunk of colour is.</summary>
        public float Size;

        /// <summary>The colour of this section.</summary>
        public byte R, G, B;

        /// <summary>The colour of this section, represented in ColorChord hue.</summary>
        public float Colour;

        public int CompareTo(ContinuousDataUnit? other)
        {
            if (other == null) { return 1; }
            if (other.Location > this.Location) { return -1; }
            if (other.Location < this.Location) { return 1; }
            else { return 0; }
        }
    }

    /// <summary> Used by visualizers to indicate that they output a 1D variable-size array of colour boundaries with no specified resolution. </summary>
    public interface IContinuous1D : IVisualizerFormat
    {
        int GetCountContinuous();
        ContinuousDataUnit[] GetDataContinuous();
        float GetAdvanceContinuous();
        int MaxPossibleUnits { get; }
    }

    /// <summary> Used to distinguish formats that don't fit other descriptions. </summary>
    public interface IOtherFormat : IVisualizerFormat { }
}