using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LanMountainDesktop.Services.Plonds;

internal sealed class PlondsPackageStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _rootDirectory;

    public PlondsPackageStore(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("PLONDS package store root is required.", nameof(rootDirectory));
        }

        _rootDirectory = Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(_rootDirectory);
    }

    public async Task<PlondsPackageStaging> CreateStagingAsync(
        PlondsClientManifest manifest,
        PlondsSourceDescriptor source,
        PlondsPackageMode mode,
        CancellationToken cancellationToken)
    {
        if (!PlondsManifestSelector.TryParseVersion(manifest.CurrentVersion, out var version))
        {
            throw new InvalidDataException($"Invalid PLONDS version: {manifest.CurrentVersion}");
        }

        var modeDirectoryName = mode is PlondsPackageMode.Delta ? "delta" : "full";
        var stagingRoot = Path.Combine(
            _rootDirectory,
            SanitizePathSegment(version.ToString()),
            SanitizePathSegment(source.Id),
            modeDirectoryName);

        EnsureCleanDirectory(stagingRoot);

        var manifestPath = Path.Combine(stagingRoot, "PLONDS.json");
        await using (var manifestStream = File.Create(manifestPath))
        {
            await JsonSerializer.SerializeAsync(manifestStream, manifest, JsonOptions, cancellationToken).ConfigureAwait(false);
        }

        var zipPath = Path.Combine(stagingRoot, mode is PlondsPackageMode.Delta ? "changed.zip" : "Files.zip");
        var extractDirectory = Path.Combine(stagingRoot, mode is PlondsPackageMode.Delta ? "changed" : "Files");
        Directory.CreateDirectory(extractDirectory);

        return new PlondsPackageStaging(version, mode, stagingRoot, manifestPath, zipPath, extractDirectory);
    }

    public void ExtractPackage(string zipPath, string destinationDirectory)
    {
        var resolvedDestination = Path.GetFullPath(destinationDirectory);
        EnsureStorePath(resolvedDestination);
        EnsureCleanDirectory(resolvedDestination);

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            var destinationPath = Path.GetFullPath(Path.Combine(resolvedDestination, entry.FullName));
            EnsureChildPath(resolvedDestination, destinationPath);

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            entry.ExtractToFile(destinationPath, overwrite: true);
        }
    }

    private void EnsureCleanDirectory(string path)
    {
        var resolvedPath = Path.GetFullPath(path);
        EnsureStorePath(resolvedPath);
        if (Directory.Exists(resolvedPath))
        {
            Directory.Delete(resolvedPath, recursive: true);
        }

        Directory.CreateDirectory(resolvedPath);
    }

    private void EnsureStorePath(string path)
    {
        if (!IsSameOrChildPath(_rootDirectory, path))
        {
            throw new InvalidOperationException($"PLONDS staging path is outside the package store: {path}");
        }
    }

    private static void EnsureChildPath(string parent, string child)
    {
        if (!IsSameOrChildPath(parent, child))
        {
            throw new InvalidDataException($"PLONDS package entry escapes the staging directory: {child}");
        }
    }

    private static bool IsSameOrChildPath(string parent, string child)
    {
        var resolvedParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var resolvedChild = Path.GetFullPath(child);
        return string.Equals(resolvedParent, resolvedChild.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
               || resolvedChild.StartsWith(resolvedParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || resolvedChild.StartsWith(resolvedParent + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        var sanitized = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }
}

internal sealed record PlondsPackageStaging(
    Version Version,
    PlondsPackageMode Mode,
    string RootDirectory,
    string ManifestPath,
    string PackageZipPath,
    string ExtractDirectory)
{
    public PlondsPreparedPackage ToPreparedPackage()
    {
        return new PlondsPreparedPackage(
            Version,
            Mode,
            ManifestPath,
            Mode is PlondsPackageMode.Delta ? PackageZipPath : null,
            Mode is PlondsPackageMode.Delta ? ExtractDirectory : null,
            Mode is PlondsPackageMode.Full ? PackageZipPath : null,
            Mode is PlondsPackageMode.Full ? ExtractDirectory : null);
    }
}
