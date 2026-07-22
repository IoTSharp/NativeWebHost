using System.Runtime.InteropServices;

namespace NativeWebHost.Windows.Win32;

/// <summary>
/// Raw Win32 P/Invoke declarations used by <see cref="Win32HostWindow"/>.
/// All imports use ExactSpelling + CharSet.Unicode for AOT safety.
/// </summary>
internal static class NativeMethods
{
    // ── Window styles ─────────────────────────────────────────────────────────
    internal const uint WS_OVERLAPPED   = 0x00000000u;
    internal const uint WS_POPUP        = 0x80000000u;
    internal const uint WS_CAPTION      = 0x00C00000u;
    internal const uint WS_SYSMENU      = 0x00080000u;
    internal const uint WS_THICKFRAME   = 0x00040000u;
    internal const uint WS_MINIMIZEBOX  = 0x00020000u;
    internal const uint WS_MAXIMIZEBOX  = 0x00010000u;
    internal const uint WS_MAXIMIZE     = 0x01000000u;
    internal const uint WS_VISIBLE      = 0x10000000u;
    internal const uint WS_CLIPCHILDREN = 0x02000000u;
    internal const uint WS_CLIPSIBLINGS = 0x04000000u;
    internal const uint WS_OVERLAPPEDWINDOW =
        WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX;

    // ── Extended window styles ────────────────────────────────────────────────
    internal const uint WS_EX_APPWINDOW = 0x00040000u;
    internal const uint WS_EX_TOPMOST   = 0x00000008u;
    internal const uint WS_EX_TOOLWINDOW = 0x00000080u;
    internal const uint WS_EX_NOACTIVATE = 0x08000000u;

    // ── Class styles ──────────────────────────────────────────────────────────
    internal const uint CS_HREDRAW = 0x0002u;
    internal const uint CS_VREDRAW = 0x0001u;

    // ── ShowWindow commands ───────────────────────────────────────────────────
    internal const int SW_HIDE         = 0;
    internal const int SW_SHOWNORMAL   = 1;
    internal const int SW_SHOWMINIMIZED = 2;
    internal const int SW_SHOWMAXIMIZED = 3;
    internal const int SW_SHOWNOACTIVATE = 4;
    internal const int SW_SHOW         = 5;
    internal const int SW_RESTORE      = 9;

    // ── Window messages ───────────────────────────────────────────────────────
    internal const uint WM_NCCREATE      = 0x0081u;
    internal const uint WM_CREATE        = 0x0001u;
    internal const uint WM_DESTROY       = 0x0002u;
    internal const uint WM_SIZE          = 0x0005u;
    internal const uint WM_ACTIVATE      = 0x0006u;
    internal const uint WM_THEMECHANGED  = 0x031Au;
    internal const uint WM_NULL          = 0x0000u;
    internal const uint WM_CLOSE         = 0x0010u;
    internal const uint WM_ERASEBKGND   = 0x0014u;
    internal const uint WM_NCCALCSIZE   = 0x0083u;
    internal const uint WM_NCHITTEST    = 0x0084u;
    internal const uint WM_SYSCOMMAND   = 0x0112u;
    internal const uint WM_NCLBUTTONDOWN = 0x00A1u;
    internal const uint WM_COMMAND      = 0x0111u;
    internal const uint WM_SETICON      = 0x0080u;
    internal const uint WM_CONTEXTMENU  = 0x007Bu;
    internal const uint WM_LBUTTONDBLCLK = 0x0203u;
    internal const uint WM_RBUTTONUP    = 0x0205u;
    internal const uint WM_DWMCOMPOSITIONCHANGED = 0x031Eu;
    internal const uint WM_APP          = 0x8000u;

    // Custom WM_APP slots
    internal const uint WM_APP_INIT     = WM_APP + 0u;   // trigger async WebView2 init
    internal const uint WM_APP_DELEGATE = WM_APP + 1u;   // drain SynchronizationContext queue
    internal const uint WM_APP_TRAY     = WM_APP + 2u;   // native tray icon callback

