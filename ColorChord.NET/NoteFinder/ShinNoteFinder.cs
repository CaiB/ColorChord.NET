using ColorChord.NET.API;
using ColorChord.NET.API.NoteFinder;
using ColorChord.NET.API.Sources;
using ColorChord.NET.Config;
using ColorChord.NET.Outputs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Intrinsics;
using System.Threading;

namespace ColorChord.NET.NoteFinder;

public class ShinNoteFinder : NoteFinderCommon, ITimingSource
{
    private const int NOTE_QTY = 12;

    public override ReadOnlySpan<float> AllBinValues => ShinNoteFinderDFT.AllBinValues.AsSpan(1, ShinNoteFinderDFT.AllBinValues.Length - 2);
    public override ReadOnlySpan<float> OctaveBinValues => ShinNoteFinderDFT.OctaveBinValues;

    private Note[] P_Notes;
    public override ReadOnlySpan<Note> Notes => this.P_Notes;

    private int[] P_PersistentNoteIDs;
    public override ReadOnlySpan<int> PersistentNoteIDs => this.P_PersistentNoteIDs;

    private static Thread? ProcessThread;
    private static bool KeepGoing = true;

    private static Stopwatch CycleTimer = new();
    private static float CycleTimeTicks;
    private static uint CycleCount = 0;

    private static bool IsTimingSource = false;
    private static TimingReceiverData[] TimingReceivers = Array.Empty<TimingReceiverData>();

    public ShinNoteFinder(string name, Dictionary<string, object> config)
    {
        Configurer.Configure(typeof(ShinNoteFinderDFT), config, false);
        Configurer.Configure(this, config);
        P_Notes = new Note[NOTE_QTY];
        P_PersistentNoteIDs = new int[NOTE_QTY];
        SetupBuffers();
        ShinNoteFinderDFT.Reconfigure();
    }

    public override int NoteCount => NOTE_QTY;
    public override int BinsPerOctave => (int)ShinNoteFinderDFT.BinsPerOctave;

    public override void AdjustOutputSpeed(uint period)
    {
        if (period < ShortestPeriod) { ShortestPeriod = period; }
    }

    public override void SetSampleRate(int sampleRate) => ShinNoteFinderDFT.UpdateSampleRate((uint)sampleRate);

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
        bool MoreBuffers;
        do
        {
            short[]? Buffer = GetBufferToRead(out int NFBufferRef, out uint BufferSize, out MoreBuffers);
            if (Buffer == null) { break; }

            CycleTimer.Restart();
            ShinNoteFinderDFT.AddAudioData(Buffer, BufferSize);
            CycleTimer.Stop();

            FinishBufferRead(NFBufferRef);

            const float TIMER_IIR = 0.97F;
            if (BufferSize > 32) { CycleTimeTicks = (CycleTimeTicks * TIMER_IIR) + ((float)CycleTimer.ElapsedTicks / BufferSize * (1F - TIMER_IIR)); }
            if (++CycleCount % 500 == 0) { Log.Debug($"{nameof(ShinNoteFinder)} DFT is taking {CycleTimeTicks * 0.1F:F3}us per sample."); }

        } while (MoreBuffers);
    }

    public override void UpdateOutputs()
    {
        ShinNoteFinderDFT.CalculateOutput();
        for (int i = 0; i < AllBinValues.Length; i++)
        {
            //Vector256<float> 
        }
    }

    public override void Stop()
    {
        KeepGoing = false;
        InputDataEvent.Set();
        ProcessThread?.Join();
    }

    public void AddTimingReceiver(TimingReceiver receiver, float period)
    {
        lock (TimingReceivers)
        {
            TimingReceiverData[] OldData = TimingReceivers;
            TimingReceiverData[] NewData = new TimingReceiverData[OldData.Length + 1];
            Array.Copy(OldData, NewData, OldData.Length);
            NewData[^1] = new()
            {
                Receiver = receiver,
                OriginalPeriod = period,
                Period = (period <= 0) ? (uint)MathF.Round(-period) : (uint)MathF.Round(period * ShinNoteFinderDFT.SampleRate)
            };

            TimingReceivers = NewData;
            IsTimingSource = true;
        }
    }

    public void RemoveTimingReceiver(TimingReceiver receiver)
    {
        lock (TimingReceivers)
        {
            int Index = Array.FindIndex(TimingReceivers, x => x.Receiver == receiver);
            if (Index < 0) { return; }

            if (TimingReceivers.Length == 1) // Last receiver
            {
                TimingReceivers = Array.Empty<TimingReceiverData>();
                IsTimingSource = false;
                return;
            }

            TimingReceiverData[] OldData = TimingReceivers;
            TimingReceiverData[] NewData = new TimingReceiverData[OldData.Length - 1];
            if (Index > 0) { Array.Copy(OldData, 0, NewData, 0, Index); } // Copy items on the left of the removed item
            else if (Index < OldData.Length - 1) { Array.Copy(OldData, Index + 1, NewData, Index, OldData.Length - Index - 1); } // Copy items on the right of the removed item

            TimingReceivers = NewData;
        }
    }

    internal static void RunTimingReceivers(uint samplesProcessed)
    {
        if (!IsTimingSource) { return; }
        bool CalculatedOutput = false;
        lock (TimingReceivers)
        {
            for (int i = 0; i < TimingReceivers.Length; i++)
            {
                ref TimingReceiverData Receiver = ref TimingReceivers[i];
                Receiver.CurrentIncrement += samplesProcessed;
                if (Receiver.CurrentIncrement >= Receiver.Period)
                {
                    if (!CalculatedOutput) { ShinNoteFinderDFT.CalculateOutput(); CalculatedOutput = true; }
                    Receiver.Receiver.Invoke();
                    Receiver.CurrentIncrement -= Receiver.Period;
                    if (Receiver.Period != 0 && Receiver.CurrentIncrement > Receiver.Period * 16)
                    {
                        Log.Warn($"{nameof(ShinNoteFinder)} has timing receiver that is falling behind, the receiver {Receiver.Receiver} has a period of {Receiver.Period} samples which is too short to effectively call.");
                    }
                }
            }
        }
    }

    private struct TimingReceiverData
    {
        /// <summary> The receiver to call whenever this event occurs. </summary>
        public TimingReceiver Receiver { get; init; }

        /// <summary> The original request from the receiver. If a period in seconds was requested, the internal period in samples needs to be updated if the sample rate changes. </summary>
        public float OriginalPeriod { get; init; }

        /// <summary> The period on which to send this event, in samples. </summary>
        public uint Period { get; set; }

        /// <summary> How many samples have been processed since this callback was last dispatched. </summary>
        public uint CurrentIncrement { get; set; }
    }
}
