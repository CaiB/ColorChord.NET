﻿using ColorChord.NET.API;
using ColorChord.NET.API.Config;
using ColorChord.NET.API.NoteFinder;
using ColorChord.NET.API.Sources;
using ColorChord.NET.API.Utility;
using ColorChord.NET.Config;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Vannatech.CoreAudio.Constants;
using Vannatech.CoreAudio.Enumerations;
using Vannatech.CoreAudio.Externals;
using Vannatech.CoreAudio.Interfaces;
using Vannatech.CoreAudio.Structures;

namespace ColorChord.NET.Sources;

public class WASAPILoopback : IAudioSource
{
    private const CLSCTX CLSCTX_ALL = CLSCTX.CLSCTX_INPROC_SERVER | CLSCTX.CLSCTX_INPROC_HANDLER | CLSCTX.CLSCTX_LOCAL_SERVER | CLSCTX.CLSCTX_REMOTE_SERVER;
    private static readonly Guid FriendlyNamePKEY = new(0xa45c254e, 0xdf1c, 0x4efd, 0x80, 0x20, 0x67, 0xd1, 0x46, 0xa8, 0x50, 0xe0);
    private const int FriendlyNamePKEY_PID = 14;
    private const bool FORCE_DEFAULT_FORMAT = false;

    public string Name { get; private init; }
    private NoteFinderCommon? NoteFinder;

    [ConfigString("Device", "default")]
    private readonly string DesiredDevice = "default";

    [ConfigBool("ShowDeviceInfo", true)]
    private readonly bool PrintDeviceInfo = true;

    private const ulong BufferLength = 50 * 10000; // 50 ms, in ticks
    private ulong SystemBufferLength;
    private ulong ActualBufferDuration;
    private int BytesPerFrame;
    
    private bool KeepGoing = true;
    private bool StreamReady = false;
    private Thread? ProcessThread;

    private IMMDeviceEnumerator? DeviceEnumerator;
    private IAudioClient3? Client;
    private IAudioCaptureClient? CaptureClient;
    private WaveFormatExtensible MixFormat;
    private bool FormatIsPCM;
    private readonly AutoResetEvent AudioEvent = new(false);
    private GCHandle AudioEventHandle;

    public WASAPILoopback(string name, Dictionary<string, object> config)
    {
        this.Name = name;
        Configurer.Configure(this, config);
    }

    public void AttachNoteFinder(NoteFinderCommon noteFinder) => this.NoteFinder = noteFinder;

