using System.Text.Json;

namespace LanMountainDesktop.Shared.Contracts.Deployment;

internal static class ReleaseManifestLocator
{
    internal static IReadOnlyList<string> SelectAssetUrls(string json, string platform, bool includePrerelease)
    {
        using var document = JsonDocument.Parse(json);
        var candidates = document.RootElement.EnumerateArray()
            .Where(release => !release.GetProperty("draft").GetBoolean() &&
                (includePrerelease || !release.GetProperty("prerelease").GetBoolean()))
            .Where(release => Version.TryParse(release.GetProperty("tag_name").GetString()?.TrimStart('v', 'V'), out _))
            .OrderByDescending(release => Version.Parse(release.GetProperty("tag_name").GetString()!.TrimStart('v', 'V')));
        var urls = new List<string>();
        foreach (var release in candidates)
        {
            var assets = release.GetProperty("assets").EnumerateArray().ToArray();
            var asset = assets.FirstOrDefault(item => item.GetProperty("name").GetString() == $"PLONDS-{platform}.json");
            if (asset.ValueKind == JsonValueKind.Undefined && platform == "windows-x64")
                asset = assets.FirstOrDefault(item => item.GetProperty("name").GetString() == "PLONDS.json");
            if (asset.ValueKind != JsonValueKind.Undefined)
                urls.Add(asset.GetProperty("browser_download_url").GetString()!);
        }
        return urls;
    }

    internal static string? ObjectUrl(string manifestUrl, string? bucket, string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(bucket) ||
            !Uri.TryCreate(manifestUrl, UriKind.Absolute, out var uri)) return null;
        var bucketPrefix = "/" + bucket + "/";
        return uri.AbsolutePath.StartsWith(bucketPrefix, StringComparison.Ordinal)
            ? uri.GetLeftPart(UriPartial.Authority) + bucketPrefix + key.TrimStart('/') : null;
    }
}
