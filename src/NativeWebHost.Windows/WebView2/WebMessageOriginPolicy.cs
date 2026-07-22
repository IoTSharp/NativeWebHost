namespace NativeWebHost.Windows;

/// <summary>规范化并校验允许调用宿主 JavaScript 桥接的文档来源。</summary>
internal sealed class WebMessageOriginPolicy
{
    private const string AnyOrigin = "*";
    private readonly HashSet<string> _allowedOrigins;
    private readonly bool _allowAnyOrigin;

    /// <summary>保存初始化期间复制并规范化的来源集合，避免运行时配置被并发修改。</summary>
    private WebMessageOriginPolicy(HashSet<string> allowedOrigins, bool allowAnyOrigin)
    {
        _allowedOrigins = allowedOrigins;
        _allowAnyOrigin = allowAnyOrigin;
    }

    /// <summary>按显式来源列表创建策略；未配置列表时仅信任启动地址的来源。</summary>
    internal static WebMessageOriginPolicy Create(NativeWebHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var configuredOrigins = options.WebView2JsBridgeAllowedOrigins;
        if (configuredOrigins is null)
        {
            return new WebMessageOriginPolicy(
                new HashSet<string>(
                    [NormalizeOrigin(options.StartUrl, nameof(options.StartUrl))],
                    StringComparer.OrdinalIgnoreCase),
                allowAnyOrigin: false);
        }

        var allowedOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allowAnyOrigin = false;
        for (var index = 0; index < configuredOrigins.Count; index++)
        {
            var configuredOrigin = configuredOrigins[index];
            if (string.Equals(configuredOrigin?.Trim(), AnyOrigin, StringComparison.Ordinal))
            {
                allowAnyOrigin = true;
                continue;
            }

            allowedOrigins.Add(NormalizeOrigin(
                configuredOrigin,
                $"{nameof(options.WebView2JsBridgeAllowedOrigins)}[{index}]"));
        }

        return new WebMessageOriginPolicy(allowedOrigins, allowAnyOrigin);
    }

    /// <summary>校验消息来源，并返回用于导航竞态比较的规范化来源。</summary>
    internal bool TryGetAllowedOrigin(string? source, out string normalizedOrigin)
    {
        if (!TryNormalizeOrigin(source, out normalizedOrigin))
        {
            return false;
        }

        return _allowAnyOrigin || _allowedOrigins.Contains(normalizedOrigin);
    }

    /// <summary>把配置来源转换为稳定比较值，并为无效绝对地址提供明确异常。</summary>
    private static string NormalizeOrigin(string? value, string parameterName)
    {
        if (!TryNormalizeOrigin(value, out var normalizedOrigin))
        {
            throw new ArgumentException("JavaScript 桥接来源必须是有效的绝对 URI。", parameterName);
        }

        return normalizedOrigin;
    }

    /// <summary>有主机名的 URI 只保留源，about、file 等 URI 则保留不含查询的绝对路径。</summary>
    private static bool TryNormalizeOrigin(string? value, out string normalizedOrigin)
    {
        normalizedOrigin = string.Empty;
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        normalizedOrigin = string.IsNullOrEmpty(uri.Host)
            ? uri.GetLeftPart(UriPartial.Path)
            : uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped).TrimEnd('/');
        return !string.IsNullOrWhiteSpace(normalizedOrigin);
    }
}
