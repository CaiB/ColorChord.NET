using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Text;

namespace ColorChord.NET.Outputs.DisplayD3D12Support;

public static unsafe class MarshalHelpers
{
public static unsafe string? Win32WideCharArrToString(char* unmanagedArr)
{
    if (unmanagedArr == null) { return null; }
    int Length = 0;
    while (*(unmanagedArr + Length) != 0x0000) { Length++; }
    return Encoding.Unicode.GetString((byte*)unmanagedArr, Length * sizeof(char));
}
}

[CustomMarshaller(typeof(WindowClass), MarshalMode.UnmanagedToManagedIn, typeof(WindowClassMarshaler))]
[CustomMarshaller(typeof(WindowClass), MarshalMode.ManagedToUnmanagedIn, typeof(ManagedToUnmanagedIn))]
internal static unsafe class WindowClassMarshaler
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal unsafe struct WindowClassUnmanaged
    {
        public uint StructSize;
        public uint Style;
        public IntPtr WindowProcedure;
        public int ClassAdditionalBytes;
        public int WindowAdditionalBytes;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr BackgroundBrush;
        public char* ClassMenuResourceName;
        public char* ClassName;
        public IntPtr SmallIcon;
    }

    internal static unsafe WindowClass ConvertToManaged(WindowClassUnmanaged unmanaged)
    {
        return new()
        {
            StructSize = unmanaged.StructSize,
            Style = (WindowClassStyle)unmanaged.Style,
            WindowProcedure = Marshal.GetDelegateForFunctionPointer<Win32API.WindowProcedure>(unmanaged.WindowProcedure),
            ClassAdditionalBytes = unmanaged.ClassAdditionalBytes,
            WindowAdditionalBytes = unmanaged.WindowAdditionalBytes,
            Instance = unmanaged.Instance,
            Icon = unmanaged.Icon,
            Cursor = unmanaged.Cursor,
            BackgroundBrush = new(unmanaged.BackgroundBrush),
            ClassMenuResourceName = MarshalHelpers.Win32WideCharArrToString(unmanaged.ClassMenuResourceName),
            ClassName = MarshalHelpers.Win32WideCharArrToString(unmanaged.ClassName),
            SmallIcon = unmanaged.SmallIcon
        };
    }

    internal unsafe ref struct ManagedToUnmanagedIn
    {
        public static int BufferSize => sizeof(WindowClassUnmanaged);

        private byte* UnmanagedBufferStruct;
        private char* UnmanagedStrResourceName, UnmanagedStrClassName;

        public void FromManaged(WindowClass managed, Span<byte> buffer)
        {
            IntPtr WindowProcedure = (managed.WindowProcedure == null) ? IntPtr.Zero : Marshal.GetFunctionPointerForDelegate(managed.WindowProcedure);
            this.UnmanagedStrResourceName = (managed.ClassMenuResourceName == null) ? null : (char*)Marshal.StringToHGlobalUni(managed.ClassMenuResourceName);
            this.UnmanagedStrClassName = (managed.ClassName == null) ? null : (char*)Marshal.StringToHGlobalUni(managed.ClassName);

            WindowClassUnmanaged Result = new()
            {
                StructSize = managed.StructSize,
                Style = (uint)managed.Style,
                WindowProcedure = WindowProcedure,
                ClassAdditionalBytes = managed.ClassAdditionalBytes,
                WindowAdditionalBytes = managed.WindowAdditionalBytes,
                Instance = managed.Instance,
                Icon = managed.Icon,
                Cursor = managed.Cursor,
                BackgroundBrush = managed.BackgroundBrush,
                ClassMenuResourceName = this.UnmanagedStrResourceName,
                ClassName = this.UnmanagedStrClassName,
                SmallIcon = managed.SmallIcon
            };

            Span<byte> ResultByteView = MemoryMarshal.Cast<WindowClassUnmanaged, byte>(MemoryMarshal.CreateSpan(ref Result, 1));
            Debug.Assert(buffer.Length >= ResultByteView.Length, "Target buffer isn't large enough to hold the struct data.");
            ResultByteView.CopyTo(buffer);

            this.UnmanagedBufferStruct = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(buffer));
        }

        public byte* ToUnmanaged() => this.UnmanagedBufferStruct;

        public void Free()
        {
            if (this.UnmanagedStrResourceName != null)
            {
                Marshal.FreeHGlobal((nint)this.UnmanagedStrResourceName);
                this.UnmanagedStrResourceName = null;
            }
            if (this.UnmanagedStrClassName != null)
            {
                Marshal.FreeHGlobal((nint)this.UnmanagedStrClassName);
                this.UnmanagedStrClassName = null;
            }
        }
    }
}
