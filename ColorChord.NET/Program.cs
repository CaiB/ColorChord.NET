using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Vannatech.CoreAudio.Constants;
using Vannatech.CoreAudio.Externals;
using Vannatech.CoreAudio.Interfaces;
using Vannatech.CoreAudio.Enumerations;
using System.Threading;
using System.Linq;

namespace ColorChord.NET
{
    class Program
    {
        private const CLSCTX CLSCTX_ALL = CLSCTX.CLSCTX_INPROC_SERVER | CLSCTX.CLSCTX_INPROC_HANDLER | CLSCTX.CLSCTX_LOCAL_SERVER | CLSCTX.CLSCTX_REMOTE_SERVER;
        private const ulong BufferLength = 5 * 10000; // 5ms interval

        private static float[] AudioBuffer = new float[8096];
        private static int AudioBufferHead = 0;

        private static bool KeepGoing = true;
        private static bool StreamReady = false;

        static void Main(string[] args)
        {
            Thread AudioThread = new Thread(new ThreadStart(AudioTask));
            AudioThread.Start();

            while (!StreamReady) { Thread.Sleep(10); }

            while (Console.KeyAvailable) { Console.ReadKey(); } // Clear any previous presses.
            Console.WriteLine("Press any key to stop.");
            while (!Console.KeyAvailable)
            {
                Console.WriteLine(AudioBufferHead + ":" + AudioBuffer[AudioBufferHead]);
                float[] NoteInfo = ColorChord.RunNoteFinder(AudioBuffer, AudioBufferHead, AudioBuffer.Length);
                Console.WriteLine(string.Join(", ", NoteInfo.Select(x => x.ToString()).ToArray()));
                Thread.Sleep(100);
            }
            KeepGoing = false;
        }

        private static void AudioTask()
        {
            int ErrorCode;
            Type DeviceEnumeratorType = Type.GetTypeFromCLSID(new Guid(ComCLSIDs.MMDeviceEnumeratorCLSID));
            IMMDeviceEnumerator DeviceEnumerator = (IMMDeviceEnumerator)Activator.CreateInstance(DeviceEnumeratorType);

            ErrorCode = DeviceEnumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eConsole, out IMMDevice Device);
            Marshal.ThrowExceptionForHR(ErrorCode);

            ErrorCode = Device.Activate(new Guid(ComIIDs.IAudioClientIID), (uint)CLSCTX_ALL, IntPtr.Zero, out object ClientObj);
            Marshal.ThrowExceptionForHR(ErrorCode);
            IAudioClient Client = (IAudioClient)ClientObj;

            ErrorCode = Client.GetMixFormat(out IntPtr MixFormatPtr);
            AudioTools.WAVEFORMATEX MixFormat = AudioTools.FormatFromPointer(MixFormatPtr);
            Marshal.ThrowExceptionForHR(ErrorCode);

            Console.WriteLine("Audio format: ");
            Console.WriteLine("  Channels: " + MixFormat.nChannels);
            Console.WriteLine("  Sample rate: " + MixFormat.nSamplesPerSec);
            Console.WriteLine("  Bits per sample: " + MixFormat.wBitsPerSample);

            ColorChord.Init((int)MixFormat.nSamplesPerSec);

            ErrorCode = Client.Initialize(AUDCLNT_SHAREMODE.AUDCLNT_SHAREMODE_SHARED, AUDCLNT_STREAMFLAGS_XXX.AUDCLNT_STREAMFLAGS_LOOPBACK, BufferLength, 0, MixFormatPtr);
            Marshal.ThrowExceptionForHR(ErrorCode);

            ErrorCode = Client.GetBufferSize(out uint BufferFrameCount);
            Marshal.ThrowExceptionForHR(ErrorCode);

            ErrorCode = Client.GetService(new Guid(ComIIDs.IAudioCaptureClientIID), out object CaptureClientObj);
            Marshal.ThrowExceptionForHR(ErrorCode);
            IAudioCaptureClient CaptureClient = (IAudioCaptureClient)CaptureClientObj;

            ulong ActualBufferDuration = (ulong)((double)BufferLength * BufferFrameCount / MixFormat.nSamplesPerSec);

            ErrorCode = Client.Start();
            Marshal.ThrowExceptionForHR(ErrorCode);
            StreamReady = true;

            while (KeepGoing)
            {
                Thread.Sleep((int)(ActualBufferDuration / (BufferLength / 1000) / 2));

                ErrorCode = CaptureClient.GetNextPacketSize(out uint PacketLength);
                Marshal.ThrowExceptionForHR(ErrorCode);

                while (PacketLength != 0)
                {
                    ErrorCode = CaptureClient.GetBuffer(out IntPtr DataArray, out uint NumFramesAvail, out AUDCLNT_BUFFERFLAGS BufferStatus, out ulong DevicePosition, out ulong CounterPosition);
                    Marshal.ThrowExceptionForHR(ErrorCode);

                    if (BufferStatus.HasFlag(AUDCLNT_BUFFERFLAGS.AUDCLNT_BUFFERFLAGS_SILENT))
                    {
                        //Console.WriteLine("Silence.");
                        AudioBuffer[AudioBufferHead] = 0;
                        AudioBufferHead = (AudioBufferHead + 1) % AudioBuffer.Length;
                    }
                    else
                    {
                        byte[] AudioData = new byte[NumFramesAvail];
                        Marshal.Copy(DataArray, AudioData, 0, (int)NumFramesAvail);

                        for (int i = 0; i < (AudioData.Length / sizeof(float)); i++)
                        {
                            float Sample = BitConverter.ToSingle(AudioData, i * sizeof(float));
                            AudioBuffer[AudioBufferHead] = Sample;
                            AudioBufferHead = (AudioBufferHead + 1) % AudioBuffer.Length;
                        }
                        //Console.WriteLine("Data! PacketLen: " + BufferFrameCount + " ChunkLen: " + AudioData.Length + ", Sample: " + AudioBuffer[Math.Max(AudioBufferHead - 1, 0)]);
                    }

                    ErrorCode = CaptureClient.ReleaseBuffer(NumFramesAvail);
                    Marshal.ThrowExceptionForHR(ErrorCode);

                    ErrorCode = CaptureClient.GetNextPacketSize(out PacketLength);
                    Marshal.ThrowExceptionForHR(ErrorCode);
                }
                //Console.WriteLine("Cycle end.");
            }
            ErrorCode = Client.Stop();
            Marshal.ThrowExceptionForHR(ErrorCode);
        }

        public static string BytesToNiceString(byte[] Data, int MaxLen = -1)
        {
            if (Data == null || Data.Length == 0) { return string.Empty; }
            if (MaxLen == -1 || MaxLen > Data.Length) { MaxLen = Data.Length; }
            StringBuilder Output = new StringBuilder();
            for (int i = 0; i < MaxLen; i++)
            {
                Output.Append(Data[i].ToString("X2"));
                Output.Append(' ');
            }
            Output.Remove(Output.Length - 1, 1);
            return Output.ToString();
        }
    }
}
