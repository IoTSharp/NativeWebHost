---
title: Getting Started
---

# Getting Started

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) or later
- Windows 10/11 with [WebView2 Evergreen Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) installed

For the experimental Linux path, you also need GTK 3 and WebKitGTK runtime libraries installed on the machine.

## Installation

```bash
dotnet add package OmniHost
dotnet add package OmniHost.Windows
dotnet add package OmniHost.WebView2
```

If you prefer a Windows Forms host window instead of the raw Win32 host, also add:

```bash
dotnet add package OmniHost.WinForms
```

For the experimental Linux path, use `OmniHost.Gtk` with `OmniHost.WebKitGtk`.
The getting-started sample below still focuses on the more complete Windows/WebView2 path.
You can also inspect `samples/OmniHost.Sample.Gtk` for the current Linux example.
If you want one sample entry that chooses the platform-specific runtime/adapter automatically, inspect `samples/OmniHost.Sample.CrossPlatform`.
For web application embedding patterns, inspect `samples/OmniHost.Sample.WebShowcase`.

## Your First App

Create a console project targeting `net10.0-windows`:

```csharp
using OmniHost;
using OmniHost.Windows;
using OmniHost.WebView2;

var app = OmniApp.CreateBuilder(args)
    .Configure(o =>
    {
        o.Title           = "Hello OmniHost";
        o.CustomScheme    = "app";
        o.ContentRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        o.StartUrl        = "app://localhost/index.html";
        o.Width           = 1280;
        o.Height          = 800;
        o.EnableDevTools  = true;
    })
    .UseAdapter(new WebView2AdapterFactory())
    .UseRuntime(new Win32Runtime())       // AOT-compatible, no WinForms/WPF
    .UseDesktopApp(new MyApp())
    .Build();

await app.RunAsync();

sealed class MyApp : IDesktopApp
{
    public Task OnStartAsync(IWebViewAdapter adapter, CancellationToken ct = default)
    {
        adapter.JsBridge.RegisterHandler("greet", async payload =>
        {
            var name = payload.Trim('"');
            return $"\"Hello, {name} from .NET {Environment.Version}!\"";
        });
        return Task.CompletedTask;
    }

    public Task OnClosingAsync(CancellationToken ct = default) => Task.CompletedTask;
}
```

`OmniHost.WebView2` registers the configured custom scheme when creating the underlying `CoreWebView2Environment`, so `app://localhost/index.html` can be used directly as the startup page.

Optional host-level behaviour can be configured through `OmniHostOptions`, including:

- `WindowStyle = OmniWindowStyle.Frameless` for custom HTML/CSS chrome
- `WindowStyle = OmniWindowStyle.DwmBlurGlass` for a system-frame window with a Windows 11 DWM backdrop when available
- `WindowStyle = OmniWindowStyle.VsCode` for a frameless VS Code-like shell that still uses `omni-drag` and `omni.window.*`
- `BuiltInTitleBarStyle = OmniBuiltInTitleBarStyle.VsCode` or `Office` when the host should render a maintained caption bar for you
- `ScrollBarMode = OmniScrollBarMode.Auto`, `Hidden`, `VerticalOnly`, or `Custom`
- `ScrollBarCustomCss = "..."`
- `AddWindow("secondary", options => { ... })` for extra startup windows on runtimes that support `IMultiWindowDesktopRuntime`
- `IWindowAwareDesktopApp` + `IOmniWindowManager` for runtime window operations such as dynamic open/close, activation, context lookup, and event broadcast

Add `wwwroot/index.html`:

```html
<!DOCTYPE html>
<html>
  <body>
    <button id="btn">Greet</button>
    <p id="out"></p>
    <script>
      document.getElementById('btn').onclick = async () => {
        document.getElementById('out').textContent =
          await omni.invoke('greet', 'World');
      };
    </script>
  </body>
</html>
```

The `omni` bridge helper is automatically injected by OmniHost before each page loads, so no extra script tag is required.

When you use `Win32Runtime`, the host also emits native window lifecycle events back into the page. For example:

```js
omni.on('window.stateChanged', (data) => {
  console.log('Window state:', data.state);
});
```

The `CancellationToken` passed to `OnStartAsync(...)` is tied to the lifetime of that specific host window, which is especially useful for per-window background tasks in multi-window apps.
When you need the window id, the runtime window manager, or per-window option snapshots, implement `IWindowAwareDesktopApp` and use `OnWindowStartAsync(OmniWindowContext, ...)`.

## Win32Runtime and STA

`Win32Runtime` always creates a dedicated STA thread internally, so you can call
`await app.RunAsync()` from a standard async `Main` without any extra threading setup.
It requires no WinForms or WPF dependency, making the application compatible with
.NET AOT (Ahead-of-Time) compilation.

If you want a Windows Forms host window instead, swap in `new WinFormsRuntime()` from
`OmniHost.WinForms`. That path lives in a separate package so the default Win32 runtime
can stay lean and AOT-friendly.

## Next Steps

- [Architecture](architecture.md) — understand how the pieces fit together
- [JS Bridge](js-bridge.md) — full bridge API reference
- [Adapters](adapters.md) — choose or implement a browser adapter

