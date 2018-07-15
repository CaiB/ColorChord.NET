using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Vannatech.CoreAudio.Constants;
using Vannatech.CoreAudio.Externals;
using Vannatech.CoreAudio.Interfaces;
using Vannatech.CoreAudio.Enumerations;
using System.Threading;

namespace ColorChord.NET
{
    class Program
    {
        private const CLSCTX CLSCTX_ALL = CLSCTX.CLSCTX_INPROC_SERVER | CLSCTX.CLSCTX_INPROC_HANDLER | CLSCTX.CLSCTX_LOCAL_SERVER | CLSCTX.CLSCTX_REMOTE_SERVER;
        private const ulong BufferLength = 10000000;

        static void Main(string[] args)
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

            while (Console.KeyAvailable) { Console.ReadKey(); } // Clear any previous presses.
            Console.WriteLine("Press any key to stop.");
            while (!Console.KeyAvailable)
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
                        Console.WriteLine("Silence.");
                    }
                    else
                    {
                        byte[] AudioData = new byte[NumFramesAvail];
                        Marshal.Copy(DataArray, AudioData, 0, (int)NumFramesAvail);

                        Console.WriteLine("Data! Len: " + AudioData.Length + ", Sample: " + BytesToNiceString(AudioData, 10));
                    }

                    ErrorCode = CaptureClient.ReleaseBuffer(NumFramesAvail);
                    Marshal.ThrowExceptionForHR(ErrorCode);

                    ErrorCode = CaptureClient.GetNextPacketSize(out PacketLength);
                    Marshal.ThrowExceptionForHR(ErrorCode);
                }
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
