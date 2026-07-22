namespace NativeWebHost;

/// <summary>
/// Global configuration options for an NativeWebHost application.
/// </summary>
public class NativeWebHostOptions
{
    /// <summary>The title shown in the host window's title bar.</summary>
    public string Title { get; set; } = "NativeWebHost App";

    /// <summary>The initial URL loaded when the application starts.</summary>
    public string StartUrl { get; set; } = "about:blank";

    /// <summary>Initial width of the host window in device-independent pixels.</summary>
    public int Width { get; set; } = 1280;

    /// <summary>Initial height of the host window in device-independent pixels.</summary>
    public int Height { get; set; } = 800;

    /// <summary>Whether the host window should start maximized.</summary>
    public bool StartMaximized { get; set; }

    /// <summary>窗口首次显示时相对于工作区的定位方式。</summary>
    public NativeWebWindowPlacement WindowPlacement { get; set; } = NativeWebWindowPlacement.Default;

    /// <summary>应用定位策略后沿水平方向向工作区内侧缩进的像素数。</summary>
    public int WindowOffsetX { get; set; }

    /// <summary>应用定位策略后沿垂直方向向工作区内侧缩进的像素数。</summary>
    public int WindowOffsetY { get; set; }

    /// <summary>窗口是否始终置于普通窗口之上。</summary>
    public bool AlwaysOnTop { get; set; }

    /// <summary>窗口是否在任务栏中显示按钮。</summary>
    public bool ShowInTaskbar { get; set; } = true;

    /// <summary>窗口首次显示时是否取得输入焦点。</summary>
    public bool ActivateOnShow { get; set; } = true;

    /// <summary>用户是否可以调整窗口尺寸。</summary>
    public bool Resizable { get; set; } = true;
    /// <summary>Whether browser DevTools are enabled.</summary>
    public bool EnableDevTools { get; set; }

    /// <summary>
    /// 允许调用 WebView2 宿主 JavaScript 桥接的文档来源。null 表示仅允许 StartUrl 的来源，
    /// 空列表表示禁用入站调用；"*" 会恢复任意来源访问，仅应用于完全可信内容。
    /// </summary>
    public IReadOnlyList<string>? WebView2JsBridgeAllowedOrigins { get; set; }

    /// <summary>
    /// Custom URI scheme name used to serve local assets (e.g. <c>"app"</c> → <c>app://localhost/</c>).
    /// Defaults to <c>"app"</c>.
    /// </summary>
    public string CustomScheme { get; set; } = "app";

    /// <summary>
    /// Root directory on disk that is mapped to <see cref="CustomScheme"/>://.
    /// When <see langword="null"/> the custom scheme is not registered.
    /// </summary>
    public string? ContentRootPath { get; set; }

    /// <summary>
    /// Directory used to store the browser profile / user data.
    /// When <see langword="null"/> a temporary folder is used.
    /// </summary>
    public string? UserDataFolder { get; set; }

    /// <summary>
    /// Optional .ico file used for the native window, taskbar entry, and tray icon.
    /// </summary>
    public string? IconPath { get; set; }

    /// <summary>Whether the Windows runtime should create a tray icon for the main window.</summary>
    public bool EnableTrayIcon { get; set; }

    /// <summary>
    /// 主窗口收到关闭请求时是否隐藏到托盘；仅在启用托盘图标的主窗口上生效。
    /// </summary>
    public bool HideMainWindowOnClose { get; set; }

    /// <summary>Tooltip shown when hovering the tray icon.</summary>
    public string? TrayToolTip { get; set; }

    /// <summary>Text for the tray menu command that restores or focuses the main window.</summary>
    public string TrayOpenText { get; set; } = "Open";

    /// <summary>Text for the tray menu command that exits the application.</summary>
    public string TrayExitText { get; set; } = "Exit";

    /// <summary>托盘图标的可选动态提示文本提供器。</summary>
    public Func<string>? TrayToolTipProvider { get; set; }

