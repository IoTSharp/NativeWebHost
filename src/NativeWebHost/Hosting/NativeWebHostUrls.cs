using System.Net;
using System.Net.Sockets;

namespace NativeWebHost;

/// <summary>
/// Helpers for resolving local URLs used by NativeWebHost apps.
/// </summary>
public static class NativeWebHostUrls
{
    public static string ResolveLoopbackUrl(string[] args, string environmentVariableName = "ASPNETCORE_URLS")
    {
        ArgumentNullException.ThrowIfNull(args);

        var explicitUrl = args.FirstOrDefault(arg =>
            arg.StartsWith("--urls=", StringComparison.OrdinalIgnoreCase));
        if (explicitUrl is not null)
        {
            var value = explicitUrl["--urls=".Length..].Trim('"');
            if (!string.IsNullOrWhiteSpace(value))
            {
                return GetFirstUrl(value);
            }
        }

        var urlsIndex = Array.FindIndex(args, arg =>
            string.Equals(arg, "--urls", StringComparison.OrdinalIgnoreCase));
        if (urlsIndex >= 0 &&
            urlsIndex + 1 < args.Length &&
            !string.IsNullOrWhiteSpace(args[urlsIndex + 1]))
        {
            return GetFirstUrl(args[urlsIndex + 1].Trim('"'));
        }

        var environmentUrl = Environment.GetEnvironmentVariable(environmentVariableName);
        if (!string.IsNullOrWhiteSpace(environmentUrl))
        {
            return GetFirstUrl(environmentUrl);
        }

        return $"http://127.0.0.1:{GetAvailableLoopbackPort()}";
    }

    public static int GetAvailableLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static string GetFirstUrl(string value)
        => value.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)[0];
}
