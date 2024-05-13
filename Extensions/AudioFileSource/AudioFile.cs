using ColorChord.NET.API;
using ColorChord.NET.API.NoteFinder;
using ColorChord.NET.API.Sources;
using ColorChord.NET.API.Utility;
using NAudio.Wave;
using System;

namespace ColorChord.NET.Extensions.AudioFileSource;

/// <summary> An audio source which reads supported audio file formats as if that audio was playing on the system, but at the maximum speed supported by the system. </summary>
/// <remarks> Uses NAudio for reading, see their documentation for supported file formats. </remarks>
public class AudioFile : IAudioSource
{
    internal static AudioFile? Instance { get; private set; }

    /// <summary> The queue of audio files which remain to be read. Use <see cref="AddToQueue(string, bool)"/> to add items to the queue. </summary>
    /// <remarks> Items are removed just before they begin being read. </remarks>
    public List<string> FilesToRead { get; private init; }

    /// <summary> The path of the audio file currently being read, as provided. <see cref="null"/> when no file is currently being read. </summary>
    public string? CurrentFile { get; private set; }

    /// <summary> Called synchronously just before reading of an audio file begins. </summary>
    public event EventHandler<string>? FileChanged;

    private Thread? ReadingThread;
    private bool KeepGoing = false;

    public AudioFile(string name, Dictionary<string, object> config)
    {
        const string CFG_FILE_NAME = "InputFiles";
        if (!config.TryGetValue(CFG_FILE_NAME, out object? FileNameObj)) { throw new Exception($"{CFG_FILE_NAME} is required for {nameof(AudioFile)}"); }

        if (FileNameObj is string[] FileNameArr) { this.FilesToRead = new(FileNameArr); }
        else if (FileNameObj is string FileName) { this.FilesToRead = new(1) { FileName }; }
        else { throw new Exception($"{CFG_FILE_NAME} is not valid, must be a string or string array of file names to read."); }

        Instance = this;
    }

    public void Start() { }

    public void StartReading()
    {
        if (this.KeepGoing) { Log.Error($"<{nameof(AudioFile)}> tried to begin reading files when reading was already in progress."); return; }
        this.KeepGoing = true;
        this.ReadingThread = new(ReadOnThread) { Name = $"Source <{nameof(AudioFile)}>"};
        this.ReadingThread.Start();
    }

    /// <summary> Adds a music file to the queue for reading. Optionally begins reading queued file(s). </summary>
    /// <param name="fileName"> The relative or absolute path of the file to add to the queue </param>
    /// <param name="start"> Whether to begin reading audio files if not already in progress </param>
    public void AddToQueue(string fileName, bool start = true)
    {
        this.FilesToRead.Add(fileName);
        if (start && !this.KeepGoing) { StartReading(); }
    }

    private void ReadOnThread()
    {
        while (this.KeepGoing && this.FilesToRead.Any())
        {
            string NewFile = this.FilesToRead[0];
            this.FilesToRead.RemoveAt(0);
            if (!File.Exists(NewFile)) { Log.Error($"Source <{nameof(AudioFile)}> could not find the file \"{NewFile}\", it will be skipped."); continue; }
            this.CurrentFile = NewFile;
            this.FileChanged?.Invoke(this, this.CurrentFile);

            AudioFileReader InputStream = new(NewFile);
            int Channels = InputStream.WaveFormat.Channels;
            float[] FloatBuffer = Array.Empty<float>();
            while (this.KeepGoing && InputStream.HasData(sizeof(float)))
            {
                if (!NoteFinderCommon.IsBufferAvailableToWrite()) { Thread.Sleep(10); continue; }
                short[]? Buffer = NoteFinderCommon.GetBufferToWrite(out int NFBufferID);
                if (Buffer == null) { Thread.Sleep(10); continue; }
                if (Buffer.Length * Channels > FloatBuffer.Length) { FloatBuffer = new float[Buffer.Length * Channels]; }

                int SamplesRead = InputStream.Read(FloatBuffer, 0, FloatBuffer.Length);
                if (SamplesRead == 0) { break; }
                uint Frames = (uint)(SamplesRead / Channels);
                SampleConverter.FloatToShortMixdown(Channels, Frames, FloatBuffer, Buffer);

                NoteFinderCommon.FinishBufferWrite(NFBufferID, Frames);
                NoteFinderCommon.LastDataAdd = DateTime.UtcNow;
                NoteFinderCommon.InputDataEvent.Set();
            }
            Log.Info("Audio file finished processing.");
            InputStream.Dispose();
        }
        this.KeepGoing = false;
        this.ReadingThread = null;
    }

    public void Stop()
    {
        this.KeepGoing = false;
        this.ReadingThread?.Join();
    }
}
