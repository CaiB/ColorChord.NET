using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Vannatech.CoreAudio.Constants;
using Vannatech.CoreAudio.Enumerations;
using Vannatech.CoreAudio.Externals;
using Vannatech.CoreAudio.Interfaces;

namespace ColorChord.NET.Sources
{
    public class WASAPILoopback : IAudioSource
    {
        private const CLSCTX CLSCTX_ALL = CLSCTX.CLSCTX_INPROC_SERVER | CLSCTX.CLSCTX_INPROC_HANDLER | CLSCTX.CLSCTX_LOCAL_SERVER | CLSCTX.CLSCTX_REMOTE_SERVER;
        private static readonly Guid FriendlyNamePKEY = new Guid(0xa45c254e, 0xdf1c, 0x4efd, 0x80, 0x20, 0x67, 0xd1, 0x46, 0xa8, 0x50, 0xe0);
        private const int FriendlyNamePKEY_PID = 14;

        private string DesiredDevice;
        private bool UseInput = false;
        private bool PrintDeviceInfo = false;

        private const ulong BufferLength = 50 * 10000; // 50 ms, in ticks
        private ulong SystemBufferLength;
        private ulong ActualBufferDuration;
        private int BytesPerFrame;
        
        private bool KeepGoing = true;
        private bool StreamReady = false;
        private Thread ProcessThread;

        private IMMDeviceEnumerator DeviceEnumerator;
        private IAudioClient Client;
        private IAudioCaptureClient CaptureClient;
        private AudioTools.WAVEFORMATEX MixFormat;
        AutoResetEvent AudioEvent = new AutoResetEvent(false);
        GCHandle AudioEventHandle;

        public WASAPILoopback(string name) { }

        public void ApplyConfig(Dictionary<string, object> options)
        {
            Log.Info("Reading config for WASAPILoopback.");
            this.UseInput = ConfigTools.CheckBool(options, "useInput", false, true);
            this.DesiredDevice = ConfigTools.CheckString(options, "device", "default", true);
            this.PrintDeviceInfo = ConfigTools.CheckBool(options, "printDeviceInfo", true, true);
            ConfigTools.WarnAboutRemainder(options, typeof(IAudioSource));
        }

