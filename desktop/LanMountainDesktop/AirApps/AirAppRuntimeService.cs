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
using LanMountainDesktop.AirApps;
using LanMountainDesktop.AirAppSdk;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.Shared.IPC;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LanMountainDesktop.Services;

public sealed class AirAppRuntimeService : IDisposable
{
    private const string PendingDeletionFileName = ".pending-plugin-deletions.json";

    private readonly AirAppLoaderOptions _loaderOptions;
    private readonly AirAppLoader _loader;
    private readonly IHostApplicationLifecycle _applicationLifecycle = new HostApplicationLifecycleService();
    private readonly AirAppExportRegistry _exportRegistry = new();
    private readonly AirAppSharedContractManager _sharedContractManager;
    private readonly IServiceProvider _hostServices;
    private readonly IAirAppPackageManager _packageManager;
    private readonly ISettingsFacadeService _settingsFacade;
    private readonly SettingsCatalogService _settingsCatalogService;
    private readonly PublicIpcHostService? _publicIpcHostService;
    private readonly IMaterialColorService _materialColorService;
    private readonly List<LoadedAirApp> _loadedAirApps = [];
    private readonly List<AirAppLoadResult> _loadResults = [];
    private readonly List<AirAppCatalogEntry> _catalog = [];
    private readonly List<AirAppSettingsSectionContribution> _settingsSections = [];
    private readonly List<AirAppDesktopComponentContribution> _desktopComponents = [];
    private readonly List<AirAppDesktopComponentEditorContribution> _desktopComponentEditors = [];
    private readonly object _packageMutationGate = new();

    public AirAppRuntimeService(
        ISettingsFacadeService? settingsFacade = null,
        PublicIpcHostService? publicIpcHostService = null)
    {
        var dataRoot = AppDataPathProvider.GetDataRoot();
        AirAppsDirectory = Path.Combine(dataRoot, "Extensions", "AirApps");
        _sharedContractManager = new AirAppSharedContractManager(
            AppDataPathProvider.GetAirAppMarketDirectory());
        _packageManager = new AirAppRuntimePackageManager(this);
        _settingsFacade = settingsFacade ?? new SettingsFacadeService();
        _publicIpcHostService = publicIpcHostService;
        _materialColorService = HostMaterialColorProvider.GetOrCreate();
        _settingsCatalogService = _settingsFacade.Catalog as SettingsCatalogService
            ?? new SettingsCatalogService();
        if (_settingsFacade is SettingsFacadeService concreteFacade)
        {
            concreteFacade.BindAirAppRuntime(this);
        }
        _hostServices = new AirAppHostServiceProvider(
            _packageManager,
            _applicationLifecycle,
            _exportRegistry,
            _settingsFacade,
            _settingsFacade.Settings,
            _settingsFacade.Catalog,
            _publicIpcHostService);
        _loaderOptions = CreateOptions();
        _loader = new AirAppLoader(_loaderOptions);
        _materialColorService.MaterialColorChanged += OnMaterialColorChanged;
    }

    public string AirAppsDirectory { get; }

    public IReadOnlyList<LoadedAirApp> LoadedAirApps => _loadedAirApps;

    public IReadOnlyList<AirAppLoadResult> LoadResults => _loadResults;

    public IReadOnlyList<AirAppCatalogEntry> Catalog => _catalog;

    public IReadOnlyList<AirAppSettingsSectionContribution> SettingsSections => _settingsSections;

    public IReadOnlyList<AirAppDesktopComponentContribution> DesktopComponents => _desktopComponents;
    public IReadOnlyList<AirAppDesktopComponentEditorContribution> DesktopComponentEditors => _desktopComponentEditors;

    public IAirAppExportRegistry ExportRegistry => _exportRegistry;

    public ISettingsFacadeService SettingsFacade => _settingsFacade;

    public void LoadInstalledAirApps()
    {
        Directory.CreateDirectory(AirAppsDirectory);
        UnloadInstalledAirApps();
        ApplyPendingAirAppDeletions();
        ApplyPendingPluginOperations();
        MergeDevSettingsFromSnapshot();
        AppLogger.Info("AirAppRuntime", $"Loading installed plugins from '{AirAppsDirectory}'.");

        var disabledAirAppIds = GetDisabledPluginIds();
        var settingsSnapshot = LoadAppSettingsSnapshot();
        var hostLanguageCode = AirAppLocalizer.NormalizeLanguageCode(settingsSnapshot.LanguageCode);
        var hostProperties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [AirAppHostPropertyKeys.HostApplicationName] = "LanMountainDesktop",
            [AirAppHostPropertyKeys.HostVersion] = typeof(App).Assembly.GetName().Version?.ToString(),
            [AirAppHostPropertyKeys.AirAppSdkApiVersion] = AirAppSdkInfo.ApiVersion,
            [AirAppHostPropertyKeys.HostLanguageCode] = hostLanguageCode
        };

