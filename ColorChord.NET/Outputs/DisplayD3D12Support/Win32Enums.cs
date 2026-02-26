using System;

namespace ColorChord.NET.Outputs.DisplayD3D12Support;

/// <summary>Values for commonly used Windows messages.</summary>
/// <remarks>
/// IDs 0x0000-0x3FFF are reserved for use by the system.<br />
/// IDs 0x0400-0x7FFF are for use by private window classes.<br />
/// IDs 0x8000-0xBFFF are available for use by any application.<br />
/// IDs 0xC000-0xFFFF are string messages for use by applications.<br />
/// </remarks>
public enum MessageID : uint
{
    /// <summary>Performs no operation. An application sends the <see cref="WM_NULL"/> message if it wants to post a message that the recipient window will ignore.</summary>
    WM_NULL = 0x0000,
    /// <summary>Sent when an application requests that a window be created by calling the CreateWindowEx or CreateWindow function.</summary>
    WM_CREATE = 0x0001,
    /// <summary>Sent when a window is being destroyed. It is sent to the window procedure of the window being destroyed after the window is removed from the screen.</summary>
    WM_DESTROY = 0x0002,
    /// <summary>Sent after a window has been moved.</summary>
    WM_MOVE = 0x0003,
    /// <summary>Sent to a window after its size has changed.</summary>
    WM_SIZE = 0x0005,
    /// <summary>Sent to both the window being activated and the window being deactivated.</summary>
    WM_ACTIVATE = 0x0006,
    /// <summary>Sent to a window after it has gained the keyboard focus.</summary>
    WM_SETFOCUS = 0x0007,
    /// <summary>Sent to a window immediately before it loses the keyboard focus.</summary>
    WM_KILLFOCUS = 0x0008,
    /// <summary>Sent when an application changes the enabled state of a window. It is sent to the window whose enabled state is changing.</summary>
    WM_ENABLE = 0x000A,
    /// <summary>Sent to a window to allow changes in that window to be redrawn, or to prevent changes in that window from being redrawn.</summary>
    WM_SETREDRAW = 0x000B,
    /// <summary>Sets the text of a window or control.</summary>
    WM_SETTEXT = 0x000C,
    /// <summary>Copies the text that corresponds to a window or control into a buffer provided by the caller.</summary>
    WM_GETTEXT = 0x000D,
    /// <summary>Determines the length, in characters, of the text associated with a window or control.</summary>
    WM_GETTEXTLENGTH = 0x000E,
    /// <summary>Sent when the system or another application makes a request to paint a portion of an application's window.</summary>
    WM_PAINT = 0x000F,
    /// <summary>Sent as a signal that a window or an application should terminate.</summary>
    WM_CLOSE = 0x0010,
    /// <summary>Sent when the user chooses to end the session or when an application calls one of the system shutdown functions.</summary>
    WM_QUERYENDSESSION = 0x0011,
    /// <summary>Indicates a request to terminate an application.</summary>
    /// <remarks>When the thread retrieves this from its message queue, it should exit its message loop and return control to the system. The exit value returned to the system must be the wParam parameter of the <see cref="WM_QUIT"/> message.</remarks>
    WM_QUIT = 0x0012,
    /// <summary>Sent to an icon when the user requests that the window be restored to its previous size and position.</summary>
    WM_QUERYOPEN = 0x0013,
    /// <summary>Sent when the window background must be erased (for example, when a window is resized). The message is sent to prepare an invalidated portion of a window for painting.</summary>
    WM_ERASEBKGND = 0x0014,
    /// <summary>Sent to all top-level windows when a change is made to a system color setting.</summary>
    WM_SYSCOLORCHANGE = 0x0015,
    /// <summary>Sent to an application after the system processes the results of the <see cref="WM_QUERYENDSESSION"/> message to inform the application whether the session is ending.</summary>
    WM_ENDSESSION = 0x0016,
    /// <summary>Sent to a window when the window is about to be hidden or shown.</summary>
    WM_SHOWWINDOW = 0x0018,
    /// <summary>Ssed in 16-bit versions of Windows to change the color scheme of various controls.</summary>
    [Obsolete("The WM_CTLCOLOR message from 16-bit Windows has been replaced by more specific notifications.")]
    WM_CTLCOLOR = 0x0019,
    /// <summary>An application sends this message to all top-level windows after making a change to the WIN.INI file.</summary>
    [Obsolete("Applications should use the WM_SETTINGCHANGE message.")]
    WM_WININICHANGE = 0x001A,
    /// <summary>Sent to all top-level windows when the SystemParametersInfo function changes a system-wide setting or when policy settings have changed.</summary>
    WM_SETTINGCHANGE = 0x001A,
    /// <summary>Sent to all top-level windows whenever the user changes device-mode settings.</summary>
    WM_DEVMODECHANGE = 0x001B,
    /// <summary>Sent when a window belonging to a different application than the active window is about to be activated.</summary>
    WM_ACTIVATEAPP = 0x001C,
    /// <summary>An application sends this to all top-level windows in the system after changing the pool of font resources.</summary>
    WM_FONTCHANGE = 0x001D,
    /// <summary>Sent whenever there is a change in the system time.</summary>
    WM_TIMECHANGE = 0x001E,
    /// <summary>Sent to cancel certain modes, such as mouse capture.</summary>
    WM_CANCELMODE = 0x001F,
    /// <summary>Sent to a window if the mouse causes the cursor to move within a window and mouse input is not captured.</summary>
    WM_SETCURSOR = 0x0020,
    /// <summary>Sent when the cursor is in an inactive window and the user presses a mouse button.</summary>
    WM_MOUSEACTIVATE = 0x0021,
    /// <summary>Sent to a child window when the user clicks the window's title bar or when the window is activated, moved, or sized.</summary>
    WM_CHILDACTIVATE = 0x0022,
    /// <summary>Sent by a computer-based training (CBT) application to separate user-input messages from other messages sent through the WH_JOURNALPLAYBACK procedure.</summary>
    [Obsolete("Journaling Hooks APIs are unsupported starting in Windows 11 and will be removed in a future release.")]
    WM_QUEUESYNC = 0x0023,
    /// <summary>Sent to a window when the size or position of the window is about to change.</summary>
    WM_GETMINMAXINFO = 0x0024,
    /// <summary>Sent to a dialog box procedure to set the keyboard focus to a different control in the dialog box.</summary>
    WM_NEXTDLGCTL = 0x0028,
    /// <summary>Sent from Print Manager whenever a job is added to or removed from the Print Manager queue.</summary>
    [Obsolete("Not supported after Windows XP")]
    WM_SPOOLERSTATUS = 0x002A,
    /// <summary>Sent to the parent window of an owner-drawn button, combo box, list box, or menu when a visual aspect of the button, combo box, list box, or menu has changed.</summary>
    WM_DRAWITEM = 0x002B,
    /// <summary>Sent to the owner window of a combo box, list box, list-view control, or menu item when the control or menu is created.</summary>
    WM_MEASUREITEM = 0x002C,
    /// <summary>Sent to the owner of a list box or combo box when the list box or combo box is destroyed or when items are removed, for each deleted item.</summary>
    WM_DELETEITEM = 0x002D,
    /// <summary>Sent by a list box with the LBS_WANTKEYBOARDINPUT style to its owner in response to a WM_KEYDOWN message.</summary>
    WM_VKEYTOITEM = 0x002E,
    /// <summary>Sent by a list box with the LBS_WANTKEYBOARDINPUT style to its owner in response to a WM_CHAR message.</summary>
    WM_CHARTOITEM = 0x002F,

