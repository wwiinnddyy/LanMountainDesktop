using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LanMountainDesktop.Models;
using LanMountainDesktop.Plugins;
using LanMountainDesktop.PluginSdk;

namespace LanMountainDesktop.Services;

public sealed class PluginRuntimeService : IDisposable
{
    private const string PendingDeletionFileName = ".pending-plugin-deletions.json";

    private readonly PluginLoader _loader;
    private readonly AppSettingsService _appSettingsService = new();
    private readonly IHostApplicationLifecycle _applicationLifecycle = new HostApplicationLifecycleService();
    private readonly IServiceProvider _hostServices;
    private readonly IPluginPackageManager _packageManager;
    private readonly List<LoadedPlugin> _loadedPlugins = [];
    private readonly List<PluginLoadResult> _loadResults = [];
    private readonly List<PluginCatalogEntry> _catalog = [];
    private readonly List<PluginSettingsPageContribution> _settingsPages = [];
    private readonly List<PluginDesktopComponentContribution> _desktopComponents = [];
    private readonly object _packageMutationGate = new();

    public PluginRuntimeService()
    {
        PluginsDirectory = Path.Combine(AppContext.BaseDirectory, "Extensions", "Plugins");
        _packageManager = new PluginRuntimePackageManager(this);
        _hostServices = new PluginHostServiceProvider(_packageManager, _applicationLifecycle);
        _loader = new PluginLoader(CreateOptions());
    }

    public string PluginsDirectory { get; }

    public IReadOnlyList<LoadedPlugin> LoadedPlugins => _loadedPlugins;

    public IReadOnlyList<PluginLoadResult> LoadResults => _loadResults;

    public IReadOnlyList<PluginCatalogEntry> Catalog => _catalog;

    public IReadOnlyList<PluginSettingsPageContribution> SettingsPages => _settingsPages;

    public IReadOnlyList<PluginDesktopComponentContribution> DesktopComponents => _desktopComponents;

    public void LoadInstalledPlugins()
    {
        Directory.CreateDirectory(PluginsDirectory);
        ApplyPendingPluginDeletions();
        UnloadInstalledPlugins();

        var disabledPluginIds = GetDisabledPluginIds();
        var settingsSnapshot = _appSettingsService.Load();
        var hostLanguageCode = PluginLocalizer.NormalizeLanguageCode(settingsSnapshot.LanguageCode);
        var hostProperties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [PluginHostPropertyKeys.HostApplicationName] = "LanMountainDesktop",
            [PluginHostPropertyKeys.HostVersion] = typeof(App).Assembly.GetName().Version?.ToString(),
            [PluginHostPropertyKeys.PluginSdkApiVersion] = PluginSdkInfo.ApiVersion,
            [PluginHostPropertyKeys.HostLanguageCode] = hostLanguageCode
        };

        var discoveryFailures = new List<PluginLoadResult>();
        var candidates = DiscoverCandidates(discoveryFailures);
        _loadResults.AddRange(discoveryFailures);

        var selectedPluginIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            if (!selectedPluginIds.Add(candidate.Manifest.Id))
            {
                var duplicateFailure = PluginLoadResult.Failure(
                    candidate.SourcePath,
                    candidate.Manifest,
                    new InvalidOperationException(
                        $"Duplicate plugin id '{candidate.Manifest.Id}' was found. Source '{candidate.SourcePath}' was ignored because a higher-priority source was already selected."));
                _loadResults.Add(duplicateFailure);
                continue;
            }

            var isEnabled = !disabledPluginIds.Contains(candidate.Manifest.Id);
            if (!isEnabled)
            {
                _catalog.Add(new PluginCatalogEntry(
                    candidate.Manifest,
                    candidate.SourcePath,
                    candidate.SourceKind == PluginCatalogSourceKind.Package,
                    false,
                    false,
                    null,
                    0,
                    0));
                continue;
            }

            var loadResult = candidate.SourceKind switch
            {
                PluginCatalogSourceKind.Package => _loader.LoadFromPackage(
                    candidate.SourcePath,
                    PluginsDirectory,
                    services: _hostServices,
                    hostProperties),
                _ => _loader.LoadFromManifest(
                    candidate.SourcePath,
                    services: _hostServices,
                    hostProperties)
            };

            _loadResults.Add(loadResult);

