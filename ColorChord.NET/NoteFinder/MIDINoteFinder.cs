﻿using ColorChord.NET.API.NoteFinder;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
/*
namespace ColorChord.NET.NoteFinder
{
    public class MIDINoteFinder : NoteFinderCommon
    {
        private const byte NOTES = 128;
        private const byte CHANNELS = 16;
        private const byte OCTAVE_NOTES = 12;

        /// <summary> The speed (in ms between runs) at which the note finder needs to run, set by the fastest visualizer. </summary>
        public uint ShortestPeriod { get; private set; } = 100;

        /// <summary> Whether to keep processing, or shut down operations.  </summary>
        private bool KeepGoing = true;

        /// <summary> The thread doing the actual note data processing. </summary>
        private Thread? ProcessThread;

        /// <summary>Maps [Channel, NoteID] => Volume</summary>
        private readonly byte[,] InputNoteStatus = new byte[CHANNELS, NOTES];

        /// <summary>Keeps track of the summed volume of all current notes.</summary>
        private uint TotalVolume = 0;

        /// <summary>Maps [Channel] => Pitch bend. Extents are +/- 0x2000</summary>
        private readonly short[] PitchBends = new short[CHANNELS];

        public readonly Note[] Notes = new Note[OCTAVE_NOTES];

        public override int NoteCount { get => OCTAVE_NOTES; }

        public override int BinsPerOctave { get => OCTAVE_NOTES; }

        public readonly int[] PersistentNoteIDs = new int[OCTAVE_NOTES];

        /// <summary> Starts the processing thread. </summary>
        public override void Start()
        {
            KeepGoing = true;
            ProcessThread = new Thread(DoProcessing);
            ProcessThread.Start();
        }

        /// <summary> Stops the processing thread. </summary>
        public override void Stop()
        {
            KeepGoing = false;
            ProcessThread?.Join();
        }

        public override void SetSampleRate(int sampleRate) { } // Does nothing, sample rate doesn't mean anything for MIDI

        /// <summary> Adjusts the note finder run interval if the newly added visualizer/output needs it to run faster, otherwise does nothing. </summary>
        /// <param name="period"> The period, in milliseconds, that you need the note finder to run at or faster than. </param>
        public override void AdjustOutputSpeed(uint period)
        {
            if (period < this.ShortestPeriod) { this.ShortestPeriod = period; }
        }

        /// <summary> Runs until <see cref="KeepGoing"/> becomes false, processing incoming audio data. </summary>
        private void DoProcessing()
        {
            Stopwatch Timer = new();
            while (KeepGoing)
            {
                Timer.Restart();
                //if (LastDataAdd.AddSeconds(5) > DateTime.UtcNow) { Cycle(); }
                Cycle();
                int WaitTime = (int)(this.ShortestPeriod - Timer.ElapsedMilliseconds);
                if (WaitTime > 0) { Thread.Sleep(WaitTime); }
            }
        }

        public void ProcessMessage(byte status, ushort data)
        {
            byte Channel = (byte)(status & 0x0F);
            byte Velocity = (byte)(data >> 8);
            byte Note = (byte)data;

            if((status & 0xF0) == 0x90) // Note on
            {
                TotalVolume -= InputNoteStatus[Channel, Note];
                InputNoteStatus[Channel, Note] = Velocity;
                TotalVolume += Velocity;
            }
            if((status & 0xF0) == 0x80) // Note off
            {
                TotalVolume -= InputNoteStatus[Channel, Note];
                InputNoteStatus[Channel, Note] = Velocity;
            }
            if((status & 0xF0) == 0xE0) // Pitch bend
            {
                short Bend = (short)(((data & 0x7F) << 7) | ((data >> 8) & 0x7F));
                if (((data >> 13) & 1) == 1) { unchecked { Bend |= (short)0xC000; } } // sign-extend if negative
                PitchBends[Channel] = Bend;
            }
        }

        private static readonly uint[,] SummedNotes = new uint[CHANNELS, OCTAVE_NOTES];

        private static void Cycle()
        {
            for (byte Channel = 0; Channel < CHANNELS; Channel++)
            {
                for (byte Note = 0; Note < 12; Note++) { SummedNotes[Channel, Note] = InputNoteStatus[Channel, Note]; }
                for (byte Note = 12; Note < NOTES; Note++) { SummedNotes[Channel, Note % 12] += InputNoteStatus[Channel, Note]; }
            }



        }

    }
}
*/