    /// <summary>Sets the font that a control is to use when drawing text.</summary>
    WM_SETFONT = 0x0030,
    /// <summary>Retrieves the font with which the control is currently drawing its text.</summary>
    WM_GETFONT = 0x0031,
    /// <summary>Sent to a window to associate a hot key with the window. When the user presses the hot key, the system activates the window.</summary>
    WM_SETHOTKEY = 0x0032,
    /// <summary>Sent to determine the hot key associated with a window.</summary>
    WM_GETHOTKEY = 0x0033,
    /// <summary>Sent to a minimized (iconic) window. The window is about to be dragged by the user but does not have an icon defined for its class, this requests one.</summary>
    WM_QUERYDRAGICON = 0x0037,
    /// <summary>Sent to determine the relative position of a new item in the sorted list of an owner-drawn combo box or list box.</summary>
    WM_COMPAREITEM = 0x0039,
    /// <summary>Sent to all top-level windows when the system detects more than 12.5 percent of system time over a 30- to 60-second interval is being spent compacting memory. This indicates that system memory is low.</summary>
    [Obsolete("Only for compatibility with 16-bit Windows-based applications.")]
    WM_COMPACTING = 0x0041,
    /// <summary>Sent to a window whose size, position, or place in the Z order is about to change.</summary>
    WM_WINDOWPOSCHANGING = 0x0046,
    /// <summary>Sent to a window whose size, position, or place in the Z order has changed</summary>
    WM_WINDOWPOSCHANGED = 0x0047,
    /// <summary>Notifies applications that the system, typically a battery-powered personal computer, is about to enter a suspended mode.</summary>
    [Obsolete("Only for compatibility with 16-bit Windows-based applications. Applications should use the WM_POWERBROADCAST message.")]
    WM_POWER = 0x0048,
    /// <summary>Used by one application to pass data to another application.</summary>
    WM_COPYDATA = 0x004A,
    /// <summary>Posted to an application when a user cancels the application's journaling activities.</summary>
    [Obsolete("Journaling Hooks APIs are unsupported starting in Windows 11 and will be removed in a future release.")]
    WM_CANCELJOURNAL = 0x004B,
    /// <summary>Sent by a common control to its parent window when an event has occurred or the control requires some information.</summary>
    WM_NOTIFY = 0x004E,
    /// <summary>Posted to the window with the focus when the user chooses a new input language, either with the hotkey or from the indicator on the system taskbar.</summary>
    WM_INPUTLANGCHANGEREQUEST = 0x0050,
    /// <summary>Sent to the topmost affected window after an application's input language has been changed.</summary>
    WM_INPUTLANGCHANGE = 0x0051,
    /// <summary>Sent to an application that has initiated a training card with Windows Help.</summary>
    WM_TCARD = 0x0052,
    /// <summary>Indicates that the user pressed the F1 key.</summary>
    WM_HELP = 0x0053,
    /// <summary>Sent to all windows after the user has logged on or off.</summary>
    WM_USERCHANGED = 0x0054,
    /// <summary>Determines if a window accepts ANSI or Unicode structures in the WM_NOTIFY notification message.</summary>
    WM_NOTIFYFORMAT = 0x0055,
    /// <summary>Notifies a window that the user desires a context menu to appear.</summary>
    WM_CONTEXTMENU = 0x007B,
    /// <summary>Sent to a window when the SetWindowLong function is about to change one or more of the window's styles.</summary>
    WM_STYLECHANGING = 0x007C,
    /// <summary>Sent to a window after the SetWindowLong function has changed one or more of the window's styles.</summary>
    WM_STYLECHANGED = 0x007D,
    /// <summary>The WM_DISPLAYCHANGE message is sent to all windows when the display resolution has changed.</summary>
    WM_DISPLAYCHANGE = 0x007E,
    /// <summary>Sent to a window to retrieve a handle to the large or small icon associated with a window.</summary>
    WM_GETICON = 0x007F,
    /// <summary>Associates a new large or small icon with a window.</summary>
    WM_SETICON = 0x0080,

