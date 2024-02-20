using System;
using System.Runtime.InteropServices;
using Vannatech.CoreAudio.Constants;
using Vannatech.CoreAudio.Enumerations;
using Vannatech.CoreAudio.Structures;

namespace Vannatech.CoreAudio.Enumerations
{
    public enum AudioStreamCategory
    {
        /// <summary>All other streams (default)</summary>
        Other = 0,

        /// <summary>Music, Streaming audio</summary>
        [Obsolete("Deprecated since Windows 10")] ForegroundOnlyMedia = 1,

        /// <summary>Video with audio</summary>
        [Obsolete("Deprecated since Windows 10")] BackgroundCapableMedia = 2,

        /// <summary>VOIP, chat, phone call</summary>
        Communications = 3,

        /// <summary>Alarm, Ring tones</summary>
        Alerts = 4,

        /// <summary>Sound effects, clicks, dings</summary>
        SoundEffects = 5,

        /// <summary>Game sound effects</summary>
        GameEffects = 6,

        /// <summary>Background audio for games</summary>
        GameMedia = 7,

        /// <summary>In game player chat</summary>
        GameChat = 8,

        /// <summary>Speech recognition</summary>
        Speech = 9,

        /// <summary>Video with audio</summary>
        Movie = 10,

        /// <summary>Music, Streaming audio</summary>
        Media = 11
    }

    [Flags]
    public enum AudioClientStreamOptions
    {
        None = 0,
        Raw = 1,
        MatchFormat = 2,
        Ambisonics = 4
    }

    [Flags]
    public enum SpeakerLocations : uint
    {
        FrontLeft = 0x1,
        FrontRight = 0x2,
        FrontCenter = 0x4,
        LowFrequency = 0x8,
        BackLeft = 0x10,
        BackRight = 0x20,
        FrontLeftOfCenter = 0x40,
        FrontRightOfCenter = 0x80,
        BackCenter = 0x100,
        SideLeft = 0x200,
        SideRight = 0x400,
        TopCenter = 0x800,
        TopFrontLeft = 0x1000,
        TopFrontCenter = 0x2000,
        TopFrontRight = 0x4000,
        TopBackLeft = 0x8000,
        TopBackCenter = 0x10000,
        TopBackRight = 0x20000,

        Stereo = FrontLeft | FrontRight,
        FivePointOne = FrontLeft | FrontRight | FrontCenter | BackLeft | BackRight | LowFrequency
    }

    public enum WaveFormatBasic : ushort
    {
        PCM = 1,
        Float = 3,
        Extensible = 0xFFFE
    }
}

namespace Vannatech.CoreAudio.Structures
{
    [StructLayout(LayoutKind.Sequential)]
    public struct AudioClientProperties
    {
        [MarshalAs(UnmanagedType.U4)]
        public uint cbSize;

        [MarshalAs(UnmanagedType.Bool)]
        public bool IsOffload;

        [MarshalAs(UnmanagedType.I4)]
        public AudioStreamCategory Category;

