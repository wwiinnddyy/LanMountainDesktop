using System.Text.Json;
using Plonds.Core.Security;
using Plonds.Shared.Models;

namespace Plonds.Core.Publishing;

public sealed class PlondsReleaseIndexBuilder
{
    private readonly RsaFileSigner _signer = new();

    public string Build(PlondsReleaseIndexOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var summariesDirectory = Path.GetFullPath(options.PlatformSummariesDirectory);
        if (!Directory.Exists(summariesDirectory))
        {
            throw new DirectoryNotFoundException($"Platform summary directory not found: {summariesDirectory}");
        }

        var summaries = Directory
            .EnumerateFiles(summariesDirectory, "platform-summary-*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .Select(ReadSummary)
            .OrderBy(static entry => entry.Platform, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var manifest = new PlondsReleaseManifest(
            FormatVersion: "1.0",
            ReleaseTag: options.ReleaseTag,
            Version: options.Version,
            Channel: options.Channel,
            GeneratedAt: DateTimeOffset.UtcNow,
            Platforms: summaries);

        var outputRoot = Path.GetFullPath(options.OutputRoot);
        var releaseAssetsRoot = Path.Combine(outputRoot, "release-assets");
        Directory.CreateDirectory(releaseAssetsRoot);

        var manifestPath = Path.Combine(releaseAssetsRoot, "plonds.json");
        PayloadUtilities.WriteJson(manifestPath, manifest);
        _signer.SignFile(manifestPath, options.PrivateKeyPath, manifestPath + ".sig");
        return manifestPath;
    }

    private static PlondsReleasePlatformEntry ReadSummary(string path)
    {
        var json = File.ReadAllText(path);
        var summary = JsonSerializer.Deserialize<PlondsReleasePlatformEntry>(json, PayloadUtilities.JsonOptions);
        if (summary is null)
        {
            throw new InvalidOperationException($"Unable to deserialize PLONDS platform summary: {path}");
        }

        return summary;
    }
}
