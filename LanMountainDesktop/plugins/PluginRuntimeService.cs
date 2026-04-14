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
using LanMountainDesktop.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LanMountainDesktop.Services;

public sealed class PluginRuntimeService : IDisposable
{
    private const string PendingDeletionFileName = ".pending-plugin-deletions.json";

    private readonly PluginLoaderOptions _loaderOptions;
    private readonly PluginLoader _loader;
    private readonly IHostApplicationLifecycle _applicationLifecycle = new HostApplicationLifecycleService();
    private readonly PluginExportRegistry _exportRegistry = new();
    private readonly PluginSharedContractManager _sharedContractManager;
    private readonly IServiceProvider _hostServices;
    private readonly IPluginPackageManager _packageManager;
    private readonly ISettingsFacadeService _settingsFacade;
    private readonly SettingsCatalogService _settingsCatalogService;
    private readonly List<LoadedPlugin> _loadedPlugins = [];
    private readonly List<PluginLoadResult> _loadResults = [];
    private readonly List<PluginCatalogEntry> _catalog = [];
    private readonly List<PluginSettingsSectionContribution> _settingsSections = [];
    private readonly List<PluginDesktopComponentContribution> _desktopComponents = [];
    private readonly List<PluginDesktopComponentEditorContribution> _desktopComponentEditors = [];
    private readonly object _packageMutationGate = new();

    public PluginRuntimeService(ISettingsFacadeService? settingsFacade = null)
    {
        PluginsDirectory = Path.Combine(GetUserDataRootDirectory(), "Extensions", "Plugins");
        _sharedContractManager = new PluginSharedContractManager(
            Path.Combine(GetUserDataRootDirectory(), "PluginMarket"));
        _packageManager = new PluginRuntimePackageManager(this);
        _settingsFacade = settingsFacade ?? new SettingsFacadeService();
        _settingsCatalogService = _settingsFacade.Catalog as SettingsCatalogService
            ?? new SettingsCatalogService();
        if (_settingsFacade is SettingsFacadeService concreteFacade)
        {
            concreteFacade.BindPluginRuntime(this);
        }
        _hostServices = new PluginHostServiceProvider(
            _packageManager,
            _applicationLifecycle,
            _exportRegistry,
            _settingsFacade,
            _settingsFacade.Settings,
            _settingsFacade.Catalog);
        _loaderOptions = CreateOptions();
        _loader = new PluginLoader(_loaderOptions);
    }

    public string PluginsDirectory { get; }

    public IReadOnlyList<LoadedPlugin> LoadedPlugins => _loadedPlugins;

    public IReadOnlyList<PluginLoadResult> LoadResults => _loadResults;

    public IReadOnlyList<PluginCatalogEntry> Catalog => _catalog;

    public IReadOnlyList<PluginSettingsSectionContribution> SettingsSections => _settingsSections;

    public IReadOnlyList<PluginDesktopComponentContribution> DesktopComponents => _desktopComponents;
    public IReadOnlyList<PluginDesktopComponentEditorContribution> DesktopComponentEditors => _desktopComponentEditors;

    public IPluginExportRegistry ExportRegistry => _exportRegistry;

    public ISettingsFacadeService SettingsFacade => _settingsFacade;

