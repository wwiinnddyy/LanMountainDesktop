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

        // Prefer the selected source first so S3 stays primary for 智慧更新.
        if (sourceKind is "s3")
        {
            AddS3(urls, manifest, mode);
            urls.Add(DerivePackageUrl(source.ManifestUrl, mode));
            urls.Add(DeriveFromStaticBase(mode));
            AddGitHub(urls, manifest, mode);
        }
        else if (sourceKind is "github")
        {
            AddGitHub(urls, manifest, mode);
            urls.Add(DerivePackageUrl(source.ManifestUrl, mode));
            AddS3(urls, manifest, mode);
            urls.Add(DeriveFromStaticBase(mode));
        }
        else
        {
            urls.Add(DerivePackageUrl(source.ManifestUrl, mode));
            AddS3(urls, manifest, mode);
            AddGitHub(urls, manifest, mode);
            urls.Add(DeriveFromStaticBase(mode));
        }

        return urls
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => Uri.TryCreate(url!.Trim(), UriKind.Absolute, out var uri) ? uri : null)
            .OfType<Uri>()
            .Where(uri => uri.Scheme is "http" or "https")
            .DistinctBy(uri => uri.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddS3(List<string?> urls, PlondsClientManifest manifest, PlondsPackageMode mode)
    {
        if (mode is PlondsPackageMode.Delta)
        {
            urls.Add(manifest.Downloads?.S3?.ChangedZipUrl);
            urls.Add(manifest.Downloads?.S3?.ChangedFolderUrl);
        }
        else
        {
            urls.Add(manifest.Downloads?.S3?.FilesZipUrl);
            urls.Add(manifest.Downloads?.S3?.FilesFolderUrl);
        }
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

        // Accept both "Files.zip" (tool convention) and "files.zip".
        var packageName = mode is PlondsPackageMode.Delta ? "changed.zip" : "Files.zip";
        var builder = new UriBuilder(uri)
        {
            Query = string.Empty,
            Fragment = string.Empty
        };
        var lastSlash = builder.Path.LastIndexOf('/');
        builder.Path = lastSlash >= 0
            ? $"{builder.Path[..(lastSlash + 1)]}{packageName}"
            : packageName;
        return builder.Uri.AbsoluteUri;
    }

    private static string? DeriveFromStaticBase(PlondsPackageMode mode)
    {
        var baseUrl = Environment.GetEnvironmentVariable(UpdateSettingsValues.PlondsStaticBaseUrlEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = UpdateSettingsValues.DefaultPlondsStaticBaseUrl;
        }

        baseUrl = baseUrl.Trim().TrimEnd('/');
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out _))
        {
            return null;
        }

        var packageName = mode is PlondsPackageMode.Delta ? "changed.zip" : "Files.zip";
        return $"{baseUrl}/plonds/{packageName}";
    }
}