    /// <summary>Sent prior to the WM_CREATE message when a window is first created.</summary>
    WM_NCCREATE = 0x0081,
    /// <summary>Notifies a window that its nonclient area is being destroyed. Sent after WM_DESTROY.</summary>
    WM_NCDESTROY = 0x0082,
    /// <summary>Sent when the size and position of a window's client area must be calculated.</summary>
    WM_NCCALCSIZE = 0x0083,
    /// <summary>Sent to a window in order to determine what part of the window corresponds to a particular screen coordinate.</summary>
    WM_NCHITTEST = 0x0084,
    /// <summary>The WM_NCPAINT message is sent to a window when its frame must be painted.</summary>
    WM_NCPAINT = 0x0085,
    /// <summary>Sent to a window when its nonclient area needs to be changed to indicate an active or inactive state.</summary>
    WM_NCACTIVATE = 0x0086,
    /// <summary>Sent to the window procedure associated with a control for input it has registered custom handling for.</summary>
    WM_GETDLGCODE = 0x0087,
    /// <summary>Used to synchronize painting while avoiding linking independent GUI threads.</summary>
    WM_SYNCPAINT = 0x0088,
    /// <summary>Posted to a window when the cursor is moved within the nonclient area of the window.</summary>
    WM_NCMOUSEMOVE = 0x00A0,
    /// <summary>Posted when the user presses the left mouse button while the cursor is within the nonclient area of a window.</summary>
    WM_NCLBUTTONDOWN = 0x00A1,
    /// <summary>Posted when the user releases the left mouse button while the cursor is within the nonclient area of a window.</summary>
    WM_NCLBUTTONUP = 0x00A2,
    /// <summary>Posted when the user double-clicks the left mouse button while the cursor is within the nonclient area of a window.</summary>
    WM_NCLBUTTONDBLCLK = 0x00A3,
    /// <summary>Posted when the user presses the right mouse button while the cursor is within the nonclient area of a window.</summary>
    WM_NCRBUTTONDOWN = 0x00A4,
    /// <summary>Posted when the user releases the right mouse button while the cursor is within the nonclient area of a window.</summary>
    WM_NCRBUTTONUP = 0x00A5,
    /// <summary>Posted when the user double-clicks the right mouse button while the cursor is within the nonclient area of a window.</summary>
    WM_NCRBUTTONDBLCLK = 0x00A6,
    /// <summary>Posted when the user presses the middle mouse button while the cursor is within the nonclient area of a window.</summary>
    WM_NCMBUTTONDOWN = 0x00A7,
    /// <summary>Posted when the user releases the middle mouse button while the cursor is within the nonclient area of a window.</summary>
    WM_NCMBUTTONUP = 0x00A8,
    /// <summary>Posted when the user double-clicks the middle mouse button while the cursor is within the nonclient area of a window.</summary>
    WM_NCMBUTTONDBLCLK = 0x00A9,
    /// <summary>Posted when the user presses the first or second X button while the cursor is in the nonclient area of a window.</summary>
    WM_NCXBUTTONDOWN = 0x00AB,
    /// <summary>Posted when the user releases the first or second X button while the cursor is in the nonclient area of a window.</summary>
    WM_NCXBUTTONUP = 0x00AC,
    /// <summary>Posted when the user double-clicks the first or second X button while the cursor is in the nonclient area of a window.</summary>
    WM_NCXBUTTONDBLCLK = 0x00AD,
    
    /// <summary>Sent to the window that is getting raw input.</summary>
    WM_INPUT = 0x00FF,
    
    /// <summary>This is not a real message, but rather marks the beginning of the range of message IDs related to keyboard input.</summary>
    WM_KEYFIRST = 0x0100,
    /// <summary>Posted to the window with the keyboard focus when a nonsystem key is pressed (while the ALT key is not pressed).</summary>
    WM_KEYDOWN = 0x0100,
    /// <summary>Posted to the window with the keyboard focus when a nonsystem key is released (while the ALT key is not pressed).</summary>
    WM_KEYUP = 0x0101,
    /// <summary>Posted to the window with the keyboard focus when a <see cref="WM_KEYDOWN"/> message is translated by the TranslateMessage function.</summary>
    WM_CHAR = 0x0102,
    /// <summary>Posted to the window with the keyboard focus when a WM_KEYUP message is translated by the TranslateMessage function, for a character that is part of a composite character.</summary>
    WM_DEADCHAR = 0x0103,
    /// <summary>Posted to the window with the keyboard focus when the user presses the F10 key (which activates the menu bar) or holds down the ALT key and then presses another key.</summary>
    WM_SYSKEYDOWN = 0x0104,
    /// <summary>Posted to the window with the keyboard focus when the user releases a key that was pressed while the ALT key was held down.</summary>
    WM_SYSKEYUP = 0x0105,
    /// <summary>Posted to the window with the keyboard focus when a WM_SYSKEYDOWN message is translated by the TranslateMessage function, for a character entered while ALT is held down.</summary>
    WM_SYSCHAR = 0x0106,
    /// <summary>Sent to the window with the keyboard focus when a WM_SYSKEYDOWN message is translated by the TranslateMessage function, for a dead character entered while ALT is held down.</summary>
    WM_SYSDEADCHAR = 0x0107,
    /// <summary>Can be used by an application to post input to other windows.</summary>
    WM_UNICHAR = 0x0109,
    /// <summary>This is not a real message, but rather marks the end of the range of message IDs related to keyboard input.</summary>
    WM_KEYLAST = 0x0109,

    /// <summary>Sent immediately before the IME generates the composition string as a result of a keystroke.</summary>
    WM_IME_STARTCOMPOSITION = 0x010D,
    /// <summary>Sent to an application when the IME ends composition.</summary>
    WM_IME_ENDCOMPOSITION = 0x010E,
    /// <summary>Sent to an application when the IME changes composition status as a result of a keystroke.</summary>
    WM_IME_COMPOSITION = 0x010F,