        public void Start()
        {
            int ErrorCode;
            Type DeviceEnumeratorType = Type.GetTypeFromCLSID(new Guid(ComCLSIDs.MMDeviceEnumeratorCLSID));
            this.DeviceEnumerator = (IMMDeviceEnumerator)Activator.CreateInstance(DeviceEnumeratorType);

            if (this.PrintDeviceInfo) { PrintDeviceList(); }

            IMMDevice Device;
            bool? DeviceIsCapture = null; // null if we don't know because the device was specified by ID.

            if (this.DesiredDevice == "default")
            {
                Log.Info("Using default " + (this.UseInput ? "capture" : "render") + " device.");
                Device = GetDefaultDevice(this.UseInput);
                DeviceIsCapture = this.UseInput;
            }
            // TODO: Implement "defaultTracking"
            else
            {
                ErrorCode = this.DeviceEnumerator.GetDevice(this.DesiredDevice, out Device);
                if (IsError(ErrorCode) || Device == null)
                {
                    Log.Warn("Given audio device does not exist on this system. Using default " + (this.UseInput ? "capture" : "render") + " device instead.");
                    Device = GetDefaultDevice(this.UseInput);
                    DeviceIsCapture = this.UseInput;
                }
                else
                {
                    Log.Info("Using device specified in configuration.");
                    ErrorCode = Device.GetState(out uint DeviceState);
                    IsErrorAndOut(ErrorCode, "Failed to get status of device.");
                    if (DeviceState == DEVICE_STATE_XXX.DEVICE_STATE_DISABLED) { Log.Error("The specified device is disabled."); return; } // TODO: Make it configurable what happens in these 3 cases.
                    if (DeviceState == DEVICE_STATE_XXX.DEVICE_STATE_NOTPRESENT) { Log.Error("The specified device is known, but not currently present."); return; }
                    if (DeviceState == DEVICE_STATE_XXX.DEVICE_STATE_UNPLUGGED) { Log.Error("The specified device is unplugged."); return; }
                }
            }

            if (Device == null) { Log.Error("Audio device is not valid!"); return; }

            if (DeviceIsCapture == null) // We don't know what type of device it is, find out.
            {
                IMMEndpoint Endpoint = (IMMEndpoint)Device; // equivalent to QueryInterface()
                if (Endpoint == null) { Log.Error("Couldn't get device endpoint.");  return; }
                
                ErrorCode = Endpoint.GetDataFlow(out EDataFlow DataFlow);
                if (IsErrorAndOut(ErrorCode, "Could not determine endpoint type.")) { return; }

                DeviceIsCapture = (DataFlow == EDataFlow.eCapture);
            }

            ErrorCode = Device.Activate(new Guid(ComIIDs.IAudioClientIID), (uint)CLSCTX_ALL, IntPtr.Zero, out object ClientObj);
            if (IsErrorAndOut(ErrorCode, "Could not get audio client.")) { return; }
            this.Client = (IAudioClient)ClientObj;

            ErrorCode = this.Client.GetMixFormat(out IntPtr MixFormatPtr);
            if (IsErrorAndOut(ErrorCode, "Could not get mix format.")) { return; }
            this.MixFormat = AudioTools.FormatFromPointer(MixFormatPtr);

            ErrorCode = this.Client.GetDevicePeriod(out ulong DefaultInterval, out ulong MinimumInterval);
            if (IsErrorAndOut(ErrorCode, "Could not get device timing info.")) { return; }

            Log.Info(string.Format("Audio format is {0} channel, {1}Hz sample rate, {2}b per sample.", this.MixFormat.nChannels, this.MixFormat.nSamplesPerSec, this.MixFormat.wBitsPerSample));
            Log.Info(string.Format("Default transaction period is {0} ticks, minimum is {1} ticks.", DefaultInterval, MinimumInterval));
            this.BytesPerFrame = this.MixFormat.nChannels * (this.MixFormat.wBitsPerSample / 8);

            NoteFinder.SetSampleRate((int)this.MixFormat.nSamplesPerSec);

            uint StreamFlags;
            if (DeviceIsCapture == true) { StreamFlags = AUDCLNT_STREAMFLAGS_XXX.AUDCLNT_STREAMFLAGS_NOPERSIST | AUDCLNT_STREAMFLAGS_XXX.AUDCLNT_STREAMFLAGS_EVENTCALLBACK; }
            else if (DeviceIsCapture == false) { StreamFlags = AUDCLNT_STREAMFLAGS_XXX.AUDCLNT_STREAMFLAGS_LOOPBACK | AUDCLNT_STREAMFLAGS_XXX.AUDCLNT_STREAMFLAGS_EVENTCALLBACK; }
            else { Log.Error("Device type was not determined!"); return; }

            ErrorCode = this.Client.Initialize(AUDCLNT_SHAREMODE.AUDCLNT_SHAREMODE_SHARED, StreamFlags, MinimumInterval, MinimumInterval, MixFormatPtr);
            if (IsErrorAndOut(ErrorCode, "Could not init audio client.")) { return; }

            this.AudioEventHandle = GCHandle.Alloc(this.AudioEvent);

            ErrorCode = this.Client.SetEventHandle(this.AudioEvent.SafeWaitHandle.DangerousGetHandle()); // DANGEROUS, OH NO

            ErrorCode = this.Client.GetBufferSize(out uint BufferFrameCount);
            if (IsErrorAndOut(ErrorCode, "Could not get audio client buffer size.")) { return; }
            this.SystemBufferLength = BufferFrameCount;

            ErrorCode = this.Client.GetService(new Guid(ComIIDs.IAudioCaptureClientIID), out object CaptureClientObj);
            if (IsErrorAndOut(ErrorCode, "Could not get audio capture client.")) { return; }
            this.CaptureClient = (IAudioCaptureClient)CaptureClientObj;

            this.ActualBufferDuration = (ulong)((double)BufferLength * BufferFrameCount / this.MixFormat.nSamplesPerSec);

            ErrorCode = this.Client.Start();
            if (IsErrorAndOut(ErrorCode, "Could not start audio client.")) { return; }
            this.StreamReady = true;

            this.KeepGoing = true;
            this.ProcessThread = new Thread(ProcessEventAudio) { Name = "WASAPILoopbackEVT" };
            this.ProcessThread.Start();
        }

