# NativeWebHost macOS Sample

This sample shows the intended macOS wiring for `NativeWebHost.Mac`:

```csharp
.UseAdapter(new WKWebViewAdapterFactory())
.UseRuntime(new MacRuntime())
```

It uses the same multi-window bridge surface as the Windows and Linux adapter samples.

The current `NativeWebHost.Mac` package is still a placeholder for the AppKit runtime and WKWebView adapter, so this sample documents the app shape now and will become runnable once the macOS runtime is implemented.

Run from macOS:

```bash
dotnet run --project samples/NativeWebHost.Sample.Mac
```