    // ── Hit-test return values ────────────────────────────────────────────────
    internal const nint HTNOWHERE      = 0;
    internal const nint HTCLIENT       = 1;
    internal const nint HTCAPTION      = 2;
    internal const nint HTLEFT         = 10;
    internal const nint HTRIGHT        = 11;
    internal const nint HTTOP          = 12;
    internal const nint HTTOPLEFT      = 13;
    internal const nint HTTOPRIGHT     = 14;
    internal const nint HTBOTTOM       = 15;
    internal const nint HTBOTTOMLEFT   = 16;
    internal const nint HTBOTTOMRIGHT  = 17;

    // ── WM_SIZE wParam values ─────────────────────────────────────────────────
    internal const uint SIZE_RESTORED  = 0u;
    internal const uint SIZE_MINIMIZED = 1u;
    internal const uint SIZE_MAXIMIZED = 2u;

    // ── WM_SYSCOMMAND wParam values ───────────────────────────────────────────
    internal const uint SC_MINIMIZE = 0xF020u;
    internal const uint SC_MAXIMIZE = 0xF030u;
    internal const uint SC_RESTORE  = 0xF120u;
    internal const uint SC_CLOSE    = 0xF060u;

    // ── SetWindowLongPtr index ────────────────────────────────────────────────
    internal const int GWLP_USERDATA = -21;

    // ── Misc ──────────────────────────────────────────────────────────────────
    internal const int CW_USEDEFAULT      = unchecked((int)0x80000000);
    internal const int IDC_ARROW          = 32512;
    internal const int IDI_APPLICATION    = 32512;
    internal const int IMAGE_ICON         = 1;
    internal const int ICON_SMALL         = 0;
    internal const int ICON_BIG           = 1;
    internal const int ResizeBorderSize   = 8;   // px — frameless resize hit-zone
    internal const uint MB_OK             = 0x00000000u;
    internal const uint MB_ICONERROR      = 0x00000010u;
    internal const uint SWP_NOSIZE        = 0x0001u;
    internal const uint SWP_NOMOVE        = 0x0002u;
    internal const uint SWP_NOZORDER      = 0x0004u;
    internal const uint SWP_NOACTIVATE    = 0x0010u;
    internal const uint SWP_FRAMECHANGED  = 0x0020u;
    internal const uint TPM_LEFTALIGN     = 0x0000u;
    internal const uint TPM_RETURNCMD     = 0x0100u;
    internal const uint TPM_RIGHTBUTTON   = 0x0002u;
    internal const uint MF_STRING         = 0x0000u;
    internal const uint MF_GRAYED         = 0x0001u;
    internal const uint MF_CHECKED        = 0x0008u;
    internal const uint MF_SEPARATOR      = 0x0800u;
    internal const uint LR_LOADFROMFILE   = 0x0010u;
    internal const uint LR_DEFAULTSIZE    = 0x0040u;
    internal const uint LR_SHARED         = 0x8000u;
    internal const uint NIM_ADD           = 0x00000000u;
    internal const uint NIM_MODIFY        = 0x00000001u;
    internal const uint NIM_DELETE        = 0x00000002u;
    internal const uint NIF_MESSAGE       = 0x00000001u;
    internal const uint NIF_ICON          = 0x00000002u;
    internal const uint NIF_TIP           = 0x00000004u;
    internal const uint SPI_GETWORKAREA   = 0x0030u;

    // DWM window attributes
    internal const uint DWMWA_USE_IMMERSIVE_DARK_MODE = 20u;
    internal const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33u;
    internal const uint DWMWA_BORDER_COLOR = 34u;
    internal const uint DWMWA_CAPTION_COLOR = 35u;
    internal const uint DWMWA_TEXT_COLOR = 36u;
    internal const uint DWMWA_SYSTEMBACKDROP_TYPE = 38u;

