using System.Reflection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace NativeWebHost;

/// <summary>
/// Serves web assets embedded as manifest resources.
/// </summary>
public sealed class EmbeddedWebAssetFileProvider : IFileProvider
{
    private static readonly DateTimeOffset EmbeddedLastModified = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly Assembly _assembly;
    private readonly string _resourcePrefix;
    private readonly Dictionary<string, string> _resources;

    public EmbeddedWebAssetFileProvider(Assembly assembly, string resourcePrefix = "wwwroot/")
    {
        _assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
        _resourcePrefix = NormalizeResourcePrefix(resourcePrefix);
        _resources = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(_resourcePrefix, StringComparison.Ordinal))
            .ToDictionary(NormalizeResourcePath, StringComparer.OrdinalIgnoreCase);
    }

    public bool HasIndex => HasFile("index.html");

    public bool HasFile(string subpath)
        => _resources.ContainsKey(NormalizeRequestPath(subpath));

    public IDirectoryContents GetDirectoryContents(string subpath)
    {
        var prefix = NormalizeRequestPath(subpath);
        if (prefix.Length > 0 && !prefix.EndsWith("/", StringComparison.Ordinal))
        {
            prefix += "/";
        }

        var entries = new Dictionary<string, IFileInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var resource in _resources)
        {
            if (!resource.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relative = resource.Key[prefix.Length..];
            if (relative.Length == 0)
            {
                continue;
            }

            var slashIndex = relative.IndexOf('/');
            if (slashIndex >= 0)
            {
                var directoryName = relative[..slashIndex];
                entries.TryAdd(directoryName, new EmbeddedDirectoryInfo(directoryName));
                continue;
            }

            entries.TryAdd(relative, new EmbeddedWebAssetFileInfo(_assembly, resource.Value, relative));
        }

        return entries.Count == 0
            ? NotFoundDirectoryContents.Singleton
            : new EnumerableDirectoryContents(entries.Values);
    }

    public IFileInfo GetFileInfo(string subpath)
    {
        var resourcePath = NormalizeRequestPath(subpath);
        return _resources.TryGetValue(resourcePath, out var resourceName)
            ? new EmbeddedWebAssetFileInfo(_assembly, resourceName, Path.GetFileName(resourcePath))
            : new NotFoundFileInfo(Path.GetFileName(resourcePath));
    }

    public IChangeToken Watch(string filter)
        => NullChangeToken.Singleton;

    public async Task WriteFileAsync(string subpath, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);

        var fileInfo = GetFileInfo(subpath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException($"Embedded web asset not found: {subpath}", subpath);
        }

        await using var source = fileInfo.CreateReadStream();
        await source.CopyToAsync(destination, cancellationToken);
    }

    private string NormalizeResourcePath(string resourceName)
        => resourceName[_resourcePrefix.Length..].Replace('\\', '/');

    private static string NormalizeResourcePrefix(string resourcePrefix)
    {
        var prefix = string.IsNullOrWhiteSpace(resourcePrefix)
            ? string.Empty
            : resourcePrefix.Trim().TrimStart('/').Replace('\\', '/');

        return prefix.Length == 0 || prefix.EndsWith("/", StringComparison.Ordinal)
            ? prefix
            : prefix + "/";
    }

    private static string NormalizeRequestPath(string subpath)
        => subpath.Trim().TrimStart('/').Replace('\\', '/').Replace("//", "/", StringComparison.Ordinal);

    private sealed class EmbeddedWebAssetFileInfo : IFileInfo
    {
        private readonly Assembly _assembly;
        private readonly string _resourceName;

        public EmbeddedWebAssetFileInfo(Assembly assembly, string resourceName, string name)
        {
            _assembly = assembly;
            _resourceName = resourceName;
            Name = name;
        }

        public bool Exists => true;

        public long Length
        {
            get
            {
                using var stream = CreateReadStream();
                return stream.CanSeek ? stream.Length : -1;
            }
        }

        public string? PhysicalPath => null;

        public string Name { get; }

        public DateTimeOffset LastModified => EmbeddedLastModified;

        public bool IsDirectory => false;

        public Stream CreateReadStream()
            => _assembly.GetManifestResourceStream(_resourceName) ??
               throw new FileNotFoundException($"Embedded resource not found: {_resourceName}", _resourceName);
    }

    private sealed class EmbeddedDirectoryInfo : IFileInfo
    {
        public EmbeddedDirectoryInfo(string name)
            => Name = name;

        public bool Exists => true;

        public long Length => -1;

        public string? PhysicalPath => null;

        public string Name { get; }

        public DateTimeOffset LastModified => EmbeddedLastModified;

        public bool IsDirectory => true;

        public Stream CreateReadStream()
            => Stream.Null;
    }

    private sealed class EnumerableDirectoryContents : IDirectoryContents
    {
        private readonly IReadOnlyList<IFileInfo> _entries;

        public EnumerableDirectoryContents(IEnumerable<IFileInfo> entries)
            => _entries = entries.ToArray();

        public bool Exists => true;

        public IEnumerator<IFileInfo> GetEnumerator()
            => _entries.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            => GetEnumerator();
    }
}