        private IMMDevice GetDefaultDevice(bool isCapture)
        {
            int ErrorCode = this.DeviceEnumerator.GetDefaultAudioEndpoint(isCapture ? EDataFlow.eCapture : EDataFlow.eRender, ERole.eMultimedia, out IMMDevice Device);
            if (IsErrorAndOut(ErrorCode, "Failed to get default device.")) { return null; }
            return Device;
        }

        private void PrintDeviceList()
        {
            PrintDeviceList(EDataFlow.eRender);
            PrintDeviceList(EDataFlow.eCapture);
        }

        private void PrintDeviceList(EDataFlow dataFlow)
        {
            Log.Info((dataFlow == EDataFlow.eCapture ? "Capture" : "Render") + " Devices:");
            int ErrorCode = this.DeviceEnumerator.EnumAudioEndpoints(dataFlow, DEVICE_STATE_XXX.DEVICE_STATE_ACTIVE, out IMMDeviceCollection Devices);
            if (IsErrorAndOut(ErrorCode, "Failed to get audio endpoints.")) { return; }

            ErrorCode = Devices.GetCount(out uint DeviceCount);
            if (IsErrorAndOut(ErrorCode, "Failed to get audio endpoint count.")) { return; }

            for(uint DeviceIndex = 0; DeviceIndex < DeviceCount; DeviceIndex++)
            {
                ErrorCode = Devices.Item(DeviceIndex, out IMMDevice Device);
                if (IsErrorAndOut(ErrorCode, "Failed to get audio device at " + DeviceIndex)) { continue; }

                ErrorCode = Device.GetId(out string DeviceID);
                if (IsErrorAndOut(ErrorCode, "Failed to get device ID at " + DeviceIndex)) { continue; }

                ErrorCode = Device.OpenPropertyStore(STGM.STGM_READ, out IPropertyStore Properties);
                if (IsErrorAndOut(ErrorCode, "Failed to get device properties at " + DeviceIndex)) { continue; }

                string DeviceFriendlyName = "[Name Retrieval Failed]";
                ErrorCode = Properties.GetCount(out uint PropertyCount);
                if (IsErrorAndOut(ErrorCode, "Failed to get device property count at " + DeviceIndex)) { continue; }

                for (uint PropIndex = 0; PropIndex < PropertyCount; PropIndex++)
                {
                    ErrorCode = Properties.GetAt(PropIndex, out PROPERTYKEY Property);
                    if (IsErrorAndOut(ErrorCode, "Failed to get device property at " + PropIndex)) { continue; }

                    if (Property.fmtid == FriendlyNamePKEY && Property.pid == FriendlyNamePKEY_PID)
                    {
                        ErrorCode = Properties.GetValue(ref Property, out PROPVARIANT Variant);
                        if (IsErrorAndOut(ErrorCode, "Failed to get device friendly name value.")) { continue; }

                        DeviceFriendlyName = Marshal.PtrToStringUni(Variant.Data.AsStringPtr);
                        break;
                    }
                }

                Log.Info(string.Format("[{0}]: \"{1}\" = \"{2}\"", DeviceIndex, DeviceFriendlyName, DeviceID));
            }
        }

        public void Stop()
        {
            this.KeepGoing = false;
            this.ProcessThread.Join();
        }


        private void ProcessEventAudio()
        {
            int ErrorCode;
            while (this.KeepGoing)
            {
                this.AudioEvent.WaitOne();

                ErrorCode = this.CaptureClient.GetNextPacketSize(out uint PacketLength);
                if (IsErrorAndOut(ErrorCode, "Failed to get audio packet size.")) { continue; }

                ErrorCode = this.CaptureClient.GetBuffer(out IntPtr DataBuffer, out uint FramesAvailable, out AUDCLNT_BUFFERFLAGS BufferStatus, out ulong DevicePosition, out ulong CounterPosition);
                if (IsErrorAndOut(ErrorCode, "Failed to get audio buffer.")) { continue; }

                if (BufferStatus.HasFlag(AUDCLNT_BUFFERFLAGS.AUDCLNT_BUFFERFLAGS_SILENT))
                {
                    // TODO: Clear the buffer, as the device is not currently playing audio.
                }
                else
                {
                    byte[] AudioData = new byte[FramesAvailable * this.BytesPerFrame];
                    Marshal.Copy(DataBuffer, AudioData, 0, (int)(FramesAvailable * this.BytesPerFrame));
                    for (int Frame = 0; Frame < FramesAvailable; Frame++)
                    {
                        float Sample = 0;
                        // TODO: Make multi-channel downmixing toggleable, maybe some stereo visualizations?
                        for (ushort Chn = 0; Chn < this.MixFormat.nChannels; Chn++) { Sample += BitConverter.ToSingle(AudioData, (Frame * this.BytesPerFrame) + ((this.MixFormat.wBitsPerSample / 8) * Chn)); }
                        NoteFinder.AudioBuffer[NoteFinder.AudioBufferHeadWrite] = Sample / this.MixFormat.nChannels; // Use the average of the channels.
                        NoteFinder.AudioBufferHeadWrite = (NoteFinder.AudioBufferHeadWrite + 1) % NoteFinder.AudioBuffer.Length;
                    }
                    NoteFinder.LastDataAdd = DateTime.UtcNow;
                }

                ErrorCode = this.CaptureClient.ReleaseBuffer(FramesAvailable);
                if (IsErrorAndOut(ErrorCode, "Failed to release audio buffer.")) { continue; }
            }

            ErrorCode = this.Client.Stop();
            IsErrorAndOut(ErrorCode, "Failed to stop audio client.");

            this.StreamReady = false;
        }

