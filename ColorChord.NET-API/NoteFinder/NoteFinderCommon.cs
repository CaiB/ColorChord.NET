using ColorChord.NET.API.Config;
using System;

namespace ColorChord.NET.API.NoteFinder;

public abstract class NoteFinderCommon : IConfigurableAttr
{
    /// <summary> The buffer for audio data gathered from a system device. Circular buffer, with the current write position stored in <see cref="AudioBufferHeadWrite"/>. </summary>
    public static float[] AudioBuffer { get; protected set; } = new float[8192]; // TODO: Make buffer size adjustable or auto-set based on sample rate (might be too short for super-high rates)

    /// <summary> Where in the <see cref="AudioBuffer"/> we are currently adding new audio data. </summary>
    public static int AudioBufferHeadWrite { get; set; } = 0;

    /// <summary> When data was last added to the buffer. Used to detect idle state. </summary>
    public static DateTime LastDataAdd { get; set; }

    /// <summary> The speed (in ms between runs) at which the note finder needs to run, set by the fastest visualizer. </summary>
    public static uint ShortestPeriod { get; protected set; } = 100;

    /// <summary> The notes we have detected in the current cycle. </summary>
    public static Note[] Notes { get; protected set; } = Array.Empty<Note>();

    /// <summary> Used to keep track of locations of notes that stay between frames in <see cref="Notes"/>, as that array's order may change. </summary>
    public static int[] PersistentNoteIDs = Array.Empty<int>();

    /// <summary>How many note slots there are. Usually not all are in use.</summary>
    public abstract int NoteCount { get; } // TODO: Finish docs

    public abstract int BinsPerOctave { get; }

    public abstract void Start();

    public abstract void Stop();

    public abstract void AdjustOutputSpeed(uint period);

    public abstract void SetSampleRate(int sampleRate);
}

/// <summary> A note after filtering, stabilization and denoising of the raw DFT output. </summary>
public struct Note
{
    /// <summary> Where on the scale this note is. Range: 0~<see cref="OctaveBinCount"/>. </summary>
    public float Position;

    /// <summary> The note amplitude as of the previous cycle, with minimal filtering. </summary>
    public float Amplitude;

    /// <summary> The note amplitude, with some inter-frame smoothing applied. </summary>
    public float AmplitudeFiltered;

    /// <summary> The note amplitude, zeroed if very low amplitude. Based on <see cref="Amplitude"/>, so minimal inter-frame smoothing is applied. </summary>
    public float AmplitudeFinal;
}
