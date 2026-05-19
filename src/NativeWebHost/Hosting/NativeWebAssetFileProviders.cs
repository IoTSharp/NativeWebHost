using System.Reflection;
using Microsoft.Extensions.FileProviders;

namespace NativeWebHost;

/// <summary>
/// Helpers for resolving hosted web asset file providers.
/// </summary>
public static class NativeWebAssetFileProviders
{
    public static IFileProvider CreateHybridWebAssetProvider(
        string physicalRoot,
        Assembly embeddedAssembly,
        string resourcePrefix = "wwwroot/",
        string indexFileName = "index.html")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(physicalRoot);
        ArgumentNullException.ThrowIfNull(embeddedAssembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(indexFileName);

        var embeddedProvider = new EmbeddedWebAssetFileProvider(embeddedAssembly, resourcePrefix);
        var hasEmbeddedAssets = embeddedProvider.HasFile(indexFileName);
        var hasPhysicalAssets = File.Exists(Path.Combine(physicalRoot, indexFileName));

        if (hasEmbeddedAssets && hasPhysicalAssets)
        {
            return new CompositeFileProvider(
                new PhysicalFileProvider(physicalRoot),
                embeddedProvider);
        }

        if (hasPhysicalAssets)
        {
            return new PhysicalFileProvider(physicalRoot);
        }

        return hasEmbeddedAssets
            ? embeddedProvider
            : new PhysicalFileProvider(physicalRoot);
    }
}
