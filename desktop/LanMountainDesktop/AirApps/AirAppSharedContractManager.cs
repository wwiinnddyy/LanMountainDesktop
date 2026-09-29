using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Threading;
using LanMountainDesktop.AirAppSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.AirAppMarket;
using LanMountainDesktop.Shared.IO;

namespace LanMountainDesktop.AirApps;

internal sealed class AirAppSharedContractManager : IDisposable
{
    private readonly string _contractsDirectory;
    private readonly AirAppMarketIndexService _indexService;
    private readonly HttpClient _httpClient;
    private readonly object _gate = new();
    private readonly Dictionary<string, LoadedSharedContract> _loadedContracts =
        new(StringComparer.OrdinalIgnoreCase);

    public AirAppSharedContractManager(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        // Shared contracts live alongside the rest of the AirApp market data so that a single
        // storage location (driven by AppDataPathProvider.GetDataRoot() / the OOBE-chosen path)
        // owns every AirApp asset: index cache, downloads, and shared contracts.
        _contractsDirectory = Path.Combine(dataDirectory, "SharedContracts");
        _indexService = new AirAppMarketIndexService(new AirAppMarketCacheService(dataDirectory));
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(2)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(HttpUserAgents.SharedContracts);
    }

    public string ContractsDirectory => _contractsDirectory;

    public void EnsureInstalled(AirAppManifest manifest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (manifest.SharedContracts is not { Count: > 0 })
        {
            return;
        }

        var document = LoadIndex(cancellationToken);
        AppLogger.Info(
            "AirAppSharedContracts",
            $"Shared contract index loaded for AirApp '{manifest.Id}'. SourceContracts={document.Contracts.Count}.");
        foreach (var reference in manifest.SharedContracts)
        {
            EnsureInstalled(document, reference, cancellationToken);
        }
    }

