namespace LanDesktopPLONDS.Installer.Services;

internal static class InstallerPlondsUrlResolver
{
    public static IReadOnlyList<Uri> ResolveFilesZipUrls(
        InstallerPlondsManifest manifest,
        InstallerPlondsSource source)
    {
        var urls = new List<string?>();
        var sourceKind = source.Kind.Trim().ToLowerInvariant();

        if (sourceKind is "s3")
        {
            urls.Add(manifest.Downloads?.S3?.FilesZipUrl);
        }
        else if (sourceKind is "github")
        {
            urls.Add(manifest.Downloads?.GitHub?.FilesZipUrl);
        }

        urls.Add(DerivePackageUrl(source.ManifestUrl));
        urls.Add(manifest.Downloads?.S3?.FilesZipUrl);
        urls.Add(manifest.Downloads?.GitHub?.FilesZipUrl);

        return urls
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : null)
            .OfType<Uri>()
            .Where(uri => uri.Scheme is "http" or "https")
            .DistinctBy(uri => uri.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? DerivePackageUrl(string manifestUrl)
    {
        if (!Uri.TryCreate(manifestUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            return null;
        }

        var builder = new UriBuilder(uri);
        var lastSlash = builder.Path.LastIndexOf('/');
        builder.Path = lastSlash >= 0
            ? $"{builder.Path[..(lastSlash + 1)]}Files.zip"
            : "Files.zip";
        builder.Query = string.Empty;
        builder.Fragment = string.Empty;
        return builder.Uri.AbsoluteUri;
    }
}
