using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using DirectN;
using DirectN.Extensions.Com;
using DirectN.Extensions.Utilities;
using WebView2;
using WebView2.Utilities;

namespace NativeWebHost.Windows;

/// <summary>
/// 基于 WebView2Aot generated COM binding 的 WebView2 适配器。
/// </summary>
public sealed class NativeWebView2Adapter : IWebViewAdapter
{
    private readonly NativeWebView2JsBridge _bridge = new();
    private ComObject<ICoreWebView2Environment>? _environment;
    private ComObject<ICoreWebView2Controller>? _controller;
    private ComObject<ICoreWebView2>? _webView;
    private CoreWebView2NavigationStartingEventHandler? _navigationStartingHandler;
    private CoreWebView2NavigationCompletedEventHandler? _navigationCompletedHandler;
    private CoreWebView2WebResourceRequestedEventHandler? _webResourceRequestedHandler;
    private EventRegistrationToken _navigationStartingToken;
    private EventRegistrationToken _navigationCompletedToken;
    private EventRegistrationToken _webResourceRequestedToken;
    private BrowserCapabilities _capabilities = new()
    {
        EngineName = "Native WebView2",
        EngineVersion = "unknown",
        SupportsJavaScript = true,
        SupportsJsBridge = true,
        SupportsCustomSchemes = true,
        SupportsDevTools = true,
        SupportedHostSurfaces = new[] { HostSurfaceKind.Hwnd },
    };

    public string AdapterId => "native-webview2";

    public BrowserCapabilities Capabilities => _capabilities;

    public IJsBridge JsBridge => _bridge;

    public async Task InitializeAsync(
        HostSurfaceDescriptor surface,
        NativeWebHostOptions options,
        CancellationToken cancellationToken = default)
    {
        if (surface.Kind != HostSurfaceKind.Hwnd)
            throw new NotSupportedException(
                $"NativeWebView2Adapter only supports {HostSurfaceKind.Hwnd} host surfaces.");

        if (!surface.IsCreated)
            throw new InvalidOperationException(
                "The supplied host surface has not been created yet.");

        WebView2Utilities.Initialize(typeof(NativeWebView2Adapter).Assembly);

        var browserVersion = WebView2Utilities.GetAvailableCoreWebView2BrowserVersionString();
        if (browserVersion is null)
            throw new InvalidOperationException("WebView2 Runtime is not installed.");

        var userDataFolder = options.UserDataFolder
            ?? Path.Combine(Path.GetTempPath(), "NativeWebHost", SanitizeFolderName(options.Title));

        _environment = await CreateEnvironmentAsync(userDataFolder, options, cancellationToken);
        _controller = await CreateControllerAsync(_environment.Object, surface.Handle, cancellationToken);
        _controller.Object.put_IsVisible(BOOL.TRUE).ThrowOnError();
        _controller.Object.get_CoreWebView2(out var core).ThrowOnError();
        _webView = new ComObject<ICoreWebView2>(core);

        _capabilities = new BrowserCapabilities
        {
            EngineName = "Native WebView2",
            EngineVersion = browserVersion,
            SupportsJavaScript = true,
            SupportsJsBridge = true,
            SupportsCustomSchemes = true,
            SupportsDevTools = true,
            SupportedHostSurfaces = new[] { HostSurfaceKind.Hwnd },
        };

        ConfigureSettings(_webView.Object, options);
        RegisterCustomScheme(_webView.Object, options);
        await _bridge.InitializeAsync(_webView.Object, options, cancellationToken);
        RegisterNavigationReadyTracking(_webView.Object);
        await InjectWindowChromeSupportAsync(_webView.Object, options, cancellationToken);
        await InjectBuiltInTitleBarAsync(_webView.Object, options, cancellationToken);
        await InjectHostCssAsync(_webView.Object, options, cancellationToken);
    }

    public async Task NavigateAsync(string url, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventRegistrationToken token = default;

        using var registration = cancellationToken.Register(
            static state => ((TaskCompletionSource)state!).TrySetCanceled(),
            completion);

        var handler = new CoreWebView2NavigationCompletedEventHandler((_, _) =>
        {
            // 首屏显示只需要等主文档完成一次导航；成功和失败都交给页面自身或 WebView2 错误页呈现。
            completion.TrySetResult();
        });

        _webView!.Object.add_NavigationCompleted(handler, ref token).ThrowOnError();

        try
        {
            _webView.Object.Navigate(PWSTR.From(url)).ThrowOnError();
            await completion.Task;
        }
        finally
        {
            _webView?.Object.remove_NavigationCompleted(token);
        }
    }