    /// <summary>Sent to the dialog box procedure immediately before a dialog box is displayed.</summary>
    WM_INITDIALOG = 0x0110,
    /// <summary>Sent when the user selects a command item from a menu, when a control sends a notification message to its parent window, or when an accelerator keystroke is translated.</summary>
    WM_COMMAND = 0x0111,
    /// <summary>A window receives this message when the user chooses a command from the Window menu (formerly known as the system or control menu) or when the user chooses the maximize button, minimize button, restore button, or close button.</summary>
    WM_SYSCOMMAND = 0x0112,
    /// <summary>Posted to the installing thread's message queue when a timer expires.</summary>
    WM_TIMER = 0x0113,
    /// <summary>Sent to a window when a scroll event occurs in the window's standard horizontal scroll bar</summary>
    WM_HSCROLL = 0x0114,
    /// <summary>Sent to a window when a scroll event occurs in the window's standard vertical scroll bar.</summary>
    WM_VSCROLL = 0x0115,
    /// <summary>Sent when a menu is about to become active. It occurs when the user clicks an item on the menu bar or presses a menu key.</summary>
    WM_INITMENU = 0x0116,
    /// <summary>Sent when a drop-down menu or submenu is about to become active.</summary>
    WM_INITMENUPOPUP = 0x0117,
    /// <summary>Sent to a menu's owner window when the user selects a menu item.</summary>
    WM_MENUSELECT = 0x011F,
    /// <summary>Sent when a menu is active and the user presses a key that does not correspond to any mnemonic or accelerator key.</summary>
    WM_MENUCHAR = 0x0120,
    /// <summary>Sent to the owner window of a modal dialog box or menu that is entering an idle state.</summary>
    WM_ENTERIDLE = 0x0121,
    /// <summary>Sent when the user releases the right mouse button while the cursor is on a menu item.</summary>
    WM_MENURBUTTONUP = 0x0122,
    /// <summary>Sent to the owner of a drag-and-drop menu when the user drags a menu item.</summary>
    WM_MENUDRAG = 0x0123,
    /// <summary>Sent to the owner of a drag-and-drop menu when the mouse cursor enters a menu item or moves from the center of the item to the top or bottom of the item.</summary>
    WM_MENUGETOBJECT = 0x0124,
    /// <summary>Sent when a drop-down menu or submenu has been destroyed.</summary>
    WM_UNINITMENUPOPUP = 0x0125,
    /// <summary>Sent when the user makes a selection from a menu.</summary>
    WM_MENUCOMMAND = 0x0126,
    /// <summary>An application sends this message to indicate that the UI state should be changed.</summary>
    WM_CHANGEUISTATE = 0x0127,
    /// <summary>An application sends this message to change the UI state for the specified window and all its child windows.</summary>
    WM_UPDATEUISTATE = 0x0128,
    /// <summary>An application sends this message to retrieve the UI state for a window.</summary>
    WM_QUERYUISTATE = 0x0129,

    /// <summary>This is not a real message, but rather marks the beginning of the range of message IDs related to typical mouse clicks and movement.</summary>
    WM_MOUSEFIRST = 0x0200,
    /// <summary>Posted to a window when the cursor moves.</summary>
    WM_MOUSEMOVE = 0x0200,
    /// <summary>Posted when the user presses the left mouse button while the cursor is in the client area of a window.</summary>
    WM_LBUTTONDOWN = 0x0201,
    /// <summary>Posted when the user releases the left mouse button while the cursor is in the client area of a window.</summary>
    WM_LBUTTONUP = 0x0202,
    /// <summary>Posted when the user double-clicks the left mouse button while the cursor is in the client area of a window.</summary>
    WM_LBUTTONDBLCLK = 0x0203,
    /// <summary>Posted when the user presses the right mouse button while the cursor is in the client area of a window.</summary>
    WM_RBUTTONDOWN = 0x0204,
    /// <summary>Posted when the user releases the right mouse button while the cursor is in the client area of a window.</summary>
    WM_RBUTTONUP = 0x0205,
    /// <summary>Posted when the user double-clicks the right mouse button while the cursor is in the client area of a window.</summary>
    WM_RBUTTONDBLCLK = 0x0206,
    /// <summary>Posted when the user presses the middle mouse button while the cursor is in the client area of a window.</summary>
    WM_MBUTTONDOWN = 0x0207,
    /// <summary>Posted when the user releases the middle mouse button while the cursor is in the client area of a window.</summary>
    WM_MBUTTONUP = 0x0208,
    /// <summary>Posted when the user double-clicks the middle mouse button while the cursor is in the client area of a window.</summary>
    WM_MBUTTONDBLCLK = 0x0209,
    /// <summary>This is not a real message, but rather marks the end of the range of message IDs related to typical mouse clicks and movement.</summary>
    WM_MOUSELAST = 0x0209,

    /// <summary>Sent to the focus window when the mouse wheel is rotated.</summary>
    WM_MOUSEWHEEL = 0x020A,
    /// <summary>Posted when the user presses the first or second X button while the cursor is in the client area of a window.</summary>
    WM_XBUTTONDOWN = 0x020B,
    /// <summary>Posted when the user releases the first or second X button while the cursor is in the client area of a window.</summary>
    WM_XBUTTONUP = 0x020C,
    /// <summary>Posted when the user double-clicks the first or second X button while the cursor is in the client area of a window.</summary>
    WM_XBUTTONDBLCLK = 0x020D,
    /// <summary>Sent to the active window when the mouse's horizontal scroll wheel is tilted or rotated.</summary>
    /// <remarks>Note this message is for horizontal scrolling, not the more typical vertical, use <see cref="WM_MOUSEWHEEL"/> for that.</remarks>
    WM_MOUSEHWHEEL = 0x020E,

    /// <summary>Sent to a window when a significant action occurs on a descendant window.</summary>
    WM_PARENTNOTIFY = 0x0210,
    /// <summary>Notifies an application's main window procedure that a menu modal loop has been entered.</summary>
    WM_ENTERMENULOOP = 0x0211,
    /// <summary>Notifies an application's main window procedure that a menu modal loop has been exited.</summary>
    WM_EXITMENULOOP = 0x0212,
    /// <summary>Sent to an application when the right or left arrow key is used to switch between the menu bar and the system menu.</summary>
    WM_NEXTMENU = 0x0213,
    /// <summary>Sent to a window that the user is resizing. By processing this message, an application can monitor the size and position of the drag rectangle and, if needed, change its size or position.</summary>
    WM_SIZING = 0x0214,
    /// <summary>Sent to the window that is losing the mouse capture.</summary>
    WM_CAPTURECHANGED = 0x0215,
    /// <summary>Sent to a window that the user is moving. By processing this message, an application can monitor the position of the drag rectangle and, if needed, change its position.</summary>
    WM_MOVING = 0x0216,
    /// <summary>Notifies applications that a power-management event has occurred.</summary>
    WM_POWERBROADCAST = 0x0218,
    /// <summary>Notifies an application of a change to the hardware configuration of a device or the computer.</summary>
    WM_DEVICECHANGE = 0x0219,

