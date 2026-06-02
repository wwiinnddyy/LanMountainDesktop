using System.Security.Cryptography;
using System.Text.Json;
using Plonds.Shared;
using Plonds.Shared.Models;

namespace Plonds.Core.Publishing;

public sealed class PlondsCommitDeltaBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly Dictionary<string, string[]> SourceToArtifactMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LanMountainDesktop"] = ["LanMountainDesktop.dll", "LanMountainDesktop.exe"],
        ["LanMountainDesktop.Launcher"] = ["LanMountainDesktop.Launcher.exe"],
        ["LanMountainDesktop.Shared.Contracts"] = ["LanMountainDesktop.Shared.Contracts.dll"],
        ["LanMountainDesktop.PluginSdk"] = ["LanMountainDesktop.PluginSdk.dll"],
        ["LanMountainDesktop.Appearance"] = ["LanMountainDesktop.Appearance.dll"],
        ["LanMountainDesktop.Settings.Core"] = ["LanMountainDesktop.Settings.Core.dll"],
        ["LanMountainDesktop.ComponentSystem"] = ["LanMountainDesktop.ComponentSystem.dll"]
    };

    private static readonly string[] FallbackAllArtifacts =
    [
        "LanMountainDesktop.dll",
        "LanMountainDesktop.exe",
        "LanMountainDesktop.Launcher.exe",
        "LanMountainDesktop.Shared.Contracts.dll",
        "LanMountainDesktop.PluginSdk.dll",
        "LanMountainDesktop.Appearance.dll",
        "LanMountainDesktop.Settings.Core.dll",
        "LanMountainDesktop.ComponentSystem.dll"
    ];

    public PlondsDeltaBuildResult Build(PlondsCommitDeltaBuildOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var hashAlgorithm = ValidateHashAlgorithm(options.HashAlgorithm);
        var sourceDirs = ParseSourceDirs(options.SourceDirs);

        var currentPayloadZip = Path.GetFullPath(options.CurrentPayloadZip);
        if (!File.Exists(currentPayloadZip))
        {
            throw new FileNotFoundException("Current payload zip not found.", currentPayloadZip);
        }

        var outputRoot = Path.GetFullPath(options.OutputRoot);
        var workRoot = Path.Combine(outputRoot, "work", options.Platform);
        var currentExtractRoot = Path.Combine(workRoot, "current");

        Directory.CreateDirectory(outputRoot);
        PayloadUtilities.ExtractZip(currentPayloadZip, currentExtractRoot);
        var currentAppRoot = PlondsDeltaBuilder.ResolvePayloadAppRoot(currentExtractRoot, options.CurrentVersion);

        var changedSourceFiles = GetChangedSourceFiles(options.BaselineTag, options.CurrentTag, sourceDirs);

        if (changedSourceFiles.Count == 0)
        {
            Console.WriteLine("No source code changes detected between tags. Falling back to file-compare method.");
            return FallbackToFileCompare(options, currentExtractRoot, outputRoot, hashAlgorithm);
        }

        Console.WriteLine($"Detected {changedSourceFiles.Count} changed source file(s) between {options.BaselineTag} and {options.CurrentTag}.");
        foreach (var file in changedSourceFiles.Take(20))
        {
            Console.WriteLine($"  {file}");
        }

        if (changedSourceFiles.Count > 20)
        {
            Console.WriteLine($"  ... and {changedSourceFiles.Count - 20} more");
        }

        var artifactFiles = MapSourceToArtifacts(changedSourceFiles, sourceDirs);
        var currentManifest = PayloadUtilities.ScanDirectory(currentAppRoot);

        var filesMap = new Dictionary<string, PlondsFileEntry>(StringComparer.OrdinalIgnoreCase);
        var changedFilesMap = new Dictionary<string, PlondsChangedFileEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var artifactFile in artifactFiles)
        {
            var normalizedPath = artifactFile.Replace('\\', '/');
            if (!currentManifest.TryGetValue(normalizedPath, out var fingerprint))
            {
                Console.WriteLine($"  Artifact not found in current zip: {normalizedPath}, skipping.");
                continue;
            }

            var fileHash = PlondsDeltaBuilder.ComputeHash(fingerprint.FullPath, hashAlgorithm);
            var action = PlondsConstants.ActionReplace;

            filesMap[normalizedPath] = new PlondsFileEntry(action, fileHash, fingerprint.Size, hashAlgorithm);
            changedFilesMap[normalizedPath] = new PlondsChangedFileEntry(normalizedPath, fileHash, fingerprint.Size, hashAlgorithm);
        }

        var changedZipPath = CreateChangedZipFromList(currentAppRoot, artifactFiles, outputRoot, options.Platform);
        var changedZipMd5 = ComputeMd5Hex(changedZipPath);

        var launcherInChanges = artifactFiles.Any(f =>
            string.Equals(Path.GetFileName(f), "LanMountainDesktop.Launcher.exe", StringComparison.OrdinalIgnoreCase));

        var manifest = new PlondsManifest(
            FormatVersion: PlondsConstants.FormatVersion,
            CurrentVersion: options.CurrentVersion,
            PreviousVersion: options.BaselineTag.TrimStart('v'),
            IsFullUpdate: false,
            RequiresCleanInstall: launcherInChanges,
            Channel: options.Channel,
            Platform: options.Platform,
            UpdatedAt: DateTimeOffset.UtcNow,
            FilesMap: filesMap,
            ChangedFilesMap: changedFilesMap,
            Checksums: new Dictionary<string, string>
            {
                ["changed.zip"] = $"md5:{changedZipMd5}"
            });

        var manifestPath = Path.Combine(outputRoot, "PLONDS.json");
        var manifestJson = JsonSerializer.Serialize(manifest, JsonOptions);
        File.WriteAllText(manifestPath, manifestJson);

        return new PlondsDeltaBuildResult(
            Platform: options.Platform,
            ChangedZipPath: changedZipPath,
            ManifestPath: manifestPath,
            IsFullUpdate: false,
            RequiresCleanInstall: launcherInChanges,
            CurrentVersion: options.CurrentVersion,
            BaselineVersion: options.BaselineTag.TrimStart('v'));
    }

    private static List<string> GetChangedSourceFiles(string baselineTag, string currentTag, string[] sourceDirs)
    {
        var changedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalizedBaseline = baselineTag.StartsWith("v") ? baselineTag : $"v{baselineTag}";
        var normalizedCurrent = currentTag.StartsWith("v") ? currentTag : $"v{currentTag}";

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = $"diff --name-only {normalizedBaseline}..{normalizedCurrent}",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git process.");

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git diff failed with exit code {process.ExitCode}.");
        }

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            var isSourceFile = sourceDirs.Any(dir =>
                trimmed.StartsWith(dir + "/", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith(dir + "\\", StringComparison.OrdinalIgnoreCase));

            if (isSourceFile)
            {
                changedFiles.Add(trimmed);
            }
        }

        return changedFiles.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static HashSet<string> MapSourceToArtifacts(IReadOnlyList<string> changedSourceFiles, string[] sourceDirs)
    {
        var artifacts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hasUnmappedChanges = false;

        foreach (var sourceFile in changedSourceFiles)
        {
            var mapped = false;

            foreach (var dir in sourceDirs)
            {
                if (!sourceFile.StartsWith(dir + "/", StringComparison.OrdinalIgnoreCase) &&
                    !sourceFile.StartsWith(dir + "\\", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (SourceToArtifactMap.TryGetValue(dir, out var artifactList))
                {
                    foreach (var artifact in artifactList)
                    {
                        artifacts.Add(artifact);
                    }

                    mapped = true;
                    break;
                }
            }

            if (!mapped)
            {
                var extension = Path.GetExtension(sourceFile).ToLowerInvariant();
                if (extension is ".json" or ".xml" or ".config")
                {
                    var fileName = Path.GetFileName(sourceFile);
                    artifacts.Add(fileName);
                    mapped = true;
                }
            }

            if (!mapped)
            {
                hasUnmappedChanges = true;
            }
        }

        if (hasUnmappedChanges)
        {
            Console.WriteLine("Unmapped source changes detected. Including all core artifacts as a conservative fallback.");
            foreach (var artifact in FallbackAllArtifacts)
            {
                artifacts.Add(artifact);
            }
        }

        return artifacts;
    }

    private static PlondsDeltaBuildResult FallbackToFileCompare(
        PlondsCommitDeltaBuildOptions options,
        string currentExtractRoot,
        string outputRoot,
        string hashAlgorithm)
    {
        var fallbackZip = string.IsNullOrWhiteSpace(options.FallbackBaselineZip)
            ? null
            : Path.GetFullPath(options.FallbackBaselineZip);

        if (string.IsNullOrWhiteSpace(fallbackZip) || !File.Exists(fallbackZip))
        {
            Console.WriteLine("No fallback baseline zip available. Generating full update.");
            var fullBuilder = new PlondsDeltaBuilder();
            return fullBuilder.Build(new PlondsDeltaBuildOptions(
                Platform: options.Platform,
                CurrentVersion: options.CurrentVersion,
                CurrentPayloadZip: options.CurrentPayloadZip,
                OutputRoot: outputRoot,
                Channel: options.Channel,
                HashAlgorithm: hashAlgorithm,
                LauncherRelativePath: options.LauncherRelativePath));
        }

        Console.WriteLine($"Falling back to file-compare using baseline: {fallbackZip}");
        var deltaBuilder = new PlondsDeltaBuilder();
        return deltaBuilder.Build(new PlondsDeltaBuildOptions(
            Platform: options.Platform,
            CurrentVersion: options.CurrentVersion,
            CurrentPayloadZip: options.CurrentPayloadZip,
            OutputRoot: outputRoot,
            Channel: options.Channel,
            BaselineVersion: options.BaselineTag.TrimStart('v'),
            BaselinePayloadZip: fallbackZip,
            HashAlgorithm: hashAlgorithm,
            LauncherRelativePath: options.LauncherRelativePath));
    }

    private static string CreateChangedZipFromList(
        string currentExtractRoot,
        IEnumerable<string> artifactFiles,
        string outputRoot,
        string platform)
    {
        var changedZipPath = Path.Combine(outputRoot, "changed.zip");
        var stagingRoot = Path.Combine(outputRoot, "work", platform, "staging");
        PayloadUtilities.EnsureCleanDirectory(stagingRoot);

        foreach (var artifactFile in artifactFiles)
        {
            var normalizedPath = artifactFile.Replace('\\', '/');
            var sourcePath = Path.Combine(currentExtractRoot, normalizedPath);
            if (!File.Exists(sourcePath))
            {
                continue;
            }

            var destPath = Path.Combine(stagingRoot, normalizedPath);
            var destDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrWhiteSpace(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            File.Copy(sourcePath, destPath, overwrite: true);
        }

        PayloadUtilities.CreatePayloadZip(stagingRoot, changedZipPath);
        return changedZipPath;
    }

    private static string ValidateHashAlgorithm(string algorithm)
    {
        var normalized = algorithm.Trim().ToLowerInvariant();
        if (normalized is not (PlondsConstants.HashAlgorithmSha256 or PlondsConstants.HashAlgorithmMd5))
        {
            throw new ArgumentException($"Unsupported hash algorithm: {algorithm}. Supported: sha256, md5");
        }

        return normalized;
    }

    private static string[] ParseSourceDirs(string? sourceDirs)
    {
        if (string.IsNullOrWhiteSpace(sourceDirs))
        {
            return PlondsConstants.DefaultSourceDirs;
        }

        return sourceDirs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string ComputeMd5Hex(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(MD5.HashData(stream)).ToLowerInvariant();
    }
}
