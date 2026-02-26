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
    public readonly int Left { get; init; }
    public readonly int Top { get; init; }
    public readonly int Right { get; init; }
    public readonly int Bottom { get; init; }
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