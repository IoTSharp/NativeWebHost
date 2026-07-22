# NativeWebHost

**A .NET WebView host focused on native OS shells.**

[![NuGet](https://img.shields.io/nuget/v/NativeWebHost?label=NuGet)](https://www.nuget.org/packages/NativeWebHost)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Build](https://github.com/IoTSharp/NativeWebHost/actions/workflows/build.yml/badge.svg)](https://github.com/IoTSharp/NativeWebHost/actions)

NativeWebHost lets a .NET application host a web UI inside a native OS shell. The project now keeps one native path per operating system instead of carrying multiple UI-framework-specific shells.

## Platform Strategy

| OS | Window runtime | WebView adapter | Status |
|----|----------------|-----------------|--------|
| Windows | raw Win32 in `NativeWebHost.Windows` | WebView2 in `NativeWebHost.Windows` | Primary path |
| Linux | GTK 3 in `NativeWebHost.Linux` | WebKitGTK in `NativeWebHost.Linux` | Experimental |
| macOS | AppKit in `NativeWebHost.Mac` | WKWebView in `NativeWebHost.Mac` | Experimental |
| Android | Activity in `NativeWebHost.Android` | system WebView in `NativeWebHost.Android` | Experimental |

Removed paths: `NativeWebHost.WinForms`, `NativeWebHost.WebView2`, and `NativeWebHost.Cef`. The Windows path is now raw Win32 plus WebView2Aot-based native WebView2, with no WinForms or WPF dependency.

The packaging goal is that end users do not install extra frameworks manually. Windows uses the OS WebView2 runtime or an app-packaged fixed WebView2 runtime. macOS uses system WebKit. Linux should package the needed GTK/WebKitGTK native libraries with the app image/package. Android uses the system WebView and serves app assets from the APK/AAB.

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

## Windows Application Integration

`NativeWebHost.Windows` can opt into elevation, an elevated interactive logon task, and a desktop shortcut before the main window opens:

```csharp
var runtime = new Win32Runtime(new Win32RuntimeOptions
{
    RequireAdministrator = true,
    EnsureElevatedAutoStart = true,
    AutoStartTaskName = "My App",
    AutoStartArguments = ["--autostart"],
    EnsureDesktopShortcut = true,
    DesktopShortcutName = "My App"
});
```

NuGet consumers can embed the supplied administrator manifest before process startup by adding this opt-in build property to the executable project:

```xml
<NativeWebHostRequireAdministrator>true</NativeWebHostRequireAdministrator>
```

The runtime check remains in place as a fallback if a consuming executable omits the manifest. `WindowsApplicationRegistration` also exposes the individual shortcut and elevated-logon-task operations for installers and other hosts.

Native AOT-compatible Windows SAPI playback is available independently of the window runtime:

```csharp
using var speech = new WindowsTextToSpeech();
speech.TrySpeak("Vehicle notification");
```

The Windows WebView2 JavaScript bridge accepts messages only from the `StartUrl` origin by default. Set `WebView2JsBridgeAllowedOrigins` to an explicit origin list for trusted redirects, to an empty list to disable inbound bridge calls, or to `["*"]` only when every navigated document is fully trusted.

`NativeWebHost.Windows`, `NativeWebHost.Linux`, and `NativeWebHost.Mac` support `app://localhost/...` local assets, JavaScript bridge injection, and native window hosting for their platform WebView engines. `NativeWebHost.Android` provides an Activity base, APK asset loading through `https://appassets.androidplatform.net/`, JavaScript bridge injection, and same-origin `/api/...` fetch interception for app-provided handlers.

The `samples` folder contains adapter samples for Windows, Linux, and macOS. Android is currently consumed through `NativeWebHostAndroidActivity`.

## Features

- Native host windows with shared runtime/adapter abstractions
- `nativeWeb.invoke(...)` and `nativeWeb.on(...)` JavaScript bridge
- `app://localhost/...` local asset loading
- Android APK/AAB asset loading through the platform WebView
- Multi-window startup and dynamic window management
- Splash windows
- Windows tray menus, elevated auto-start, desktop shortcuts, and SAPI text-to-speech
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
