using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace ColorChord.NET.Outputs.DisplayD3D12Support;

public class Window
{
    public IntPtr Handle { get; private set; }
    private IntPtr Instance;
    public uint BackgroundColour { get; init; } = 0xCC000000; // 0xBBGGRR00
    public string WindowTitle { get; init; } = "Test Window";
    private int InitialWidth = 800;
    public int Width
    {
        get
        {
            Win32API.GetClientRect(this.Handle, out RectI32 ClientRect);
            return ClientRect.Right - ClientRect.Left;
        }
        set
        {
            if (this.Handle == 0) { this.InitialWidth = value; }
            else
            {
                Win32API.GetWindowRect(this.Handle, out RectI32 PrevWindowRect);
                RectI32 NewRect = new()
                {
                    Left = PrevWindowRect.Left, // TODO: is it fine to use window here?
                    Right = PrevWindowRect.Left + value,
                    Top = PrevWindowRect.Top,
                    Bottom = PrevWindowRect.Bottom
                };
                Win32API.AdjustWindowRectEx(NewRect, (WindowStyle)Win32API.GetWindowLongW(this.Handle, WindowMemoryOffset.WindowStyle), false, (WindowStyleEx)Win32API.GetWindowLongW(this.Handle, WindowMemoryOffset.WindowStyleEx));
                Win32API.MoveWindow(this.Handle, NewRect.Left, NewRect.Top, NewRect.Right - NewRect.Left, NewRect.Bottom - NewRect.Top, true);
            }
        }
    }
    private int InitialHeight = 600;
    public int Height
    {
        get
        {
            Win32API.GetClientRect(this.Handle, out RectI32 ClientRect);
            return ClientRect.Bottom - ClientRect.Top;
        }
        set
        {
            if (this.Handle == 0) { this.InitialHeight = value; }
            else
            {
                Win32API.GetWindowRect(this.Handle, out RectI32 PrevWindowRect);
                RectI32 NewRect = new()
                {
                    Left = PrevWindowRect.Left, // TODO: is it fine to use window here?
                    Right = PrevWindowRect.Right,
                    Top = PrevWindowRect.Top,
                    Bottom = PrevWindowRect.Top + value
                };
                Win32API.AdjustWindowRectEx(NewRect, (WindowStyle)Win32API.GetWindowLongW(this.Handle, WindowMemoryOffset.WindowStyle), false, (WindowStyleEx)Win32API.GetWindowLongW(this.Handle, WindowMemoryOffset.WindowStyleEx));
                Win32API.MoveWindow(this.Handle, NewRect.Left, NewRect.Top, NewRect.Right - NewRect.Left, NewRect.Bottom - NewRect.Top, true);
            }
        }
    }

    public readonly IntPtr BackgroundBrush;
    private ushort ClassAtom;

    public event EventHandler? OnResize, OnClose;

    public Window()
    {
        this.Instance = Process.GetCurrentProcess().MainModule!.BaseAddress; // TODO: See if we can just get this passed in somehow - or use GetModuleHandleW
        this.BackgroundBrush = Win32API.CreateSolidBrush(this.BackgroundColour >>> 8);

        WindowClass Class = new()
        {
            BackgroundBrush = this.BackgroundBrush,
            ClassMenuResourceName = null,
            ClassName = "ColorChord.NET_D3D12",
            Cursor = Win32API.LoadCursor(NativeCursorIndex.Arrow),
            Icon = IntPtr.Zero,
            Instance = this.Instance,
            SmallIcon = IntPtr.Zero,
            Style = WindowClassStyle.HorizontalRedraw | WindowClassStyle.VerticalRedraw | WindowClassStyle.DoubleClicks,
            WindowProcedure = BaseWindowProcedure
        };

        this.ClassAtom = Win32API.RegisterClassEx(Class);
        if (ClassAtom == 0) { throw new Exception($"Registering the window class resulted in error 0x{Marshal.GetLastWin32Error():X8}"); }
    }

    public void Create()
    {
        this.Handle = Win32API.CreateWindowEx(WindowStyleEx.NoRedirectionBitmap, ClassAtom, this.WindowTitle, WindowStyle.OverlappedWindow | WindowStyle.Visible, 0, 0, InitialWidth, InitialHeight, IntPtr.Zero, IntPtr.Zero, this.Instance, IntPtr.Zero);
        if (this.Handle == IntPtr.Zero) { throw new Exception($"Creating the window resulted in error 0x{Marshal.GetLastWin32Error():X8}"); }
    }

    public void RunMessageLoop()
    {
        int ResultCode;
        do
        {
            ResultCode = Win32API.GetMessage(out Message Message, IntPtr.Zero, 0, 0);
            if (ResultCode >= 0) // Normal message
            {
                Win32API.TranslateMessage(Message);
                Win32API.DispatchMessage(Message); // TODO: This seems wasteful, since it'll only ever go to this window's proc

                // Do any additional handling here
                switch (Message.MessageID)
                {
                    case MessageID.WM_NULL: continue;
                    case MessageID.WM_QUIT:
                        break;
                    case MessageID.WM_QUERYENDSESSION:
                        // TODO: Handle this
                        break;
                    case MessageID.WM_ENDSESSION:
                        // TODO: Handle this
                        break;

                }
            }
            else { throw new Exception("An error occurred in the message pump."); }
        }
        while (ResultCode != 0);
        Debug.WriteLine("Main message pump has terminated.");

        return;
    }

    public nint BaseWindowProcedure(IntPtr windowHandle, MessageID messageID, nuint wParam, nint lParam)
    {
        // Do queued tasks
        // Call custom proc here
        switch (messageID)
        {
            case MessageID.WM_NULL: break;
            case MessageID.WM_PAINT:
                // TODO: Handle this event
                break;
            case MessageID.WM_SIZE:
                this.OnResize?.Invoke(this, new()); // TODO: remove new
                break;
            case MessageID.WM_MOVE:
                break;
            case MessageID.WM_CLOSE:
                this.OnClose?.Invoke(this, new());
                break;
            case MessageID.WM_DESTROY:
                Win32API.PostQuitMessage(0);
                break;

        }
        return Win32API.DefWindowProc(windowHandle, messageID, wParam, lParam);
    }
}
