using ColorChord.NET.API.Config;
using System.Diagnostics;

namespace ColorChord.NET.API.NoteFinder;

public abstract class NoteFinderCommon : IConfigurableAttr
{
    public abstract string Name { get; protected init; }

    /// <summary> When data was last added to the buffer. Used to detect idle state. </summary>
    public DateTime LastDataAdd { get; private set; }

    /// <summary> The speed (in ms between runs) at which the note finder needs to run, set by the fastest visualizer. </summary>
    public uint ShortestPeriod { get; protected set; } = 100;

    /// <summary> The frequency spectrum data, before folding into a single octave. </summary>
    public abstract ReadOnlySpan<float> AllBinValues { get; }

    /// <summary> The frequency spectrum data, folded to overlap into a single octave length. </summary>
    public abstract ReadOnlySpan<float> OctaveBinValues { get; }

    /// <summary> The notes we have detected in the current cycle. </summary>
    public abstract ReadOnlySpan<Note> Notes { get; }

    /// <summary> Used to keep track of locations of notes that stay between frames in <see cref="Notes"/>, as that array's order may change. </summary>
    public abstract ReadOnlySpan<int> PersistentNoteIDs { get; }

    public AutoResetEvent InputDataEvent { get; protected set; } = new(false);

    /// <summary>How many note slots there are. Usually not all are in use.</summary>
    public abstract int NoteCount { get; } // TODO: Finish docs

    public abstract int BinsPerOctave { get; }

    public abstract float StartFrequency { get; }

    public abstract int Octaves { get; }

    public abstract void Start();
    public abstract void UpdateOutputs();

    public abstract void Stop();

    public abstract void AdjustOutputSpeed(uint period);

    public abstract void SetSampleRate(int sampleRate);


    private const int INTERMEDIATE_BUFFER_COUNT = 4;
    private readonly IntermediateBuffer[] IntermediateBuffers = new IntermediateBuffer[INTERMEDIATE_BUFFER_COUNT];
    private readonly int[] IntermediateBuffersToRead = new int[INTERMEDIATE_BUFFER_COUNT];

    public void SetupBuffers()
    {
        for (int i = 0; i < INTERMEDIATE_BUFFER_COUNT; i++)
        {
            IntermediateBuffers[i] = new();
            IntermediateBuffersToRead[i] = -1;
        }
    }

    public short[]? GetBufferToWrite(out int bufferRef)
    {
        for (int i = 0; i < INTERMEDIATE_BUFFER_COUNT; i++)
        {
            if (!IntermediateBuffers[i].ReadMode)
            {
                bufferRef = i;
                return IntermediateBuffers[i].Buffer;
            }
        }
        Log.Warn("NoteFinder is unable to keep up with incoming audio, some data will be dropped.");
        bufferRef = -1;
        return null;
    }

    public short[]? GetBufferToRead(out int bufferRef, out uint amountToRead, out bool moreAvailable)
    {
        lock (IntermediateBuffersToRead)
        {
            if (IntermediateBuffersToRead[0] == -1 || !IntermediateBuffers[IntermediateBuffersToRead[0]].ReadMode)
            {
                bufferRef = -1;
                amountToRead = 0;
                moreAvailable = false;
                return null;
            }
            else
            {
                bufferRef = IntermediateBuffersToRead[0];
                amountToRead = IntermediateBuffers[bufferRef].DataCount;
                moreAvailable = IntermediateBuffersToRead[1] != -1 && IntermediateBuffers[IntermediateBuffersToRead[1]].ReadMode;
                for (int i = 0; i < INTERMEDIATE_BUFFER_COUNT - 1; i++) { IntermediateBuffersToRead[i] = IntermediateBuffersToRead[i + 1]; } // Shift everything left 1
                IntermediateBuffersToRead[INTERMEDIATE_BUFFER_COUNT - 1] = -1;
                return IntermediateBuffers[bufferRef].Buffer;
            }
        }
    }

    public bool IsBufferAvailableToWrite()
    {
        for (int i = 0; i < INTERMEDIATE_BUFFER_COUNT; i++)
        {
            if (!IntermediateBuffers[i].ReadMode) { return true; }
        }
        return false;
    }

    public void FinishBufferRead(int bufferRef)
    {
        IntermediateBuffers[bufferRef].ReadMode = false;
    }

    public void FinishBufferWrite(int bufferRef, uint amountWritten)
    {
        if (bufferRef == -1) { return; }
        IntermediateBuffers[bufferRef].DataCount = amountWritten;
        IntermediateBuffers[bufferRef].ReadMode = true;
        this.LastDataAdd = DateTime.UtcNow;
        lock (IntermediateBuffersToRead)
        {
            for (int i = 0; i < INTERMEDIATE_BUFFER_COUNT; i++)
            {
                if (IntermediateBuffersToRead[i] == -1)
                {
                    IntermediateBuffersToRead[i] = bufferRef;
                    return;
                }
            }
            Debug.Fail("Have buffer but nowhere to put it???");
            Log.Error("NoteFinder buffer system encountered something that should be impossible.");
        }
    }
}

internal struct IntermediateBuffer
{
    private const int BUFFER_SIZE = 4096; // Even at 20ms 192KHz, audio data is 3840 elements long
    public short[] Buffer;
    public uint DataCount;
    public bool ReadMode;

    public IntermediateBuffer()
    {
        this.Buffer = new short[BUFFER_SIZE];
        this.DataCount = 0;
        this.ReadMode = false;
    }
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