            if (loadResult.IsSuccess && loadResult.LoadedPlugin is not null)
            {
                _loadedPlugins.Add(loadResult.LoadedPlugin);
                CollectContributions(loadResult.LoadedPlugin);
                _catalog.Add(new PluginCatalogEntry(
                    loadResult.LoadedPlugin.Manifest,
                    loadResult.SourcePath,
                    candidate.SourceKind == PluginCatalogSourceKind.Package,
                    true,
                    true,
                    null,
                    loadResult.LoadedPlugin.SettingsPages.Count,
                    loadResult.LoadedPlugin.DesktopComponents.Count));
                Debug.WriteLine($"[PluginRuntime] Loaded '{loadResult.Manifest?.Id}' from '{loadResult.SourcePath}'.");
                continue;
            }

            _catalog.Add(new PluginCatalogEntry(
                candidate.Manifest,
                candidate.SourcePath,
                candidate.SourceKind == PluginCatalogSourceKind.Package,
                true,
                false,
                loadResult.Error?.Message,
                0,
                0));
            Debug.WriteLine($"[PluginRuntime] Failed to load plugin from '{loadResult.SourcePath}': {loadResult.Error}");
        }

        if (_catalog.Count == 0 && discoveryFailures.Count == 0)
        {
            Debug.WriteLine($"[PluginRuntime] No .laapp packages or loose plugin manifests found under '{PluginsDirectory}'.");
        }
    }

    public bool SetPluginEnabled(string pluginId, bool isEnabled)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
        {
            return false;
        }

        var snapshot = _appSettingsService.Load();
        var disabledPluginIds = snapshot.DisabledPluginIds is { Count: > 0 }
            ? new HashSet<string>(snapshot.DisabledPluginIds, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var changed = isEnabled
            ? disabledPluginIds.Remove(pluginId)
            : disabledPluginIds.Add(pluginId);

        if (!changed)
        {
            return false;
        }

        snapshot.DisabledPluginIds = disabledPluginIds
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _appSettingsService.Save(snapshot);
        PendingRestartStateService.SetPending(PendingRestartStateService.PluginCatalogReason, true);

        for (var i = 0; i < _catalog.Count; i++)
        {
            if (string.Equals(_catalog[i].Manifest.Id, pluginId, StringComparison.OrdinalIgnoreCase))
            {
                _catalog[i] = _catalog[i] with { IsEnabled = isEnabled };
            }
        }

        return true;
    }

    public PluginManifest InstallPluginPackage(string packagePath)
    {
        lock (_packageMutationGate)
        {
            return InstallPluginPackageCore(packagePath).Manifest;
        }
    }

    public PluginManifest RegisterInstalledPluginPackage(string packagePath)
    {
        lock (_packageMutationGate)
        {
            return RegisterInstalledPluginPackageCore(packagePath);
        }
    }

    public bool DeleteInstalledPlugin(string pluginId)
    {
        lock (_packageMutationGate)
        {
            return DeleteInstalledPluginCore(pluginId);
        }
    }

    private bool DeleteInstalledPluginCore(string pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
        {
            return false;
        }

        var entry = _catalog.FirstOrDefault(candidate =>
            string.Equals(candidate.Manifest.Id, pluginId, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return false;
        }

        var targetPath = ResolvePluginRemovalTargetPath(entry);
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return false;
        }

        var fullTargetPath = Path.GetFullPath(targetPath);
        if (!TryDeletePluginTarget(fullTargetPath))
        {
            RegisterPendingPluginDeletion(fullTargetPath);
        }

        RemovePluginFromSnapshot(pluginId);
        RemovePluginFromCatalog(pluginId);
        PendingRestartStateService.SetPending(PendingRestartStateService.PluginCatalogReason, true);
        return true;
    }

    internal IReadOnlyList<InstalledPluginInfo> GetInstalledPluginsSnapshot()
    {
        return _catalog
            .OrderBy(entry => entry.Manifest.Name, StringComparer.OrdinalIgnoreCase)
            .Select(entry => new InstalledPluginInfo(
                entry.Manifest,
                entry.IsEnabled,
                entry.IsLoaded,
                entry.IsPackage,
                entry.ErrorMessage))
            .ToArray();
    }

    private PluginPackageInstallResult InstallPluginPackageCore(string packagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);

        var fullPackagePath = Path.GetFullPath(packagePath);
        if (!File.Exists(fullPackagePath))
        {
            throw new FileNotFoundException($"Plugin package '{fullPackagePath}' was not found.", fullPackagePath);
        }

        if (!string.Equals(Path.GetExtension(fullPackagePath), PluginSdkInfo.PackageFileExtension, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Plugin package must use the '{PluginSdkInfo.PackageFileExtension}' extension.");
        }

        Directory.CreateDirectory(PluginsDirectory);

        var manifest = ReadManifestFromPackage(fullPackagePath);
        AppLogger.Info(
            "PluginRuntime",
            $"Installing package. PluginId='{manifest.Id}'; Source='{fullPackagePath}'; PluginsDirectory='{PluginsDirectory}'.");
        var replacedExisting = RemoveExistingPluginPackages(manifest.Id, fullPackagePath);

        var destinationPath = Path.Combine(PluginsDirectory, BuildInstalledPackageFileName(manifest.Id));
        if (!string.Equals(fullPackagePath, Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase))
        {
            FileOperationRetryHelper.CopyWithRetry(fullPackagePath, destinationPath, overwrite: true, "PluginRuntime");
        }

        UpdateCatalogAfterPackageInstall(manifest, destinationPath);
        PendingRestartStateService.SetPending(PendingRestartStateService.PluginCatalogReason, true);
        AppLogger.Info(
            "PluginRuntime",
            $"Package staged. PluginId='{manifest.Id}'; Destination='{destinationPath}'; ReplacedExisting={replacedExisting}.");

        return new PluginPackageInstallResult(manifest, replacedExisting, RestartRequired: true);
    }

    private PluginManifest RegisterInstalledPluginPackageCore(string packagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);

        var fullPackagePath = Path.GetFullPath(packagePath);
        if (!File.Exists(fullPackagePath))
        {
            throw new FileNotFoundException($"Plugin package '{fullPackagePath}' was not found.", fullPackagePath);
        }

        var manifest = ReadManifestFromPackage(fullPackagePath);
        AppLogger.Info(
            "PluginRuntime",
            $"Registering externally installed package. PluginId='{manifest.Id}'; Source='{fullPackagePath}'.");
        UpdateCatalogAfterPackageInstall(manifest, fullPackagePath);
        PendingRestartStateService.SetPending(PendingRestartStateService.PluginCatalogReason, true);
        return manifest;
    }

    public void Dispose()
    {
        UnloadInstalledPlugins();
    }

    private void UnloadInstalledPlugins()
    {
        for (var i = _loadedPlugins.Count - 1; i >= 0; i--)
        {
            _loadedPlugins[i].Dispose();
        }

        _loadedPlugins.Clear();
        _loadResults.Clear();
        _catalog.Clear();
        _settingsPages.Clear();
        _desktopComponents.Clear();
    }

    private HashSet<string> GetDisabledPluginIds()
    {
        var snapshot = _appSettingsService.Load();
        return snapshot.DisabledPluginIds is { Count: > 0 }
            ? new HashSet<string>(snapshot.DisabledPluginIds, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private IReadOnlyList<PluginCandidate> DiscoverCandidates(List<PluginLoadResult> failures)
    {
        var candidates = new List<PluginCandidate>();

        foreach (var packagePath in EnumerateCandidatePaths($"*{PluginSdkInfo.PackageFileExtension}"))
        {
            try
            {
                var manifest = ReadManifestFromPackage(packagePath);
                candidates.Add(new PluginCandidate(packagePath, manifest, PluginCatalogSourceKind.Package));
            }
            catch (Exception ex)
            {
                failures.Add(PluginLoadResult.Failure(packagePath, null, ex));
            }
        }

        foreach (var manifestPath in EnumerateCandidatePaths("plugin.json"))
        {
            try
            {
                var manifest = PluginManifest.Load(manifestPath);
                candidates.Add(new PluginCandidate(manifestPath, manifest, PluginCatalogSourceKind.Manifest));
            }
            catch (Exception ex)
            {
                failures.Add(PluginLoadResult.Failure(manifestPath, null, ex));
            }
        }

        return candidates
            .OrderBy(candidate => candidate.SourceKind)
            .ThenBy(candidate => candidate.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IEnumerable<string> EnumerateCandidatePaths(string searchPattern)
    {
        var runtimeRootDirectory = EnsureTrailingSeparator(Path.Combine(Path.GetFullPath(PluginsDirectory), ".runtime"));

        return Directory
            .EnumerateFiles(PluginsDirectory, searchPattern, SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .Where(path => !path.StartsWith(runtimeRootDirectory, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
    }

    private static PluginManifest ReadManifestFromPackage(string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var entries = archive.Entries
            .Where(entry => string.Equals(entry.Name, "plugin.json", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (entries.Length == 0)
        {
            throw new InvalidOperationException($"Plugin package '{packagePath}' does not contain 'plugin.json'.");
        }

        if (entries.Length > 1)
        {
            throw new InvalidOperationException($"Plugin package '{packagePath}' contains multiple 'plugin.json' files.");
        }

        using var stream = entries[0].Open();
        return PluginManifest.Load(stream, $"{packagePath}!/{entries[0].FullName}");
    }

    private bool RemoveExistingPluginPackages(string pluginId, string packagePathToKeep)
    {
        var replacedExisting = false;
        foreach (var existingPackagePath in EnumerateCandidatePaths($"*{PluginSdkInfo.PackageFileExtension}"))
        {
            if (string.Equals(
                    Path.GetFullPath(existingPackagePath),
                    Path.GetFullPath(packagePathToKeep),
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var existingManifest = ReadManifestFromPackage(existingPackagePath);
                if (!string.Equals(existingManifest.Id, pluginId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                FileOperationRetryHelper.DeleteFileWithRetry(existingPackagePath, "PluginRuntime");
                replacedExisting = true;
            }
            catch
            {
                // Ignore unrelated or invalid packages during replacement.
            }
        }

        return replacedExisting;
    }

    private void UpdateCatalogAfterPackageInstall(PluginManifest manifest, string destinationPath)
    {
        var isEnabled = !GetDisabledPluginIds().Contains(manifest.Id);
        var entry = new PluginCatalogEntry(
            manifest,
            destinationPath,
            IsPackage: true,
            IsEnabled: isEnabled,
            IsLoaded: false,
            ErrorMessage: null,
            SettingsPageCount: 0,
            WidgetCount: 0);

        for (var i = 0; i < _catalog.Count; i++)
        {
            if (string.Equals(_catalog[i].Manifest.Id, manifest.Id, StringComparison.OrdinalIgnoreCase))
            {
                _catalog[i] = entry;
                return;
            }
        }

        _catalog.Add(entry);
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

    private static PluginLoaderOptions CreateOptions()
    {
        var options = new PluginLoaderOptions();
        AddSharedAssembly(options, typeof(App).Assembly);

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var assemblyName = assembly.GetName().Name;
            if (string.IsNullOrWhiteSpace(assemblyName))
            {
                continue;
            }

            if (assemblyName.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(assemblyName, "MicroCom.Runtime", StringComparison.OrdinalIgnoreCase))
            {
                AddSharedAssembly(options, assembly);
            }
        }

        return options;
    }

    private static void AddSharedAssembly(PluginLoaderOptions options, Assembly assembly)
    {
        var assemblyName = assembly.GetName().Name;
        if (!string.IsNullOrWhiteSpace(assemblyName))
        {
            options.SharedAssemblyNames.Add(assemblyName);
        }
    }

    private void CollectContributions(LoadedPlugin loadedPlugin)
    {
        foreach (var settingsPage in loadedPlugin.SettingsPages)
        {
            _settingsPages.Add(new PluginSettingsPageContribution(loadedPlugin, settingsPage));
        }

        foreach (var desktopComponent in loadedPlugin.DesktopComponents)
        {
            _desktopComponents.Add(new PluginDesktopComponentContribution(loadedPlugin, desktopComponent));
        }
    }

    private void ApplyPendingPluginDeletions()
    {
        var pendingPaths = ReadPendingPluginDeletions();
        if (pendingPaths.Count == 0)
        {
            return;
        }

        var remainingPaths = new List<string>();
        foreach (var path in pendingPaths)
        {
            if (!TryDeletePluginTarget(path))
            {
                remainingPaths.Add(path);
            }
        }

        SavePendingPluginDeletions(remainingPaths);
    }

    private string ResolvePluginRemovalTargetPath(PluginCatalogEntry entry)
    {
        if (entry.IsPackage)
        {
            return entry.SourcePath;
        }

        var fullSourcePath = Path.GetFullPath(entry.SourcePath);
        if (File.Exists(fullSourcePath) &&
            string.Equals(Path.GetFileName(fullSourcePath), "plugin.json", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetDirectoryName(fullSourcePath) ?? fullSourcePath;
        }

        return fullSourcePath;
    }

    private static bool TryDeletePluginTarget(string targetPath)
    {
        try
        {
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }
            else if (Directory.Exists(targetPath))
            {
                Directory.Delete(targetPath, recursive: true);
            }

            return !File.Exists(targetPath) && !Directory.Exists(targetPath);
        }
        catch
        {
            return false;
        }
    }

    private void RegisterPendingPluginDeletion(string targetPath)
    {
        var pendingPaths = ReadPendingPluginDeletions();
        if (pendingPaths.Contains(targetPath, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        pendingPaths.Add(targetPath);
        SavePendingPluginDeletions(pendingPaths);
    }

    private List<string> ReadPendingPluginDeletions()
    {
        var pendingDeletionFilePath = GetPendingDeletionFilePath();
        if (!File.Exists(pendingDeletionFilePath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(pendingDeletionFilePath);
            var paths = JsonSerializer.Deserialize<List<string>>(json);
            return paths?
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void SavePendingPluginDeletions(IEnumerable<string> pendingPaths)
    {
        var pendingDeletionFilePath = GetPendingDeletionFilePath();
        var normalizedPaths = pendingPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedPaths.Length == 0)
        {
            if (File.Exists(pendingDeletionFilePath))
            {
                File.Delete(pendingDeletionFilePath);
            }

            return;
        }

        Directory.CreateDirectory(PluginsDirectory);
        var json = JsonSerializer.Serialize(normalizedPaths, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(pendingDeletionFilePath, json);
    }

    private string GetPendingDeletionFilePath()
    {
        return Path.Combine(PluginsDirectory, PendingDeletionFileName);
    }

    private void RemovePluginFromSnapshot(string pluginId)
    {
        var snapshot = _appSettingsService.Load();
        if (snapshot.DisabledPluginIds.RemoveAll(id => string.Equals(id, pluginId, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            _appSettingsService.Save(snapshot);
        }
    }

    private void RemovePluginFromCatalog(string pluginId)
    {
        _catalog.RemoveAll(entry => string.Equals(entry.Manifest.Id, pluginId, StringComparison.OrdinalIgnoreCase));
        _settingsPages.RemoveAll(entry => string.Equals(entry.Plugin.Manifest.Id, pluginId, StringComparison.OrdinalIgnoreCase));
        _desktopComponents.RemoveAll(entry => string.Equals(entry.Plugin.Manifest.Id, pluginId, StringComparison.OrdinalIgnoreCase));
        _loadResults.RemoveAll(entry => string.Equals(entry.Manifest?.Id, pluginId, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record PluginCandidate(
        string SourcePath,
        PluginManifest Manifest,
        PluginCatalogSourceKind SourceKind);

    private sealed class PluginHostServiceProvider : IServiceProvider
    {
        private readonly IPluginPackageManager _packageManager;
        private readonly IHostApplicationLifecycle _applicationLifecycle;

        public PluginHostServiceProvider(
            IPluginPackageManager packageManager,
            IHostApplicationLifecycle applicationLifecycle)
        {
            _packageManager = packageManager;
            _applicationLifecycle = applicationLifecycle;
        }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IPluginPackageManager))
            {
                return _packageManager;
            }

            if (serviceType == typeof(IHostApplicationLifecycle))
            {
                return _applicationLifecycle;
            }

            return null;
        }
    }

    private sealed class PluginRuntimePackageManager : IPluginPackageManager
    {
        private readonly PluginRuntimeService _runtimeService;

        public PluginRuntimePackageManager(PluginRuntimeService runtimeService)
        {
            _runtimeService = runtimeService;
        }

        public IReadOnlyList<InstalledPluginInfo> GetInstalledPlugins()
        {
            return _runtimeService.GetInstalledPluginsSnapshot();
        }

        public PluginPackageInstallResult InstallPackage(string packagePath)
        {
            return _runtimeService.InstallPluginPackageCore(packagePath);
        }
    }
}
