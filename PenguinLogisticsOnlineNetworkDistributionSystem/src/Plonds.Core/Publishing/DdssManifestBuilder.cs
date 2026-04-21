using Plonds.Core.Security;
using Plonds.Shared.Models;

namespace Plonds.Core.Publishing;

public sealed class DdssManifestBuilder
{
    private readonly RsaFileSigner _signer = new();

    public string Build(DdssBuildOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var assetsDirectory = Path.GetFullPath(options.AssetsDirectory);
        if (!Directory.Exists(assetsDirectory))
        {
            throw new DirectoryNotFoundException($"DDSS assets directory not found: {assetsDirectory}");
        }

        var assetEntries = Directory
            .EnumerateFiles(assetsDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(static path =>
            {
                var name = Path.GetFileName(path);
                return !name.Equals("ddss.json", StringComparison.OrdinalIgnoreCase)
                       && !name.Equals("ddss.json.sig", StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(static path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .Select(path => BuildAssetEntry(path, options.Repository, options.ReleaseTag, options.S3BaseUrl))
            .ToArray();

        var manifest = new DdssManifest(
            FormatVersion: "1.0",
            ReleaseTag: options.ReleaseTag,
            GeneratedAt: DateTimeOffset.UtcNow,
            Assets: assetEntries);

        var outputRoot = Path.GetFullPath(options.OutputRoot);
        Directory.CreateDirectory(outputRoot);
        var manifestPath = Path.Combine(outputRoot, "ddss.json");
        PayloadUtilities.WriteJson(manifestPath, manifest);
        _signer.SignFile(manifestPath, options.PrivateKeyPath, manifestPath + ".sig");
        return manifestPath;
    }

    private static DdssAssetEntry BuildAssetEntry(string assetPath, string repository, string releaseTag, string? s3BaseUrl)
    {
        var fileName = Path.GetFileName(assetPath);
        var mirrors = new List<DdssMirrorEntry>
        {
            new("github", $"https://github.com/{repository}/releases/download/{releaseTag}/{Uri.EscapeDataString(fileName)}")
        };

        if (!string.IsNullOrWhiteSpace(s3BaseUrl))
        {
            mirrors.Add(new DdssMirrorEntry(
                "s3",
                $"{s3BaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(fileName)}"));
        }

        return new DdssAssetEntry(
            AssetId: fileName,
            FileName: fileName,
            Sha256: PayloadUtilities.ComputeSha256(assetPath),
            Size: new FileInfo(assetPath).Length,
            Mirrors: mirrors);
    }
}
