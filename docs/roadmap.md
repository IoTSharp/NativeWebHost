---
title: Roadmap
---

# Roadmap

## Decision

NativeWebHost keeps one native desktop path per operating system:

| OS | Runtime | WebView |
|----|---------|---------|
| Windows | raw Win32 | Native WebView2 |
| Linux | GTK 3 | WebKitGTK |
| macOS | AppKit | WKWebView |

Removed paths: WinForms, the old managed `NativeWebHost.WebView2` adapter, and CefSharp/CEF.

## Current

- Windows: `NativeWebHost` + `NativeWebHost.Windows`
- Linux: `NativeWebHost` + `NativeWebHost.Linux`
- Shared: multi-window lifecycle, splash windows, JavaScript bridge, `app://localhost/...` local asset loading

## Next

- Implement `NativeWebHost.Mac` with AppKit + WKWebView.
- Bring macOS bridge, custom-scheme loading, and window management to parity with Windows/Linux.
- Add packaging guidance for:
  - Windows fixed WebView2 runtime when Evergreen cannot be assumed
  - macOS `.app` / `.dmg`
  - Linux AppImage or distro packages that bundle GTK/WebKitGTK dependencies where appropriate

## Samples

- `samples/NativeWebHost.Sample.Windows`
- `samples/NativeWebHost.Sample.Linux`
- `samples/NativeWebHost.Sample.Mac`
