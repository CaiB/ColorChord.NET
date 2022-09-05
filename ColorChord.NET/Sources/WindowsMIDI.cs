using ColorChord.NET.API.Config;
using ColorChord.NET.API.Sources;
using ColorChord.NET.Config;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ColorChord.NET.Sources
{
    public class WindowsMIDI : IAudioSource
    {
        private readonly MIDIInProc Callback;
        private readonly GCHandle CallbackHandle;
        private IntPtr DeviceHandle;

        [ConfigString("Device", "default")]
        private readonly string DeviceName = "default";

        [ConfigBool("ShowDeviceInfo", true)]
        private readonly bool ShowDeviceInfo = true;

        public WindowsMIDI(string name, Dictionary<string, object> config)
        {
            this.Callback = MIDICallback;
            this.CallbackHandle = GCHandle.Alloc(Callback);
            Configurer.Configure(this, config);
        }

        public void Start()
        {
            uint DeviceCount = GetDeviceCount();
            Log.Info($"There {(DeviceCount != 1 ? "are" : "is")} {DeviceCount} MIDI input {(DeviceCount != 1 ? "devices" : "device")} connected.");
            if (DeviceCount == 0)
            {
                Log.Error("No MIDI devices were found. No audio will be received by ColorChord.NET!");
                return;
            }

            uint DeviceToUse = 0;
            string DeviceToUseName = "N/A";
            for (uint i = 0; i < DeviceCount; i++)
            {
                MIDIINCAPS? DeviceInfoPerhaps = GetDeviceInfo(i);
                if (DeviceInfoPerhaps == null) { Log.Info($"  [{i}] Could not get info."); }
                else
                {
                    MIDIINCAPS DeviceInfo = DeviceInfoPerhaps.Value;
                    if (this.ShowDeviceInfo) { Log.Info($"  [{i}] \"{DeviceInfo.ProductName}\" Manufacturer {DeviceInfo.ManufacturerID:X2}, Product {DeviceInfo.ProductID:X2}, Driver {DeviceInfo.DriverVersion}"); }
                    if (this.DeviceName != "default" && this.DeviceName == DeviceInfo.ProductName)
                    {
                        DeviceToUse = i;
                        DeviceToUseName = DeviceInfo.ProductName;
                    }
                    if (this.DeviceName == "default" && i == 0) { DeviceToUseName = DeviceInfo.ProductName; }
                }
            }

            Log.Info($"Using device ID {DeviceToUse}, \"{DeviceToUseName}\".");
            bool Opened = OpenDevice(DeviceToUse, this.Callback);
            if (!Opened) { return; }
            bool Started = StartReceiver(this.DeviceHandle);
            Log.Info(Started ? "Started MIDI receiver." : "Failed to start MIDI receiver.");
        }

        public void Stop()
        {
            bool Stopped = StopReceiver(this.DeviceHandle);
            bool Closed = CloseDevice(this.DeviceHandle);
            this.DeviceHandle = IntPtr.Zero;
            this.CallbackHandle.Free();
        }

        
        /// <summary>Gets the number of available MIDI input devices on the system.</summary>
        /// <returns>The number of available devices.</returns>
        private static uint GetDeviceCount() => midiInGetNumDevs();

        private static MIDIINCAPS? GetDeviceInfo(uint deviceID)
        {
            MIDIINCAPS Output = new();
            uint Result = midiInGetDevCaps(deviceID, ref Output, (uint)Marshal.SizeOf<MIDIINCAPS>());
            return Result == 0 ? Output : null;
        }

        /// <summary>Opens the device, setting up the handle for further operations.</summary>
        /// <param name="deviceID">The index of the device to open.</param>
        /// <param name="callback">The callback which will receive input events.</param>
        /// <returns>Whether opening the device succeeded.</returns>
        private bool OpenDevice(uint deviceID, MIDIInProc callback)
        {
            uint Result = midiInOpen(out IntPtr DeviceHandle, deviceID, Marshal.GetFunctionPointerForDelegate(callback), IntPtr.Zero, 0x00030000);
            LogError("open", Result);
            this.DeviceHandle = DeviceHandle;
            return Result == 0;
        }

        /// <summary>Tells the system to begin sending us input events from the device.</summary>
        /// <param name="deviceHandle">The device handle obtained when opening the device.</param>
        /// <returns>Whether starting succeeded.</returns>
        private static bool StartReceiver(IntPtr deviceHandle)
        {
            uint Result = midiInStart(deviceHandle);
            LogError("start", Result);
            return Result == 0;
        }

        /// <summary>Tells the system to stop sending us input events from the device.</summary>
        /// <param name="deviceHandle">The device handle obtained when opening the device.</param>
        /// <returns>Wheter stopping succeeded.</returns>
        private static bool StopReceiver(IntPtr deviceHandle)
        {
            uint Result = midiInStop(deviceHandle);
            LogError("stop", Result);
            return Result == 0;
        }

        /// <summary>Closes the device, freeing resources and our lock on it.</summary>
        /// <param name="deviceHandle">The handle of the device obtained when opening it.</param>
        /// <returns>Whether closing the device succeeded.</returns>
        private static bool CloseDevice(IntPtr deviceHandle)
        {
            uint Result = midiInClose(deviceHandle);
            LogError("close", Result);
            return Result == 0;
        }

        /// <summary>Logs readable error info from various MIDI device operations.</summary>
        /// <param name="verb">What was attempted that created the error.</param>
        /// <param name="error">The error code (nothing is output if 0).</param>
        private static void LogError(string verb, uint error)
        {
            switch (error)
            {
                case 0: break;
                case 2: Log.Error($"Could not {verb} MIDI input device as it doesn't exist. Either it was removed, or something went wrong when checking for devices."); break;
                case 3: Log.Error($"Could not {verb} MIDI input device as its driver failed to enable it. Make sure the device is working and drivers are installed."); break;
                case 4: Log.Error($"Could not {verb} MIDI input device as it was already allocated. Make sure other applications are not using it."); break;
                case 5: Log.Error($"Could not {verb} MIDI input device as the handle was invalid. Please try restarting ColorCHord.NET."); break;
                case 6: Log.Error($"Could not {verb} MIDI input device as the driver is not present. Please make sure the driver is installed and up to date."); break;
                case 7: Log.Error($"Could not {verb} MIDI input device as getting memory to open the device failed. Make sure you have sufficient RAM."); break;
                case 8: Log.Error($"Could not {verb} MIDI input device as it doesn't support this function."); break;
                case 10: Log.Error($"Could not {verb} MIDI input device as invalid flags were provided. This should never happen, please report it to the ColorChord.NET developers."); break;
                case 11: Log.Error($"Could not {verb} MIDI input device as an invalid parameter was passed. Please report it to the ColorChord.NET developers."); break;
                default: Log.Error($"Could not {verb} MIDI input device as an unknown error occured: MMSYSERR ID {error}. Please report this to the ColorChord.NET developers."); break;
            }
        }

        /// <summary>Receives MIDI data and passes it onto the next processing step.</summary>
        private void MIDICallback(IntPtr deviceHandle, uint inputMessage, IntPtr instance, uint message1, uint message2)
        {
            switch(inputMessage)
            {
                case 0x3C1: // MIM_OPEN: device was opened
                    Log.Debug("MIDI device was opened."); break;
                case 0x3C2: // MIM_CLOSE: device was closed
                    Log.Debug("MIDI device was closed."); break;
                case 0x3C3: // MIM_DATA: directly given data
                    byte MIDIStatus = (byte)message1;
                    ushort MIDIData = (ushort)(message1 >> 8);
                    uint Timestamp = message2; // Milliseconds since StartReceiver() called
                    Log.Debug($"Got MIDI data: status 0x{MIDIStatus:X2} data 0x{MIDIData:X4} time {Timestamp}."); break;
                case 0x3C4: // MIM_LONGDATA: buffer has been filled with data
                    Log.Debug("Got long MIDI data."); break;
                case 0x3C5: // MIM_ERROR: invalid MIDI data received
                    uint MIDIDataInvalid = message1;
                    uint TimestampInvalid = message2;
                    Log.Debug($"Invalid MIDI data received: 0x{MIDIDataInvalid:X8} time {TimestampInvalid}."); break;
                case 0x3C6: // MIM_LONGERROR: invalid system-exclusive message received
                    Log.Debug("Got long MIDI error."); break;
                case 0x3CC: // MIM_MOREDATA: more data was received, but we are mot processing MIM_DATA messages quickly enough
                    Log.Warn("More MIDI data was received than we could process in time!"); break;
                default:
                    Log.Warn($"Unknown MIDI data type received: 0x{inputMessage:X8}"); break;
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct MIDIINCAPS
        {
            public readonly byte ManufacturerID;
            public readonly byte ProductID;
            public readonly uint DriverVersion;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public readonly string ProductName;
            
            public readonly uint Ignore;
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void MIDIInProc(IntPtr deviceHandle, uint inputMessage, IntPtr instance, uint message1, uint message2); // TODO: message1 and message2 appear to be platform-dependent size?

        [DllImport("winmm.dll", SetLastError = true)]
        private static extern uint midiInGetNumDevs();

        [DllImport("winmm.dll", SetLastError = true)]
        private static extern uint midiInGetDevCaps(uint deviceID, ref MIDIINCAPS caps, uint numBytesToFill);

        [DllImport("winmm.dll", SetLastError = true)]
        private static extern uint midiInOpen(out IntPtr midiDevHandle, uint deviceID, IntPtr callback, IntPtr instanceData, uint callbackFlags);

        [DllImport("winmm.dll", SetLastError = true)]
        private static extern uint midiInStart(IntPtr midiDevHandle);

        [DllImport("winmm.dll", SetLastError = true)]
        private static extern uint midiInStop(IntPtr midiDevHandle);

        [DllImport("winmm.dll", SetLastError = true)]
        private static extern uint midiInClose(IntPtr midiDevHandle);
    }
}
