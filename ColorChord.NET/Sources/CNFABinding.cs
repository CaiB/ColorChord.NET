﻿using ColorChord.NET.API.Config;
using ColorChord.NET.API.NoteFinder;
using ColorChord.NET.API.Sources;
using ColorChord.NET.Config;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ColorChord.NET.Sources
{
    public class CNFABinding : IAudioSource
    {
        public string Name { get; private init; }
        private NoteFinderCommon? NoteFinder;

        private CNFAConfig Driver;
        private IntPtr DriverPtr;

        private CNFACallback Callback;
        private GCHandle CallbackHandle;

        [ConfigString("Driver", "AUTO")]
        private readonly string DriverMode = "AUTO";

        [ConfigInt("SampleRate", 8000, 384000, 48000)]
        private readonly int SuggestedSampleRate = 48000;

        [ConfigInt("ChannelCount", 1, 20, 2)]
        private readonly int SuggestedChannelCount = 2;

        [ConfigInt("BufferSize", 1, 10000, 480)]
        private readonly int SuggestedBufferSize = 480;

        [ConfigString("Device", "default")]
        private string DeviceRecord = "default";
        
        [ConfigString("DeviceOutput", "default")]
        private string DevicePlay = "default";

        public CNFABinding(string name, Dictionary<string, object> config)
        {
            this.Name = name;
            Configurer.Configure(this, config);
            this.Callback = SoundCallback;
            this.CallbackHandle = GCHandle.Alloc(this.Callback);
        }
        
        public void Start()
        {
            this.DriverPtr = Initialize(this.DriverMode.ToUpper() == "AUTO" ? null : this.DriverMode.ToUpper(), "ColorChord.NET", Marshal.GetFunctionPointerForDelegate(this.Callback), this.SuggestedSampleRate, this.SuggestedSampleRate, this.SuggestedChannelCount, this.SuggestedChannelCount, this.SuggestedBufferSize, this.DevicePlay, this.DeviceRecord, IntPtr.Zero);
            this.Driver = Marshal.PtrToStructure<CNFAConfig>(this.DriverPtr);
            this.NoteFinder?.SetSampleRate(this.Driver.SampleRateRecord);
        }

        public uint GetSampleRate() => (uint)this.Driver.SampleRateRecord;

        public void AttachNoteFinder(NoteFinderCommon noteFinder) => this.NoteFinder = noteFinder;

        public void Stop()
        {
            this.Driver.Close(this.DriverPtr);
            this.CallbackHandle.Free();
        }

        /// <summary> Called by CNFA when audio data is ready for sending/receiving. </summary>
        /// <param name="driver"> The <see cref="CNFAConfig"/> for the driver. </param>
        /// <param name="input"> The data coming in from the system. </param>
        /// <param name="output"> Place data here to send to the system. </param>
        /// <param name="framesIn"> How many frames of input data are available. </param>
        /// <param name="framesOut"> How many frames of space are available for output data. </param>
        private unsafe void SoundCallback(IntPtr driver, IntPtr input, IntPtr output, int framesIn, int framesOut)
        {
            if (this.NoteFinder == null) { return; }
            if (input == IntPtr.Zero) { return; } // Needed for ALSA?
            //Console.WriteLine("CALLBACK with " + framesIn + " frames of input, and " + framesOut + " frames of space for output.");

            short[]? ProcessedData = this.NoteFinder.GetBufferToWrite(out int NFBufferRef);
            if (ProcessedData == null) { return; }
            Debug.Assert(framesIn <= ProcessedData.Length); // TODO: Handle this properly (if needed?)

            short Channels = this.Driver.ChannelCountRecord;
            for (int Frame = 0; Frame < framesIn; Frame++)
            {
                int Sample = 0;
                for (ushort Chn = 0; Chn < Channels; Chn++) { Sample += BitConverter.ToInt16(new ReadOnlySpan<byte>(((short*)input) + ((Frame * Channels) + Chn), sizeof(short))); } // TODO: Double-check if this is correct
                ProcessedData[Frame] = (short)Sample;
            }

            this.NoteFinder.FinishBufferWrite(NFBufferRef, (uint)framesIn);
            this.NoteFinder.InputDataEvent.Set();
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct CNFAConfig
        {
            /// <summary> The function to call when you want to shut down the driver. </summary>
            internal readonly CloseFunction Close;

            /// <summary> The function to call when you want to know the driver's state. </summary>
            internal readonly StatusFunction Status;

            /// <summary> The <see cref="CNFACallback"/>. </summary>
            internal readonly IntPtr Callback;

            /// <summary> The number of channels for playback. </summary>
            internal readonly short ChannelCountPlay;

            /// <summary> The number of channels for recording. </summary>
            internal readonly short ChannelCountRecord;

            /// <summary> The sample rate for playback. </summary>
            internal readonly int SampleRatePlay;

            /// <summary> The sample rate for recording. </summary>
            internal readonly int SampleRateRecord;

            [Obsolete("Do not use")]
            internal readonly IntPtr Unused;
        }

        /// <summary> Stops the driver and cleans up resources. </summary>
        /// <param name="driver"> The <see cref="CNFAConfig"/> of the driver. </param>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void CloseFunction(IntPtr driver);

        /// <summary> Gets the current state of the sound driver. </summary>
        /// <param name="driver"> The <see cref="CNFAConfig"/> of the driver. </param>
        /// <returns> 0 for inactive, 1 for recording, 2 for playing, 3 for both. </returns>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int StatusFunction(IntPtr driver);

        [DllImport("CNFA", CallingConvention = CallingConvention.Cdecl, EntryPoint = "CNFAInit", CharSet = CharSet.Ansi)]
        internal static extern IntPtr Initialize(string? driverName, string ourName, IntPtr callback, int requestedSampleRatePlay, int requestedSampleRateRecord, int requestedChannelsPlay, int requestedChannelRecord, int suggestedBufferSize, string outputSelect, string inputSelect, IntPtr notUsed);

        /// <summary> The delegate for the function you must implement to receive sound callbacks from CNFA. </summary>
        /// <param name="driver"> The <see cref="CNFAConfig"/> for the driver. </param>
        /// <param name="inputData"> The data coming in from the system. Type <see cref="short[]"/> </param>
        /// <param name="outputData"> Place data here to send to the system. Type <see cref="short[]"/> </param>
        /// <param name="framesIn"> How many frames of input data are available. </param>
        /// <param name="framesOut"> How many frames of space are available for output data. </param>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void CNFACallback([In] IntPtr driver, [In] IntPtr inputData, [In, Out] IntPtr outputData, [In] int framesIn, [In] int framesOut);
    }
}