        var discoveryFailures = new List<AirAppLoadResult>();
        var candidates = DiscoverCandidates(discoveryFailures);
        _loadResults.AddRange(discoveryFailures);
        AppLogger.Info(
            "AirAppRuntime",
            $"AirApp discovery completed. Candidates={candidates.Count}; DiscoveryFailures={discoveryFailures.Count}; AirAppsDirectory='{AirAppsDirectory}'.");

        var selectedAirAppIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            var isDevAirApp = candidate.SourceKind == AirAppCatalogSourceKind.DevAirApp;

            if (!selectedAirAppIds.Add(candidate.Manifest.Id))
            {
                if (isDevAirApp)
                {
                    AppLogger.Info(
                        "DevAirApp",
                        $"Developer plugin '{candidate.Manifest.Id}' overrides an already-registered plugin from '{candidate.SourcePath}'.");
                }
                else
                {
                    var duplicateFailure = AirAppLoadResult.Failure(
                        candidate.SourcePath,
                        candidate.Manifest,
                        new InvalidOperationException(
                            $"Duplicate plugin id '{candidate.Manifest.Id}' was found. Source '{candidate.SourcePath}' was ignored because a higher-priority source was already selected."));
                    _loadResults.Add(duplicateFailure);
                    LogAirAppFailure("CatalogSelection", duplicateFailure, treatAsError: false);
                    continue;
                }
            }

            var isEnabled = isDevAirApp || !disabledAirAppIds.Contains(candidate.Manifest.Id);
            if (!isEnabled)
            {
                _catalog.Add(new AirAppCatalogEntry(
                    candidate.Manifest,
                    candidate.SourcePath,
                    candidate.SourceKind == AirAppCatalogSourceKind.Package,
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
                    "AirAppRuntime",
                    $"Preparing shared contracts. AirAppId='{candidate.Manifest.Id}'; SourcePath='{candidate.SourcePath}'; SourceKind='{candidate.SourceKind}'.");
                RegisterSharedContractsForLoad(candidate.Manifest);
                AppLogger.Info(
                    "AirAppRuntime",
                    $"Shared contracts ready. AirAppId='{candidate.Manifest.Id}'; SourcePath='{candidate.SourcePath}'.");
            }
            catch (Exception ex)
            {
                var dependencyFailure = AirAppLoadResult.Failure(candidate.SourcePath, candidate.Manifest, ex);
                _loadResults.Add(dependencyFailure);
                _catalog.Add(new AirAppCatalogEntry(
                    candidate.Manifest,
                    candidate.SourcePath,
                    candidate.SourceKind == AirAppCatalogSourceKind.Package,
                    true,
                    false,
                    ex.Message,
                    0,
                    0));
                LogAirAppFailure("DependencyPrepare", dependencyFailure, treatAsError: false);
                continue;
            }

            AppLogger.Info(
                "AirAppRuntime",
                $"Starting plugin load. AirAppId='{candidate.Manifest.Id}'; SourcePath='{candidate.SourcePath}'; SourceKind='{candidate.SourceKind}'.");
            var loadResult = candidate.SourceKind switch
            {
                AirAppCatalogSourceKind.Package => _loader.LoadFromPackage(
                    candidate.SourcePath,
                    AirAppsDirectory,
                    services: _hostServices,
                    hostProperties),
                AirAppCatalogSourceKind.DevAirApp => _loader.LoadFromManifest(
                    candidate.SourcePath,
                    services: _hostServices,
                    hostProperties),
                _ => _loader.LoadFromManifest(
                    candidate.SourcePath,
                    services: _hostServices,
                    hostProperties)
            };

            _loadResults.Add(loadResult);

            if (loadResult.IsSuccess && loadResult.LoadedAirApp is not null)
            {
                _loadedAirApps.Add(loadResult.LoadedAirApp);
                CollectContributions(loadResult.LoadedAirApp);
                _catalog.Add(new AirAppCatalogEntry(
                    loadResult.LoadedAirApp.Manifest,
                    loadResult.SourcePath,
                    candidate.SourceKind == AirAppCatalogSourceKind.Package,
                    true,
                    true,
                    null,
                    loadResult.LoadedAirApp.SettingsSections.Count,
                    loadResult.LoadedAirApp.DesktopComponents.Count,
                    IsDevAirApp: isDevAirApp));
                AppLogger.Info(
                    "AirAppRuntime",
                    $"AirApp loaded. AirAppId='{loadResult.LoadedAirApp.Manifest.Id}'; SourcePath='{loadResult.SourcePath}'; ManifestVersion='{loadResult.LoadedAirApp.Manifest.Version ?? "<unknown>"}'; ApiVersion='{loadResult.LoadedAirApp.Manifest.ApiVersion ?? "<unknown>"}'; SourceKind='{candidate.SourceKind}'; SettingsSections={loadResult.LoadedAirApp.SettingsSections.Count}; Widgets={loadResult.LoadedAirApp.DesktopComponents.Count}; Editors={loadResult.LoadedAirApp.DesktopComponentEditors.Count}.");
                Debug.WriteLine($"[AirAppRuntime] Loaded '{loadResult.Manifest?.Id}' from '{loadResult.SourcePath}'.");
                continue;
            }

            _catalog.Add(new AirAppCatalogEntry(
                candidate.Manifest,
                candidate.SourcePath,
                candidate.SourceKind == AirAppCatalogSourceKind.Package,
                true,
                false,
                loadResult.Error?.Message,
                0,
                0,
                IsDevAirApp: isDevAirApp));
            LogAirAppFailure("Load", loadResult, treatAsError: true);
            Debug.WriteLine($"[AirAppRuntime] Failed to load plugin from '{loadResult.SourcePath}': {loadResult.Error}");
        }