    // DWM corner preferences
    internal const uint DWMWCP_DEFAULT = 0u;
    internal const uint DWMWCP_DONOTROUND = 1u;
    internal const uint DWMWCP_ROUND = 2u;
    internal const uint DWMWCP_ROUNDSMALL = 3u;

    // DWM system backdrop types
    internal const int DWMSBT_AUTO = 0;
    internal const int DWMSBT_NONE = 1;
    internal const int DWMSBT_MAINWINDOW = 2;
    internal const int DWMSBT_TRANSIENTWINDOW = 3;
    internal const int DWMSBT_TABBEDWINDOW = 4;

    // ── Structs ───────────────────────────────────────────────────────────────

    internal delegate IntPtr WndProcDelegate(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSG
    {
        public IntPtr hwnd;
        public uint   message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint   time;
        public POINT  pt;
        public uint   lPrivate;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT { public int x; public int y; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MARGINS
    {
        public int cxLeftWidth;
        public int cxRightWidth;
        public int cyTopHeight;
        public int cyBottomHeight;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WNDCLASSEXW
    {
        public int    cbSize;
        public uint   style;
        public IntPtr lpfnWndProc;
        public int    cbClsExtra;
        public int    cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string  lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersionOrTimeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    // ── user32.dll ────────────────────────────────────────────────────────────

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern ushort RegisterClassExW(ref WNDCLASSEXW lpWndClass);

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr CreateWindowExW(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern bool UpdateWindow(IntPtr hWnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern int GetMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern IntPtr DispatchMessageW(ref MSG lpmsg);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern void PostQuitMessage(int nExitCode);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern IntPtr DefWindowProcW(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    internal static extern bool SetWindowTextW(IntPtr hWnd, string lpString);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern bool PostMessageW(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern IntPtr SendMessageW(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern bool ReleaseCapture();

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr LoadCursorW(IntPtr hInstance, int lpCursorName);

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr LoadIconW(IntPtr hInstance, int lpIconName);

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr LoadImageW(
        IntPtr hinst,
        string lpszName,
        uint uType,
        int cxDesired,
        int cyDesired,
        uint fuLoad);

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr LoadImageW(
        IntPtr hinst,
        IntPtr lpszName,
        uint uType,
        int cxDesired,
        int cyDesired,
        uint fuLoad);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern IntPtr GetWindowLongPtrW(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    internal static extern int MessageBoxW(IntPtr hWnd, string lpText, string lpCaption, uint uType);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern uint TrackPopupMenuEx(
        IntPtr hmenu,
        uint uFlags,
        int x,
        int y,
        IntPtr hwnd,
        IntPtr lptpm);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    internal static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, nuint uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SystemParametersInfoW(
        uint uiAction,
        uint uiParam,
        out RECT pvParam,
        uint fWinIni);

    // ── kernel32.dll ──────────────────────────────────────────────────────────

    [DllImport("kernel32.dll", ExactSpelling = true)]
    internal static extern IntPtr GetModuleHandleW(IntPtr lpModuleName);

    // ── shcore.dll (DPI awareness — Windows 8.1+) ─────────────────────────────

    [DllImport("shcore.dll", ExactSpelling = true)]
    internal static extern int SetProcessDpiAwareness(int value);

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    internal static extern int DwmIsCompositionEnabled(out bool pfEnabled);

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    internal static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    internal static extern int DwmDefWindowProc(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        IntPtr lParam,
        out IntPtr plResult);

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    internal static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        uint dwAttribute,
        ref int pvAttribute,
        uint cbAttribute);

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    internal static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        uint dwAttribute,
        ref uint pvAttribute,
        uint cbAttribute);

    // ── shell32.dll ──────────────────────────────────────────────────────────

    [DllImport("shell32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    internal static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);
}