    public void Resize(int width, int height)
    {
        _controller?.Object.put_Bounds(RECT.Sized(0, 0, width, height)).ThrowOnError();
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            _bridge.Dispose();
            UnregisterNavigationReadyTracking();
            UnregisterCustomScheme();
            _controller?.Object.Close();
        }
        finally
        {
            _webView?.Dispose();
            _controller?.Dispose();
            _environment?.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private static Task<ComObject<ICoreWebView2Environment>> CreateEnvironmentAsync(
        string userDataFolder,
        NativeWebHostOptions options,
        CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<ComObject<ICoreWebView2Environment>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(
            static state => ((TaskCompletionSource<ComObject<ICoreWebView2Environment>>)state!).TrySetCanceled(),
            tcs);

        var environmentOptions = CreateEnvironmentOptions(options);
        var hr = WebView2.Functions.CreateCoreWebView2EnvironmentWithOptions(
            PWSTR.Null,
            PWSTR.From(userDataFolder),
            environmentOptions,
            new CoreWebView2CreateCoreWebView2EnvironmentCompletedHandler((result, environment) =>
            {
                try
                {
                    if (result.IsError)
                    {
                        tcs.TrySetException(Marshal.GetExceptionForHR(result)!);
                        return;
                    }

                    tcs.TrySetResult(new ComObject<ICoreWebView2Environment>(environment));
                }
                finally
                {
                    environmentOptions.Dispose();
                }
            }));

        if (hr.IsError)
        {
            environmentOptions.Dispose();
            tcs.TrySetException(Marshal.GetExceptionForHR(hr)!);
        }

        return tcs.Task;
    }

    private static CoreWebView2EnvironmentOptions CreateEnvironmentOptions(NativeWebHostOptions options)
    {
        var environmentOptions = new CoreWebView2EnvironmentOptions();

        if (!string.IsNullOrWhiteSpace(options.ContentRootPath))
        {
            var customScheme = new CoreWebView2CustomSchemeRegistration(options.CustomScheme);
            customScheme.put_HasAuthorityComponent(BOOL.TRUE).ThrowOnError();
            customScheme.put_TreatAsSecure(BOOL.TRUE).ThrowOnError();
            customScheme.SetAllowedOrigins([$"{options.CustomScheme}://*"]);
            environmentOptions.SetCustomSchemeRegistrations([customScheme]);
        }

        return environmentOptions;
    }

    private static Task<ComObject<ICoreWebView2Controller>> CreateControllerAsync(
        ICoreWebView2Environment environment,
        nint parentWindow,
        CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<ComObject<ICoreWebView2Controller>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(
            static state => ((TaskCompletionSource<ComObject<ICoreWebView2Controller>>)state!).TrySetCanceled(),
            tcs);

        var hr = environment.CreateCoreWebView2Controller(
            new HWND(parentWindow),
            new CoreWebView2CreateCoreWebView2ControllerCompletedHandler((result, controller) =>
            {
                if (result.IsError)
                {
                    tcs.TrySetException(Marshal.GetExceptionForHR(result)!);
                    return;
                }

                tcs.TrySetResult(new ComObject<ICoreWebView2Controller>(controller));
            }));

        if (hr.IsError)
            tcs.TrySetException(Marshal.GetExceptionForHR(hr)!);

        return tcs.Task;
    }

    private static void ConfigureSettings(ICoreWebView2 webView, NativeWebHostOptions options)
    {
        webView.get_Settings(out var settings).ThrowOnError();
        using var settingsObject = new ComObject<ICoreWebView2Settings>(settings);
        var devTools = options.EnableDevTools ? BOOL.TRUE : BOOL.FALSE;

        settingsObject.Object.put_IsScriptEnabled(BOOL.TRUE).ThrowOnError();
        settingsObject.Object.put_IsWebMessageEnabled(BOOL.TRUE).ThrowOnError();
        settingsObject.Object.put_AreDevToolsEnabled(devTools).ThrowOnError();
        settingsObject.Object.put_AreDefaultContextMenusEnabled(devTools).ThrowOnError();
    }

    private void RegisterNavigationReadyTracking(ICoreWebView2 webView)
    {
        _navigationStartingHandler = new CoreWebView2NavigationStartingEventHandler(
            (_, args) => _bridge.SetDocumentReady(false, TryGetNavigationUri(args)));
        webView.add_NavigationStarting(_navigationStartingHandler, ref _navigationStartingToken).ThrowOnError();

        _navigationCompletedHandler = new CoreWebView2NavigationCompletedEventHandler(
            (_, _) => _bridge.SetDocumentReady(true, TryGetCurrentSource(webView)));
        webView.add_NavigationCompleted(_navigationCompletedHandler, ref _navigationCompletedToken).ThrowOnError();
    }

    /// <summary>读取即将导航的顶层文档地址；读取失败时返回 null 并禁用该文档桥接。</summary>
    private static string? TryGetNavigationUri(ICoreWebView2NavigationStartingEventArgs args)
    {
        PWSTR uri = default;
        try
        {
            args.get_Uri(out uri).ThrowOnError();
            return uri.Value == 0 ? null : uri.ToString();
        }
        catch
        {
            return null;
        }
        finally
        {
            if (uri.Value != 0)
            {
                Marshal.FreeCoTaskMem(uri.Value);
            }
        }
    }

    /// <summary>读取导航完成后的实际地址，覆盖服务端重定向带来的来源变化。</summary>
    private static string? TryGetCurrentSource(ICoreWebView2 webView)
    {
        PWSTR source = default;
        try
        {
            webView.get_Source(out source).ThrowOnError();
            return source.Value == 0 ? null : source.ToString();
        }
        catch
        {
            return null;
        }
        finally
        {
            if (source.Value != 0)
            {
                Marshal.FreeCoTaskMem(source.Value);
            }
        }
    }

    private void UnregisterNavigationReadyTracking()
    {
        if (_webView is null)
            return;

        if (_navigationStartingHandler is not null)
            _webView.Object.remove_NavigationStarting(_navigationStartingToken);

        if (_navigationCompletedHandler is not null)
            _webView.Object.remove_NavigationCompleted(_navigationCompletedToken);

        _navigationStartingHandler = null;
        _navigationCompletedHandler = null;
    }

    private void RegisterCustomScheme(ICoreWebView2 webView, NativeWebHostOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ContentRootPath) || _environment is null)
            return;

