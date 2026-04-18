using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ColorChord.NET.Outputs.DisplayD3D12Support;

public class Window
{
    private readonly IntPtr Instance;
    private readonly IntPtr BackgroundBrush;
    private readonly ushort ClassAtom;
    private readonly Win32API.WindowProcedure WindowProcObj;
    private GCHandle WindowProcHandle;
    private int InitialClientWidth = 800;
    private int InitialClientHeight = 600;
    
    public IntPtr Handle { get; private set; }
    public string WindowTitle { get; init; } = "Test Window";
    public uint BackgroundColour { get; init; } = 0xCC000000; // 0xBBGGRR00

    public RectI32 ClientSize
    {
        get
        {
            if (this.Handle == 0) { return new(this.InitialClientWidth, this.InitialClientHeight); }
            Win32API.GetClientRect(this.Handle, out RectI32 ClientRect);
            return ClientRect;
        }
        set
        {
            if (this.Handle == 0)
            {
                this.InitialClientWidth = value.Width;
                this.InitialClientHeight = value.Height;
            }
            else
            {
                Win32API.GetWindowRect(this.Handle, out RectI32 PrevWindowRect);
                RectI32 NewRect = PrevWindowRect with
                {
                    Right = PrevWindowRect.Left + value.Width,
                    Bottom = PrevWindowRect.Bottom + value.Height
                };
                Win32API.AdjustWindowRectEx(ref NewRect, (WindowStyle)Win32API.GetWindowLongW(this.Handle, WindowMemoryOffset.WindowStyle), false, (WindowStyleEx)Win32API.GetWindowLongW(this.Handle, WindowMemoryOffset.WindowStyleEx));
                Win32API.MoveWindow(this.Handle, NewRect.Left, NewRect.Top, NewRect.Width, NewRect.Height, true);
            }
        }
    }
    public int Width
    {
        get => this.ClientSize.Width;
        set
        {
            RectI32 CurrentSize = this.ClientSize;
            this.ClientSize = CurrentSize with { Right = CurrentSize.Left + value };
        }
    }
    public int Height
    {
        get => this.ClientSize.Height;
        set
        {
            RectI32 CurrentSize = this.ClientSize;
            this.ClientSize = CurrentSize with { Bottom = CurrentSize.Top + value };
        }
    }

    public event EventHandler? OnResize, OnClose;
    private bool Destroying = false;

    public Window()
    {
        this.Instance = Process.GetCurrentProcess().MainModule!.BaseAddress; // TODO: See if we can just get this passed in somehow - or use GetModuleHandleW
        this.BackgroundBrush = Win32API.CreateSolidBrush(this.BackgroundColour >>> 8);
        this.WindowProcObj = BaseWindowProcedure;
        Win32API.SetProcessDPIAware();

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
            WindowProcedure = this.WindowProcObj
        };

        this.ClassAtom = Win32API.RegisterClassEx(Class);
        if (ClassAtom == 0) { throw new Exception($"Registering the window class resulted in error 0x{Marshal.GetLastWin32Error():X8}"); }
        this.WindowProcHandle = GCHandle.Alloc(this.WindowProcObj);
    }

    public void Create()
    {
        const WindowStyle INIT_STYLE = WindowStyle.OverlappedWindow | WindowStyle.Visible;
        const WindowStyleEx INIT_STYLE_EX = WindowStyleEx.NoRedirectionBitmap;
        RectI32 InitialSize = new(this.InitialClientWidth, this.InitialClientHeight);
        Win32API.AdjustWindowRectEx(ref InitialSize, INIT_STYLE, false, INIT_STYLE_EX);

        this.Handle = Win32API.CreateWindowEx(INIT_STYLE_EX, ClassAtom, this.WindowTitle, INIT_STYLE, int.MinValue, int.MinValue, InitialSize.Width, InitialSize.Height, IntPtr.Zero, IntPtr.Zero, this.Instance, IntPtr.Zero);
        if (this.Handle == IntPtr.Zero) { throw new Exception($"Creating the window resulted in error 0x{Marshal.GetLastWin32Error():X8}"); }
        ColorChord.OnStopped += DoDestroy;
    }

    public void DoDestroy()
    {
        this.Destroying = true;
        Win32API.PostMessage(this.Handle, MessageID.WM_NULL, 0, 0);
    }

    public void RunMessageLoop()
    {
        int ResultCode;
        do
        {
            ResultCode = Win32API.GetMessage(out Message Message, IntPtr.Zero, 0, 0);
            if (ResultCode >= 0) // Normal message
            {
                if (Message.MessageID == MessageID.WM_QUIT) { break; }
                if (this.Destroying && Message.MessageID == MessageID.WM_NULL) { Win32API.DestroyWindow(this.Handle); }

                Win32API.TranslateMessage(Message);
                Win32API.DispatchMessage(Message);
            }
            else { throw new Exception("An error occurred in the message pump."); }
        }
        while (ResultCode != 0);
        Debug.WriteLine("Main message pump has terminated.");
        this.WindowProcHandle.Free();

        return;
    }

    public nint BaseWindowProcedure(IntPtr windowHandle, MessageID messageID, nuint wParam, nint lParam)
    {
        // Do queued tasks
        // Call custom proc here
        switch (messageID)
        {
            case MessageID.WM_NULL: break;
            case MessageID.WM_PAINT: break;
            case MessageID.WM_SIZE:
                this.OnResize?.Invoke(this, new()); // TODO: remove new
                break;
            case MessageID.WM_MOVE:
                break;
            case MessageID.WM_CLOSE:
                this.OnClose?.Invoke(this, new());
                return 0;
            case MessageID.WM_QUERYENDSESSION:
                this.OnClose?.Invoke(this, new());
                return 1;
            case MessageID.WM_DESTROY:
                Win32API.PostQuitMessage(0);
                break;
            default:
                return Win32API.DefWindowProc(windowHandle, messageID, wParam, lParam);
        }
        return 0;
    }
}