    /// <summary>Sent to a multiple-document interface (MDI) client window to create an MDI child window.</summary>
    WM_MDICREATE = 0x0220,
    /// <summary>Sent to a multiple-document interface (MDI) client window to close an MDI child window.</summary>
    WM_MDIDESTROY = 0x0221,
    /// <summary>Sent to a multiple-document interface (MDI) client window to instruct the client window to activate a different MDI child window.</summary>
    WM_MDIACTIVATE = 0x0222,
    /// <summary>Sent to a multiple-document interface (MDI) client window to restore an MDI child window from maximized or minimized size.</summary>
    WM_MDIRESTORE = 0x0223,
    /// <summary>Sent to a multiple-document interface (MDI) client window to activate the next or previous child window.</summary>
    WM_MDINEXT = 0x0224,
    /// <summary>Sent to a multiple-document interface (MDI) client window to maximize an MDI child window.</summary>
    WM_MDIMAXIMIZE = 0x0225,
    /// <summary>Sent to a multiple-document interface (MDI) client window to arrange all of its MDI child windows in a tile format.</summary>
    WM_MDITILE = 0x0226,
    /// <summary>Sent to a multiple-document interface (MDI) client window to arrange all its child windows in a cascade format.</summary>
    WM_MDICASCADE = 0x0227,
    /// <summary>Sent to a multiple-document interface (MDI) client window to arrange all minimized MDI child windows. It does not affect child windows that are not minimized.</summary>
    WM_MDIICONARRANGE = 0x0228,
    /// <summary>Sent to a multiple-document interface (MDI) client window to retrieve the handle to the active MDI child window.</summary>
    WM_MDIGETACTIVE = 0x0229,
    /// <summary>Sent to a multiple-document interface (MDI) client window to replace the entire menu of an MDI frame window, to replace the window menu of the frame window, or both.</summary>
    WM_MDISETMENU = 0x0230,
    /// <summary>Sent one time to a window after it enters the moving or sizing modal loop.</summary>
    WM_ENTERSIZEMOVE = 0x0231,
    /// <summary>Sent one time to a window, after it has exited the moving or sizing modal loop.</summary>
    WM_EXITSIZEMOVE = 0x0232,
    /// <summary>Sent when the user drops a file on the window of an application that has registered itself as a recipient of dropped files.</summary>
    WM_DROPFILES = 0x0233,
    /// <summary>Sent to a multiple-document interface (MDI) client window to refresh the window menu of the MDI frame window.</summary>
    WM_MDIREFRESHMENU = 0x0234,

    /// <summary>Sent to an application when a window is activated.</summary>
    WM_IME_SETCONTEXT = 0x0281,
    /// <summary>Sent to an application to notify it of changes to the IME window.</summary>
    WM_IME_NOTIFY = 0x0282,
    /// <summary>Sent by an application to direct the IME window to carry out the requested command.</summary>
    WM_IME_CONTROL = 0x283,
    /// <summary>Sent to an application when the IME window finds no space to extend the area for the composition window.</summary>
    WM_IME_COMPOSITIONFULL = 0x0284,
    /// <summary>Sent to an application when the operating system is about to change the current IME.</summary>
    WM_IME_SELECT = 0x0285,
    /// <summary>Sent to an application when the IME gets a character of the conversion result.</summary>
    WM_IME_CHAR = 0x0286,
    /// <summary>Sent to an application to provide commands and request information.</summary>
    WM_IME_REQUEST = 0x0288,
    /// <summary>Sent to an application by the IME to notify the application of a key press and to keep message order.</summary>
    WM_IME_KEYDOWN = 0x290,
    /// <summary>Sent to an application by the IME to notify the application of a key release and to keep message order.</summary>
    WM_IME_KEYUP = 0x0291,

    /// <summary>Posted to a window when the cursor hovers over the nonclient area of the window for the period of time specified in a prior call to TrackMouseEvent.</summary>
    WM_NCMOUSEHOVER = 0x02A0,
    /// <summary>Posted to a window when the cursor hovers over the client area of the window for the period of time specified in a prior call to TrackMouseEvent.</summary>
    WM_MOUSEHOVER = 0x02A1,
    /// <summary>Posted to a window when the cursor leaves the nonclient area of the window specified in a prior call to TrackMouseEvent.</summary>
    WM_NCMOUSELEAVE = 0x02A2,
    /// <summary>Posted to a window when the cursor leaves the client area of the window specified in a prior call to TrackMouseEvent.</summary>
    WM_MOUSELEAVE = 0x02A3,

    /// <summary>Sent to an edit control or combo box to delete (cut) the current selection</summary>
    WM_CUT = 0x0300,
    /// <summary>Sent to an edit control or combo box to copy the current selection</summary>
    WM_COPY = 0x0301,
    /// <summary>Sent to an edit control or combo box to paste to the edit control from the clipboard</summary>
    WM_PASTE = 0x0302,
    /// <summary>Sent to an edit control or combo box to delete (clear) the current selection</summary>
    WM_CLEAR = 0x0303,
    /// <summary>An application sends a WM_UNDO message to an edit control to undo the last operation</summary>
    WM_UNDO = 0x0304,
    /// <summary>Sent to the clipboard owner if it has delayed rendering a specific clipboard format and if an application has requested data in that format.</summary>
    WM_RENDERFORMAT = 0x0305,
    /// <summary>Sent to the clipboard owner before it is destroyed, if the clipboard owner has delayed rendering one or more clipboard formats.</summary>
    WM_RENDERALLFORMATS = 0x0306,
    /// <summary>Sent to the clipboard owner when a call to the EmptyClipboard function empties the clipboard.</summary>
    WM_DESTROYCLIPBOARD = 0x0307,
    /// <summary>Sent to the first window in the clipboard viewer chain when the content of the clipboard changes.</summary>
    WM_DRAWCLIPBOARD = 0x0308,
    /// <summary>Sent to the clipboard owner by a clipboard viewer window when the clipboard contains data in the CF_OWNERDISPLAY format and the clipboard viewer's client area needs repainting.</summary>
    WM_PAINTCLIPBOARD = 0x0309,
    /// <summary>Sent to the clipboard owner by a clipboard viewer window when the clipboard contains data in the CF_OWNERDISPLAY format and an event occurs in the clipboard viewer's vertical scroll bar.</summary>
    WM_VSCROLLCLIPBOARD = 0x030A,
    /// <summary>Sent to the clipboard owner by a clipboard viewer window when the clipboard contains data in the CF_OWNERDISPLAY format and the clipboard viewer's client area has changed size.</summary>
    WM_SIZECLIPBOARD = 0x030B,
    /// <summary>Sent to the clipboard owner by a clipboard viewer window to request the name of a CF_OWNERDISPLAY clipboard format.</summary>
    WM_ASKCBFORMATNAME = 0x030C,
    /// <summary>Sent to the first window in the clipboard viewer chain when a window is being removed from the chain.</summary>
    WM_CHANGECBCHAIN = 0x030D,
    /// <summary>Sent to the clipboard owner by a clipboard viewer window. This occurs when the clipboard contains data in the CF_OWNERDISPLAY format and an event occurs in the clipboard viewer's horizontal scroll bar.</summary>
    WM_HSCROLLCLIPBOARD = 0x030E,

