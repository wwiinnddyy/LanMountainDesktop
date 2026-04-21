using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Plonds.Core.Publishing;

public static class PayloadUtilities
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static void CreatePayloadZip(string sourceDirectory, string outputZipPath)
    {
        var resolvedSourceDirectory = Path.GetFullPath(sourceDirectory);
        if (!Directory.Exists(resolvedSourceDirectory))
        {
            throw new DirectoryNotFoundException($"Payload source directory not found: {resolvedSourceDirectory}");
        }

        var resolvedOutputZipPath = Path.GetFullPath(outputZipPath);
        var outputDirectory = Path.GetDirectoryName(resolvedOutputZipPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        if (File.Exists(resolvedOutputZipPath))
        {
            File.Delete(resolvedOutputZipPath);
        }

        using var archive = ZipFile.Open(resolvedOutputZipPath, ZipArchiveMode.Create);
        foreach (var filePath in Directory.EnumerateFiles(resolvedSourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = NormalizeRelativePath(Path.GetRelativePath(resolvedSourceDirectory, filePath));
            if (ShouldIgnore(relativePath))
            {
                continue;
            }

            archive.CreateEntryFromFile(filePath, relativePath, CompressionLevel.Optimal);
        }
    }

    internal static void ExtractZip(string zipPath, string destinationDirectory)
    {
        var resolvedZipPath = Path.GetFullPath(zipPath);
        if (!File.Exists(resolvedZipPath))
        {
            throw new FileNotFoundException("Payload archive not found.", resolvedZipPath);
        }

        EnsureCleanDirectory(destinationDirectory);
        ZipFile.ExtractToDirectory(resolvedZipPath, destinationDirectory, overwriteFiles: true);
    }

    internal static Dictionary<string, FileFingerprint> ScanDirectory(string? root)
    {
        var manifest = new Dictionary<string, FileFingerprint>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return manifest;
        }

        var resolvedRoot = Path.GetFullPath(root);
        foreach (var filePath in Directory.EnumerateFiles(resolvedRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = NormalizeRelativePath(Path.GetRelativePath(resolvedRoot, filePath));
            if (ShouldIgnore(relativePath))
            {
                continue;
            }

            var fileInfo = new FileInfo(filePath);
            manifest[relativePath] = new FileFingerprint(
                relativePath,
                filePath,
                ComputeSha256(filePath),
                fileInfo.Length,
                ResolveUnixFileMode(filePath));
        }

        return manifest;
    }

    internal static string CopyObject(string sourcePath, string objectsRoot, string sha256)
    {
        var normalizedSha256 = sha256.Trim().ToLowerInvariant();
        var prefix = normalizedSha256[..Math.Min(2, normalizedSha256.Length)];
        var relativePath = NormalizeRelativePath(Path.Combine(prefix, normalizedSha256));
        var destinationPath = Path.Combine(objectsRoot, prefix, normalizedSha256);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        if (!File.Exists(destinationPath))
        {
            File.Copy(sourcePath, destinationPath, overwrite: true);
        }

        return relativePath;
    }

    internal static void EnsureCleanDirectory(string path)
    {
        var resolvedPath = Path.GetFullPath(path);
        if (Directory.Exists(resolvedPath))
        {
            Directory.Delete(resolvedPath, recursive: true);
        }

        Directory.CreateDirectory(resolvedPath);
    }

    internal static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    internal static void WriteJson<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(value, JsonOptions);
        File.WriteAllText(path, json, new UTF8Encoding(false));
    }

    internal static string NormalizeRelativePath(string value)
    {
        return value.Replace('\\', '/').TrimStart('/');
    }

    internal static string ResolveArch(string platform)
    {
        if (platform.EndsWith("-x86", StringComparison.OrdinalIgnoreCase))
        {
            return "x86";
        }

        if (platform.EndsWith("-arm64", StringComparison.OrdinalIgnoreCase))
        {
            return "arm64";
        }

        return "x64";
    }

    internal static bool ShouldIgnore(string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath.Trim());
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return true;
        }

        return normalized.Equals(".current", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals(".partial", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals(".destroy", StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith(".current/", StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith(".partial/", StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith(".destroy/", StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith("logs/", StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith("cache/", StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith("snapshots/", StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith("snapshot/", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveUnixFileMode(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            var mode = File.GetUnixFileMode(path);
            return Convert.ToString((int)mode, 8);
        }
        catch
        {
            return InferUnixFileMode(path);
        }
    }

    private static string? InferUnixFileMode(string path)
    {
        if (!LooksExecutable(path))
        {
            return null;
        }

        return "755";
    }

    private static bool LooksExecutable(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[4];
            var read = stream.Read(header);
            if (read >= 4 &&
                header[0] == 0x7F &&
                header[1] == (byte)'E' &&
                header[2] == (byte)'L' &&
                header[3] == (byte)'F')
            {
                return true;
            }

            if (read >= 2 && header[0] == (byte)'#' && header[1] == (byte)'!')
            {
                return true;
            }
        }
        catch
        {
            return false;
        }

        var extension = Path.GetExtension(path);
        return string.IsNullOrWhiteSpace(extension) &&
               !OperatingSystem.IsWindows() &&
               Path.GetFileName(path).Contains("LanMountainDesktop", StringComparison.OrdinalIgnoreCase);
    }

    internal sealed record FileFingerprint(string RelativePath, string FullPath, string Sha256, long Size, string? UnixFileMode);
}
