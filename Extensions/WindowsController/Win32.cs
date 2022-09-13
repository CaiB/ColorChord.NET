using System;
using System.Runtime.InteropServices;

namespace ColorChord.NET.Extensions.WindowsController
{
    internal static class Win32
    {
        public delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WNDCLASS
        {
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpszMenuName;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpszClassName;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern ushort RegisterClassW([In] ref WNDCLASS lpWndClass);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr CreateWindowExW(uint dwExStyle, [MarshalAs(UnmanagedType.LPWStr)] string lpClassName, [MarshalAs(UnmanagedType.LPWStr)] string lpWindowName, uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam );

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool UpdateWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr DispatchMessage([In] ref MSG lpmsg);

        [DllImport("user32.dll")]
        public static extern bool TranslateMessage([In] ref MSG lpMsg);

        [DllImport("user32.dll")]
        public static extern sbyte GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, KeyModifiers fsModifiers, uint vkey);

        [Flags]
        public enum KeyModifiers
        {
            None = 0x0000,
            Alt = 0x0001,
            Ctrl = 0x0002,
            Shift = 0x0004,
            Win = 0x0008,
            NoRepeat = 0x4000 // Only sends the shortcut once, no matter how long you hold down the keys
        }

        public enum Keycode : byte
        {
            Backspace = 0x08,
            Tab = 0x09,
            Clear = 0x0C,
            Enter = 0x0D,
            Pause = 0x13,
            CapsLock = 0x14,
            Escape = 0x1B,
            Space = 0x20,
            PageUp = 0x21,
            PageDown = 0x22,
            End = 0x23,
            Home = 0x24,
            Left = 0x25,
            Up = 0x26,
            Right = 0x27,
            Down = 0x28,
            Select = 0x29,
            Print = 0x2A,
            Execute = 0x2B,
            PrintScreen = 0x2C,
            Insert = 0x2D,
            Delete = 0x2E,
            Help = 0x2F,
            Row0 = 0x30,
            Row1 = 0x31,
            Row2 = 0x32,
            Row3 = 0x33,
            Row4 = 0x34,
            Row5 = 0x35,
            Row6 = 0x36,
            Row7 = 0x37,
            Row8 = 0x38,
            Row9 = 0x39,
            A = 0x41,
            B = 0x42,
            C = 0x43,
            D = 0x44,
            E = 0x45,
            F = 0x46,
            G = 0x47,
            H = 0x48,
            I = 0x49,
            J = 0x4A,
            K = 0x4B,
            L = 0x4C,
            M = 0x4D,
            N = 0x4E,
            O = 0x4F,
            P = 0x50,
            Q = 0x51,
            R = 0x52,
            S = 0x53,
            T = 0x54,
            U = 0x55,
            V = 0x56,
            W = 0x57,
            X = 0x58,
            Y = 0x59,
            Z = 0x5A,
            //WindowsLeft = 0x5B,
            //WindowsRight = 0x5C,
            Applications = 0x5D,
            Sleep = 0x5F,
            Numpad0 = 0x60,
            Numpad1 = 0x61,
            Numpad2 = 0x62,
            Numpad3 = 0x63,
            Numpad4 = 0x64,
            Numpad5 = 0x65,
            Numpad6 = 0x66,
            Numpad7 = 0x67,
            Numpad8 = 0x68,
            Numpad9 = 0x69,
            Multiply = 0x6A,
            Add = 0x6B,
            Separator = 0x6C,
            Subtract = 0x6D,
            Decimal = 0x6E,
            Divide = 0x6F,
            F1 = 0x70,
            F2 = 0x71,
            F3 = 0x72,
            F4 = 0x73,
            F5 = 0x74,
            F6 = 0x75,
            F7 = 0x76,
            F8 = 0x77,
            F9 = 0x78,
            F10 = 0x79,
            F11 = 0x7A,
            F12 = 0x7B,
            F13 = 0x7C,
            F14 = 0x7D,
            F15 = 0x7E,
            F16 = 0x7F,
            F17 = 0x80,
            F18 = 0x81,
            F19 = 0x82,
            F20 = 0x83,
            F21 = 0x84,
            F22 = 0x85,
            F23 = 0x86,
            F24 = 0x87,
            NumLock = 0x90,
            ScrollLock = 0x91,
            OEM92 = 0x92,
            OEM93 = 0x93,
            OEM94 = 0x94,
            OEM95 = 0x95,
            OEM96 = 0x96,
            //ShiftLeft = 0xA0,
            //ShiftRight = 0xA1,
            //ControlLeft = 0xA2,
            //ControlRight = 0xA3,
            //AltLeft = 0xA4,
            //AltRight = 0xA5,
            BrowserBack = 0xA6,
            BrowserForward = 0xA7,
            BrowserRefresh = 0xA8,
            BrowserStop = 0xA9,
            BrowserSearch = 0xAA,
            BrowserFavourites = 0xAB,
            BrowserHome = 0xAC,
            VolumeMute = 0xAD,
            VolumeDown = 0xAE,
            VolumeUp = 0xAF,
            MediaNext = 0xB0,
            MediaPrev = 0xB1,
            MediaStop = 0xB2,
            MediaPlayPause = 0xB3,
            StartMail = 0xB4,
            SelectMedia = 0xB5,
            Application1 = 0xB6,
            Application2 = 0xB7,
            Misc1 = 0xBA, // US Layout: ;:
            Plus = 0xBB,
            Comma = 0xBC,
            Minus = 0xBD,
            Period = 0xBE,
            Misc2 = 0xBF, // US Layout: /?
            Misc3 = 0xC0, // US Layout: `~
            Misc4 = 0xDB, // US Layout: [{
            Misc5 = 0xDC, // US Layout: \|
            Misc6 = 0xDD, // US Layout: ]}
            Misc7 = 0xDE, // US Layout: '"
            Misc8 = 0xDF,
            OEME1 = 0xE1,
            Misc9 = 0xE2, // US Layout: <>   Other: \|
            OEME3 = 0xE3,
            OEME4 = 0xE4,
            OEME6 = 0xE6,
            OEME9 = 0xE9,
            OEMEA = 0xEA,
            OEMEB = 0xEB,
            OEMEC = 0xEC,
            OEMED = 0xED,
            OEMEE = 0xEE,
            OEMEF = 0xEF,
            OEMF0 = 0xF0,
            OEMF1 = 0xF1,
            OEMF2 = 0xF2,
            OEMF3 = 0xF3,
            OEMF4 = 0xF4,
            OEMF5 = 0xF5,
            Attn = 0xF6,
            CrSel = 0xF7,
            ExSel = 0xF8,
            EraseEOF = 0xF9,
            Play = 0xFA,
            Zoom = 0xFB,
            PA1 = 0xFD,
            OEMClear = 0xFE
        }
    }
}