    /// <summary>Informs a window that it is about to receive the keyboard focus, giving the window the opportunity to realize its logical palette when it receives the focus.</summary>
    WM_QUERYNEWPALETTE = 0x030F,
    /// <summary>Informs applications that an application is going to realize its logical palette.</summary>
    WM_PALETTEISCHANGING = 0x0310,
    /// <summary>Sent to all top-level and overlapped windows after the window with the keyboard focus has realized its logical palette, thereby changing the system palette.</summary>
    WM_PALETTECHANGED = 0x0311,

    /// <summary>Posted when the user presses a hot key registered by the RegisterHotKey function.</summary>
    WM_HOTKEY = 0x0312,
    /// <summary>Sent to a window to request that it draw itself in the specified device context, most commonly in a printer device context.</summary>
    WM_PRINT = 0x0317,
    /// <summary>Sent to a window to request that it draw its client area in the specified device context, most commonly in a printer device context.</summary>
    WM_PRINTCLIENT = 0x0318,
    /// <summary>Notifies a window that the user generated an application command event, for example, by clicking an application command button using the mouse or typing an application command key on the keyboard.</summary>
    WM_APPCOMMAND = 0x0319,

    /// <summary>Used to define private messages for use by private window classes, usually of the form WM_USER+x, where x is an integer value.</summary>
    WM_USER = 0x0400,
    /// <summary>Used to define private messages, usually of the form WM_APP+x, where x is an integer value.</summary>
    WM_APP = 0x8000,
}

/// <summary>Specifies how messages are to be handled by <see cref="Win32API.PeekMessage"/>. This parameter can be one or more of the enum values.</summary>
/// <remarks>By default, all message types are processed. To specify that only certain message should be processed, apply one or more of the Process* values.</remarks>
[Flags]
public enum PeekMessageHandling : uint
{
    /// <summary>Messages are not removed from the queue after processing by PeekMessage.</summary>
    NoRemove = 0x0000,
    /// <summary>Messages are removed from the queue after processing by PeekMessage.</summary>
    Remove = 0x0001,
    /// <summary>Prevents the system from releasing any thread that is waiting for the caller to go idle. Use with <see cref="NoRemove"/> or <see cref="Remove"/>.</summary>
    NoYield = 0x0002,

    /// <summary>Process mouse and keyboard messages.</summary>
    ProcessInput = 0x1C070000,
    /// <summary>Process paint messages.</summary>
    ProcessPaint = 0x00200000,
    /// <summary>Process all posted messages, including timers and hotkeys.</summary>
    ProcessPosted = 0x00980000,
    /// <summary>Process all sent messages.</summary>
    ProcessSent = 0x00400000,
}

/// <summary>Defines additional aspects of the window class.</summary>
[Flags]
public enum WindowClassStyle : uint
{
    /// <summary>Redraws the entire window if a movement or size adjustment changes the height of the client area.</summary>
    VerticalRedraw = 0x0001,
    /// <summary>Redraws the entire window if a movement or size adjustment changes the width of the client area.</summary>
    HorizontalRedraw = 0x0002,

    /// <summary>Aligns the window's client area on a byte boundary (in the x direction). This style affects the width of the window and its horizontal placement on the display.</summary>
    ByteAlignClient = 0x1000,
    /// <summary>Aligns the window on a byte boundary (in the x direction). This style affects the width of the window and its horizontal placement on the display.</summary>
    ByteAlignWindow = 0x2000,

    /// <summary>Allocates one device context to be shared by all windows in the class. Because window classes are process specific, it is possible for multiple threads of an application to create a window of the same class. It is also possible for the threads to attempt to use the device context simultaneously. When this happens, the system allows only one thread to successfully finish its drawing operation.</summary>
    [Obsolete("This mode is not recommended for modern applications due to the possibility of race conditions.")]
    ClassDC = 0x0040,
    /// <summary>Allocates a unique device context for each window in the class.</summary>
    OwnDC = 0x0020,
    /// <summary>Sets the clipping rectangle of the child window to that of the parent window so that the child can draw on the parent.</summary>
    /// <remarks>A window with the <see cref="ParentDC"/> style bit receives a regular device context from the system's cache of device contexts. It does not give the child the parent's device context or device context settings. Specifying <see cref="ParentDC"/> enhances an application's performance.</remarks>
    ParentDC = 0x0080,

