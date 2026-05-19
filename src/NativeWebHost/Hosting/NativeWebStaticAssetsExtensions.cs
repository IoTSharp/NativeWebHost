using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;

namespace NativeWebHost;

/// <summary>
/// ASP.NET Core middleware helpers for NativeWebHost static web apps.
/// </summary>
public static class NativeWebStaticAssetsExtensions
{
    public static WebApplication UseNativeWebStaticAssets(
        this WebApplication app,
        IFileProvider fileProvider,
        string indexFileName = "index.html")
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(fileProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(indexFileName);

        app.UseDefaultFiles(new DefaultFilesOptions
        {
            FileProvider = fileProvider
        });

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = fileProvider
        });

        app.MapFallback(async context =>
        {
            var indexFile = fileProvider.GetFileInfo(indexFileName);
            if (!indexFile.Exists)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            context.Response.ContentType = "text/html; charset=utf-8";
            try
            {
                await using var stream = indexFile.CreateReadStream();
                await stream.CopyToAsync(context.Response.Body, context.RequestAborted);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
            }
        });

        return app;
    }
}
