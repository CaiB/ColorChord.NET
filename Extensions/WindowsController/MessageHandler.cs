using ColorChord.NET.API;
using System;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;

namespace ColorChord.NET.Extensions.WindowsController
{
    internal class MessageHandler// : IDisposable
    {
        private const string WINDOW_CLASS_NAME = "ColorChord.NET-InternalWindow";
        private const int ERROR_CLASS_ALREADY_EXISTS = 1410;
        private const uint WS_OVERLAPPEDWINDOW = 0xcf0000;
        private const uint WS_VISIBLE = 0x10000000;
        private const uint WINDOWS_MESSAGE_ID_HOTKEY = 0x0312;
        private const uint WINDOWS_MESSAGE_ID_CLOSE = 0x0010;
        private const uint WINDOWS_MESSAGE_ID_QUIT = 0x0012;
        private const uint WINDOWS_MESSAGE_KEYDOWN = 0x0100;

        public delegate void ShortcutHandler(string shortcutName, Win32.KeyModifiers modifiers, Win32.Keycode key);

        private bool _mDisposed;
        public IntPtr WindowHandle;
        private readonly Win32.WndProc? WindowProcedureDelegate;

        private int CurrentID = 1;
        private readonly Dictionary<int, string> ShortcutNames = new();
        private ShortcutHandler? ShortcutCallback;
        private uint ThreadID;

        public MessageHandler(bool setupWindow)
        {
            if (setupWindow)
            {
                this.WindowProcedureDelegate = CustomWindowProcedure;

                Win32.WNDCLASS WindowClass = new()
                {
                    lpszClassName = WINDOW_CLASS_NAME,
                    lpfnWndProc = Marshal.GetFunctionPointerForDelegate(WindowProcedureDelegate)
                };

                ushort ClassAtom = Win32.RegisterClassW(ref WindowClass);
                int lastError = Marshal.GetLastWin32Error();
                if (ClassAtom == 0 && lastError != ERROR_CLASS_ALREADY_EXISTS) { throw new Exception("Could not register window class"); }

                this.WindowHandle = Win32.CreateWindowExW(0, WINDOW_CLASS_NAME, "CC.NET Internal Window", WS_OVERLAPPEDWINDOW | WS_VISIBLE, 0, 0, 300, 400, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

                Win32.ShowWindow(this.WindowHandle, 1);
                Win32.UpdateWindow(this.WindowHandle);
            }
        }

        public bool AddShortcut(string uniqueName, Win32.KeyModifiers modifiers, Win32.Keycode key)
        {
            int ID = this.CurrentID++;
            this.ShortcutNames.Add(ID, uniqueName);
            return Win32.RegisterHotKey(IntPtr.Zero, ID, modifiers, (uint)key);
        }

        public void SetCallback(ShortcutHandler handler) => this.ShortcutCallback = handler;

        public void Start()
        {
            this.ThreadID = Win32.GetCurrentThreadId();
            MessageLoop();
        }

        public void Stop()
        {
            Win32.PostThreadMessageA(this.ThreadID, WINDOWS_MESSAGE_ID_QUIT, IntPtr.Zero, IntPtr.Zero);
        }

        private void MessageLoop()
        {
            while (true)
            {
                int ReturnCode = Win32.GetMessage(out Win32.MSG msg, IntPtr.Zero, 0, 0);

                if (ReturnCode > 0) // Message
                {
                    Win32.TranslateMessage(ref msg);
                    Win32.DispatchMessage(ref msg);

                    if (msg.message == WINDOWS_MESSAGE_ID_HOTKEY)
                    {
                        int ShortcutID = (int)msg.wParam.ToInt64();
                        if (this.ShortcutNames.TryGetValue(ShortcutID, out string? ShortcutName))
                        {
                            Win32.KeyModifiers Modifiers = (Win32.KeyModifiers)(msg.lParam.ToInt64() & 0xFFFF);
                            Win32.Keycode Key = (Win32.Keycode)(msg.lParam.ToInt64() >> 16);
                            this.ShortcutCallback!.Invoke(ShortcutName, Modifiers, Key);
                        }
                    }
                    else if (msg.message == WINDOWS_MESSAGE_KEYDOWN)
                    {
                        long Keycode = msg.wParam.ToInt64();
                        long LPARAM = msg.lParam.ToInt64();
                        ushort RepeatCount = (ushort)(LPARAM & 0x7FFF);
                        byte ScanCode = (byte)((LPARAM >> 16) & 0xFF);
                        bool Extended = ((LPARAM >> 24) & 0b1) == 0b1;
                        bool PrevState = ((LPARAM >> 30) & 0b1) == 0b1;
                        Win32.Keycode Key = (Win32.Keycode)Keycode;
                        string KeyName = Enum.IsDefined(Key) ? Key.ToString() : "<UNKNOWN>";
                        Log.Debug($"Key pressed: VKC=0x{Keycode:X2} RepCnt={RepeatCount} ScanCode=0x{ScanCode:X2} Ext={(Extended ? '1' : '0')} Prev={(PrevState ? '1' : '0')} Raw=0x{LPARAM:X8} => Interpreted as {KeyName}");
                    }
                    else if (msg.message == WINDOWS_MESSAGE_ID_QUIT || msg.message == WINDOWS_MESSAGE_ID_CLOSE) { break; }
                }
                else if (ReturnCode < 0) { Log.Error("An error occured getting messages from Windows. You may need to restart ColorChord.NET."); } // Error
                else { break; } // Exiting
            }
        }

        private IntPtr CustomWindowProcedure(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            //Messages.Add(msg);
            //Console.WriteLine($"Message ID 0x{msg:X8} received");
            return Win32.DefWindowProcW(hWnd, msg, wParam, lParam);
        }

        /*public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (!_mDisposed)
            {
                if (disposing)
                {
                    // Dispose managed resources
                }

                // Dispose unmanaged resources
                if (WindowHandle != IntPtr.Zero)
                {
                    Win32.DestroyWindow(WindowHandle);
                    WindowHandle = IntPtr.Zero;
                }
            }
        }*/
    }
}
