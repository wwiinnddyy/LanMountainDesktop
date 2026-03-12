using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Threading;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Views.SettingsPages;

namespace LanMountainDesktop.Plugins;

internal sealed class PluginSharedContractManager : IDisposable
{
    private readonly string _contractsDirectory;
    private readonly AirAppMarketIndexService _indexService;
    private readonly HttpClient _httpClient;
    private readonly object _gate = new();
    private readonly Dictionary<string, LoadedSharedContract> _loadedContracts =
        new(StringComparer.OrdinalIgnoreCase);

    public PluginSharedContractManager(string cacheDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);

        _contractsDirectory = Path.Combine(
            GetSharedContractRootDirectory(),
            "SharedContracts");
        _indexService = new AirAppMarketIndexService(new AirAppMarketCacheService(cacheDirectory));
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(2)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("LanMountainDesktop-SharedContracts/1.0");
    }

    public string ContractsDirectory => _contractsDirectory;

    public void EnsureInstalled(PluginManifest manifest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (manifest.SharedContracts is not { Count: > 0 })
        {
            return;
        }

        var document = LoadIndex(cancellationToken);
        foreach (var reference in manifest.SharedContracts)
        {
            EnsureInstalled(document, reference, cancellationToken);
        }
    }

    public IReadOnlyList<string> PrepareForLoad(PluginManifest manifest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (manifest.SharedContracts is not { Count: > 0 })
        {
            return Array.Empty<string>();
        }

        var assemblyNames = new List<string>(manifest.SharedContracts.Count);
        foreach (var reference in manifest.SharedContracts)
        {
            var assemblyPath = GetInstalledAssemblyPath(reference);
            if (!File.Exists(assemblyPath))
            {
                throw new InvalidOperationException(
                    $"Plugin '{manifest.Id}' requires shared contract '{reference.Id}' version '{reference.Version}', but '{assemblyPath}' is not installed. Install the dependency from the market first.");
            }

            var loaded = LoadSharedAssembly(reference, assemblyPath);
            assemblyNames.Add(loaded.AssemblyName);
        }

        return assemblyNames;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _indexService.Dispose();
    }

    private void EnsureInstalled(
        AirAppMarketIndexDocument document,
        PluginSharedContractReference reference,
        CancellationToken cancellationToken)
    {
        var entry = document.Contracts.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, reference.Id, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.Version, reference.Version, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            throw new InvalidOperationException(
                $"Shared contract '{reference.Id}' version '{reference.Version}' is not published in the configured market index.");
        }

        if (!string.Equals(entry.AssemblyName, reference.AssemblyName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Shared contract '{reference.Id}' version '{reference.Version}' expects assembly '{reference.AssemblyName}', but the market entry provides '{entry.AssemblyName}'.");
        }

        var destinationPath = GetInstalledAssemblyPath(reference);
        if (IsInstalledAndMatches(destinationPath, entry))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        var temporaryPath = destinationPath + ".download";
        try
        {
            if (AirAppMarketDefaults.TryResolveWorkspaceFile(entry.DownloadUrl, out var localSourcePath))
            {
                File.Copy(localSourcePath, temporaryPath, overwrite: true);
            }
            else
            {
                using var response = _httpClient.GetAsync(entry.DownloadUrl, cancellationToken)
                    .GetAwaiter()
                    .GetResult();
                response.EnsureSuccessStatusCode();
                using var responseStream = response.Content.ReadAsStreamAsync(cancellationToken)
                    .GetAwaiter()
                    .GetResult();
                using var fileStream = File.Create(temporaryPath);
                responseStream.CopyTo(fileStream);
            }

            ValidateInstalledFile(temporaryPath, entry);
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    private AirAppMarketIndexDocument LoadIndex(CancellationToken cancellationToken)
    {
        var result = _indexService.LoadAsync(cancellationToken).GetAwaiter().GetResult();
        if (!result.Success || result.Document is null)
        {
            throw new InvalidOperationException(
                $"Failed to load market index for shared contract resolution: {result.ErrorMessage ?? "Unknown error"}");
        }

        return result.Document;
    }

    private LoadedSharedContract LoadSharedAssembly(
        PluginSharedContractReference reference,
        string assemblyPath)
    {
        var assemblyName = AssemblyLoadContext.GetAssemblyName(assemblyPath).Name
            ?? throw new InvalidOperationException($"Failed to determine assembly name of '{assemblyPath}'.");

        lock (_gate)
        {
            if (_loadedContracts.TryGetValue(assemblyName, out var existing))
            {
                if (!string.Equals(existing.ContractId, reference.Id, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(existing.ContractVersion, reference.Version, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Shared contract assembly '{assemblyName}' is already loaded as '{existing.ContractId}' version '{existing.ContractVersion}', so plugin dependency '{reference.Id}' version '{reference.Version}' cannot be activated in the same host process.");
                }

                return existing;
            }

            var assembly = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(candidate =>
                string.Equals(candidate.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase))
                ?? AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);

            var loaded = new LoadedSharedContract(reference.Id, reference.Version, assemblyName, assemblyPath, assembly);
            _loadedContracts[assemblyName] = loaded;
            return loaded;
        }
    }

    private static bool IsInstalledAndMatches(string assemblyPath, AirAppMarketSharedContractEntry entry)
    {
        if (!File.Exists(assemblyPath))
        {
            return false;
        }

        try
        {
            ValidateInstalledFile(assemblyPath, entry);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ValidateInstalledFile(string assemblyPath, AirAppMarketSharedContractEntry entry)
    {
        var actualSize = new FileInfo(assemblyPath).Length;
        if (actualSize != entry.PackageSizeBytes)
        {
            throw new InvalidOperationException(
                $"Shared contract '{entry.Id}' version '{entry.Version}' size mismatch. Expected {entry.PackageSizeBytes}, actual {actualSize}.");
        }

        using var stream = File.OpenRead(assemblyPath);
        var actualHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!string.Equals(actualHash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Shared contract '{entry.Id}' version '{entry.Version}' hash mismatch. Expected {entry.Sha256}, actual {actualHash}.");
        }
    }

    private string GetInstalledAssemblyPath(PluginSharedContractReference reference)
    {
        return Path.Combine(
            _contractsDirectory,
            Sanitize(reference.Id),
            Sanitize(reference.Version),
            reference.AssemblyName);
    }

    private static string GetSharedContractRootDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            return Path.Combine(AppContext.BaseDirectory, "Data");
        }

        return Path.Combine(localAppData, "LanMountainDesktop");
    }

    private static string Sanitize(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return new string(value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore cleanup failures.
        }
    }

    private sealed record LoadedSharedContract(
        string ContractId,
        string ContractVersion,
        string AssemblyName,
        string AssemblyPath,
        Assembly Assembly);
}