    /// <summary>Sends a double-click message to the window procedure when the user double-clicks the mouse while the cursor is within a window belonging to the class.</summary>
    DoubleClicks = 0x0008,
    /// <summary>Enables the drop shadow effect on a window. The effect is turned on and off through SPI_SETDROPSHADOW. Typically, this is enabled for small, short-lived windows such as menus to emphasize their Z-order relationship to other windows. Windows created from a class with this style must be top-level windows; they may not be child windows.</summary>
    DropShadow = 0x00020000,
    /// <summary>Indicates that the window class is an application global class.</summary>
    GlobalClass = 0x4000,
    /// <summary>Disables Close on the window menu.</summary>
    NoClose = 0x0200,
    /// <summary>Saves, as a bitmap, the portion of the screen image obscured by a window of this class.</summary>
    /// <remarks>
    /// When the window is removed, the system uses the saved bitmap to restore the screen image, including other windows that were obscured. Therefore, the system does not send <see cref="MessageID.WM_PAINT"/> messages to windows that were obscured if the memory used by the bitmap has not been discarded and if other screen actions have not invalidated the stored image.
    /// This style is useful for small windows (for example, menus or dialog boxes) that are displayed briefly and then removed before other screen activity takes place. This style increases the time required to display the window, because the system must first allocate memory to store the bitmap.
    /// </remarks>
    SaveBits = 0x0800,
}

/// <summary>Style options that define appearance and attributes of a window.</summary>
[Flags]
public enum WindowStyle : uint
{
    None = 0,
    /// <summary>The window has a thin-line border.</summary>
    Border = 0x00800000,
    /// <summary>The window has a title bar. Also sets <see cref="Border"/>.</summary>
    Caption = 0x00C00000,
    /// <summary>The window is a child window. A window with this style cannot have a menu bar. This style cannot be used with the <see cref="Popup"/> style.</summary>
    Child = 0x40000000,
    /// <summary>The window has a border of a style typically used with dialog boxes. A window with this style cannot have a title bar.</summary>
    DialogFrame = 0x00400000,
    /// <summary>The window is an overlapped window. An overlapped window has a title bar and a border. Same as the <see cref="Tiled"/> style.</summary>
    Overlapped = 0x00000000,
    /// <summary>The window is an overlapped window. Multiple common styles are included. Same as the <see cref="TiledWindow"/> style.</summary>
    OverlappedWindow = Overlapped | Caption | SysMenu | ThickFrame | MinimizeBox | MaximizeBox,
    /// <summary>The window is a pop-up window. This style cannot be used with the <see cref="Child"/> style.</summary>
    Popup = 0x80000000,
    /// <summary>The window is a pop-up window. The <see cref="Caption"/> and <see cref="PopupWindow"/> styles must be combined to make the window menu visible.</summary>
    PopupWindow = Popup | Border | SysMenu,
    /// <summary>The window is an overlapped window. An overlapped window has a title bar and a border. Same as the <see cref="Overlapped"/> style.</summary>
    Tiled = 0x00000000,
    /// <summary>The window is an overlapped window. Same as the <see cref="OverlappedWindow"/> style.</summary>
    TiledWindow = Overlapped | Caption | SysMenu | ThickFrame | MinimizeBox | MaximizeBox,

    /// <summary>Excludes the area occupied by child windows when drawing occurs within the parent window. This style is used when creating the parent window.</summary>
    ClipChildren = 0x02000000,
    /// <summary>Clips child windows relative to each other; that is, when a particular child window receives a <see cref="MessageID.WM_PAINT"/> message, the <see cref="ClipSiblings"/> style clips all other overlapping child windows out of the region of the child window to be updated. If <see cref="ClipSiblings"/> is not specified and child windows overlap, it is possible, when drawing within the client area of a child window, to draw within the client area of a neighboring child window.</summary>
    ClipSiblings = 0x04000000,

    /// <summary>The window is initially disabled. A disabled window cannot receive input from the user. To change this after a window has been created, use the <see cref="Win32API.EnableWindow"/> function.</summary>
    Disabled = 0x08000000,
    /// <summary>The window has a horizontal scroll bar.</summary>
    HorizontalScroll = 0x00100000,
    /// <summary>The window is initially minimized. Same as <see cref="Minimize"/>.</summary>
    Iconic = 0x20000000,
    /// <summary>The window is initially minimized. Same as <see cref="Iconic"/>.</summary>
    Minimize = 0x20000000,
    /// <summary>The window is initially maximized.</summary>
    Maximize = 0x01000000,
    /// <summary>The window has a window menu on its title bar. The <see cref="Caption"/> style must also be specified.</summary>
    SysMenu = 0x00080000,
    /// <summary>The window is initially visible. This style can be turned on and off by using the ShowWindow or SetWindowPos function.</summary>
    Visible = 0x10000000,
    /// <summary>The window has a vertical scroll bar.</summary>
    VerticalScroll = 0x00200000,

    /// <summary>The window is the first control of a group of controls.</summary>
    Group = 0x00020000,
    /// <summary>The window is a control that can receive the keyboard focus when the user presses the TAB key.</summary>
    TabStop = 0x00010000,

    /// <summary>The window has a maximize button. Cannot be combined with the <see cref="WindowStyleEx.ContextHelp"/> style. The <see cref="SysMenu"/> style must also be specified.</summary>
    MaximizeBox = 0x00010000,
    /// <summary>The window has a minimize button. Cannot be combined with the <see cref="WindowStyleEx.ContextHelp"/> style. The <see cref="SysMenu"/> style must also be specified.</summary>
    MinimizeBox = 0x00020000,
    /// <summary>The window has a sizing border. Same as the <see cref="ThickFrame"/> style.</summary>
    SizeBox = 0x00040000,
    /// <summary>The window has a sizing border. Same as the <see cref="SizeBox"/> style.</summary>
    ThickFrame = 0x00040000,
}

