using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace ColorChord.NET.Outputs.DisplayD3D12Support;

public static partial class Win32API
{
    /// <summary>Retrieves a message from the calling thread's message queue. The function dispatches incoming sent messages until a posted message is available for retrieval.</summary>
    /// <remarks>Alternatively, <see cref="PeekMessage"/> does not wait for a message to be posted before returning.</remarks>
    /// <param name="message">A <see cref="Message"/> structure that receives message information from the thread's message queue.</param>
    /// <param name="windowHandle">
    /// A handle to the window whose messages are to be retrieved. The window must belong to the current thread.<br />
    /// If NULL, this retrieves messages for any window that belongs to the current thread, and any messages on the current thread's message queue whose hwnd value is NULL (see the <see cref="Message"/> structure). Therefore, both window messages and thread messages are processed.<br />
    /// If -1, this retrieves only messages on the current thread's message queue whose hwnd value is NULL, that is, thread messages as posted by PostMessage (when the hWnd parameter is NULL) or PostThreadMessage.
    /// </param>
    /// <param name="messageFilterMin">The integer value of the lowest message value to be retrieved. Use WM_KEYFIRST/WM_MOUSEFIRST to specify the first keyboard/mouse message ID. Set 0 for both min and max to receive all messages.</param>
    /// <param name="messageFilterMax">The integer value of the highest message value to be retrieved. Use WM_KEYLAST/WM_MOUSELAST to specify the last keyboard/mouse message ID. Set 0 for both min and max to receive all messages.</param>
    /// <returns>If the function retrieves a message other than WM_QUIT, the return value is nonzero. If the function retrieves the WM_QUIT message, the return value is zero. If there is an error, the return value is -1, and you can use <see cref="Marshal.GetLastWin32Error"/> to see why.</returns>
    [LibraryImport("user32.dll", SetLastError = true, EntryPoint = "GetMessageW")]
    public static partial int GetMessage(out Message message, IntPtr windowHandle, MessageID messageFilterMin, MessageID messageFilterMax);

