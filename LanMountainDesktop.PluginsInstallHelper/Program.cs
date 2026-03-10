using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LanMountainDesktop.PluginSdk;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var result = new HelperResult();
        string? resultPath = null;

        try
        {
            var parsedArgs = ParseArgs(args);
            if (!parsedArgs.TryGetValue("source", out var sourcePath) ||
                !parsedArgs.TryGetValue("plugins-dir", out var pluginsDirectory) ||
                !parsedArgs.TryGetValue("result", out resultPath) ||
                string.IsNullOrWhiteSpace(sourcePath) ||
                string.IsNullOrWhiteSpace(pluginsDirectory) ||
                string.IsNullOrWhiteSpace(resultPath))
            {
                throw new InvalidOperationException("Required arguments: --source <path> --plugins-dir <path> --result <path>.");
            }

            var fullSourcePath = Path.GetFullPath(sourcePath);
            var fullPluginsDirectory = Path.GetFullPath(pluginsDirectory);
            resultPath = Path.GetFullPath(resultPath);

            if (!File.Exists(fullSourcePath))
            {
                throw new FileNotFoundException($"Plugin package '{fullSourcePath}' was not found.", fullSourcePath);
            }

            var manifest = ReadManifestFromPackage(fullSourcePath);
            Directory.CreateDirectory(fullPluginsDirectory);
            RemoveExistingPluginPackages(fullPluginsDirectory, manifest.Id);

            var destinationPath = Path.Combine(fullPluginsDirectory, BuildInstalledPackageFileName(manifest.Id));
            File.Copy(fullSourcePath, destinationPath, overwrite: true);

            result = new HelperResult
            {
                Success = true,
                InstalledPackagePath = destinationPath,
                ManifestId = manifest.Id,
                ManifestName = manifest.Name
            };
        }
        catch (Exception ex)
        {
            result = new HelperResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }

        if (!string.IsNullOrWhiteSpace(resultPath))
        {
            var resultDirectory = Path.GetDirectoryName(resultPath);
            if (!string.IsNullOrWhiteSpace(resultDirectory))
            {
                Directory.CreateDirectory(resultDirectory);
            }

            await File.WriteAllTextAsync(
                resultPath,
                JsonSerializer.Serialize(result, new JsonSerializerOptions
                {
                    WriteIndented = true
                }),
                Encoding.UTF8);
        }

        return result.Success ? 0 : 1;
    }

    private static Dictionary<string, string> ParseArgs(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            var current = args[i];
            if (!current.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var key = current[2..];
            if (string.IsNullOrWhiteSpace(key) || i + 1 >= args.Length)
            {
                continue;
            }

            values[key] = args[++i];
        }

        return values;
    }

    private static PluginManifest ReadManifestFromPackage(string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var entries = archive.Entries
            .Where(entry => string.Equals(entry.Name, PluginSdkInfo.ManifestFileName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (entries.Length == 0)
        {
            throw new InvalidOperationException(
                $"Plugin package '{packagePath}' does not contain '{PluginSdkInfo.ManifestFileName}'.");
        }

        if (entries.Length > 1)
        {
            throw new InvalidOperationException(
                $"Plugin package '{packagePath}' contains multiple '{PluginSdkInfo.ManifestFileName}' files.");
        }

        using var stream = entries[0].Open();
        return PluginManifest.Load(stream, $"{packagePath}!/{entries[0].FullName}");
    }

    private static void RemoveExistingPluginPackages(string pluginsDirectory, string pluginId)
    {
        var runtimeRootDirectory = EnsureTrailingSeparator(Path.Combine(Path.GetFullPath(pluginsDirectory), PluginSdkInfo.RuntimeDirectoryName));
        foreach (var existingPackagePath in Directory
                     .EnumerateFiles(pluginsDirectory, "*" + PluginSdkInfo.PackageFileExtension, SearchOption.AllDirectories)
                     .Select(Path.GetFullPath)
                     .Where(path => !path.StartsWith(runtimeRootDirectory, StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var existingManifest = ReadManifestFromPackage(existingPackagePath);
                if (!string.Equals(existingManifest.Id, pluginId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                File.Delete(existingPackagePath);
            }
            catch
            {
                // Ignore unrelated or malformed packages while replacing an install target.
            }
        }
    }

    private static string BuildInstalledPackageFileName(string pluginId)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var fileName = new string(pluginId.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
        return fileName + PluginSdkInfo.PackageFileExtension;
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private sealed class HelperResult
    {
        public bool Success { get; init; }

        public string? InstalledPackagePath { get; init; }

        public string? ManifestId { get; init; }

        public string? ManifestName { get; init; }

        public string? ErrorMessage { get; init; }
    }
}