    /// <summary>可选的动态托盘菜单项提供器。</summary>
    public Func<IReadOnlyList<NativeWebTrayMenuItem>>? TrayMenuProvider { get; set; }

    /// <summary>动态托盘菜单项的可选命令处理器。</summary>
    public Func<string, CancellationToken, ValueTask>? TrayCommandHandler { get; set; }

    /// <summary>Additional adapter-specific settings as key-value pairs.</summary>
    public Dictionary<string, string> AdapterSettings { get; set; } = new();

    /// <summary>
    /// Host-level scrollbar behaviour applied to hosted pages.
    /// Defaults to <see cref="NativeWebScrollBarMode.Auto"/>, which leaves overflow styling
    /// entirely up to the page.
    /// </summary>
    public NativeWebScrollBarMode ScrollBarMode { get; set; } = NativeWebScrollBarMode.Auto;

    /// <summary>
    /// Custom CSS injected when <see cref="ScrollBarMode"/> is set to
    /// <see cref="NativeWebScrollBarMode.Custom"/>.
    /// </summary>
    public string? ScrollBarCustomCss { get; set; }

    /// <summary>
    /// Visual style of the native host window chrome.
    /// Defaults to <see cref="NativeWebWindowStyle.Normal"/> (standard OS title bar).
    /// Set to <see cref="NativeWebWindowStyle.Frameless"/> to remove the system title bar
    /// and implement a fully custom HTML/CSS chrome.
    /// Additional Windows-oriented presets such as <see cref="NativeWebWindowStyle.DwmBlurGlass"/>
    /// and <see cref="NativeWebWindowStyle.VsCode"/> are runtime-specific and fall back gracefully
    /// when the selected host runtime does not expose equivalent native capabilities.
    /// </summary>
    public NativeWebWindowStyle WindowStyle { get; set; } = NativeWebWindowStyle.Normal;

    /// <summary>
    /// Optional built-in title-bar preset rendered by the host instead of by page HTML.
    /// This is especially useful for editor-like and productivity-like shells that should
    /// ship a consistent chrome without requiring each hosted page to duplicate it.
    /// </summary>
    public NativeWebBuiltInTitleBarStyle BuiltInTitleBarStyle { get; set; } = NativeWebBuiltInTitleBarStyle.None;

    /// <summary>
    /// Creates a detached copy of the current options instance.
    /// </summary>
    public NativeWebHostOptions Clone()
        => new()
        {
            Title = Title,
            StartUrl = StartUrl,
            Width = Width,
            Height = Height,
            StartMaximized = StartMaximized,
            WindowPlacement = WindowPlacement,
            WindowOffsetX = WindowOffsetX,
            WindowOffsetY = WindowOffsetY,
            AlwaysOnTop = AlwaysOnTop,
            ShowInTaskbar = ShowInTaskbar,
            ActivateOnShow = ActivateOnShow,
            Resizable = Resizable,
            EnableDevTools = EnableDevTools,
            WebView2JsBridgeAllowedOrigins = WebView2JsBridgeAllowedOrigins?.ToArray(),
            CustomScheme = CustomScheme,
            ContentRootPath = ContentRootPath,
            UserDataFolder = UserDataFolder,
            IconPath = IconPath,
            EnableTrayIcon = EnableTrayIcon,
            HideMainWindowOnClose = HideMainWindowOnClose,
            TrayToolTip = TrayToolTip,
            TrayOpenText = TrayOpenText,
            TrayExitText = TrayExitText,
            TrayToolTipProvider = TrayToolTipProvider,
            TrayMenuProvider = TrayMenuProvider,
            TrayCommandHandler = TrayCommandHandler,
            AdapterSettings = new Dictionary<string, string>(AdapterSettings, StringComparer.Ordinal),
            ScrollBarMode = ScrollBarMode,
            ScrollBarCustomCss = ScrollBarCustomCss,
            WindowStyle = WindowStyle,
            BuiltInTitleBarStyle = BuiltInTitleBarStyle,
        };
}