    public IReadOnlyList<string> PrepareForLoad(AirAppManifest manifest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (manifest.SharedContracts is not { Count: > 0 })
        {
            return Array.Empty<string>();
        }

        var assemblyNames = new List<string>(manifest.SharedContracts.Count);
        AirAppMarketIndexDocument? document = null;
        foreach (var reference in manifest.SharedContracts)
        {
            var assemblyPath = GetInstalledAssemblyPath(reference);
            if (!File.Exists(assemblyPath))
            {
                document ??= LoadIndex(cancellationToken);
                AppLogger.Info(
                    "AirAppSharedContracts",
                    $"Installing missing shared contract during AirApp load. AirAppId='{manifest.Id}'; ContractId='{reference.Id}'; Version='{reference.Version}'; Destination='{assemblyPath}'.");
                EnsureInstalled(document, reference, cancellationToken);
            }

            if (!File.Exists(assemblyPath))
            {
                throw new InvalidOperationException(
                    $"AirApp '{manifest.Id}' requires shared contract '{reference.Id}' version '{reference.Version}', but '{assemblyPath}' is not installed. Install the dependency from the market first.");
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

    /// <summary>
    /// 把没有任何现存包再引用的契约程序集从盘上摘掉，连带收掉空掉的两层目录。返回真删掉的文件数。
    /// </summary>
    /// <remarks>
    /// 这里原本<b>一条删除路径都没有</b>（全仓在 <c>SharedContracts</c> 上唯一的删除是同文件
    /// <c>:163</c> 清 <c>.download</c> 临时文件）：契约按 <c>{id}/{version}/{assembly}</c> 落盘、
    /// 跨应用共享，市场每滚一版就永久多留一份旧程序集，谁也不会回收它。
    /// 保留集合取"盘上现存包（含被禁用的）的清单引用"——<b>禁用不是卸载</b>，
    /// 按启用状态来删会让用户重新启用一个应用时面对一个下载不回来的契约。
    /// 内存里已加载的那条也一律保留：<see cref="AssemblyLoadContext"/> 已经把它钉在本进程里，
    /// 删了既不会真的释放磁盘（Windows 上文件被占用），又可能让后续加载走到半路。
    /// </remarks>
    /// <param name="installedManifests">现存包的清单（含禁用的）。空集合按"没量到"处理，直接不扫。</param>
    public int PruneUnused(IReadOnlyList<AirAppManifest> installedManifests)
    {
        ArgumentNullException.ThrowIfNull(installedManifests);

        // 空清单是"发现环节本身坏了"的更合理解释，而不是"用户把所有包都卸了"。
        // 拿这个猜下去会把所有契约删光——离线用户就此修不回来，所以宁可什么都不删。
        if (installedManifests.Count == 0 || !Directory.Exists(_contractsDirectory))
        {
            return 0;
        }

        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var manifest in installedManifests)
        {
            if (manifest.SharedContracts is not { Count: > 0 })
            {
                continue;
            }

            foreach (var reference in manifest.SharedContracts)
            {
                referenced.Add(GetInstalledAssemblyPath(reference));
            }
        }

        List<string> loadedPaths;
        lock (_gate)
        {
            loadedPaths = _loadedContracts.Values
                .Select(contract => contract.AssemblyPath)
                .ToList();
        }

        var deleted = 0;
        foreach (var file in Directory.EnumerateFiles(_contractsDirectory, "*", SearchOption.AllDirectories))
        {
            var fullPath = Path.GetFullPath(file);
            if (referenced.Contains(fullPath))
            {
                continue;
            }

            if (loadedPaths.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (FileOperationRetryHelper.TryDeleteFile(fullPath, "AirAppSharedContracts"))
            {
                deleted++;
                AppLogger.Info(
                    "AirAppSharedContracts",
                    $"Removed unused shared contract. Path='{fullPath}'.");
            }
        }

        RemoveEmptyDirectories();
        return deleted;
    }

    private void RemoveEmptyDirectories()
    {
        // 由深到浅：先收版本目录，再收 id 目录，最深的那层先空。
        foreach (var directory in Directory
                     .EnumerateDirectories(_contractsDirectory, "*", SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length))
        {
            FileOperationRetryHelper.TryDeleteDirectory(directory, recursive: false, "AirAppSharedContracts");
        }
    }

    private void EnsureInstalled(
        AirAppMarketIndexDocument document,
        AirAppSharedContractReference reference,
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
        var resolvedSource = entry.DownloadUrl;
        try
        {
            if (AirAppMarketDefaults.TryResolveWorkspaceFile(entry.DownloadUrl, out var localSourcePath))
            {
                resolvedSource = localSourcePath;
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
            AppLogger.Info(
                "AirAppSharedContracts",
                $"Installed shared contract. ContractId='{reference.Id}'; Version='{reference.Version}'; Source='{resolvedSource}'; Destination='{destinationPath}'.");
        }
        finally
        {
            FileOperationRetryHelper.TryDeleteFile(temporaryPath, "AirAppSharedContracts");
        }
    }

    private AirAppMarketIndexDocument LoadIndex(CancellationToken cancellationToken)
    {
        AppLogger.Info("AirAppSharedContracts", "Loading market index for shared contract resolution.");
        var result = _indexService.LoadAsync(cancellationToken).GetAwaiter().GetResult();
        if (!result.Success || result.Document is null)
        {
            throw new InvalidOperationException(
                $"Failed to load market index for shared contract resolution: {result.ErrorMessage ?? "Unknown error"}");
        }

        AppLogger.Info(
            "AirAppSharedContracts",
            $"Market index ready. Source='{result.Source}'; Location='{result.SourceLocation}'; Warning='{result.WarningMessage ?? string.Empty}'.");

        return result.Document;
    }

    private LoadedSharedContract LoadSharedAssembly(
        AirAppSharedContractReference reference,
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
                        $"Shared contract assembly '{assemblyName}' is already loaded as '{existing.ContractId}' version '{existing.ContractVersion}', so AirApp dependency '{reference.Id}' version '{reference.Version}' cannot be activated in the same host process.");
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

    private string GetInstalledAssemblyPath(AirAppSharedContractReference reference)
    {
        return Path.Combine(
            _contractsDirectory,
            Sanitize(reference.Id),
            Sanitize(reference.Version),
            reference.AssemblyName);
    }

    private static string Sanitize(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return new string(value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
    }

    private sealed record LoadedSharedContract(
        string ContractId,
        string ContractVersion,
        string AssemblyName,
        string AssemblyPath,
        Assembly Assembly);
}
