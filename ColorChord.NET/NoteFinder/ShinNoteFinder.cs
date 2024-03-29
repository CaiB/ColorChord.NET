﻿using ColorChord.NET.API.NoteFinder;
using ColorChord.NET.Config;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Xml;

namespace ColorChord.NET.NoteFinder;

public class ShinNoteFinder : NoteFinderCommon
{
    private const int NOTE_QTY = 12;
    internal const int BINS_PER_OCTAVE = 24;

    private static uint SampleRate = 48000;

    private static Thread? ProcessThread;
    private static bool KeepGoing = true;

    private static int AudioBufferHeadRead = 0;

    public ShinNoteFinder(string name, Dictionary<string, object> config)
    {
        Configurer.Configure(this, config);
        ShinNoteFinderDFT.UpdateSampleRate(SampleRate);
        Notes = new Note[NOTE_QTY];
        PersistentNoteIDs = new int[NOTE_QTY];
        OctaveBinValues = new float[BINS_PER_OCTAVE];
        AllBinValues = new float[BINS_PER_OCTAVE * ShinNoteFinderDFT.OctaveCount];
        SetupBuffers();
    }

    public override int NoteCount => NOTE_QTY;
    public override int BinsPerOctave => BINS_PER_OCTAVE;

    public override void AdjustOutputSpeed(uint period)
    {
        if (period < ShortestPeriod) { ShortestPeriod = period; }
    }

    public override void SetSampleRate(int sampleRate)
    {
        
    }

    public override void Start()
    {
        KeepGoing = true;
        ProcessThread = new Thread(DoProcessing) { Name = nameof(ShinNoteFinder) };
        ProcessThread.Start();
    }

    private static void DoProcessing()
    {
        while (KeepGoing)
        {
            InputDataEvent.WaitOne();
            Cycle();
        }
    }

    private static void Cycle()
    {
        do
        {
            short[]? Buffer = GetBufferToRead(out int NFBufferRef, out uint BufferSize, out bool MoreBuffers);
            if (Buffer == null) { break; }
            ShinNoteFinderDFT.AddAudioData(Buffer, BufferSize);

            FinishBufferRead(NFBufferRef);
            if (!MoreBuffers) { break; }
        } while (true);

        ShinNoteFinderDFT.CalculateOutput();
    }

    public override void Stop()
    {
        KeepGoing = false;
        InputDataEvent.Set();
        ProcessThread?.Join();
    }
}