    public void Start()
    {
        if (!OperatingSystem.IsWindows()) { throw new InvalidOperationException($"{nameof(WASAPILoopback)} is only supported on Windows. Use another audio source type, such as {nameof(CNFABinding)}, on other platforms."); }
        int ErrorCode;
        Type? DeviceEnumeratorType = Type.GetTypeFromCLSID(new Guid(ComCLSIDs.MMDeviceEnumeratorCLSID));
        if (DeviceEnumeratorType == null) { Log.Error("Couldn't get device enumerator type info."); return; }
        this.DeviceEnumerator = (IMMDeviceEnumerator?)Activator.CreateInstance(DeviceEnumeratorType);
        if (this.DeviceEnumerator == null) { Log.Error("Couldn't create device enumerator."); return; }

        if (this.PrintDeviceInfo) { PrintDeviceList(); }

        // Selecting audio device
        IMMDevice? Device;
        bool? DeviceIsCapture = null; // null if we don't know because the device was specified by ID.

        if (this.DesiredDevice == "default")
        {
            Log.Info("Using default render device.");
            Device = GetDefaultDevice(this.DeviceEnumerator, false);
            DeviceIsCapture = false;
        }
        else if (this.DesiredDevice == "defaultInput")
        {
            Log.Info("Using default capture device.");
            Device = GetDefaultDevice(this.DeviceEnumerator, true);
            DeviceIsCapture = true;
        }
        // TODO: Implement "defaultTracking"
        else
        {
            ErrorCode = this.DeviceEnumerator.GetDevice(this.DesiredDevice, out Device);
            if (IsError(ErrorCode) || Device == null)
            {
                Log.Warn("Given audio device does not exist on this system. Using default render device instead.");
                Device = GetDefaultDevice(this.DeviceEnumerator, false);
                DeviceIsCapture = false;
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

        // Get a client on the selected device, and query the system for current settings
        ErrorCode = Device.Activate(new Guid(AdditionalComIIDs.IAudioClient3IID), (uint)CLSCTX_ALL, IntPtr.Zero, out object ClientObj);
        if (IsErrorAndOut(ErrorCode, "Could not get audio client.")) { return; }
        this.Client = (IAudioClient3)ClientObj;

        ErrorCode = this.Client.GetMixFormat(out IntPtr MixFormatPtr);
        if (IsErrorAndOut(ErrorCode, "Could not get mix format.")) { return; }
        WaveFormatExtensible? FormatEx = WaveFormatExtensible.FromPointer(MixFormatPtr, out WaveFormatEx? Format);
        if (FormatEx != null) { this.MixFormat = (WaveFormatExtensible)FormatEx; }
        else if (Format != null)
        {
            Log.Warn("WASAPI returned a non-EXTENSIBLE mix format for some reason. Please report this to the ColorChord.NET developer.");
            WaveFormatEx BasicFormat = (WaveFormatEx)Format;
            
        }
        else { Log.Error("Mix format was null"); return; }

        Log.Info($"Default audio format is {this.MixFormat.Format} {WaveFormatGUIDs.GetNameFromGUID(this.MixFormat.SubFormat)}, {this.MixFormat.ChannelCount} channel, {this.MixFormat.SampleRate}Hz sample rate, {this.MixFormat.BitsPerSample}b per sample.");
        this.FormatIsPCM = (this.MixFormat.IsExtensible() && this.MixFormat.SubFormat == WaveFormatGUIDs.PCM) || (!this.MixFormat.IsExtensible() && this.MixFormat.Format == WaveFormatBasic.PCM);

        ErrorCode = this.Client.GetDevicePeriod(out ulong DefaultInterval, out ulong MinimumInterval);
        if (IsErrorAndOut(ErrorCode, "Could not get device timing info.")) { return; }

        ErrorCode = this.Client.GetCurrentSharedModeEnginePeriod(out IntPtr MixFormatPtr2, out uint FramesPerBatch); // TODO: Automatically scale this based on the framerate of the outputs
        if (IsErrorAndOut(ErrorCode, "Could not get current engine periodicity info.")) { return; }

        // Check if we can get PCM data directly (default is usually float)
        WaveFormatExtensible RequestedFormat = new()
        {
            Format = WaveFormatBasic.Extensible,
            ChannelCount = this.MixFormat.ChannelCount,
            SampleRate = this.MixFormat.SampleRate,
            AvgBytesPerSec = this.MixFormat.ChannelCount * this.MixFormat.SampleRate * 2,
            BlockAlignment = (ushort)(this.MixFormat.ChannelCount * 2),
            BitsPerSample = 16,
            AppendedSize = 22,
            Samples = 16,
            ChannelMask = this.MixFormat.ChannelMask,
            SubFormat = WaveFormatGUIDs.PCM
        };

        IntPtr RequestedFormatPtr = Marshal.AllocCoTaskMem(Marshal.SizeOf(RequestedFormat));
        Marshal.StructureToPtr(RequestedFormat, RequestedFormatPtr, false);
        int IsRequestSupportedRaw = this.Client.IsFormatSupported(AUDCLNT_SHAREMODE.AUDCLNT_SHAREMODE_SHARED, RequestedFormatPtr, out IntPtr ResponseFormatPtr);
        bool IsRequestSupported = !FORCE_DEFAULT_FORMAT && IsRequestSupportedRaw == 0;
        if (!IsRequestSupported)
        {
            Marshal.FreeCoTaskMem(RequestedFormatPtr);
            RequestedFormatPtr = IntPtr.Zero;
            WaveFormatEx? ResponseFormat = WaveFormatEx.FromPointer(ResponseFormatPtr);

#pragma warning disable CS0162 // Unreachable code detected
            if (FORCE_DEFAULT_FORMAT) { Log.Warn("Requesting more optimal format from WASAPI was disabled at compile time."); }
            else { Log.Warn($"WASAPI responded with 0x{IsRequestSupportedRaw:X8} when PCM was requested, falling back to using its format instead."); }
#pragma warning restore CS0162 // Unreachable code detected

            this.FormatIsPCM = (this.MixFormat.IsExtensible() && this.MixFormat.SubFormat == WaveFormatGUIDs.PCM) || (!this.MixFormat.IsExtensible() && this.MixFormat.Format == WaveFormatBasic.PCM);
        }
        else
        {
            this.MixFormat = RequestedFormat;
            this.FormatIsPCM = true;
        }

        ErrorCode = this.Client.GetSharedModeEnginePeriod(IsRequestSupported ? RequestedFormatPtr : MixFormatPtr, out uint DefaultPeriodFrames, out uint FundamentalPeriodFrames, out uint MinimumPeriodFrames, out uint MaximumPeriodFrames);
        if (IsErrorAndOut(ErrorCode, "Could not get detailed engine periodicity info.")) { return; }
        Log.Debug($"WASAPI engine periodicities: default = {DefaultPeriodFrames}, fundamental = {FundamentalPeriodFrames}, min = {MinimumPeriodFrames}, max = {MaximumPeriodFrames} frames.");

        Log.Info($"Chosen audio format is {this.MixFormat.Format} {WaveFormatGUIDs.GetNameFromGUID(this.MixFormat.SubFormat)}, {this.MixFormat.ChannelCount} channel, {this.MixFormat.SampleRate}Hz sample rate, {this.MixFormat.BitsPerSample}b per sample.");
        Log.Info($"Default transaction period is {DefaultInterval} ticks, minimum is {MinimumInterval} ticks. Current mode is {FramesPerBatch} frames per dispatch.");
        this.BytesPerFrame = this.MixFormat.ChannelCount * (this.MixFormat.BitsPerSample / 8);

        this.NoteFinder?.SetSampleRate((int)this.MixFormat.SampleRate);

        // Set up the stream
        uint StreamFlags;
        if (DeviceIsCapture == true) { StreamFlags = AUDCLNT_STREAMFLAGS_XXX.AUDCLNT_STREAMFLAGS_NOPERSIST | AUDCLNT_STREAMFLAGS_XXX.AUDCLNT_STREAMFLAGS_EVENTCALLBACK; }
        else if (DeviceIsCapture == false) { StreamFlags = AUDCLNT_STREAMFLAGS_XXX.AUDCLNT_STREAMFLAGS_LOOPBACK | AUDCLNT_STREAMFLAGS_XXX.AUDCLNT_STREAMFLAGS_EVENTCALLBACK; }
        else { Log.Error("Device type was not determined!"); return; }

        ErrorCode = this.Client.Initialize(AUDCLNT_SHAREMODE.AUDCLNT_SHAREMODE_SHARED, StreamFlags, MinimumInterval, MinimumInterval, IsRequestSupported ? RequestedFormatPtr : MixFormatPtr);
        if (IsErrorAndOut(ErrorCode, "Could not init audio client.")) { return; }

        this.AudioEventHandle = GCHandle.Alloc(this.AudioEvent);

        ErrorCode = this.Client.SetEventHandle(this.AudioEvent.SafeWaitHandle.DangerousGetHandle()); // DANGEROUS, OH NO

        ErrorCode = this.Client.GetBufferSize(out uint BufferFrameCount);
        if (IsErrorAndOut(ErrorCode, "Could not get audio client buffer size.")) { return; }
        this.SystemBufferLength = BufferFrameCount;

        ErrorCode = this.Client.GetService(new Guid(ComIIDs.IAudioCaptureClientIID), out object CaptureClientObj);
        if (IsErrorAndOut(ErrorCode, "Could not get audio capture client.")) { return; }
        this.CaptureClient = (IAudioCaptureClient)CaptureClientObj;

        this.ActualBufferDuration = (ulong)((double)BufferLength * BufferFrameCount / this.MixFormat.SampleRate);

        // Begin streaming
        ErrorCode = this.Client.Start();
        if (IsErrorAndOut(ErrorCode, "Could not start audio client.")) { return; }
        this.StreamReady = true;

        this.KeepGoing = true;
        this.ProcessThread = new Thread(ProcessEventAudio) { Name = $"WASAPILoopback {this.Name}" };
        this.ProcessThread.Start();
    }

    private static IMMDevice? GetDefaultDevice(IMMDeviceEnumerator enumerator, bool isCapture)
    {
        int ErrorCode = enumerator.GetDefaultAudioEndpoint(isCapture ? EDataFlow.eCapture : EDataFlow.eRender, ERole.eMultimedia, out IMMDevice Device);
        if (IsErrorAndOut(ErrorCode, "Failed to get default device.")) { return null; }
        return Device;
    }

    private void PrintDeviceList()
    {
        if (this.DeviceEnumerator == null) { Log.Error("Tried to print device list without a valid device enumerator."); return; }
        PrintDeviceList(this.DeviceEnumerator, EDataFlow.eRender);
        PrintDeviceList(this.DeviceEnumerator, EDataFlow.eCapture);
    }

    private static void PrintDeviceList(IMMDeviceEnumerator enumerator, EDataFlow dataFlow)
    {
        Log.Info((dataFlow == EDataFlow.eCapture ? "Capture" : "Render") + " Devices:");
        int ErrorCode = enumerator.EnumAudioEndpoints(dataFlow, DEVICE_STATE_XXX.DEVICE_STATE_ACTIVE, out IMMDeviceCollection Devices);
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

                    DeviceFriendlyName = Marshal.PtrToStringUni(Variant.Data.AsStringPtr) ?? "NULL";
                    break;
                }
            }

            Log.Info(string.Format("[{0}]: \"{1}\" = \"{2}\"", DeviceIndex, DeviceFriendlyName, DeviceID));
        }
    }

    public uint GetSampleRate() => this.MixFormat.SampleRate;

    public void Stop()
    {
        this.KeepGoing = false;
        this.AudioEvent.Set();
        this.ProcessThread?.Join();
        this.AudioEvent.Dispose();
        AudioEventHandle.Free();
    }

    private unsafe void ProcessEventAudio()
    {
        int ErrorCode;
        while (this.KeepGoing)
        {
            this.AudioEvent.WaitOne();
            if (!this.KeepGoing) { break; } // Early exit for when an event was used to break this loop
            if (this.CaptureClient == null) { Log.Warn("Capture client was not ready when an audio event was received."); continue; }
            if (this.NoteFinder == null) { Log.Warn("NoteFinder was not attached when an audio event was received."); continue; }

            ErrorCode = this.CaptureClient.GetNextPacketSize(out uint PacketLength);
            if (IsErrorAndOut(ErrorCode, "Failed to get audio packet size.")) { continue; }

            ErrorCode = this.CaptureClient.GetBuffer(out IntPtr DataBuffer, out uint FramesAvailable, out AUDCLNT_BUFFERFLAGS BufferStatus, out ulong DevicePosition, out ulong CounterPosition);
            if (IsErrorAndOut(ErrorCode, "Failed to get audio buffer.")) { continue; }

            if (BufferStatus.HasFlag(AUDCLNT_BUFFERFLAGS.AUDCLNT_BUFFERFLAGS_SILENT))
            {
                // TODO: Clear the buffer, as the device is not currently playing audio.
            }
            else if (FramesAvailable > 0)
            {
                int BytesPerSample = this.MixFormat.BitsPerSample / 8;
                int ChannelCount = this.MixFormat.ChannelCount;
                bool IsPCM = this.FormatIsPCM;

                short[]? ProcessedData = this.NoteFinder.GetBufferToWrite(out int NFBufferRef);
                if (ProcessedData == null) { goto SkipBuffer; }
                Debug.Assert(FramesAvailable <= ProcessedData.Length); // TODO: Handle this properly (if needed?)

                if (IsPCM && BytesPerSample == 2)
                {
                    SampleConverter.ShortToShortMixdown(ChannelCount, FramesAvailable, (byte*)DataBuffer, ProcessedData);
                }
                else if (!IsPCM && BytesPerSample == 4)
                {
                    SampleConverter.FloatToShortMixdown(ChannelCount, FramesAvailable, (byte*)DataBuffer, ProcessedData);
                }
                else { throw new InvalidDataException($"{nameof(WASAPILoopback)} cannot handle the current audio format: Channels={ChannelCount} BytesPerSample={BytesPerSample} IsPCM={IsPCM}"); }

                this.NoteFinder.FinishBufferWrite(NFBufferRef, FramesAvailable);
                this.NoteFinder.InputDataEvent.Set();
            }

            SkipBuffer:
            ErrorCode = this.CaptureClient.ReleaseBuffer(FramesAvailable);
            if (IsErrorAndOut(ErrorCode, "Failed to release audio buffer.")) { continue; }
        }

        ErrorCode = this.Client == null ? -1 : this.Client.Stop();
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