    public void LoadInstalledPlugins()
    {
        Directory.CreateDirectory(PluginsDirectory);
        ApplyPendingPluginDeletions();
        UnloadInstalledPlugins();
        MergeDevSettingsFromSnapshot();
        AppLogger.Info("PluginRuntime", $"Loading installed plugins from '{PluginsDirectory}'.");

        var disabledPluginIds = GetDisabledPluginIds();
        var settingsSnapshot = LoadAppSettingsSnapshot();
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
        AppLogger.Info(
            "PluginRuntime",
            $"Plugin discovery completed. Candidates={candidates.Count}; DiscoveryFailures={discoveryFailures.Count}; PluginsDirectory='{PluginsDirectory}'.");

        var selectedPluginIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            var isDevPlugin = candidate.SourceKind == PluginCatalogSourceKind.DevPlugin;

            if (!selectedPluginIds.Add(candidate.Manifest.Id))
            {
                if (isDevPlugin)
                {
                    AppLogger.Info(
                        "DevPlugin",
                        $"Developer plugin '{candidate.Manifest.Id}' overrides an already-registered plugin from '{candidate.SourcePath}'.");
                }
                else
                {
                    var duplicateFailure = PluginLoadResult.Failure(
                        candidate.SourcePath,
                        candidate.Manifest,
                        new InvalidOperationException(
                            $"Duplicate plugin id '{candidate.Manifest.Id}' was found. Source '{candidate.SourcePath}' was ignored because a higher-priority source was already selected."));
                    _loadResults.Add(duplicateFailure);
                    LogPluginFailure("CatalogSelection", duplicateFailure, treatAsError: false);
                    continue;
                }
            }

            var isEnabled = isDevPlugin || !disabledPluginIds.Contains(candidate.Manifest.Id);
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

            try
            {
                AppLogger.Info(
                    "PluginRuntime",
                    $"Preparing shared contracts. PluginId='{candidate.Manifest.Id}'; SourcePath='{candidate.SourcePath}'; SourceKind='{candidate.SourceKind}'.");
                RegisterSharedContractsForLoad(candidate.Manifest);
                AppLogger.Info(
                    "PluginRuntime",
                    $"Shared contracts ready. PluginId='{candidate.Manifest.Id}'; SourcePath='{candidate.SourcePath}'.");
            }
            catch (Exception ex)
            {
                var dependencyFailure = PluginLoadResult.Failure(candidate.SourcePath, candidate.Manifest, ex);
                _loadResults.Add(dependencyFailure);
                _catalog.Add(new PluginCatalogEntry(
                    candidate.Manifest,
                    candidate.SourcePath,
                    candidate.SourceKind == PluginCatalogSourceKind.Package,
                    true,
                    false,
                    ex.Message,
                    0,
                    0));
                LogPluginFailure("DependencyPrepare", dependencyFailure, treatAsError: false);
                continue;
            }

            AppLogger.Info(
                "PluginRuntime",
                $"Starting plugin load. PluginId='{candidate.Manifest.Id}'; SourcePath='{candidate.SourcePath}'; SourceKind='{candidate.SourceKind}'.");
            var loadResult = candidate.SourceKind switch
            {
                PluginCatalogSourceKind.Package => _loader.LoadFromPackage(
                    candidate.SourcePath,
                    PluginsDirectory,
                    services: _hostServices,
                    hostProperties),
                PluginCatalogSourceKind.DevPlugin => _loader.LoadFromManifest(
                    candidate.SourcePath,
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
                    loadResult.LoadedPlugin.SettingsSections.Count,
                    loadResult.LoadedPlugin.DesktopComponents.Count,
                    IsDevPlugin: isDevPlugin));
                AppLogger.Info(
                    "PluginRuntime",
                    $"Plugin loaded. PluginId='{loadResult.LoadedPlugin.Manifest.Id}'; SourcePath='{loadResult.SourcePath}'; ManifestVersion='{loadResult.LoadedPlugin.Manifest.Version ?? "<unknown>"}'; ApiVersion='{loadResult.LoadedPlugin.Manifest.ApiVersion ?? "<unknown>"}'; SourceKind='{candidate.SourceKind}'; SettingsSections={loadResult.LoadedPlugin.SettingsSections.Count}; Widgets={loadResult.LoadedPlugin.DesktopComponents.Count}; Editors={loadResult.LoadedPlugin.DesktopComponentEditors.Count}.");
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
                0,
                IsDevPlugin: isDevPlugin));
            LogPluginFailure("Load", loadResult, treatAsError: true);
            Debug.WriteLine($"[PluginRuntime] Failed to load plugin from '{loadResult.SourcePath}': {loadResult.Error}");
        }

        if (_catalog.Count == 0 && discoveryFailures.Count == 0)
        {
            AppLogger.Info(
                "PluginRuntime",
                $"No plugin packages or loose manifests were discovered under '{PluginsDirectory}'.");
            Debug.WriteLine($"[PluginRuntime] No .laapp packages or loose plugin manifests found under '{PluginsDirectory}'.");
        }
    }

    public bool SetPluginEnabled(string pluginId, bool isEnabled)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
        {
            return false;
        }

        var catalogEntry = _catalog.FirstOrDefault(entry =>
            string.Equals(entry.Manifest.Id, pluginId, StringComparison.OrdinalIgnoreCase));
        if (catalogEntry.IsDevPlugin && !isEnabled)
        {
            AppLogger.Warn("DevPlugin", $"Cannot disable developer plugin '{pluginId}'. Developer plugins are always enabled in dev mode.");
            return false;
        }

        var snapshot = LoadAppSettingsSnapshot();
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
        SaveAppSettingsSnapshot(snapshot);
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
        _sharedContractManager.EnsureInstalled(manifest);
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
        _sharedContractManager.EnsureInstalled(manifest);
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
        _sharedContractManager.Dispose();
        if (_settingsFacade is IDisposable disposable && !ReferenceEquals(_settingsFacade, HostSettingsFacadeProvider.GetOrCreate()))
        {
            disposable.Dispose();
        }
    }

    private void UnloadInstalledPlugins()
    {
        for (var i = _loadedPlugins.Count - 1; i >= 0; i--)
        {
            var pluginId = _loadedPlugins[i].Manifest.Id;
            _exportRegistry.RemoveExports(pluginId);
            _settingsCatalogService.RemovePluginSections(pluginId);
            _loadedPlugins[i].Dispose();
        }

        _loadedPlugins.Clear();
        _exportRegistry.Clear();
        _loadResults.Clear();
        _catalog.Clear();
        _settingsSections.Clear();
        _desktopComponents.Clear();
    }

    private HashSet<string> GetDisabledPluginIds()
    {
        var snapshot = LoadAppSettingsSnapshot();
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
                var failure = PluginLoadResult.Failure(packagePath, null, ex);
                failures.Add(failure);
                LogPluginFailure("ManifestValidation", failure, treatAsError: false);
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
                var failure = PluginLoadResult.Failure(manifestPath, null, ex);
                failures.Add(failure);
                LogPluginFailure("ManifestValidation", failure, treatAsError: false);
            }
        }

        DiscoverDevPluginCandidates(candidates, failures);

        return candidates
            .OrderByDescending(candidate => candidate.SourceKind)
            .ThenBy(candidate => candidate.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void DiscoverDevPluginCandidates(List<PluginCandidate> candidates, List<PluginLoadResult> failures)
    {
        var devOptions = DevPluginOptions.Current;
        if (!devOptions.IsDevMode || devOptions.DevPluginPaths.Count == 0)
        {
            return;
        }

        AppLogger.Info("DevPlugin", $"Scanning developer plugin paths. Count={devOptions.DevPluginPaths.Count}.");

        foreach (var devPath in devOptions.DevPluginPaths)
        {
            if (File.Exists(devPath) && string.Equals(Path.GetExtension(devPath), PluginSdkInfo.PackageFileExtension, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var manifest = ReadManifestFromPackage(devPath);
                    candidates.Add(new PluginCandidate(devPath, manifest, PluginCatalogSourceKind.DevPlugin));
                    AppLogger.Info("DevPlugin", $"Found developer plugin package. PluginId='{manifest.Id}'; Path='{devPath}'.");
                }
                catch (Exception ex)
                {
                    var failure = PluginLoadResult.Failure(devPath, null, ex);
                    failures.Add(failure);
                    AppLogger.Warn("DevPlugin", $"Failed to read developer plugin package '{devPath}'.", ex);
                }

                continue;
            }

            if (Directory.Exists(devPath))
            {
                var manifestPath = Path.Combine(devPath, PluginSdkInfo.ManifestFileName);
                if (File.Exists(manifestPath))
                {
                    try
                    {
                        var manifest = PluginManifest.Load(manifestPath);
                        candidates.Add(new PluginCandidate(manifestPath, manifest, PluginCatalogSourceKind.DevPlugin));
                        AppLogger.Info("DevPlugin", $"Found developer plugin manifest. PluginId='{manifest.Id}'; Path='{manifestPath}'.");
                    }
                    catch (Exception ex)
                    {
                        var failure = PluginLoadResult.Failure(manifestPath, null, ex);
                        failures.Add(failure);
                        AppLogger.Warn("DevPlugin", $"Failed to load developer plugin manifest '{manifestPath}'.", ex);
                    }
                }
                else
                {
                    AppLogger.Warn("DevPlugin", $"Developer plugin directory '{devPath}' does not contain '{PluginSdkInfo.ManifestFileName}'. Skipping.");
                }

                continue;
            }

            AppLogger.Warn("DevPlugin", $"Developer plugin path '{devPath}' is neither a file nor a directory. Skipping.");
        }
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

    private static string GetUserDataRootDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            return Path.Combine(AppContext.BaseDirectory, "Data");
        }

        return Path.Combine(localAppData, "LanMountainDesktop");
    }

    private static PluginLoaderOptions CreateOptions()
    {
        var devOptions = DevPluginOptions.Current;
        var options = new PluginLoaderOptions { IsDevMode = devOptions.IsDevMode };
        AddSharedAssembly(options, typeof(App).Assembly);
        AddSharedAssembly(options, typeof(IServiceCollection).Assembly);
        AddSharedAssembly(options, typeof(HostBuilderContext).Assembly);

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var assemblyName = assembly.GetName().Name;
            if (string.IsNullOrWhiteSpace(assemblyName))
            {
                continue;
            }

            if (assemblyName.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(assemblyName, "FluentAvaloniaUI", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(assemblyName, "FluentIcons.Avalonia", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(assemblyName, "FluentIcons.Avalonia.Fluent", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(assemblyName, "Material.Icons.Avalonia", StringComparison.OrdinalIgnoreCase) ||
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

    private void MergeDevSettingsFromSnapshot()
    {
        var devOptions = DevPluginOptions.Current;

        try
        {
            var snapshot = LoadAppSettingsSnapshot();

            if (snapshot.IsDevModeEnabled && !devOptions.IsDevMode)
            {
                devOptions.ApplySettingsFromSnapshot(isDevMode: true, devPluginPath: snapshot.DevPluginPath);
                AppLogger.Info("DevPlugin", $"Developer mode enabled via settings. DevPluginPath='{snapshot.DevPluginPath}'.");
            }
            else if (!string.IsNullOrWhiteSpace(snapshot.DevPluginPath) && string.IsNullOrWhiteSpace(devOptions.DevPluginPath))
            {
                devOptions.ApplySettingsFromSnapshot(isDevMode: devOptions.IsDevMode, devPluginPath: snapshot.DevPluginPath);
                AppLogger.Info("DevPlugin", $"Developer plugin path merged from settings. DevPluginPath='{snapshot.DevPluginPath}'.");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("DevPlugin", "Failed to merge developer settings from snapshot.", ex);
        }
    }

    private void CollectContributions(LoadedPlugin loadedPlugin)
    {
        _exportRegistry.ReplaceExports(loadedPlugin.Manifest.Id, loadedPlugin.ExportedServices);

        _settingsCatalogService.RegisterPluginSections(loadedPlugin.Manifest.Id, loadedPlugin.SettingsSections);

        _settingsSections.RemoveAll(entry => string.Equals(
            entry.Plugin.Manifest.Id,
            loadedPlugin.Manifest.Id,
            StringComparison.OrdinalIgnoreCase));
        _desktopComponentEditors.RemoveAll(entry => string.Equals(
            entry.Plugin.Manifest.Id,
            loadedPlugin.Manifest.Id,
            StringComparison.OrdinalIgnoreCase));

        foreach (var settingsSection in loadedPlugin.SettingsSections)
        {
            _settingsSections.Add(new PluginSettingsSectionContribution(loadedPlugin, settingsSection));
        }

        foreach (var desktopComponent in loadedPlugin.DesktopComponents)
        {
            _desktopComponents.Add(new PluginDesktopComponentContribution(loadedPlugin, desktopComponent));
        }

        foreach (var desktopComponentEditor in loadedPlugin.DesktopComponentEditors)
        {
            _desktopComponentEditors.Add(new PluginDesktopComponentEditorContribution(loadedPlugin, desktopComponentEditor));
        }
    }

    private void RegisterSharedContractsForLoad(PluginManifest manifest)
    {
        foreach (var assemblyName in _sharedContractManager.PrepareForLoad(manifest))
        {
            if (!string.IsNullOrWhiteSpace(assemblyName))
            {
                _loaderOptions.SharedAssemblyNames.Add(assemblyName);
            }
        }
    }

    private void ApplyPendingPluginDeletions()
    {
        var pendingPaths = ReadPendingPluginDeletions();
        var remainingPaths = new List<string>();
        foreach (var path in pendingPaths)
        {
            if (!TryDeletePluginTarget(path))
            {
                remainingPaths.Add(path);
            }
        }

        SavePendingPluginDeletions(remainingPaths);
        CleanupPendingDeletionDirectory();
    }

    private void CleanupPendingDeletionDirectory()
    {
        var pendingDeletionDir = Path.Combine(PluginsDirectory, ".pending-deletions");
        if (!Directory.Exists(pendingDeletionDir))
        {
            return;
        }

        foreach (var pendingFile in Directory.EnumerateFiles(pendingDeletionDir, "*.pending"))
        {
            try
            {
                File.Delete(pendingFile);
            }
            catch
            {
                // Ignore cleanup failures for pending deletions.
            }
        }

        try
        {
            if (Directory.GetFiles(pendingDeletionDir).Length == 0 &&
                Directory.GetDirectories(pendingDeletionDir).Length == 0)
            {
                Directory.Delete(pendingDeletionDir);
            }
        }
        catch
        {
            // Ignore directory cleanup failures.
        }
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

    private static void LogPluginFailure(string stage, PluginLoadResult result, bool treatAsError)
    {
        var manifest = result.Manifest;
        var message =
            $"Plugin load issue. Stage='{stage}'; PluginId='{manifest?.Id ?? "<unknown>"}'; SourcePath='{result.SourcePath}'; ManifestVersion='{manifest?.Version ?? "<unknown>"}'; ApiVersion='{manifest?.ApiVersion ?? "<unknown>"}'; Error='{result.Error?.Message ?? "<none>"}'.";

        if (treatAsError)
        {
            AppLogger.Error("PluginRuntime", message, result.Error);
            return;
        }

        AppLogger.Warn("PluginRuntime", message, result.Error);
    }

    private void RemovePluginFromSnapshot(string pluginId)
    {
        var snapshot = LoadAppSettingsSnapshot();
        if (snapshot.DisabledPluginIds.RemoveAll(id => string.Equals(id, pluginId, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            SaveAppSettingsSnapshot(snapshot);
        }
    }

    private AppSettingsSnapshot LoadAppSettingsSnapshot()
    {
        return _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
    }

    private void SaveAppSettingsSnapshot(AppSettingsSnapshot snapshot)
    {
        _settingsFacade.Settings.SaveSnapshot(SettingsScope.App, snapshot);
    }

    private void RemovePluginFromCatalog(string pluginId)
    {
        _catalog.RemoveAll(entry => string.Equals(entry.Manifest.Id, pluginId, StringComparison.OrdinalIgnoreCase));
        _settingsSections.RemoveAll(entry => string.Equals(entry.Plugin.Manifest.Id, pluginId, StringComparison.OrdinalIgnoreCase));
        _desktopComponents.RemoveAll(entry => string.Equals(entry.Plugin.Manifest.Id, pluginId, StringComparison.OrdinalIgnoreCase));
        _loadResults.RemoveAll(entry => string.Equals(entry.Manifest?.Id, pluginId, StringComparison.OrdinalIgnoreCase));
        _settingsCatalogService.RemovePluginSections(pluginId);
    }

    private enum PluginCatalogSourceKind
    {
        Package = 0,
        Manifest = 1,
        DevPlugin = 2
    }

    private sealed record PluginCandidate(
        string SourcePath,
        PluginManifest Manifest,
        PluginCatalogSourceKind SourceKind);

    private sealed class PluginHostServiceProvider : IServiceProvider
    {
        private readonly IPluginPackageManager _packageManager;
        private readonly IHostApplicationLifecycle _applicationLifecycle;
    private readonly IPluginExportRegistry _exportRegistry;
    private readonly ISettingsFacadeService _settingsFacade;
    private readonly ISettingsService _settingsService;
    private readonly ISettingsCatalog _settingsCatalog;
    private readonly IAppearanceThemeService _appearanceThemeService;

        public PluginHostServiceProvider(
            IPluginPackageManager packageManager,
            IHostApplicationLifecycle applicationLifecycle,
            IPluginExportRegistry exportRegistry,
            ISettingsFacadeService settingsFacade,
            ISettingsService settingsService,
            ISettingsCatalog settingsCatalog)
        {
            _packageManager = packageManager;
            _applicationLifecycle = applicationLifecycle;
            _exportRegistry = exportRegistry;
            _settingsFacade = settingsFacade;
            _settingsService = settingsService;
            _settingsCatalog = settingsCatalog;
            _appearanceThemeService = HostAppearanceThemeProvider.GetOrCreate();
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

            if (serviceType == typeof(IPluginExportRegistry))
            {
                return _exportRegistry;
            }

            if (serviceType == typeof(ISettingsFacadeService))
            {
                return _settingsFacade;
            }

            if (serviceType == typeof(ISettingsService))
            {
                return _settingsService;
            }

            if (serviceType == typeof(ISettingsCatalog))
            {
                return _settingsCatalog;
            }

            if (serviceType == typeof(IAppearanceThemeService))
            {
                return _appearanceThemeService;
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
