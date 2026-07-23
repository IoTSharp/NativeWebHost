# Changelog

All notable changes to this project will be documented in this file.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

---

## [Unreleased]

### Fixed

- Closed the task-definition write handle before invoking `schtasks.exe`, preventing MSI autostart registration from failing with a sharing violation.

### Changed

- Renamed the public framework family to `NativeWebHost`.
- Consolidated the NuGet surface to four packages:
  - `NativeWebHost`
  - `NativeWebHost.Windows`
  - `NativeWebHost.Linux`
  - `NativeWebHost.Mac`
- Merged the former abstractions, core, hosting, Win32/WebView2, and GTK/WebKitGTK implementation projects into the shared package plus platform packages.

### Added

- `NativeWebHost.Windows`, the Windows WebView2 adapter based on WebView2Aot COM bindings.
- Native WebView2 support for `app://localhost/...` local asset loading and built-in title-bar injection.
- Windows window-style presets now include:
  - `NativeWebWindowStyle.DwmBlurGlass`
  - `NativeWebWindowStyle.VsCode`
- The WebView host bridge now exposes multi-value window style tokens to the page root via:
  - `--native-web-window-style`
  - `data-native-web-window-style`
- Native window lifecycle events are now pushed back through the JS bridge:
  - `window.stateChanged`
  - `window.closing`
  - `window.closed`
- Public startup multi-window support via:
  - `NativeWebHostBuilder.AddWindow(...)`
  - `IMultiWindowDesktopRuntime`
  - `NativeWebWindowDefinition`
- Context-aware window management via:
  - `IWindowAwareDesktopApp`
  - `NativeWebWindowContext`
  - `INativeWebWindowManager`
- Richer window manager operations via:
  - `GetWindowContext(windowId)`
  - `TryActivateWindow(windowId)`
  - `PostEventAsync(...)`
  - `BroadcastEventAsync(...)`
- `NativeWebHost.Linux`, an experimental first-pass Linux GTK runtime/window-host package.
- `NativeWebHost.Linux`, an experimental first-pass Linux WebKitGTK adapter package.
- The basic sample now shows a live window lifecycle event stream.
- The basic sample now launches a secondary startup window and can open/close an inspector window at runtime, activate windows by id, inspect live window context, and broadcast host events.
- Added one adapter sample per operating system under `samples/NativeWebHost.Sample.Windows`, `samples/NativeWebHost.Sample.Linux`, and `samples/NativeWebHost.Sample.Mac`.
- Linux GTK host windows now register richer `nativeWeb.window.*` bridge handlers, including drag start, better state synchronization, and best-effort system-menu support.
- `NativeWebHost.Linux` now serves `app://` assets through a native WebKitGTK URI scheme handler instead of relying on `file://` URL translation.
- `NativeWebHost.Android`, an experimental Android Activity + system WebView adapter package.
- Android APK/AAB asset loading from `wwwroot`, JavaScript bridge injection, and same-origin `/api/...` fetch interception for app-provided handlers.

### Changed

- Windows samples now use `NativeWebHost.Windows` with `NativeWebHost.Windows`.
- `DwmBlurGlass` support in `NativeWebHost.Windows` is implemented as an app-local, public-DWM-API preset.
  It does not embed the external `DWMBlurGlass` project's system-wide hook/injection pipeline.

### Changed - breaking

- Removed `NativeWebHost.WinForms`, `NativeWebHost.WebView2`, `NativeWebHost.Cef`, and `samples/NativeWebHost.Sample.Cef`.
- Windows applications should use `NativeWebHost.Windows` with `NativeWebHost.Windows`.
- Renamed the technical package / namespace family from `NativeWebHost*` to `NativeWebHost*`.
- Solution, project files, assembly names, namespaces, and samples now use:
  - `NativeWebHost`
  - `NativeWebHost.Abstractions`
  - `NativeWebHost.Core`
  - `NativeWebHost.Hosting`
  - `NativeWebHost.Windows`
  - `NativeWebHost.Windows`
- `UseNativeWebHost()` was renamed to `UseNativeWebHost()`.
- `NativeWebHostOptions`, `NativeWebHostBuilder`, and related `NativeWebHost*` type names
  were renamed to their `NativeWebHost*` equivalents.

---

## [0.1.0-preview.3] - 2026-03-22

### Changed - breaking

- `NativeWebHost.WebView2` no longer depends on Windows Forms.
  `<UseWindowsForms>` has been removed from the project.
  This unblocks AOT (Ahead-of-Time) compilation.
- `WinFormsRuntime` and `NativeWebHostForm` were removed and replaced by `Win32Runtime` and `Win32HostWindow`.
- `Frameless` mode now uses a DWM custom-frame path instead of relying only on stripped non-client rendering.

### Added