    /// <summary>Dispatches incoming nonqueued messages, checks the thread message queue for a posted message, and retrieves the message (if any exist).</summary>
    /// <remarks>Alternatively, <see cref="GetMessage"/> waits for a message to be posted before returning if there are none.</remarks>
    /// <param name="message">A <see cref="Message"/> structure that receives message information.</param>
    /// <param name="windowHandle">
    /// A handle to the window whose messages are to be retrieved. The window must belong to the current thread.<br />
    /// If NULL, this retrieves messages for any window that belongs to the current thread, and any messages on the current thread's message queue whose hwnd value is NULL (see the <see cref="Message"/> structure). Therefore, both window messages and thread messages are processed.< br/>
    /// If -1, this retrieves only messages on the current thread's message queue whose hwnd value is NULL, that is, thread messages as posted by PostMessage (when the hWnd parameter is NULL) or PostThreadMessage.
    /// </param>
    /// <param name="messageFilterMin">The integer value of the lowest message value to be retrieved. Use WM_KEYFIRST/WM_MOUSEFIRST to specify the first keyboard/mouse message ID. Set 0 for both min and max to receive all messages.</param>
    /// <param name="messageFilterMax">The integer value of the highest message value to be retrieved. Use WM_KEYLAST/WM_MOUSELAST to specify the last keyboard/mouse message ID. Set 0 for both min and max to receive all messages.</param>
    /// <param name="handling">Specifies how messages are to be handled. See <see cref="PeekMessageHandling"/>.</param>
    /// <returns>Whether a message is available</returns>
    [LibraryImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.I4)]
    public static partial bool PeekMessage(out Message message, IntPtr windowHandle, MessageID messageFilterMin, MessageID messageFilterMax, PeekMessageHandling handling);

    /// <summary>Yields control to other threads when a thread has no other messages in its message queue. The WaitMessage function suspends the thread and does not return until a new message is placed in the thread's message queue.</summary>
    /// <remarks>Note that WaitMessage does not return if there is unread input in the message queue after the thread has called a function to check the queue. See Microsoft documentation for more details.</remarks>
    /// <returns>Whether the function suceeded. If false, use <see cref="Marshal.GetLastWin32Error"/> to determine the error.</returns>
    [LibraryImport("user32.dll", SetLastError = true, EntryPoint = "WaitMessage")]
    [return: MarshalAs(UnmanagedType.I4)]
    public static partial bool WaitMessage();

    /// <summary>Translates virtual-key messages into character messages. The character messages are posted to the calling thread's message queue</summary>
    /// <param name="message">A <see cref="Message"/> structure that contains message information retrieved from the calling thread's message queue by using the <see cref="GetMessage"/> or <see cref="PeekMessage"/> function.</param>
    /// <returns>Whether the message was translated (i.e. a character message was posted to the thread's message queue). If the message is <see cref="MessageID.WM_KEYDOWN"/>, <see cref="MessageID.WM_KEYUP"/>, <see cref="MessageID.WM_SYSKEYDOWN"/>, or <see cref="MessageID.WM_SYSKEYUP"/>, true is always returned.</returns>
    [LibraryImport("user32.dll", EntryPoint = "TranslateMessage")]
    [return: MarshalAs(UnmanagedType.I4)]
    public static partial bool TranslateMessage(Message message);

    /// <summary>Dispatches a message to a window procedure. It is typically used to dispatch a message retrieved by <see cref="GetMessage"/>.</summary>
    /// <param name="message">A <see cref="Message"/> structure that contains message information.</param>
    /// <returns>The return value specifies the value returned by the window procedure. Although its meaning depends on the message being dispatched, the return value generally is ignored.</returns>
    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    [return: MarshalAs(UnmanagedType.I4)]
    public static partial bool DispatchMessage(Message message);

    /// <summary>Places (posts) a message in the message queue associated with the thread that created the specified window and returns without waiting for the thread to process the message.</summary>
    /// <remarks>To post a message in the message queue associated with a thread, use the <see cref="PostThreadMessage"/> function.</remarks>
    /// <param name="windowHandle">
    /// A handle to the window whose window procedure is to receive the message.<br />
    /// If 0xFFFF (broadcast), the message is posted to all top-level windows in the system, including disabled or invisible unowned windows, overlapped windows, and pop-up windows. The message is not posted to child windows.<br />
    /// If 0 (NULL), the function behaves like a call to <see cref="PostThreadMessage"/> with the threadID parameter set to the identifier of the current thread.
    /// </param>
    /// <param name="messageID">The type of message to be posted. If the message is <see cref="MessageID.WM_QUIT"/>, you should use <see cref="PostQuitMessage(int)"/> instead.</param>
    /// <param name="wParam">Additional message information. Contents depend on the message type.</param>
    /// <param name="lParam">Additional message information. Contents depend on the message type.</param>
    /// <returns>Whether posting the message succeeded. If it failed, use <see cref="Marshal.GetLastWin32Error"/> to see why.</returns>
    [LibraryImport("user32.dll", SetLastError = true, EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.I4)]
    public static partial bool PostMessage(IntPtr windowHandle, MessageID messageID, nuint wParam, nint lParam);

    /// <summary>Posts a message to the message queue of the specified thread. It returns without waiting for the thread to process the message.</summary>
    /// <param name="threadID">The identifier of the thread to which the message is to be posted.</param>
    /// <param name="messageID">The type of message to be posted.</param>
    /// <param name="wParam">Additional message information. Contents depend on the message type.</param>
    /// <param name="lParam">Additional message information. Contents depend on the message type.</param>
    /// <returns>Whether posting the message succeeded. If it failed, use <see cref="Marshal.GetLastWin32Error"/> to see why.</returns>
    [LibraryImport("user32.dll", SetLastError = true, EntryPoint = "PostThreadMessageW")]
    [return: MarshalAs(UnmanagedType.I4)]
    public static partial bool PostThreadMessage(uint threadID, MessageID messageID, nuint wParam, nint lParam);

    /// <summary>Indicates to the system that a thread has made a request to terminate (quit). It is typically used in response to a WM_DESTROY message.</summary>
    /// <remarks>The PostQuitMessage function posts a WM_QUIT message to the thread's message queue and returns immediately; the function simply indicates to the system that the thread is requesting to quit at some time in the future.</remarks>
    /// <param name="exitCode">The application exit code. This value is used as the wParam parameter of the <see cref="MessageID.WM_QUIT"/> message.</param>
    [LibraryImport("user32.dll", EntryPoint = "PostQuitMessage")]
    public static partial void PostQuitMessage(int exitCode);

    /// <summary>Retrieves the thread identifier of the calling thread.</summary>
    /// <remarks>Until the thread terminates, the thread identifier uniquely identifies the thread throughout the system.</remarks>
    /// <returns>The thread identifier of the calling thread.</returns>
    [LibraryImport("kernel32.dll", EntryPoint = "GetCurrentThreadId")]
    public static partial uint GetCurrentThreadID();

    /// <summary>Retrieves the instance handle of the ScarletUI DLL.</summary>
    /// <returns>The instance handle.</returns>
    public static IntPtr GetDLLInstance() => Marshal.GetHINSTANCE(typeof(Win32API).Module);

    /// <summary>Creates a logical brush that has the specified solid color.</summary>
    /// <param name="colour">The color of the brush, in format 0x00BBGGRR.</param>
    /// <returns>The handle to the brush.</returns>
    [LibraryImport("gdi32.dll", EntryPoint = "CreateSolidBrush")]
    public static partial IntPtr CreateSolidBrush(uint colour);

    /// <summary>Deletes a logical pen, brush, font, bitmap, region, or palette, freeing all system resources associated with the object. After the object is deleted, the specified handle is no longer valid.</summary>
    /// <remarks>Do not delete a drawing object (pen or brush) while it is still selected into a DC. When a pattern brush is deleted, the bitmap associated with the brush is not deleted. The bitmap must be deleted independently.</remarks>
    /// <param name="objectHandle">A handle to a logical pen, brush, font, bitmap, region, or palette.</param>
    /// <returns>If the function succeeds, the return value is true. If the specified handle is not valid or is currently selected into a DC, the return value is false.</returns>
    [LibraryImport("gdi32.dll", EntryPoint = "DeleteObject")]
    [return: MarshalAs(UnmanagedType.I4)]
    public static partial bool DeleteObject(IntPtr objectHandle);

    /// <summary>Registers a window class for subsequent use in calls to the CreateWindow or CreateWindowEx function.</summary>
    /// <param name="classDefinition">A <see cref="WindowClass"/> structure. You must fill the structure with the appropriate class attributes before passing it to the function.</param>
    /// <returns>Non-zero if the function succeeded. This value is a class atom that is used by other functions to uniquely identify this window class. Returns 0 on failure, use <see cref="Marshal.GetLastWin32Error"/> to see why.</returns>
    [LibraryImport("user32.dll", SetLastError = true, EntryPoint = "RegisterClassExW")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvStdcall) })]
    public static partial ushort RegisterClassEx([MarshalUsing(typeof(WindowClassMarshaler))] WindowClass classDefinition);

    /// <summary>Creates an overlapped, pop-up, or child window with an extended window style.</summary>
    /// <param name="windowStyleEx">The extended window style of the window being created.</param>
    /// <param name="windowClassID">The class atom obtained from <see cref="RegisterClassEx"/>.</param>
    /// <param name="windowName">The window name. Shown in the title bar if one is requested.</param>
    /// <param name="windowStyle">The style of the window being created.</param>
    /// <param name="x">The initial horizontal position of the upper-left corner of the window. For overlapped windows, 0x80000000 uses a system-default X and Y location.</param>
    /// <param name="y">The initial vertical position of the upper-left corner of the window. Has special meaning in some cases, see Microsoft documentation.</param>
    /// <param name="width">The width, in device units, of the window. Has special meaning in some cases, see Microsoft documentation.</param>
    /// <param name="height">The height, in device units, of the window. Has special meaning in some cases, see Microsoft documentation.</param>
    /// <param name="parentWindowHandle">A handle to the parent or owner window of the window being created.</param>
    /// <param name="menu">A handle to a menu, or specifies a child-window identifier, depending on the window style.</param>
    /// <param name="instance">A handle to the instance of the module to be associated with the window.</param>
    /// <param name="additionalData">Pointer to a value to be passed to the window through the CREATESTRUCT structure (lpCreateParams member) pointed to by the lParam param of the WM_CREATE message. This message is sent to the created window by this function before it returns.</param>
    /// <returns>If the function succeeds, the return value is a handle to the new window. Otherwise, NULL is returned and error information is available from <see cref="Marshal.GetLastWin32Error"/>.</returns>
    [LibraryImport("user32.dll", SetLastError = true, EntryPoint = "CreateWindowExW")]
    public static partial IntPtr CreateWindowEx(WindowStyleEx windowStyleEx, nuint windowClassID, [MarshalAs(UnmanagedType.LPWStr)] string? windowName, WindowStyle windowStyle, int x, int y, int width, int height, IntPtr parentWindowHandle, IntPtr menu, IntPtr instance, IntPtr additionalData);

    /// <summary>Calls the default window procedure to provide default processing for any window messages that an application does not process.</summary>
    /// <param name="windowHandle">A handle to the window procedure that received the message.</param>
    /// <param name="messageID">The message.</param>
    /// <param name="wParam">Additional message information. The content of this parameter depends on the value of the <see cref="messageID"/> parameter.</param>
    /// <param name="lParam">Additional message information. The content of this parameter depends on the value of the <see cref="messageID"/> parameter.</param>
    /// <returns>The result of the message processing, depends on the message.</returns>
    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    public static partial nint DefWindowProc(IntPtr windowHandle, MessageID messageID, nuint wParam, nint lParam);

    /// <summary>Loads the specified cursor resource from the executable (.EXE) file associated with an application instance.</summary>
    /// <param name="instanceHandle">A handle to an instance of the module whose executable file contains the cursor to be loaded.</param>
    /// <param name="cursorName">The name of the cursor resource to be loaded.</param>
    /// <returns>Handle to the loaded cursor, if loading suceeded. Otherwise, NULL. In that case, error information is available from <see cref="Marshal.GetLastWin32Error"/>.</returns>
    [LibraryImport("user32.dll", EntryPoint = "LoadCursorW")]
    public static partial IntPtr LoadCursor(IntPtr instanceHandle, [MarshalAs(UnmanagedType.LPWStr)] string cursorName);

    /// <summary>Loads one of the predefined, built-in cursors.</summary>
    /// <param name="cursor">The cursor to load, must be one of the options in <see cref="NativeCursorIndex"/>.</param>
    /// <returns>Handle to the loaded cursor, if loading suceeded. Otherwise, NULL. In that case, error information is available from <see cref="Marshal.GetLastWin32Error"/>.</returns>
    public static IntPtr LoadCursor(NativeCursorIndex cursor) => LoadCursor(IntPtr.Zero, (nint)cursor);
    
    [LibraryImport("user32.dll", EntryPoint = "LoadCursorW")]
    private static partial IntPtr LoadCursor(IntPtr instanceHandle, nint cursor);

    /// <summary>Enables or disables mouse and keyboard input to the specified window or control. When input is disabled, the window does not receive input such as mouse clicks and key presses. When input is enabled, the window receives all input.</summary>
    /// <param name="windowHandle">A handle to the window to be enabled or disabled.</param>
    /// <param name="enable">Indicates whether to enable (true) or disable (false) the window.</param>
    /// <returns>Whether the window was previously enabled (false), or disabled (true).</returns>
    [LibraryImport("user32.dll", EntryPoint = "EnableWindow")]
    [return: MarshalAs(UnmanagedType.I4)]
    public static partial bool EnableWindow(IntPtr windowHandle, [MarshalAs(UnmanagedType.I4)] bool enable);

    /// <summary>Retrieves the coordinates of a window's client area. The client coordinates specify the upper-left and lower-right corners of the client area.</summary>
    /// <param name="windowHandle">A handle to the window whose client coordinates are to be retrieved.</param>
    /// <param name="rect">The client coordinates. The left and top members are zero. The right and bottom members contain the width and height of the window.</param>
    /// <returns>If the call succeeded. If it failed, use <see cref="Marshal.GetLastWin32Error"/> to see why.</returns>
    [LibraryImport("user32.dll", EntryPoint = "GetClientRect")]
    [return: MarshalAs(UnmanagedType.I4)]
    public static partial bool GetClientRect(IntPtr windowHandle, out RectI32 rect);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowRect")]
    [return: MarshalAs(UnmanagedType.I4)]
    public static partial bool GetWindowRect(IntPtr windowHandle, out RectI32 rect);

    [LibraryImport("user32.dll", EntryPoint = "AdjustWindowRectEx")]
    [return: MarshalAs(UnmanagedType.I4)]
    public static partial bool AdjustWindowRectEx(RectI32 rect, uint windowStyle, [MarshalAs(UnmanagedType.I4)] bool hasMenu, uint extendedWindowStyle);

    [LibraryImport("user32.dll", EntryPoint = "MoveWindow")]
    [return: MarshalAs(UnmanagedType.I4)]
    public static partial bool MoveWindow(IntPtr windowHandle, int x, int y, int width, int height, [MarshalAs(UnmanagedType.I4)] bool repaint);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
    public static partial int GetWindowLongW(IntPtr windowHandle, int index);

    /// <summary>A callback function, which you define in your application, that processes messages sent to a window.</summary>
    /// <param name="windowHandle">A handle to the window.</param>
    /// <param name="messageID">The type of message.</param>
    /// <param name="wParam">Additional message information. Contents depend on the message type.</param>
    /// <param name="lParam">Additional message information. Contents depend on the message type.</param>
    /// <returns>The result of the message processing, depends on the message sent.</returns>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate nint WindowProcedure(IntPtr windowHandle, MessageID messageID, nuint wParam, nint lParam);
}