        var filter = $"{options.CustomScheme}://*";
        webView.AddWebResourceRequestedFilter(
            PWSTR.From(filter),
            COREWEBVIEW2_WEB_RESOURCE_CONTEXT.COREWEBVIEW2_WEB_RESOURCE_CONTEXT_ALL).ThrowOnError();

        _webResourceRequestedHandler = new CoreWebView2WebResourceRequestedEventHandler(
            (_, args) => HandleWebResourceRequest(args, options.ContentRootPath, _environment.Object));

        webView.add_WebResourceRequested(_webResourceRequestedHandler, ref _webResourceRequestedToken).ThrowOnError();
    }

    private void UnregisterCustomScheme()
    {
        if (_webView is null || _webResourceRequestedHandler is null)
            return;

        _webView.Object.remove_WebResourceRequested(_webResourceRequestedToken);
        _webResourceRequestedHandler = null;
    }

    private static void HandleWebResourceRequest(
        ICoreWebView2WebResourceRequestedEventArgs args,
        string contentRootPath,
        ICoreWebView2Environment environment)
    {
        args.get_Request(out var request).ThrowOnError();
        request.get_Uri(out var requestUri).ThrowOnError();
        var uriText = ToStringAndFree(requestUri);
        if (uriText is null || !Uri.TryCreate(uriText, UriKind.Absolute, out var uri))
        {
            SetWebResourceResponse(args, environment, Stream.Null, 400, "Bad Request", "text/plain");
            return;
        }

        var relative = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'))
            .Replace('/', Path.DirectorySeparatorChar);
        var root = Path.GetFullPath(contentRootPath);
        var fullPath = Path.GetFullPath(Path.Combine(root, relative));

        if (!IsPathInsideRoot(fullPath, root))
        {
            SetWebResourceResponse(args, environment, Stream.Null, 403, "Forbidden", "text/plain");
            return;
        }

        if (!File.Exists(fullPath))
        {
            SetWebResourceResponse(args, environment, Stream.Null, 404, "Not Found", "text/plain");
            return;
        }

        var stream = File.OpenRead(fullPath);
        SetWebResourceResponse(args, environment, stream, 200, "OK", GetMimeType(fullPath));
    }

    private static void SetWebResourceResponse(
        ICoreWebView2WebResourceRequestedEventArgs args,
        ICoreWebView2Environment environment,
        Stream stream,
        int statusCode,
        string reasonPhrase,
        string contentType)
    {
        var content = new ManagedIStream(stream, owned: true);
        environment.CreateWebResourceResponse(
            content,
            statusCode,
            PWSTR.From(reasonPhrase),
            PWSTR.From($"Content-Type: {contentType}\r\n"),
            out var response).ThrowOnError();

        args.put_Response(response).ThrowOnError();
    }

    private static Task InjectWindowChromeSupportAsync(
        ICoreWebView2 webView,
        NativeWebHostOptions options,
        CancellationToken cancellationToken)
    {
        var windowStyle = options.WindowStyle.ToCssToken();
        var script = $$"""
            (function () {
                var style = {{JsonSerializer.Serialize(windowStyle, NativeWebView2JsonContext.Default.String)}};

                function applyWindowStyle() {
                    if (!document.documentElement) return;
                    document.documentElement.style.setProperty('--native-web-window-style', style);
                    document.documentElement.setAttribute('data-native-web-window-style', style);
                }

                function isInteractive(target) {
                    return !!(target && target.closest('button, input, textarea, select, option, a, label, summary, [native-web-no-drag]'));
                }

                function getDragRegion(target) {
                    if (!target || isInteractive(target)) return null;
                    if (style !== 'frameless' && style !== 'vscode') return null;
                    return target.closest('[native-web-drag]');
                }

                var resizeBorder = 8;
                var resizeCursors = {
                    'top-left': 'nwse-resize',
                    'bottom-right': 'nwse-resize',
                    'top-right': 'nesw-resize',
                    'bottom-left': 'nesw-resize',
                    top: 'ns-resize',
                    bottom: 'ns-resize',
                    left: 'ew-resize',
                    right: 'ew-resize'
                };

                function getResizeDirection(e) {
                    if (style !== 'frameless' && style !== 'vscode') return null;
                    if (!e || window.innerWidth <= 0 || window.innerHeight <= 0) return null;
                    if (isInteractive(e.target)) return null;

                    var x = e.clientX;
                    var y = e.clientY;
                    var left = x <= resizeBorder;
                    var right = x >= window.innerWidth - resizeBorder;
                    var top = y <= resizeBorder;
                    var bottom = y >= window.innerHeight - resizeBorder;

                    if (top && left) return 'top-left';
                    if (top && right) return 'top-right';
                    if (bottom && left) return 'bottom-left';
                    if (bottom && right) return 'bottom-right';
                    if (top) return 'top';
                    if (bottom) return 'bottom';
                    if (left) return 'left';
                    if (right) return 'right';
                    return null;
                }

                function setResizeCursor(direction) {
                    if (!document.body) return;
                    document.body.style.cursor = direction ? resizeCursors[direction] : '';
                }

                document.addEventListener('DOMContentLoaded', applyWindowStyle);
                applyWindowStyle();

                document.addEventListener('mousedown', function (e) {
                    if (e.button !== 0) return;
                    var resizeDirection = getResizeDirection(e);
                    if (resizeDirection) {
                        e.preventDefault();
                        nativeWeb.window.startResize({ direction: resizeDirection });
                        return;
                    }

                    if (!getDragRegion(e.target)) return;
                    e.preventDefault();
                    nativeWeb.window.startDrag({
                        button: e.button + 1,
                        screenX: e.screenX,
                        screenY: e.screenY
                    });
                }, true);

                document.addEventListener('mousemove', function (e) {
                    setResizeCursor(getResizeDirection(e));
                }, true);

                document.addEventListener('mouseleave', function () {
                    setResizeCursor(null);
                }, true);

                document.addEventListener('dblclick', function (e) {
                    if (e.button !== 0) return;
                    if (!getDragRegion(e.target)) return;
                    e.preventDefault();
                    nativeWeb.window.maximize();
                }, true);

                document.addEventListener('contextmenu', function (e) {
                    if (!getDragRegion(e.target)) return;
                    e.preventDefault();
                    nativeWeb.window.showSystemMenu({
                        screenX: e.screenX,
                        screenY: e.screenY
                    });
                }, true);
            })();
            """;

        return AddScriptToExecuteOnDocumentCreatedAsync(webView, script, cancellationToken);
    }

    private static Task InjectBuiltInTitleBarAsync(
        ICoreWebView2 webView,
        NativeWebHostOptions options,
        CancellationToken cancellationToken)
    {
        var script = BuiltInTitleBarScriptBuilder.Build(options);
        return string.IsNullOrWhiteSpace(script)
            ? Task.CompletedTask
            : AddScriptToExecuteOnDocumentCreatedAsync(webView, script, cancellationToken);
    }

    private static Task InjectHostCssAsync(
        ICoreWebView2 webView,
        NativeWebHostOptions options,
        CancellationToken cancellationToken)
    {
        var css = BuildHostCss(options);
        if (string.IsNullOrWhiteSpace(css))
            return Task.CompletedTask;

        var script = $$"""
            (function () {
                var css = {{JsonSerializer.Serialize(css, NativeWebView2JsonContext.Default.String)}};
                var styleId = 'native-web-host-style';

                function ensureHostStyle() {
                    var existing = document.getElementById(styleId);
                    if (existing) {
                        existing.textContent = css;
                        return;
                    }

                    var style = document.createElement('style');
                    style.id = styleId;
                    style.textContent = css;
                    (document.head || document.documentElement).appendChild(style);
                }

                if (document.readyState === 'loading') {
                    document.addEventListener('DOMContentLoaded', ensureHostStyle, { once: true });
                }

                ensureHostStyle();
            })();
            """;

        return AddScriptToExecuteOnDocumentCreatedAsync(webView, script, cancellationToken);
    }

    private static string? BuildHostCss(NativeWebHostOptions options) =>
        options.ScrollBarMode switch
        {
            NativeWebScrollBarMode.Auto => null,
            NativeWebScrollBarMode.Hidden => """
                html, body {
                  overflow: hidden !important;
                }

                ::-webkit-scrollbar {
                  width: 0 !important;
                  height: 0 !important;
                }
                """,
            NativeWebScrollBarMode.VerticalOnly => """
                html, body {
                  overflow-x: hidden !important;
                  overflow-y: auto !important;
                }
                """,
            NativeWebScrollBarMode.Custom => !string.IsNullOrWhiteSpace(options.ScrollBarCustomCss)
                ? options.ScrollBarCustomCss
                : throw new InvalidOperationException(
                    "ScrollBarMode is Custom, but ScrollBarCustomCss was not provided."),
            _ => null,
        };

    private static async Task AddScriptToExecuteOnDocumentCreatedAsync(
        ICoreWebView2 webView,
        string script,
        CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(
            static state => ((TaskCompletionSource)state!).TrySetCanceled(),
            tcs);

        var hr = webView.AddScriptToExecuteOnDocumentCreated(
            PWSTR.From(script),
            new CoreWebView2AddScriptToExecuteOnDocumentCreatedCompletedHandler((result, _) =>
            {
                if (result.IsError)
                {
                    tcs.TrySetException(Marshal.GetExceptionForHR(result)!);
                    return;
                }

                tcs.TrySetResult();
            }));

        if (hr.IsError)
            tcs.TrySetException(Marshal.GetExceptionForHR(hr)!);

        await tcs.Task;
    }

    private void EnsureInitialized()
    {
        if (_webView is null)
            throw new InvalidOperationException(
                "NativeWebView2Adapter has not been initialized. Call InitializeAsync first.");
    }

    private static string SanitizeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray());
    }

    private static string GetMimeType(string path)
        => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".html" or ".htm" => "text/html; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".ico" => "image/x-icon",
            ".wasm" => "application/wasm",
            ".map" => "application/json; charset=utf-8",
            ".txt" => "text/plain; charset=utf-8",
            _ => "application/octet-stream",
        };

    private static bool IsPathInsideRoot(string fullPath, string root)
    {
        var normalizedRoot = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        return fullPath.Equals(root, StringComparison.OrdinalIgnoreCase) ||
               fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ToStringAndFree(PWSTR value)
    {
        if (value.Value == 0)
            return null;

        try
        {
            return value.ToString();
        }
        finally
        {
            Marshal.FreeCoTaskMem(value.Value);
        }
    }
}
