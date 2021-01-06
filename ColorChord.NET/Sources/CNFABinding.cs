using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ColorChord.NET.Sources
{
    public class CNFABinding : IAudioSource
    {
        private CNFAConfig Driver;
        private IntPtr DriverPtr;

        private CNFACallback Callback;
        private GCHandle CallbackHandle;

        private string DriverMode;
        private int SuggestedSampleRate;
        private int SuggestedChannelCount;
        private int SuggestedBufferSize;
        private string DeviceRecord, DevicePlay;

        public CNFABinding(string name) { }

        public void ApplyConfig(Dictionary<string, object> options)
        {
            Log.Info("Reading config for CFNABinding.");
            this.DriverMode = ConfigTools.CheckString(options, "Driver", "AUTO", true).ToUpper();
            this.SuggestedSampleRate = ConfigTools.CheckInt(options, "SampleRate", 8000, 384000, 48000, true);
            this.SuggestedChannelCount = ConfigTools.CheckInt(options, "ChannelCount", 1, 20, 2, true);
            this.SuggestedBufferSize = ConfigTools.CheckInt(options, "BufferSize", 1, 10000, 480, true);
            this.DeviceRecord = ConfigTools.CheckString(options, "Device", "default", true);
            this.DevicePlay = ConfigTools.CheckString(options, "DeviceOutput", "default", true); // This isn't actually used, as no sounds are played.
            ConfigTools.WarnAboutRemainder(options, typeof(IAudioSource));
        }
        
        public void Start()
        {
            this.Callback = SoundCallback;
            this.CallbackHandle = GCHandle.Alloc(this.Callback);
            this.DriverPtr = Initialize(this.DriverMode == "AUTO" ? null : this.DriverMode, "ColorChord.NET", Marshal.GetFunctionPointerForDelegate(this.Callback), this.SuggestedSampleRate, this.SuggestedSampleRate, this.SuggestedChannelCount, this.SuggestedChannelCount, this.SuggestedBufferSize, this.DevicePlay, this.DeviceRecord, IntPtr.Zero);
            this.Driver = Marshal.PtrToStructure<CNFAConfig>(this.DriverPtr);
            BaseNoteFinder.SetSampleRate(this.Driver.SampleRateRecord);
        }

        public void Stop()
        {
            this.Driver.Close(this.DriverPtr);
            this.CallbackHandle.Free();
            this.Callback = null;
        }

        /// <summary> Called by CNFA when audio data is ready for sending/receiving. </summary>
        /// <param name="driver"> The <see cref="CNFAConfig"/> for the driver. </param>
        /// <param name="input"> The data coming in from the system. </param>
        /// <param name="output"> Place data here to send to the system. </param>
        /// <param name="framesIn"> How many frames of input data are available. </param>
        /// <param name="framesOut"> How many frames of space are available for output data. </param>
        private void SoundCallback(IntPtr driver, IntPtr input, IntPtr output, int framesIn, int framesOut)
        {
            if (input == IntPtr.Zero) { return; } // Needed for ALSA?
            //Console.WriteLine("CALLBACK with " + framesIn + " frames of input, and " + framesOut + " frames of space for output.");
            short[] AudioData = new short[framesIn * this.Driver.ChannelCountRecord];
            Marshal.Copy(input, AudioData, 0, (framesIn * this.Driver.ChannelCountRecord));
            for (int Frame = 0; Frame < framesIn; Frame++)
            {
                float Sample = 0;
                for (ushort Chn = 0; Chn < this.Driver.ChannelCountRecord; Chn++) { Sample += AudioData[(Frame * this.Driver.ChannelCountRecord) + Chn] / 32767.5F; }
                BaseNoteFinder.AudioBuffer[BaseNoteFinder.AudioBufferHeadWrite] = Sample / this.Driver.ChannelCountRecord; // Use the average of the channels.
                BaseNoteFinder.AudioBufferHeadWrite = (BaseNoteFinder.AudioBufferHeadWrite + 1) % BaseNoteFinder.AudioBuffer.Length;
            }
            BaseNoteFinder.LastDataAdd = DateTime.UtcNow;
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
        internal static extern IntPtr Initialize(string driverName, string ourName, IntPtr callback, int requestedSampleRatePlay, int requestedSampleRateRecord, int requestedChannelsPlay, int requestedChannelRecord, int suggestedBufferSize, string outputSelect, string inputSelect, IntPtr notUsed);

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
