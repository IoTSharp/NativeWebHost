# NativeWebHost

**A .NET desktop WebView host focused on native OS shells.**

[![NuGet](https://img.shields.io/nuget/v/NativeWebHost?label=NuGet)](https://www.nuget.org/packages/NativeWebHost)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Build](https://github.com/IoTSharp/NativeWebHost/actions/workflows/build.yml/badge.svg)](https://github.com/IoTSharp/NativeWebHost/actions)

NativeWebHost lets a .NET application host a web UI inside a native desktop window. The project now keeps one native path per operating system instead of carrying multiple UI-framework-specific shells.

## Platform Strategy

| OS | Window runtime | WebView adapter | Status |
|----|----------------|-----------------|--------|
| Windows | raw Win32 in `NativeWebHost.Windows` | WebView2 in `NativeWebHost.Windows` | Primary path |
| Linux | GTK 3 in `NativeWebHost.Linux` | WebKitGTK in `NativeWebHost.Linux` | Experimental |
| macOS | AppKit in `NativeWebHost.Mac` | WKWebView in `NativeWebHost.Mac` | Planned |

Removed paths: `NativeWebHost.WinForms`, `NativeWebHost.WebView2`, and `NativeWebHost.Cef`. The Windows path is now raw Win32 plus WebView2Aot-based native WebView2, with no WinForms or WPF dependency.

The packaging goal is that end users do not install extra frameworks manually. Windows uses the OS WebView2 runtime or an app-packaged fixed WebView2 runtime. macOS uses system WebKit. Linux should package the needed GTK/WebKitGTK native libraries with the app image/package.

## Quick Start

```csharp
using NativeWebHost;
using NativeWebHost.Windows;

var app = NativeWebApp.CreateBuilder(args)
    .Configure(o =>
    {
        o.Title = "My App";
        o.CustomScheme = "app";
        o.ContentRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        o.StartUrl = "app://localhost/index.html";
        o.Width = 1280;
        o.Height = 800;
    })
    .UseAdapter(new NativeWebView2AdapterFactory())
    .UseRuntime(new Win32Runtime())
    .Build();

await app.RunAsync();
```

`NativeWebHost.Windows` supports `app://localhost/...` local assets, JavaScript bridge injection, custom window chrome helpers, and built-in title-bar presets.

The `samples` folder contains one adapter sample per operating system: `NativeWebHost.Sample.Windows`, `NativeWebHost.Sample.Linux`, and `NativeWebHost.Sample.Mac`.

## Features

- Native host windows with shared runtime/adapter abstractions
- `nativeWeb.invoke(...)` and `nativeWeb.on(...)` JavaScript bridge
- `app://localhost/...` local asset loading
- Multi-window startup and dynamic window management
- Splash windows
- Window style presets such as normal, frameless, DWM blur glass, and VS Code-style chrome where the OS runtime supports them
- Per-OS adapter samples for Windows, Linux, and macOS

## Documentation

| Document | Description |
|----------|-------------|
| [Getting Started](docs/getting-started.md) | Installation and first app |
| [Architecture](docs/architecture.md) | Component design and boundaries |
| [JS Bridge](docs/js-bridge.md) | C# to JavaScript messaging |
| [Adapters](docs/adapters.md) | Browser engine adapters |
| [Migration Guide](MIGRATION.md) | Upgrade notes |
| [Roadmap](docs/roadmap.md) | Release plan |

## License

MIT - see [LICENSE](LICENSE).