        if (_catalog.Count == 0 && discoveryFailures.Count == 0)
        {
            AppLogger.Info(
                "AirAppRuntime",
                $"No plugin packages or loose manifests were discovered under '{AirAppsDirectory}'.");
            Debug.WriteLine($"[AirAppRuntime] No .laapp packages or loose plugin manifests found under '{AirAppsDirectory}'.");
        }
    }

    public bool SetAirAppEnabled(string pluginId, bool isEnabled)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
        {
            return false;
        }

        var catalogEntry = _catalog.FirstOrDefault(entry =>
            string.Equals(entry.Manifest.Id, pluginId, StringComparison.OrdinalIgnoreCase));
        if (catalogEntry.IsDevAirApp && !isEnabled)
        {
            AppLogger.Warn("DevAirApp", $"Cannot disable developer plugin '{pluginId}'. Developer plugins are always enabled in dev mode.");
            return false;
        }

        var snapshot = LoadAppSettingsSnapshot();
        var disabledAirAppIds = snapshot.DisabledPluginIds is { Count: > 0 }
            ? new HashSet<string>(snapshot.DisabledPluginIds, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var changed = isEnabled
            ? disabledAirAppIds.Remove(pluginId)
            : disabledAirAppIds.Add(pluginId);

        if (!changed)
        {
            return false;
        }

        snapshot.DisabledPluginIds = disabledAirAppIds
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        SaveAppSettingsSnapshot(snapshot);
        PendingRestartStateService.SetPending(PendingRestartStateService.AirAppCatalogReason, true);

        for (var i = 0; i < _catalog.Count; i++)
        {
            if (string.Equals(_catalog[i].Manifest.Id, pluginId, StringComparison.OrdinalIgnoreCase))
            {
                _catalog[i] = _catalog[i] with { IsEnabled = isEnabled };
            }
        }

        return true;
    }

    public AirAppManifest InstallAirAppPackage(string packagePath)
    {
        lock (_packageMutationGate)
        {
            return InstallAirAppPackageCore(packagePath).Manifest;
        }
    }

    public AirAppManifest RegisterInstalledAirAppPackage(string packagePath)
    {
        lock (_packageMutationGate)
        {
            return RegisterInstalledAirAppPackageCore(packagePath);
        }
    }

    public bool DeleteInstalledAirApp(string pluginId)
    {
        lock (_packageMutationGate)
        {
            return DeleteInstalledAirAppCore(pluginId);
        }
    }

    private bool DeleteInstalledAirAppCore(string pluginId)
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

        var targetPath = ResolveAirAppRemovalTargetPath(entry);
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return false;
        }

        var fullTargetPath = Path.GetFullPath(targetPath);
        if (!TryDeleteAirAppTarget(fullTargetPath))
        {
            RegisterPendingAirAppDeletion(fullTargetPath);
        }

        RemoveAirAppFromSnapshot(pluginId);
        RemoveAirAppFromCatalog(pluginId);
        PendingRestartStateService.SetPending(PendingRestartStateService.AirAppCatalogReason, true);
        return true;
    }

    internal IReadOnlyList<AirAppInstalledInfo> GetInstalledAirAppsSnapshot()
    {
        return _catalog
            .OrderBy(entry => entry.Manifest.Name, StringComparer.OrdinalIgnoreCase)
            .Select(entry => new AirAppInstalledInfo(
                entry.Manifest,
                entry.IsEnabled,
                entry.IsLoaded,
                entry.IsPackage,
                entry.ErrorMessage))
            .ToArray();
    }

    private AirAppPackageInstallResult InstallAirAppPackageCore(string packagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);

        var fullPackagePath = Path.GetFullPath(packagePath);
        if (!File.Exists(fullPackagePath))
        {
            throw new FileNotFoundException($"AirApp package '{fullPackagePath}' was not found.", fullPackagePath);
        }

        if (!string.Equals(Path.GetExtension(fullPackagePath), AirAppSdkInfo.PackageFileExtension, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"AirApp package must use the '{AirAppSdkInfo.PackageFileExtension}' extension.");
        }

        Directory.CreateDirectory(AirAppsDirectory);

        var manifest = ReadManifestFromPackage(fullPackagePath);
        _sharedContractManager.EnsureInstalled(manifest);
        AppLogger.Info(
            "AirAppRuntime",
            $"Installing package. AirAppId='{manifest.Id}'; Source='{fullPackagePath}'; AirAppsDirectory='{AirAppsDirectory}'.");

        var destinationPath = Path.Combine(AirAppsDirectory, BuildInstalledPackageFileName(manifest.Id));
        if (!AirAppInstallTargetAccess.CanWriteDirectory(AirAppsDirectory))
        {
            return InstallAirAppPackageWithElevation(fullPackagePath, manifest, destinationPath);
        }

        var replacedExisting = RemoveExistingAirAppPackages(manifest.Id, fullPackagePath);

        if (!string.Equals(fullPackagePath, Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase))
        {
            FileOperationRetryHelper.CopyWithRetry(fullPackagePath, destinationPath, overwrite: true, "AirAppRuntime");
        }

        UpdateCatalogAfterPackageInstall(manifest, destinationPath);
        PendingRestartStateService.SetPending(PendingRestartStateService.AirAppCatalogReason, true);
        AppLogger.Info(
            "AirAppRuntime",
            $"Package staged. AirAppId='{manifest.Id}'; Destination='{destinationPath}'; ReplacedExisting={replacedExisting}.");

        return new AirAppPackageInstallResult(manifest, replacedExisting, RestartRequired: true);
    }

    private AirAppPackageInstallResult InstallAirAppPackageWithElevation(
        string fullPackagePath,
        AirAppManifest manifest,
        string destinationPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new UnauthorizedAccessException(
                $"AirApp directory '{AirAppsDirectory}' is not writable by the current process.");
        }

        var elevatedResult = new ElevatedAirAppInstallService()
            .InstallAsync(fullPackagePath, AirAppsDirectory, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        if (!elevatedResult.Success)
        {
            throw new UnauthorizedAccessException(
                elevatedResult.ErrorMessage ??
                elevatedResult.Message ??
                $"Elevated plugin install failed with code '{elevatedResult.Code ?? "unknown"}'.");
        }

        var installedPath = !string.IsNullOrWhiteSpace(elevatedResult.InstalledPackagePath)
            ? elevatedResult.InstalledPackagePath
            : destinationPath;
        UpdateCatalogAfterPackageInstall(manifest, installedPath);
        PendingRestartStateService.SetPending(PendingRestartStateService.AirAppCatalogReason, true);
        AppLogger.Info(
            "AirAppRuntime",
            $"Package staged through elevated installer. AirAppId='{manifest.Id}'; Destination='{installedPath}'.");

        return new AirAppPackageInstallResult(manifest, ReplacedExisting: false, RestartRequired: true);
    }

    private AirAppManifest RegisterInstalledAirAppPackageCore(string packagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);

        var fullPackagePath = Path.GetFullPath(packagePath);
        if (!File.Exists(fullPackagePath))
        {
            throw new FileNotFoundException($"AirApp package '{fullPackagePath}' was not found.", fullPackagePath);
        }

        var manifest = ReadManifestFromPackage(fullPackagePath);
        _sharedContractManager.EnsureInstalled(manifest);
        AppLogger.Info(
            "AirAppRuntime",
            $"Registering externally installed package. AirAppId='{manifest.Id}'; Source='{fullPackagePath}'.");
        UpdateCatalogAfterPackageInstall(manifest, fullPackagePath);
        PendingRestartStateService.SetPending(PendingRestartStateService.AirAppCatalogReason, true);
        return manifest;
    }

    public void Dispose()
    {
        _materialColorService.MaterialColorChanged -= OnMaterialColorChanged;
        UnloadInstalledAirApps();
        _sharedContractManager.Dispose();
        if (_settingsFacade is IDisposable disposable && !ReferenceEquals(_settingsFacade, HostSettingsFacadeProvider.GetOrCreate()))
        {
            disposable.Dispose();
        }
    }

    private void OnMaterialColorChanged(object? sender, MaterialColorSnapshot snapshot)
    {
        _ = sender;

        var pluginSnapshot = AirAppAppearanceSnapshotMapper.FromMaterialColorSnapshot(snapshot);
        var changedProperties = new[]
        {
            AppearanceProperty.ThemeVariant,
            AppearanceProperty.AccentColor,
            AppearanceProperty.Wallpaper,
            AppearanceProperty.SystemMaterialMode,
            AppearanceProperty.ColorSource,
            AppearanceProperty.ColorRoles,
            AppearanceProperty.MaterialSurfaces,
            AppearanceProperty.WallpaperSeedCandidates
        };

        foreach (var loadedAirApp in _loadedAirApps)
        {
            if (loadedAirApp.RuntimeContext.Appearance is AirAppAppearanceContext appearanceContext)
            {
                appearanceContext.UpdateSnapshot(pluginSnapshot, changedProperties);
            }
        }
    }

    private void UnloadInstalledAirApps()
    {
        for (var i = _loadedAirApps.Count - 1; i >= 0; i--)
        {
            var pluginId = _loadedAirApps[i].Manifest.Id;
            _exportRegistry.RemoveExports(pluginId);
            _settingsCatalogService.RemoveAirAppSections(pluginId);
            _loadedAirApps[i].Dispose();
        }

        _loadedAirApps.Clear();
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

    private IReadOnlyList<AirAppCandidate> DiscoverCandidates(List<AirAppLoadResult> failures)
    {
        var candidates = new List<AirAppCandidate>();

        foreach (var packagePath in EnumerateCandidatePaths($"*{AirAppSdkInfo.PackageFileExtension}"))
        {
            try
            {
                var manifest = ReadManifestFromPackage(packagePath);
                candidates.Add(new AirAppCandidate(packagePath, manifest, AirAppCatalogSourceKind.Package));
            }
            catch (Exception ex)
            {
                var failure = AirAppLoadResult.Failure(packagePath, null, ex);
                failures.Add(failure);
                LogAirAppFailure("ManifestValidation", failure, treatAsError: false);
            }
        }

        foreach (var manifestPath in EnumerateCandidatePaths(AirAppSdkInfo.ManifestFileName))
        {
            try
            {
                var manifest = AirAppManifest.Load(manifestPath);
                candidates.Add(new AirAppCandidate(manifestPath, manifest, AirAppCatalogSourceKind.Manifest));
            }
            catch (Exception ex)
            {
                var failure = AirAppLoadResult.Failure(manifestPath, null, ex);
                failures.Add(failure);
                LogAirAppFailure("ManifestValidation", failure, treatAsError: false);
            }
        }

        DiscoverDevAirAppCandidates(candidates, failures);

        return candidates
            .OrderByDescending(candidate => candidate.SourceKind)
            .ThenBy(candidate => candidate.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void DiscoverDevAirAppCandidates(List<AirAppCandidate> candidates, List<AirAppLoadResult> failures)
    {
        var devOptions = DevAirAppOptions.Current;
        if (!devOptions.IsDevMode || devOptions.DevPluginPaths.Count == 0)
        {
            return;
        }

        AppLogger.Info("DevAirApp", $"Scanning developer plugin paths. Count={devOptions.DevPluginPaths.Count}.");

        foreach (var devPath in devOptions.DevPluginPaths)
        {
            if (File.Exists(devPath) && string.Equals(Path.GetExtension(devPath), AirAppSdkInfo.PackageFileExtension, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var manifest = ReadManifestFromPackage(devPath);
                    candidates.Add(new AirAppCandidate(devPath, manifest, AirAppCatalogSourceKind.DevAirApp));
                    AppLogger.Info("DevAirApp", $"Found developer plugin package. AirAppId='{manifest.Id}'; Path='{devPath}'.");
                }
                catch (Exception ex)
                {
                    var failure = AirAppLoadResult.Failure(devPath, null, ex);
                    failures.Add(failure);
                    AppLogger.Warn("DevAirApp", $"Failed to read developer plugin package '{devPath}'.", ex);
                }

                continue;
            }

            if (Directory.Exists(devPath))
            {
                var manifestPath = Path.Combine(devPath, AirAppSdkInfo.ManifestFileName);
                if (File.Exists(manifestPath))
                {
                    try
                    {
                        var manifest = AirAppManifest.Load(manifestPath);
                        candidates.Add(new AirAppCandidate(manifestPath, manifest, AirAppCatalogSourceKind.DevAirApp));
                        AppLogger.Info("DevAirApp", $"Found developer plugin manifest. AirAppId='{manifest.Id}'; Path='{manifestPath}'.");
                    }
                    catch (Exception ex)
                    {
                        var failure = AirAppLoadResult.Failure(manifestPath, null, ex);
                        failures.Add(failure);
                        AppLogger.Warn("DevAirApp", $"Failed to load developer plugin manifest '{manifestPath}'.", ex);
                    }
                }
                else
                {
                    AppLogger.Warn("DevAirApp", $"Developer plugin directory '{devPath}' does not contain '{AirAppSdkInfo.ManifestFileName}'. Skipping.");
                }

                continue;
            }

            AppLogger.Warn("DevAirApp", $"Developer plugin path '{devPath}' is neither a file nor a directory. Skipping.");
        }
    }

    private IEnumerable<string> EnumerateCandidatePaths(string searchPattern)
    {
        var runtimeRootDirectory = EnsureTrailingSeparator(Path.Combine(Path.GetFullPath(AirAppsDirectory), ".runtime"));

        return Directory
            .EnumerateFiles(AirAppsDirectory, searchPattern, SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .Where(path => !path.StartsWith(runtimeRootDirectory, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
    }

    private static AirAppManifest ReadManifestFromPackage(string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var entries = archive.Entries
            .Where(entry => string.Equals(entry.Name, AirAppSdkInfo.ManifestFileName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (entries.Length == 0)
        {
            throw new InvalidOperationException($"AirApp package '{packagePath}' does not contain '{AirAppSdkInfo.ManifestFileName}'.");
        }

        if (entries.Length > 1)
        {
            throw new InvalidOperationException($"AirApp package '{packagePath}' contains multiple '{AirAppSdkInfo.ManifestFileName}' files.");
        }

        using var stream = entries[0].Open();
        return AirAppManifest.Load(stream, $"{packagePath}!/{entries[0].FullName}");
    }

    private bool RemoveExistingAirAppPackages(string pluginId, string packagePathToKeep)
    {
        var replacedExisting = false;
        foreach (var existingPackagePath in EnumerateCandidatePaths($"*{AirAppSdkInfo.PackageFileExtension}"))
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

                FileOperationRetryHelper.DeleteFileWithRetry(existingPackagePath, "AirAppRuntime");
                replacedExisting = true;
            }
            catch
            {
                // Ignore unrelated or invalid packages during replacement.
            }
        }

        return replacedExisting;
    }

    private void UpdateCatalogAfterPackageInstall(AirAppManifest manifest, string destinationPath)
    {
        var isEnabled = !GetDisabledPluginIds().Contains(manifest.Id);
        var entry = new AirAppCatalogEntry(
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
        return fileName + AirAppSdkInfo.PackageFileExtension;
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static AirAppLoaderOptions CreateOptions()
    {
        var devOptions = DevAirAppOptions.Current;
        var options = new AirAppLoaderOptions { IsDevMode = devOptions.IsDevMode };
        AddSharedAssembly(options, typeof(App).Assembly);
        AddSharedAssembly(options, typeof(IServiceCollection).Assembly);
        AddSharedAssembly(options, typeof(HostBuilderContext).Assembly);
        AddSharedAssembly(options, typeof(IExternalIpcNotificationPublisher).Assembly);
        AddSharedAssembly(options, typeof(dotnetCampus.Ipc.Pipes.IpcProvider).Assembly);

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
                string.Equals(assemblyName, "Material.Avalonia", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(assemblyName, "Material.Icons.Avalonia", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(assemblyName, "MicroCom.Runtime", StringComparison.OrdinalIgnoreCase))
            {
                AddSharedAssembly(options, assembly);
            }
        }

        return options;
    }

    private static void AddSharedAssembly(AirAppLoaderOptions options, Assembly assembly)
    {
        var assemblyName = assembly.GetName().Name;
        if (!string.IsNullOrWhiteSpace(assemblyName))
        {
            options.SharedAssemblyNames.Add(assemblyName);
        }
    }

    private void MergeDevSettingsFromSnapshot()
    {
        var devOptions = DevAirAppOptions.Current;

        try
        {
            var snapshot = LoadAppSettingsSnapshot();

            if (snapshot.IsDevModeEnabled && !devOptions.IsDevMode)
            {
                devOptions.ApplySettingsFromSnapshot(isDevMode: true, devAirAppPath: snapshot.DevPluginPath);
                AppLogger.Info("DevAirApp", $"Developer mode enabled via settings. DevPluginPath='{snapshot.DevPluginPath}'.");
            }
            else if (!string.IsNullOrWhiteSpace(snapshot.DevPluginPath) && string.IsNullOrWhiteSpace(devOptions.DevPluginPath))
            {
                devOptions.ApplySettingsFromSnapshot(isDevMode: devOptions.IsDevMode, devAirAppPath: snapshot.DevPluginPath);
                AppLogger.Info("DevAirApp", $"Developer plugin path merged from settings. DevPluginPath='{snapshot.DevPluginPath}'.");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("DevAirApp", "Failed to merge developer settings from snapshot.", ex);
        }
    }

    private void CollectContributions(LoadedAirApp loadedAirApp)
    {
        _exportRegistry.ReplaceExports(loadedAirApp.Manifest.Id, loadedAirApp.ExportedServices);

        _settingsCatalogService.RegisterAirAppSections(loadedAirApp.Manifest.Id, loadedAirApp.SettingsSections);

        _settingsSections.RemoveAll(entry => string.Equals(
            entry.AirApp.Manifest.Id,
            loadedAirApp.Manifest.Id,
            StringComparison.OrdinalIgnoreCase));
        _desktopComponentEditors.RemoveAll(entry => string.Equals(
            entry.AirApp.Manifest.Id,
            loadedAirApp.Manifest.Id,
            StringComparison.OrdinalIgnoreCase));

        foreach (var settingsSection in loadedAirApp.SettingsSections)
        {
            _settingsSections.Add(new AirAppSettingsSectionContribution(loadedAirApp, settingsSection));
        }

        foreach (var desktopComponent in loadedAirApp.DesktopComponents)
        {
            _desktopComponents.Add(new AirAppDesktopComponentContribution(loadedAirApp, desktopComponent));
        }

        foreach (var desktopComponentEditor in loadedAirApp.DesktopComponentEditors)
        {
            _desktopComponentEditors.Add(new AirAppDesktopComponentEditorContribution(loadedAirApp, desktopComponentEditor));
        }

        if (_publicIpcHostService is not null)
        {
            foreach (var publicIpcService in loadedAirApp.PublicIpcServices)
            {
                _publicIpcHostService.RegisterPublicService(
                    publicIpcService.ContractType,
                    publicIpcService.Implementation,
                    publicIpcService.ObjectId,
                    loadedAirApp.Manifest.Id,
                    publicIpcService.NotifyIds);
            }
        }
    }

    private void RegisterSharedContractsForLoad(AirAppManifest manifest)
    {
        foreach (var assemblyName in _sharedContractManager.PrepareForLoad(manifest))
        {
            if (!string.IsNullOrWhiteSpace(assemblyName))
            {
                _loaderOptions.SharedAssemblyNames.Add(assemblyName);
            }
        }
    }

    private void ApplyPendingAirAppDeletions()
    {
        var pendingPaths = ReadPendingAirAppDeletions();
        var remainingPaths = new List<string>();
        foreach (var path in pendingPaths)
        {
            if (!TryDeleteAirAppTarget(path))
            {
                remainingPaths.Add(path);
            }
        }

        SavePendingAirAppDeletions(remainingPaths);
        CleanupPendingDeletionDirectory();
    }

    private void ApplyPendingPluginOperations()
    {
        var pendingService = new PendingAirAppUpgradeService(AirAppsDirectory);
        var result = pendingService.ApplyPendingOperations(manifest => _sharedContractManager.EnsureInstalled(manifest));
        if (result.SuccessCount == 0 && result.FailureCount == 0)
        {
            return;
        }

        AppLogger.Info(
            "AirAppRuntime",
            $"Pending plugin operations applied before discovery. Success={result.SuccessCount}; Failure={result.FailureCount}; AirAppsDirectory='{AirAppsDirectory}'.");

        foreach (var failure in result.Failures)
        {
            AppLogger.Warn(
                "AirAppRuntime",
                $"Pending plugin operation failed and will remain queued. AirAppId='{failure.PluginId}'; Operation='{failure.Operation}'; Error='{failure.ErrorMessage}'.");
        }
    }

    private void CleanupPendingDeletionDirectory()
    {
        var pendingDeletionDir = Path.Combine(AirAppsDirectory, ".pending-deletions");
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

    private string ResolveAirAppRemovalTargetPath(AirAppCatalogEntry entry)
    {
        if (entry.IsPackage)
        {
            return entry.SourcePath;
        }

        var fullSourcePath = Path.GetFullPath(entry.SourcePath);
        if (File.Exists(fullSourcePath) &&
            string.Equals(Path.GetFileName(fullSourcePath), AirAppSdkInfo.ManifestFileName, StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetDirectoryName(fullSourcePath) ?? fullSourcePath;
        }

        return fullSourcePath;
    }

    private static bool TryDeleteAirAppTarget(string targetPath)
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

    private void RegisterPendingAirAppDeletion(string targetPath)
    {
        var pendingPaths = ReadPendingAirAppDeletions();
        if (pendingPaths.Contains(targetPath, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        pendingPaths.Add(targetPath);
        SavePendingAirAppDeletions(pendingPaths);
    }

    private List<string> ReadPendingAirAppDeletions()
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

    private void SavePendingAirAppDeletions(IEnumerable<string> pendingPaths)
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

        Directory.CreateDirectory(AirAppsDirectory);
        var json = JsonSerializer.Serialize(normalizedPaths, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(pendingDeletionFilePath, json);
    }

    private string GetPendingDeletionFilePath()
    {
        return Path.Combine(AirAppsDirectory, PendingDeletionFileName);
    }

    private static void LogAirAppFailure(string stage, AirAppLoadResult result, bool treatAsError)
    {
        var manifest = result.Manifest;
        var message =
            $"AirApp load issue. Stage='{stage}'; AirAppId='{manifest?.Id ?? "<unknown>"}'; SourcePath='{result.SourcePath}'; ManifestVersion='{manifest?.Version ?? "<unknown>"}'; ApiVersion='{manifest?.ApiVersion ?? "<unknown>"}'; Error='{result.Error?.Message ?? "<none>"}'.";

        if (treatAsError)
        {
            AppLogger.Error("AirAppRuntime", message, result.Error);
            return;
        }

        AppLogger.Warn("AirAppRuntime", message, result.Error);
    }

    private void RemoveAirAppFromSnapshot(string pluginId)
    {
        var snapshot = LoadAppSettingsSnapshot();
        if (snapshot.DisabledPluginIds.RemoveAll(id => string.Equals(id, pluginId, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            SaveAppSettingsSnapshot(snapshot);
        }
    }

    private AppSettingsSnapshot LoadAppSettingsSnapshot()
    {
        return _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(AirAppSettingsScope.App);
    }

    private void SaveAppSettingsSnapshot(AppSettingsSnapshot snapshot)
    {
        _settingsFacade.Settings.SaveSnapshot(AirAppSettingsScope.App, snapshot);
    }

    private void RemoveAirAppFromCatalog(string pluginId)
    {
        _catalog.RemoveAll(entry => string.Equals(entry.Manifest.Id, pluginId, StringComparison.OrdinalIgnoreCase));
        _settingsSections.RemoveAll(entry => string.Equals(entry.AirApp.Manifest.Id, pluginId, StringComparison.OrdinalIgnoreCase));
        _desktopComponents.RemoveAll(entry => string.Equals(entry.AirApp.Manifest.Id, pluginId, StringComparison.OrdinalIgnoreCase));
        _loadResults.RemoveAll(entry => string.Equals(entry.Manifest?.Id, pluginId, StringComparison.OrdinalIgnoreCase));
        _settingsCatalogService.RemoveAirAppSections(pluginId);
    }

    private enum AirAppCatalogSourceKind
    {
        Package = 0,
        Manifest = 1,
        DevAirApp = 2
    }

    private sealed record AirAppCandidate(
        string SourcePath,
        AirAppManifest Manifest,
        AirAppCatalogSourceKind SourceKind);

    private sealed class AirAppHostServiceProvider : IServiceProvider
    {
        private readonly IAirAppPackageManager _packageManager;
        private readonly IHostApplicationLifecycle _applicationLifecycle;
        private readonly IAirAppExportRegistry _exportRegistry;
        private readonly ISettingsFacadeService _settingsFacade;
        private readonly ISettingsService _settingsService;
        private readonly ISettingsCatalog _settingsCatalog;
        private readonly IAppearanceThemeService _appearanceThemeService;
        private readonly IMaterialColorService _materialColorService;
        private readonly IExternalIpcNotificationPublisher? _externalIpcNotificationPublisher;

        public AirAppHostServiceProvider(
            IAirAppPackageManager packageManager,
            IHostApplicationLifecycle applicationLifecycle,
            IAirAppExportRegistry exportRegistry,
            ISettingsFacadeService settingsFacade,
            ISettingsService settingsService,
            ISettingsCatalog settingsCatalog,
            IExternalIpcNotificationPublisher? externalIpcNotificationPublisher)
        {
            _packageManager = packageManager;
            _applicationLifecycle = applicationLifecycle;
            _exportRegistry = exportRegistry;
            _settingsFacade = settingsFacade;
            _settingsService = settingsService;
            _settingsCatalog = settingsCatalog;
            _appearanceThemeService = HostAppearanceThemeProvider.GetOrCreate();
            _materialColorService = HostMaterialColorProvider.GetOrCreate();
            _externalIpcNotificationPublisher = externalIpcNotificationPublisher;
        }

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IAirAppPackageManager))
            {
                return _packageManager;
            }

            if (serviceType == typeof(IHostApplicationLifecycle))
            {
                return _applicationLifecycle;
            }

            if (serviceType == typeof(IAirAppExportRegistry))
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
                // Compatibility-only. AirApp appearance snapshots are still sourced from the material pipeline.
                return _appearanceThemeService;
            }

            if (serviceType == typeof(IMaterialColorService))
            {
                return _materialColorService;
            }

            if (serviceType == typeof(IExternalIpcNotificationPublisher))
            {
                return _externalIpcNotificationPublisher;
            }

            return null;
        }
    }

    private sealed class AirAppRuntimePackageManager : IAirAppPackageManager
    {
        private readonly AirAppRuntimeService _runtimeService;

        public AirAppRuntimePackageManager(AirAppRuntimeService runtimeService)
        {
            _runtimeService = runtimeService;
        }

        public IReadOnlyList<AirAppInstalledInfo> GetInstalledAirApps()
        {
            return _runtimeService.GetInstalledAirAppsSnapshot();
        }

        public AirAppPackageInstallResult InstallPackage(string packagePath)
        {
            return _runtimeService.InstallAirAppPackageCore(packagePath);
        }
    }
}