        private void ProcessAudio()
        {
            int ErrorCode;

            while (this.KeepGoing)
            {
                Thread.Sleep((int)(this.ActualBufferDuration / (BufferLength / 1000) / 2));

                ErrorCode = this.CaptureClient.GetNextPacketSize(out uint PacketLength);
                if (IsErrorAndOut(ErrorCode, "Failed to get audio packet size.")) { continue; } // TODO: Recover from this.

                while (PacketLength != 0)
                {
                    ErrorCode = this.CaptureClient.GetBuffer(out IntPtr DataArray, out uint NumFramesAvail, out AUDCLNT_BUFFERFLAGS BufferStatus, out ulong DevicePosition, out ulong CounterPosition);
                    if (IsErrorAndOut(ErrorCode, "Failed to get audio buffer.")) { continue; } // TODO: Recover from this.

                    if (BufferStatus.HasFlag(AUDCLNT_BUFFERFLAGS.AUDCLNT_BUFFERFLAGS_SILENT))
                    {
                        NoteFinder.AudioBuffer[NoteFinder.AudioBufferHeadWrite] = 0;
                        NoteFinder.AudioBufferHeadWrite = (NoteFinder.AudioBufferHeadWrite + 1) % NoteFinder.AudioBuffer.Length;
                    }
                    else
                    {
                        byte[] AudioData = new byte[NumFramesAvail * this.BytesPerFrame];
                        Marshal.Copy(DataArray, AudioData, 0, (int)(NumFramesAvail * this.BytesPerFrame));

                        for (int i = 0; i < NumFramesAvail; i++)
                        {
                            float Sample = 0;
                            // TODO: Make multi-channel downmixing toggleable, maybe some stereo visualizations?
                            for (int c = 0; c < this.MixFormat.nChannels; c++) { Sample += BitConverter.ToSingle(AudioData, (i * this.BytesPerFrame) + ((this.MixFormat.wBitsPerSample / 8) * c)); }
                            NoteFinder.AudioBuffer[NoteFinder.AudioBufferHeadWrite] = Sample / this.MixFormat.nChannels; // Use the average of the channels.
                            NoteFinder.AudioBufferHeadWrite = (NoteFinder.AudioBufferHeadWrite + 1) % NoteFinder.AudioBuffer.Length;
                        }
                        NoteFinder.LastDataAdd = DateTime.UtcNow;
                    }

                    ErrorCode = this.CaptureClient.ReleaseBuffer(NumFramesAvail);
                    Marshal.ThrowExceptionForHR(ErrorCode);

                    ErrorCode = this.CaptureClient.GetNextPacketSize(out PacketLength);
                    Marshal.ThrowExceptionForHR(ErrorCode);
                }
                //Console.WriteLine("Got audio data, head now at position " + NoteFinder.AudioBufferHead);
            }

            ErrorCode = this.Client.Stop();
            IsErrorAndOut(ErrorCode, "Failed to stop audio client.");

            this.StreamReady = false;
        }

        private static bool IsError(int hresult) => hresult < 0;

        private static bool IsErrorAndOut(int hresult, string output)
        {
            bool Error = IsError(hresult);
            if (Error) { Log.Error(output + " HRESULT: 0x" + hresult.ToString("X8")); }
            return Error;
        }

    }
}