- `NativeWebWindowStyle` enum (`Normal`, `Frameless`) in `NativeWebHost.Abstractions`.
- `NativeWebScrollBarMode` enum (`Auto`, `Hidden`, `VerticalOnly`, `Custom`) in `NativeWebHost.Abstractions`.
- `NativeWebHostOptions.WindowStyle` to select `Normal` (OS chrome) or `Frameless` (custom HTML/CSS chrome).
- `NativeWebHostOptions.ScrollBarMode` and `ScrollBarCustomCss` for host-level scrollbar control.
- `Win32Runtime`, a new AOT-compatible `IDesktopRuntime` backed by raw Win32 P/Invoke.
- `Win32HostWindow`, a raw Win32 HWND host with:
  - `RegisterClassExW` / `CreateWindowExW` / Win32 message loop
  - `Win32SynchronizationContext` posting async continuations through `WM_APP + 1`
  - DWM custom-frame handling for frameless windows
  - `WM_NCHITTEST` override for resizable frameless windows
- Window-control JS bridge handlers:
  - `nativeWeb.invoke("window.minimize")`
  - `nativeWeb.invoke("window.maximize")`
  - `nativeWeb.invoke("window.close")`
  - `nativeWeb.invoke("window.startDrag")`
  - `nativeWeb.invoke("window.showSystemMenu")`
- `nativeWeb.window.*` convenience helpers injected into every page.
- Automatic `native-web-drag` support for frameless pages:
  - drag to move the window
  - double-click to maximize / restore
  - right-click to open the native system menu
- `NativeMethods` (internal) with Win32 and DWM P/Invoke declarations using `ExactSpelling = true` and `CharSet.Unicode`.
- Sample updated to use `Win32Runtime`; `wwwroot/index.html` now demonstrates a frameless title bar, window controls, and host-level scrollbar configuration.

---

## [0.1.0-preview.2] - 2026-03-22

### Added

- `IDesktopRuntime` interface in `NativeWebHost.Abstractions` to decouple the window/message-loop from the core builder.
- `NativeWebHostOptions` gained `CustomScheme`, `ContentRootPath`, and `UserDataFolder` properties.
- `NativeWebHostBuilder.UseRuntime(IDesktopRuntime)` and `UseDesktopApp(IDesktopApp)` methods.
- `WinFormsRuntime`, a production `IDesktopRuntime` backed by Windows Forms with automatic STA-thread creation.
- `NativeWebHostForm`, an internal WinForms `Form` that hosts WebView2, handles resize, and performs graceful shutdown.
- `WebView2AdapterFactory.IsAvailable` with a real runtime check via `CoreWebView2Environment.GetAvailableBrowserVersionString()`.
- `WebView2Adapter` with real `CoreWebView2Environment` and `CoreWebView2Controller` initialization.
- `WebView2Adapter` custom-scheme handler serving local files from `ContentRootPath` via `app://localhost/...` with path-traversal protection.
- `WebView2JsBridge`, fully wired:
  - `ExecuteScriptAsync` -> `CoreWebView2.ExecuteScriptAsync`
  - `RegisterHandler` + `WebMessageReceived` dispatcher
  - `PostMessageAsync` -> typed event envelope dispatched through WebView2 messaging
  - injected `nativeWeb.js` bridge script providing `nativeWeb.invoke(handler, data)` and `nativeWeb.on(event, cb)`
- `samples/NativeWebHost.Sample.Basic`, an end-to-end sample with `greet`, `sysinfo`, and `tick` handlers.
- `samples/NativeWebHost.Sample.Basic/wwwroot/index.html`, an interactive demo page.

### Changed

- `NativeWebHostApp.RunAsync` now delegates to `IDesktopRuntime.Run`.
- `WebView2Adapter.Capabilities.EngineVersion` is populated from the real WebView2 runtime version string.

---

## [0.1.0-preview.1] - 2026-03-22

### Added

- Repository structure: `src/`, `samples/`, `docs/`
- `NativeWebHost.Abstractions` public contracts:
  - `IWebViewAdapter` / `IWebViewAdapterFactory`
  - `IJsBridge`
  - `IDesktopApp`
  - `BrowserCapabilities`
  - `NativeWebHostOptions`
- `NativeWebHost.Core` builder and application runner scaffolding:
  - `NativeWebApp.CreateBuilder(args)` entry point
  - `NativeWebHostBuilder` / `INativeWebHostApp`
- `NativeWebHost`, the top-level package re-exporting the entry point.
- `NativeWebHost.Hosting` with `IHostBuilder.UseNativeWebHost()` extension.
- `NativeWebHost.WebView2`, the Windows/WebView2 adapter placeholder:
  - `WebView2Adapter`
  - `WebView2AdapterFactory`
  - `WebView2JsBridge`
- `samples/NativeWebHost.Sample.Basic`, a minimal compilable sample.
- Documentation baseline: `README.md`, `ROADMAP.md`, `CHANGELOG.md`, `docs/`.
