namespace LanMountainDesktop.Services.Plonds;

internal static class PlondsDownloadUrlResolver
{
    public static IReadOnlyList<Uri> Resolve(
        PlondsClientManifest manifest,
        PlondsSourceDescriptor source,
        PlondsPackageMode mode)
    {
        var urls = new List<string?>();
        var sourceKind = source.Kind.Trim().ToLowerInvariant();

        if (sourceKind is "s3")
        {
            AddS3(urls, manifest, mode);
        }
        else if (sourceKind is "github")
        {
            AddGitHub(urls, manifest, mode);
        }

        urls.Add(DerivePackageUrl(source.ManifestUrl, mode));
        AddS3(urls, manifest, mode);
        AddGitHub(urls, manifest, mode);

        return urls
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : null)
            .OfType<Uri>()
            .Where(uri => uri.Scheme is "http" or "https")
            .DistinctBy(uri => uri.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddS3(List<string?> urls, PlondsClientManifest manifest, PlondsPackageMode mode)
    {
        urls.Add(mode is PlondsPackageMode.Delta
            ? manifest.Downloads?.S3?.ChangedZipUrl
            : manifest.Downloads?.S3?.FilesZipUrl);
    }

    private static void AddGitHub(List<string?> urls, PlondsClientManifest manifest, PlondsPackageMode mode)
    {
        urls.Add(mode is PlondsPackageMode.Delta
            ? manifest.Downloads?.GitHub?.ChangedZipUrl
            : manifest.Downloads?.GitHub?.FilesZipUrl);
    }

    private static string? DerivePackageUrl(string manifestUrl, PlondsPackageMode mode)
    {
        if (!Uri.TryCreate(manifestUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            return null;
        }

        var packageName = mode is PlondsPackageMode.Delta ? "changed.zip" : "Files.zip";
        var builder = new UriBuilder(uri);
        var lastSlash = builder.Path.LastIndexOf('/');
        builder.Path = lastSlash >= 0
            ? $"{builder.Path[..(lastSlash + 1)]}{packageName}"
            : packageName;
        builder.Query = string.Empty;
        builder.Fragment = string.Empty;
        return builder.Uri.AbsoluteUri;
    }
}
