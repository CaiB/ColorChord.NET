using System;
using System.Runtime.InteropServices;

namespace ColorChord.NET.Outputs.DisplayD3D12Support;

/// <summary>A Windows Message, as used in the main message queue</summary>
/// <remarks>struct MSG in winuser.h (via Windows.h)</remarks>
[StructLayout(LayoutKind.Sequential)]
public readonly struct Message
{
    public IntPtr WindowHandle { get; init; }
    public MessageID MessageID { get; init; }
    public nuint WParam { get; init; }
    public nint LParam { get; init; }
    public uint Time { get; init; }
    public Point Point { get; init; }
    private uint Private { get; init; }
}

/// <summary>2D 32-bit signed integer point</summary>
/// <remarks>struct POINT in windef.h (via Windows.h)</remarks>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct Point
{
    public readonly int X;
    public readonly int Y;
}

/// <summary>A rectangle defined by its upper-left and lower-right endpoints, with signed 32-bit integer datatypes</summary>
/// <remarks>struct RECT in windef.h (via Windows.h)</remarks>
public readonly record struct RectI32
{
    private readonly int B_Left;
    private readonly int B_Top;
    private readonly int B_Right;
    private readonly int B_Bottom;

    public readonly int Left { get => this.B_Left; init => this.B_Left = value; }
    public readonly int Top { get => this.B_Top; init => this.B_Top = value; }
    public readonly int Right { get => this.B_Right; init => this.B_Right = value; }
    public readonly int Bottom { get => this.B_Bottom; init => this.B_Bottom = value; }
    public readonly int Width { get => this.Right - this.Left; }
    public readonly int Height { get => this.Bottom - this.Top; }

    public RectI32(int width, int height)
    {
        this.Left = 0;
        this.Right = width;
        this.Top = 0;
        this.Bottom = height;
    }

    public RectI32(int left, int top, int right, int bottom)
    {
        this.Left = left;
        this.Top = top;
        this.Right = right;
        this.Bottom = bottom;
    }
}

/// <summary>Contains the window class attributes that are registered by the RegisterClass function.</summary>
/// <remarks>struct WNDCLASSEXW in winuser.h (via Windows.h)</remarks>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public readonly struct WindowClass
{
    internal uint StructSize { get; init; } = (uint)Marshal.SizeOf<WindowClass>();
    /// <summary>The class style(s).</summary>
    public WindowClassStyle Style { get; init; }
    /// <summary>A pointer to the window procedure. You must use the CallWindowProc function to call the window procedure.</summary>
    public Win32API.WindowProcedure? WindowProcedure { get; init; }
    /// <summary>The number of extra bytes to allocate following the window-class structure.</summary>
    internal int ClassAdditionalBytes { get; init; }
    /// <summary>The number of extra bytes to allocate following the window instance.</summary>
    internal int WindowAdditionalBytes { get; init; }
    /// <summary>A handle to the instance that contains the window procedure for the class.</summary>
    public IntPtr Instance { get; init; }
    /// <summary>A handle to the class icon. This member must be a handle to an icon resource.</summary>
    /// <remarks>If this member is NULL, the system provides a default icon.</remarks>
    public IntPtr Icon { get; init; }
    /// <summary>A handle to the class cursor. This member must be a handle to a cursor resource.</summary>
    /// <remarks>If this member is NULL, an application must explicitly set the cursor shape whenever the mouse moves into the application's window.</remarks>
    public IntPtr Cursor { get; init; }
    /// <summary>A handle to the class background brush.</summary>
    /// <remarks>When this member is NULL, an application must paint its own background whenever it is requested to paint in its client area. It is recommended to use <see cref="BrushSolidColour.GetWin32Brush"/>.</remarks>
    public IntPtr BackgroundBrush { get; init; }
    /// <summary>The resource name of the class menu, as the name appears in the resource file.</summary>
    /// <remarks>If this member is NULL, windows belonging to this class have no default menu.</remarks>
    public string? ClassMenuResourceName { get; init; }
    /// <summary>Specifies the window class name.</summary>
    /// <remarks>The class name can be any name registered with RegisterClass or RegisterClassEx, or any of the predefined control-class names. The maximum length is 256.</remarks>
    public string? ClassName { get; init; }
    /// <summary>A handle to a small icon that is associated with the window class.</summary>
    /// <remarks>If this member is NULL, the system searches the icon resource specified by the <see cref="Icon"/> member for an icon of the appropriate size to use as the small icon.</remarks>
    public IntPtr SmallIcon { get; init; }

    public WindowClass() { }
}

public struct DWMUnsignedRatio
{
    public uint Numerator;
    public uint Denominator;

    public override readonly string ToString() => $"({Numerator} / {Denominator})";
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct DWMTimingInfo
{
    public uint cbSize;
    public DWMUnsignedRatio rateRefresh;
    public ulong qpcRefreshPeriod;
    public DWMUnsignedRatio rateCompose;
    public ulong qpcVBlank;
    public ulong cRefresh;
    public uint cDXRefresh;
    public ulong qpcCompose;
    public ulong cFrame;
    public uint cDXPresent;
    public ulong cRefreshFrame;
    public ulong cFrameSubmitted;
    public uint cDXPresentSubmitted;
    public ulong cFrameConfirmed;
    public uint cDXPresentConfirmed;
    public ulong cRefreshConfirmed;
    public uint cDXRefreshConfirmed;
    public ulong cFramesLate;
    public uint cFramesOutstanding;
    public ulong cFrameDisplayed;
    public ulong qpcFrameDisplayed;
    public ulong cRefreshFrameDisplayed;
    public ulong cFrameComplete;
    public ulong qpcFrameComplete;
    public ulong cFramePending;
    public ulong qpcFramePending;
    public ulong cFramesDisplayed;
    public ulong cFramesComplete;
    public ulong cFramesPending;
    public ulong cFramesAvailable;
    public ulong cFramesDropped;
    public ulong cFramesMissed;
    public ulong cRefreshNextDisplayed;
    public ulong cRefreshNextPresented;
    public ulong cRefreshesDisplayed;
    public ulong cRefreshesPresented;
    public ulong cRefreshStarted;
    public ulong cPixelsReceived;
    public ulong cPixelsDrawn;
    public ulong cBuffersEmpty;
}