        [MarshalAs(UnmanagedType.I4)]
        public AudioClientStreamOptions Options;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WaveFormatEx
    {
        /// <summary>Waveform-audio format type.</summary>
        public WaveFormatBasic Format;

        /// <summary>Number of audio channels.</summary>
        public ushort ChannelCount;

        /// <summary>Sample rate, in samples per second (hertz).</summary>
        public uint SampleRate;

        /// <summary>The required average data-transfer rate, in bytes per second, for the format tag.</summary>
        public uint AvgBytesPerSec;

        /// <summary>Block alignment, in bytes.</summary>
        public ushort BlockAlignment;

        /// <summary>Bits per sample for the format type.</summary>
        public ushort BitsPerSample;

        /// <summary>Size, in bytes, of extra format information appended to the end of the structure.</summary>
        public ushort AppendedSize;

        public static WaveFormatEx? FromPointer(IntPtr pointer) => (WaveFormatEx?)Marshal.PtrToStructure(pointer, typeof(WaveFormatEx));
        public readonly bool IsExtensible() => this.Format == WaveFormatBasic.Extensible;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WaveFormatExtensible
    {
        /// <summary>Waveform-audio format type. Should always be <see cref="WaveFormatBasic.Extensible"/>.</summary>
        public WaveFormatBasic Format;

        /// <summary>Number of audio channels.</summary>
        public ushort ChannelCount;

        /// <summary>Sample rate, in samples per second (hertz).</summary>
        public uint SampleRate;

        /// <summary>The required average data-transfer rate, in bytes per second, for the format tag.</summary>
        public uint AvgBytesPerSec;

        /// <summary>Block alignment, in bytes.</summary>
        public ushort BlockAlignment;

        /// <summary>Bits per sample for the format type.</summary>
        public ushort BitsPerSample;

        /// <summary>Size, in bytes, of extra format information appended to the end of the structure.</summary>
        public ushort AppendedSize;

        /// <summary>Information about samples. For uncompressed formats, the real bit count in a sample (without required padding). For compressed formats, number of samples per block.</summary>
        public ushort Samples;

        /// <summary>Bitmask specifying which speaker channels are present in the stream.</summary>
        public SpeakerLocations ChannelMask;

        /// <summary>The format of the data. See <see cref="WaveFormatGUIDs"/> for common ones.</summary>
        public Guid SubFormat;

        public WaveFormatExtensible() { Format = WaveFormatBasic.Extensible; AppendedSize = 22; }

        public static WaveFormatExtensible? FromPointer(IntPtr pointer, out WaveFormatEx? fallback)
        {
            fallback = WaveFormatEx.FromPointer(pointer);
            if (fallback is not WaveFormatEx Base) { return null; }
            if (!Base.IsExtensible()) { return null; }
            if (Base.AppendedSize < 22) { return null; } // Not large enough to contain Extensible
            return (WaveFormatExtensible?)Marshal.PtrToStructure(pointer, typeof(WaveFormatExtensible));
        }
        public readonly bool IsExtensible() => this.Format == WaveFormatBasic.Extensible;
    }
}

namespace Vannatech.CoreAudio.Constants
{
    public static class AdditionalComIIDs
    {
        public const string IAudioClient2IID = "726778CD-F60A-4eda-82DE-E47610CD78AA";
        public const string IAudioClient3IID = "7ED4EE07-8E67-4CD4-8C1A-2B7A5987AD42";
    }

    public static class WaveFormatGUIDs // TODO: init these with the component integers instead of strings
    {
        public static readonly Guid PCM   = new("00000001-0000-0010-8000-00aa00389b71");
        public static readonly Guid Float = new("00000003-0000-0010-8000-00aa00389b71");
        public static readonly Guid DRM   = new("00000009-0000-0010-8000-00aa00389b71");
        public static readonly Guid ALaw  = new("00000006-0000-0010-8000-00aa00389b71");
        public static readonly Guid MuLaw = new("00000007-0000-0010-8000-00aa00389b71");
        public static readonly Guid ADPCM = new("00000002-0000-0010-8000-00aa00389b71");
        public static readonly Guid MPEG  = new("00000050-0000-0010-8000-00aa00389b71");

