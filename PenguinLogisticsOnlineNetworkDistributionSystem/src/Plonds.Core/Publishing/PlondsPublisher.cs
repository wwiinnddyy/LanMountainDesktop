using System.Text;
using System.Text.Json;
using Plonds.Core.Security;
using Plonds.Shared;
using Plonds.Shared.Models;

namespace Plonds.Core.Publishing;

public sealed class PlondsPublisher
{
    private static readonly PlatformConfig[] SupportedPlatforms =
    [
        new("windows-x64", "app-payload-windows-x64", [".exe"], ["x64"]),
        new("windows-x86", "app-payload-windows-x86", [".exe"], ["x86"]),
        new("linux-x64", "app-payload-linux-x64", [".deb"], ["linux", "x64"])
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly PlondsGenerator _generator = new();
    private readonly RsaFileSigner _signer = new();

    public IReadOnlyList<PlatformPublishResult> Publish(PlondsPublishOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var results = new List<PlatformPublishResult>();
        var releaseAssetsRoot = Path.Combine(Path.GetFullPath(options.OutputRoot), "release-assets");
        Directory.CreateDirectory(releaseAssetsRoot);

        foreach (var config in SupportedPlatforms)
        {
            var artifactRoot = Path.Combine(Path.GetFullPath(options.AppArtifactsRoot), config.ArtifactName);
            if (!Directory.Exists(artifactRoot))
            {
                throw new DirectoryNotFoundException($"App payload artifact root not found for {config.Platform}: {artifactRoot}");
            }

            var currentAppDirectory = FindCurrentAppDirectory(artifactRoot, options.Version);
            if (currentAppDirectory is null)
            {
                throw new DirectoryNotFoundException($"Unable to locate app payload directory for {config.Platform} under {artifactRoot}");
            }

            var baselineRoot = string.IsNullOrWhiteSpace(options.BaselineRoot)
                ? Path.Combine(Path.GetFullPath(options.OutputRoot), "_baselines")
                : Path.GetFullPath(options.BaselineRoot);
            var platformBaselineRoot = Path.Combine(baselineRoot, config.Platform);
            var previousDirectory = Path.Combine(platformBaselineRoot, "current");
            var previousVersionPath = Path.Combine(platformBaselineRoot, "version.txt");
            Directory.CreateDirectory(platformBaselineRoot);
            if (!Directory.Exists(previousDirectory))
            {
                Directory.CreateDirectory(previousDirectory);
            }

            var previousVersion = File.Exists(previousVersionPath)
                ? File.ReadAllText(previousVersionPath).Trim()
                : "0.0.0";

            var installerSourceDirectory = PrepareInstallerMirrorInput(
                config,
                options.InstallerArtifactsRoot,
                Path.Combine(platformBaselineRoot, "installers"));

            var distributionId = $"plonds-{options.Version}-{config.Platform}";
            var repoBaseUrl = options.RepoBaseUrl;
            var fileMapUrl = repoBaseUrl is null
                ? null
                : $"{repoBaseUrl.TrimEnd('/').Replace("/repo/sha256", "/manifests")}/{distributionId}/plonds-filemap.json";
            var fileMapSignatureUrl = fileMapUrl is null ? null : fileMapUrl + ".sig";
            var installerBaseUrl = string.IsNullOrWhiteSpace(options.InstallerBaseUrl)
                ? null
                : $"{options.InstallerBaseUrl.TrimEnd('/')}/{config.Platform}/{options.Version}";

            var result = _generator.Generate(new PlondsGenerateOptions(
                CurrentVersion: options.Version,
                CurrentDirectory: currentAppDirectory,
                Platform: config.Platform,
                OutputRoot: options.OutputRoot,
                PreviousVersion: string.IsNullOrWhiteSpace(options.BaselineVersion) ? previousVersion : options.BaselineVersion,
                PreviousDirectory: previousDirectory,
                Channel: options.Channel,
                DistributionId: distributionId,
                RepoBaseUrl: repoBaseUrl,
                FileMapUrl: fileMapUrl,
                FileMapSignatureUrl: fileMapSignatureUrl,
                InstallerDirectory: installerSourceDirectory,
                InstallerBaseUrl: installerBaseUrl,
                IncrementalStrategy: options.IncrementalStrategy,
                BaselineVersion: string.IsNullOrWhiteSpace(options.BaselineVersion) ? previousVersion : options.BaselineVersion,
                BaselineRef: options.BaselineRef,
                SourceCommit: options.SourceCommit,
                IsFullPayloadRelease: options.IsFullPayloadRelease,
                CommitRangeStart: options.CommitRangeStart,
                CommitRangeEnd: options.CommitRangeEnd));

            _signer.SignFile(result.FileMapPath, options.PrivateKeyPath, result.SignaturePath);

            CopyReleaseAsset(result.FileMapPath, Path.Combine(releaseAssetsRoot, $"plonds-filemap-{config.Platform}.json"));
            CopyReleaseAsset(result.SignaturePath, Path.Combine(releaseAssetsRoot, $"plonds-filemap-{config.Platform}.json.sig"));
            CopyReleaseAsset(result.DistributionPath, Path.Combine(releaseAssetsRoot, $"plonds-distribution-{config.Platform}.json"));
            CopyReleaseAsset(result.LatestPath, Path.Combine(releaseAssetsRoot, $"plonds-latest-{config.Platform}.json"));

            MirrorBaseline(currentAppDirectory, previousDirectory, previousVersionPath, options.Version);
            results.Add(result);
        }

        WriteMetadataCatalog(options, results);
        return results;
    }

    private static void WriteMetadataCatalog(PlondsPublishOptions options, IReadOnlyList<PlatformPublishResult> results)
    {
        var outputRoot = Path.GetFullPath(options.OutputRoot);
        var metadataRoot = Path.Combine(outputRoot, "meta");
        Directory.CreateDirectory(metadataRoot);

        var generatedAt = DateTimeOffset.UtcNow;
        var latestPointers = results
            .Select(result => new PlondsChannelPointer(
                Channel: options.Channel,
                Platform: result.Platform,
                DistributionId: result.DistributionId,
                Version: options.Version,
                PublishedAt: generatedAt,
                DistributionPath: $"distributions/{result.DistributionId}.json",
                FileMapPath: $"../manifests/{result.DistributionId}/plonds-filemap.json"))
            .OrderBy(pointer => pointer.Channel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pointer => pointer.Platform, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var catalog = new PlondsMetadataCatalog(
            ProtocolName: PlondsConstants.ProtocolName,
            ProtocolVersion: PlondsConstants.ProtocolVersion,
            StorageRoot: outputRoot,
            MetaRoot: metadataRoot,
            Latest: latestPointers,
            Metadata: new Dictionary<string, string>
            {
                ["generatedBy"] = "Plonds.Tool",
                ["channel"] = options.Channel,
                ["generatedAt"] = generatedAt.ToString("O")
            });

        var metadataPath = Path.Combine(metadataRoot, "metadata.json");
        File.WriteAllText(metadataPath, JsonSerializer.Serialize(catalog, JsonOptions), new UTF8Encoding(false));
    }

    private static void MirrorBaseline(string currentAppDirectory, string previousDirectory, string previousVersionPath, string version)
    {
        if (Directory.Exists(previousDirectory))
        {
            Directory.Delete(previousDirectory, recursive: true);
        }

        CopyDirectory(currentAppDirectory, previousDirectory);
        File.WriteAllText(previousVersionPath, version);
    }

    private static string? FindCurrentAppDirectory(string artifactRoot, string version)
    {
        var preferred = Directory.EnumerateDirectories(artifactRoot, $"app-{version}", SearchOption.AllDirectories).FirstOrDefault();
        if (preferred is not null)
        {
            return preferred;
        }

        return Directory.EnumerateDirectories(artifactRoot, "app-*", SearchOption.AllDirectories)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static string PrepareInstallerMirrorInput(PlatformConfig config, string installerArtifactsRoot, string destinationRoot)
    {
        var installerFiles = FindInstallerFiles(config, installerArtifactsRoot);
        if (Directory.Exists(destinationRoot))
        {
            Directory.Delete(destinationRoot, recursive: true);
        }
        Directory.CreateDirectory(destinationRoot);

        foreach (var file in installerFiles)
        {
            File.Copy(file, Path.Combine(destinationRoot, Path.GetFileName(file)), overwrite: true);
        }

        return destinationRoot;
    }

    private static List<string> FindInstallerFiles(PlatformConfig config, string installerArtifactsRoot)
    {
        var files = Directory.EnumerateFiles(Path.GetFullPath(installerArtifactsRoot), "*", SearchOption.AllDirectories);
        return files
            .Where(file => config.InstallerExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .Where(file =>
            {
                var fileName = Path.GetFileName(file);
                return config.FileNameTokens.All(token => fileName.Contains(token, StringComparison.OrdinalIgnoreCase));
            })
            .ToList();
    }

    private static void CopyReleaseAsset(string sourcePath, string destinationPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.Copy(sourcePath, destinationPath, overwrite: true);
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (var directory in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDir, directory);
            Directory.CreateDirectory(Path.Combine(destinationDir, relativePath));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDir, file);
            var destinationPath = Path.Combine(destinationDir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(file, destinationPath, overwrite: true);
        }
    }

    private sealed record PlatformConfig(
        string Platform,
        string ArtifactName,
        IReadOnlyList<string> InstallerExtensions,
        IReadOnlyList<string> FileNameTokens);
}
