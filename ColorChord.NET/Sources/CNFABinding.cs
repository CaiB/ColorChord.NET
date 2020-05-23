using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace ColorChord.NET.Sources
{
    public class CNFABinding : IAudioSource
    {
        private CNFAConfig Driver;
        private IntPtr DriverPtr;

        public CNFABinding(string name) { }

        public void ApplyConfig(Dictionary<string, object> configSection)
        {

        }
        
        public void Start()
        {
            this.DriverPtr = Initialize("WASAPI", "ColorChord.NET", Marshal.GetFunctionPointerForDelegate<CNFACallback>(SoundCallback), 48000, 48000, 2, 2, 480, null, "defaultRender", IntPtr.Zero);
            this.Driver = Marshal.PtrToStructure<CNFAConfig>(this.DriverPtr);
        }

        public void Stop() => this.Driver.Close(this.DriverPtr);

        /// <summary> Called by CNFA when audio data is ready for sending/receiving. </summary>
        /// <param name="driver"> The <see cref="CNFAConfig"/> for the driver. </param>
        /// <param name="input"> The data coming in from the system. </param>
        /// <param name="output"> Place data here to send to the system. </param>
        /// <param name="framesIn"> How many frames of input data are available. </param>
        /// <param name="framesOut"> How many frames of space are available for output data. </param>
        private void SoundCallback(IntPtr driver, short[] input, short[] output, int framesIn, int framesOut)
        {
            Console.WriteLine("CALLBACK with " + framesIn + " frames of input, and " + framesOut + " frames of space for output.");
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

        [DllImport("CNFA4", CallingConvention = CallingConvention.Cdecl, EntryPoint = "CNFAInit", CharSet = CharSet.Ansi)]
        internal static extern IntPtr Initialize(string driverName, string ourName, IntPtr callback, int requestedSampleRatePlay, int requestedSampleRateRecord, int requestedChannelsPlay, int requestedChannelRecord, int suggestedBufferSize, string outputSelect, string inputSelect, IntPtr notUsed);

        /// <summary> The delegate for the function you must implement to receive sound callbacks from CNFA. </summary>
        /// <param name="driver"> The <see cref="CNFAConfig"/> for the driver. </param>
        /// <param name="input"> The data coming in from the system. </param>
        /// <param name="output"> Place data here to send to the system. </param>
        /// <param name="framesIn"> How many frames of input data are available. </param>
        /// <param name="framesOut"> How many frames of space are available for output data. </param>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void CNFACallback([In] IntPtr driver, [In] short[] input, [In, Out] short[] output, [In] int framesIn, [In] int framesOut);
    }
}
