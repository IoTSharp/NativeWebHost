---
title: Getting Started
---

# Getting Started

## Prerequisites

- .NET 10 SDK or later
- Windows 10/11 for the primary Windows path
- GTK 3 and WebKitGTK native libraries for Linux development

End-user packages should carry any native dependencies that are not guaranteed by the OS. The framework direction is native OS WebViews, not extra desktop UI frameworks.

## Installation

Windows:

```bash
dotnet add package NativeWebHost
dotnet add package NativeWebHost.Windows
```

Linux:

```bash
dotnet add package NativeWebHost
dotnet add package NativeWebHost.Linux
```

## Your First Windows App

Create a project targeting `net10.0-windows`:

```csharp
using NativeWebHost;
using NativeWebHost.Windows;

var app = NativeWebApp.CreateBuilder(args)
    .Configure(o =>
    {
        o.Title = "Hello NativeWebHost";
        o.CustomScheme = "app";
        o.ContentRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        o.StartUrl = "app://localhost/index.html";
        o.Width = 1280;
        o.Height = 800;
        o.EnableDevTools = true;
    })
    .UseAdapter(new NativeWebView2AdapterFactory())
    .UseRuntime(new Win32Runtime())
    .UseDesktopApp(new MyApp())
    .Build();

await app.RunAsync();

sealed class MyApp : IDesktopApp
{
    public Task OnStartAsync(IWebViewAdapter adapter, CancellationToken ct = default)
    {
        adapter.JsBridge.RegisterHandler("greet", payload =>
        {
            var name = payload.Trim('"');
            return Task.FromResult($"\"Hello, {name} from .NET {Environment.Version}!\"");
        });

        return Task.CompletedTask;
    }

    public Task OnClosingAsync(CancellationToken ct = default) => Task.CompletedTask;
}
```

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
          await nativeWeb.invoke('greet', 'World');
      };
    </script>
  </body>
</html>
```

The `nativeWeb` bridge helper is injected automatically. `NativeWebView2AdapterFactory` also registers the configured `app://localhost/...` scheme for local assets.

## Cross-Platform Switch

Use one application surface and select the native runtime/adapter pair at startup:

```csharp
if (OperatingSystem.IsWindows())
{
    builder.UseAdapter(new NativeWebView2AdapterFactory())
        .UseRuntime(new Win32Runtime());
}
else if (OperatingSystem.IsLinux())
{
    builder.UseAdapter(new WebKitGtkAdapterFactory())
        .UseRuntime(new GtkRuntime());
}
else if (OperatingSystem.IsMacOS())
{
    // Planned: AppKit runtime + WKWebView adapter.
}
```

See `samples/NativeWebHost.Sample.Windows`, `samples/NativeWebHost.Sample.Linux`, and `samples/NativeWebHost.Sample.Mac` for the per-OS adapter samples.

## Options

Common host options include:

- `WindowStyle = NativeWebWindowStyle.Frameless`
- `WindowStyle = NativeWebWindowStyle.DwmBlurGlass` on Windows
- `WindowStyle = NativeWebWindowStyle.VsCode`
- `BuiltInTitleBarStyle = NativeWebBuiltInTitleBarStyle.VsCode` or `Office`
- `ScrollBarMode = NativeWebScrollBarMode.Auto`, `Hidden`, `VerticalOnly`, or `Custom`
- `AddWindow("secondary", options => { ... })`
- `IWindowAwareDesktopApp` with `INativeWebWindowManager`

`Win32Runtime` creates its own STA thread and runs a raw Win32 message loop. It has no WinForms or WPF dependency and is suitable for Native AOT-oriented applications.

## Next Steps

- [Architecture](architecture.md)
- [JS Bridge](js-bridge.md)
- [Adapters](adapters.md)