        public static string GetNameFromGUID(Guid format)
        {
            if (format == PCM)   { return "PCM"; }
            if (format == Float) { return "Float"; }
            if (format == DRM)   { return "DRM"; }
            if (format == ALaw)  { return "ALaw"; }
            if (format == MuLaw) { return "MuLaw"; }
            if (format == ADPCM) { return "ADPCM"; }
            if (format == MPEG)  { return "MPEG"; }
            return $"Unknown ({format})";
        }
    }
}

namespace Vannatech.CoreAudio.Interfaces
{
    [Guid(AdditionalComIIDs.IAudioClient3IID)]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public partial interface IAudioClient3
    {
        #region IAudioClient Methods

        /// <summary>Initializes the audio stream.</summary>
        /// <param name="shareMode">The sharing mode for the connection.</param>
        /// <param name="streamFlags">One or more <see cref="AUDCLNT_STREAMFLAGS_XXX"/> flags to control creation of the stream.</param>
        /// <param name="bufferDuration">The buffer capacity as a time value.</param>
        /// <param name="devicePeriod">
        /// In exclusive mode, this parameter specifies the requested scheduling period for successive
        /// buffer accesses by the audio endpoint device. In shared mode, it should always be set to zero.
        /// </param>
        /// <param name="format">The format descriptor.</param>
        /// <param name="audioSessionId">The ID of the audio session.</param>
        /// <returns>An HRESULT code indicating whether the operation succeeded of failed.</returns>
        [PreserveSig]
        int Initialize(
            [In][MarshalAs(UnmanagedType.I4)] AUDCLNT_SHAREMODE shareMode,
            [In][MarshalAs(UnmanagedType.U4)] UInt32 streamFlags,
            [In][MarshalAs(UnmanagedType.U8)] UInt64 bufferDuration,
            [In][MarshalAs(UnmanagedType.U8)] UInt64 devicePeriod,
            [In][MarshalAs(UnmanagedType.SysInt)] IntPtr format, // TODO: Explore options for WAVEFORMATEX definition here
            [In, Optional][MarshalAs(UnmanagedType.LPStruct)] Guid audioSessionId);

        /// <summary>
        /// Retrieves the size (maximum capacity) of the audio buffer associated with the endpoint.
        /// </summary>
        /// <param name="size">Receives the number of audio frames that the buffer can hold.</param>
        /// <returns>An HRESULT code indicating whether the operation succeeded of failed.</returns>
        [PreserveSig]
        int GetBufferSize([Out][MarshalAs(UnmanagedType.U4)] out UInt32 size);

        /// <summary>
        /// Retrieves the maximum latency for the current stream and can be called any time after the stream has been initialized.
        /// </summary>
        /// <param name="latency">Receives a time value representing the latency.</param>
        /// <returns>An HRESULT code indicating whether the operation succeeded of failed.</returns>
        [PreserveSig]
        int GetStreamLatency([Out][MarshalAs(UnmanagedType.U8)] out UInt64 latency);

        /// <summary>
        /// Retrieves the number of frames of padding in the endpoint buffer.
        /// </summary>
        /// <param name="frameCount">Receives the number of audio frames of padding in the buffer.</param>
        /// <returns>An HRESULT code indicating whether the operation succeeded of failed.</returns>
        [PreserveSig]
        int GetCurrentPadding([Out][MarshalAs(UnmanagedType.U4)] out UInt32 frameCount);

        /// <summary>
        /// Indicates whether the audio endpoint device supports a particular stream format.
        /// </summary>
        /// <param name="shareMode">The sharing mode for the stream format.</param>
        /// <param name="format">The specified stream format.</param>
        /// <param name="closestMatch">The supported format that is closest to the format specified in the format parameter.</param>
        /// <returns>An HRESULT code indicating whether the operation succeeded of failed.</returns>
        [PreserveSig]
        int IsFormatSupported(
            [In][MarshalAs(UnmanagedType.I4)] AUDCLNT_SHAREMODE shareMode,
            [In][MarshalAs(UnmanagedType.SysInt)] IntPtr format, // TODO: Explore options for WAVEFORMATEX definition here
            [Out, Optional] out IntPtr closestMatch); // TODO: Sort out WAVEFORMATEX **match (returned)

        /// <summary>
        /// Retrieves the stream format that the audio engine uses for its internal processing of shared-mode streams.
        /// </summary>
        /// <param name="format">Receives the address of the mix format.</param>
        /// <returns>An HRESULT code indicating whether the operation succeeded of failed.</returns>
        [PreserveSig]
        int GetMixFormat([Out][MarshalAs(UnmanagedType.SysInt)] out IntPtr format); // TODO: Explore options for WAVEFORMATEX definition here

        /// <summary>
        /// Retrieves the length of the periodic interval separating successive processing passes by the audio engine on the data in the endpoint buffer.
        /// </summary>
        /// <param name="processInterval">Receives a time value specifying the default interval between processing passes by the audio engine.</param>
        /// <param name="minimumInterval">Receives a time value specifying the minimum interval between processing passes by the audio endpoint device.</param>
        /// <returns>An HRESULT code indicating whether the operation succeeded of failed.</returns>
        [PreserveSig]
        int GetDevicePeriod(
            [Out, Optional][MarshalAs(UnmanagedType.U8)] out UInt64 processInterval,
            [Out, Optional][MarshalAs(UnmanagedType.U8)] out UInt64 minimumInterval);

        /// <summary>Starts the audio stream.</summary>
        /// <returns>An HRESULT code indicating whether the operation succeeded of failed.</returns>
        [PreserveSig]
        int Start();

        /// <summary>Stops the audio stream.</summary>
        /// <returns>An HRESULT code indicating whether the operation succeeded of failed.</returns>
        [PreserveSig]
        int Stop();

        /// <summary>Resets the audio stream.</summary>
        /// <returns>An HRESULT code indicating whether the operation succeeded of failed.</returns>
        [PreserveSig]
        int Reset();

        /// <summary>Sets the event handle that the audio engine will signal each time a buffer becomes ready to be processed by the client.</summary>
        /// <param name="handle">The event handle.</param>
        /// <returns>An HRESULT code indicating whether the operation succeeded of failed.</returns>
        [PreserveSig]
        int SetEventHandle([In][MarshalAs(UnmanagedType.SysInt)] IntPtr handle);

        /// <summary>Accesses additional services from the audio client object.</summary>
        /// <param name="interfaceId">The interface ID for the requested service.</param>
        /// <param name="instancePtr">Receives the address of an instance of the requested interface.</param>
        /// <returns>An HRESULT code indicating whether the operation succeeded of failed.</returns>
        [PreserveSig]
        int GetService(
            [In][MarshalAs(UnmanagedType.LPStruct)] Guid interfaceId,
            [Out][MarshalAs(UnmanagedType.IUnknown)] out object instancePtr);

        #endregion

        #region IAudioClient2 Methods
        [PreserveSig]
        public int IsOffloadCapable
        (
            [In] [MarshalAs(UnmanagedType.I4)] AudioStreamCategory category,
            [Out] [MarshalAs(UnmanagedType.Bool)] out bool isOffloadCapable
        );

        [PreserveSig]
        public int SetClientProperties
        (
            [In] [MarshalAs(UnmanagedType.LPStruct)] AudioClientProperties properties
        );

        [PreserveSig]
        public int GetBufferSizeLimits
        (
            [In] [MarshalAs(UnmanagedType.SysInt)] IntPtr format,
            [In] [MarshalAs(UnmanagedType.Bool)] bool eventDriven,
            [Out] [MarshalAs(UnmanagedType.I8)] out long minBufferDuration,
            [Out] [MarshalAs(UnmanagedType.I8)] out long maxBufferDuration
        );
        #endregion

        #region IAudioClient3 Methods
        [PreserveSig]
        public int GetSharedModeEnginePeriod
        (
            [In] [MarshalAs(UnmanagedType.SysInt)] IntPtr format,
            [Out] [MarshalAs(UnmanagedType.U4)] out uint defaultPeriodInFrames,
            [Out] [MarshalAs(UnmanagedType.U4)] out uint fundamentalPeriodInFrames,
            [Out] [MarshalAs(UnmanagedType.U4)] out uint minPeriodInFrames,
            [Out] [MarshalAs(UnmanagedType.U4)] out uint maxPeriodInFrames
        );

        [PreserveSig]
        public int GetCurrentSharedModeEnginePeriod
        (
            [Out] [MarshalAs(UnmanagedType.SysInt)] out IntPtr format,
            [Out] [MarshalAs(UnmanagedType.U4)] out uint currentPeriodInFrames
        );

        [PreserveSig]
        public int InitializeSharedAudioStream
        (
            [In] [MarshalAs(UnmanagedType.U4)] uint streamFlags,
            [In] [MarshalAs(UnmanagedType.U4)] uint periodInFrames,
            [In] [MarshalAs(UnmanagedType.SysInt)] IntPtr format,
            [In, Optional] [MarshalAs(UnmanagedType.LPStruct)] Guid audioSessionGUID
        );
        #endregion
    }
}