/// <summary>Extended style options that define appearance and attributes of a window.</summary>
[Flags]
public enum WindowStyleEx : uint
{
    None = 0,
    /// <summary>The window accepts drag-drop files.</summary>
    AcceptFiles = 0x00000010,
    /// <summary>The window itself contains child windows that should take part in dialog box navigation. If this style is specified, the dialog manager recurses into children of this window when performing navigation operations such as handling the TAB key, an arrow key, or a keyboard mnemonic.</summary>
    ControlParent = 0x00010000,
    /// <summary>If the shell language is Hebrew, Arabic, or another language that supports reading order alignment, the horizontal origin of the window is on the right edge. Increasing horizontal values advance to the left. </summary>
    LayoutRTL = 0x00400000,
    /// <summary>The window has generic left-aligned properties. This is the default.</summary>
    Left = 0x00000000,
    /// <summary>If the shell language is Hebrew, Arabic, or another language that supports reading order alignment, the vertical scroll bar (if present) is to the left of the client area. For other languages, the style is ignored.</summary>
    LeftScrollBar = 0x00004000,
    /// <summary>The window text is displayed using left-to-right reading-order properties. This is the default.</summary>
    LTRReading = 0x00000000,
    /// <summary>The window has generic "right-aligned" properties. This depends on the window class. This style has an effect only if the shell language is Hebrew, Arabic, or another language that supports reading-order alignment; otherwise, the style is ignored.</summary>
    Right = 0x00001000,
    /// <summary>The vertical scroll bar (if present) is to the right of the client area. This is the default.</summary>
    RightScrollBar = 0x00000000,
    /// <summary>If the shell language is Hebrew, Arabic, or another language that supports reading-order alignment, the window text is displayed using right-to-left reading-order properties. For other languages, the style is ignored.</summary>
    RTLReading = 0x00002000,

    /// <summary>Forces a top-level window onto the taskbar when the window is visible.</summary>
    AppWindow = 0x00040000,
    /// <summary>The title bar of the window includes a question mark. This cannot be used with <see cref="WindowStyle.MaximizeBox"/> or <see cref="WindowStyle.MinimizeBox"/>.</summary>
    /// <remarks>When the user clicks the question mark, the cursor changes to a question mark with a pointer. If the user then clicks a child window, the child receives a <see cref="MessageID.WM_HELP"/> message. The child window should pass the message to the parent window procedure, which should call the WinHelp function using the HELP_WM_HELP command. The Help application displays a pop-up window that typically contains help for the child window.</remarks>
    ContextHelp = 0x00000400,
    /// <summary>The window is a MDI child window.</summary>
    MDIChild = 0x00000040,
    /// <summary>The window is an overlapped window.</summary>
    ExOverlappedWindow = WindowEdge | ClientEdge,
    /// <summary>The window is palette window, which is a modeless dialog box that presents an array of commands.</summary>
    PaletteWindow = WindowEdge | ToolWindow | Topmost,
    /// <summary>The window is intended to be used as a floating toolbar. A tool window has a title bar that is shorter than a normal title bar, and the window title is drawn using a smaller font. A tool window does not appear in the taskbar or in the dialog that appears when the user presses ALT+TAB. If a tool window has a system menu, its icon is not displayed on the title bar. However, you can display the system menu by right-clicking or by typing ALT+SPACE.</summary>
    ToolWindow = 0x00000080,
    /// <summary>The window has a border with a raised edge.</summary>
    WindowEdge = 0x00000100,

    /// <summary>The window has a border with a sunken edge.</summary>
    ClientEdge = 0x00000200,
    /// <summary>Paints all descendants of a window in bottom-to-top painting order using double-buffering. This cannot be used if the window has a class style of either <see cref="WindowClassStyle.OwnDC"/> or <see cref="WindowClassStyle.ClassDC"/>.</summary>
    /// <remarks>Bottom-to-top painting order allows a descendent window to have translucency (alpha) and transparency (color-key) effects, but only if the descendent window also has the <see cref="Transparent"/> bit set. Double-buffering allows the window and its descendents to be painted without flicker.</remarks>
    Composited = 0x02000000,
    /// <summary>The window has a double border; the window can, optionally, be created with a title bar by specifying the <see cref="WindowStyle.Caption"/> style.</summary>
    DialogModalFrame = 0x00000001,
    /// <summary>The window is a layered window. This style cannot be used if the window has a class style of either <see cref="WindowClassStyle.OwnDC"/> or <see cref="WindowClassStyle.ClassDC"/>.</summary>
    Layered = 0x00080000,
    /// <summary>The window has a three-dimensional border style intended to be used for items that do not accept user input.</summary>
    StaticEdge = 0x00020000,
    /// <summary>The window should not be painted until siblings beneath the window (that were created by the same thread) have been painted. The window appears transparent because the bits of underlying sibling windows have already been painted. To achieve transparency without these restrictions, use the SetWindowRgn function.</summary>
    Transparent = 0x00000020,

    /// <summary>
    /// A top-level window created with this style does not become the foreground window when the user clicks it. The system does not bring this window to the foreground when the user minimizes or closes the foreground window.
    /// The window should not be activated through programmatic access or via keyboard navigation by accessible technology, such as Narrator.
    /// To activate the window, use the SetActiveWindow or SetForegroundWindow function.
    /// The window does not appear on the taskbar by default. To force the window to appear on the taskbar, use the <see cref="AppWindow"/> style.
    /// </summary>
    NoActivate = 0x08000000,
    /// <summary>The window does not pass its window layout to its child windows.</summary>
    NoInheritLayout = 0x00100000,
    /// <summary>The child window created with this style does not send the <see cref="MessageID.WM_PARENTNOTIFY"/> message to its parent window when it is created or destroyed.</summary>
    NoParentNotify = 0x00000004,
    /// <summary>The window does not render to a redirection surface. This is for windows that do not have visible content or that use mechanisms other than surfaces to provide their visual.</summary>
    NoRedirectionBitmap = 0x00200000,
    /// <summary>The window should be placed above all non-topmost windows and should stay above them, even when the window is deactivated. To add or remove this style, use the SetWindowPos function.</summary>
    Topmost = 0x00000008,
}

public enum NativeCursorIndex : ushort
{
    /// <summary>Standard arrow and small hourglass</summary>
    AppStarting = 32650,
    /// <summary>Standard arrow</summary>
    Arrow = 32512,
    Crosshair = 32515,
    Hand = 32649,
    /// <summary>Arrow and question mark</summary>
    Help = 32651,
    IBeam = 32513,
    [Obsolete("Obsolete for modern applications")]
    Icon = 32641,
    /// <summary>Circle with line crossed through</summary>
    No = 32648,
    SizeAll = 32646,
    SizeNEandSW = 32643,
    SizeNandS = 32645,
    SizeNWandSE = 32642,
    SizeWandE = 32644,
    UpArrow = 32516,
    /// <summary>Hourglass</summary>
    Wait = 32514
}