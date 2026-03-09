using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;

namespace LanMountainDesktop.Services;

public sealed class PluginRuntimeService : IDisposable
{
    private readonly PluginLoader _loader;
    private readonly AppSettingsService _appSettingsService = new();
    private readonly List<LoadedPlugin> _loadedPlugins = [];
    private readonly List<PluginLoadResult> _loadResults = [];
    private readonly List<PluginCatalogEntry> _catalog = [];
    private readonly List<PluginSettingsPageContribution> _settingsPages = [];
    private readonly List<PluginDesktopComponentContribution> _desktopComponents = [];

    public PluginRuntimeService()
    {
        PluginsDirectory = Path.Combine(AppContext.BaseDirectory, "Extensions", "Plugins");
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
        UnloadInstalledPlugins();

        var disabledPluginIds = GetDisabledPluginIds();
        var hostProperties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["HostApplicationName"] = "LanMountainDesktop",
            ["HostVersion"] = typeof(App).Assembly.GetName().Version?.ToString(),
            ["PluginSdkApiVersion"] = PluginSdkInfo.ApiVersion
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
                    services: null,
                    hostProperties),
                _ => _loader.LoadFromManifest(
                    candidate.SourcePath,
                    services: null,
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

        for (var i = 0; i < _catalog.Count; i++)
        {
            if (string.Equals(_catalog[i].Manifest.Id, pluginId, StringComparison.OrdinalIgnoreCase))
            {
                _catalog[i] = _catalog[i] with { IsEnabled = isEnabled };
            }
        }

        return true;
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

    private sealed record PluginCandidate(
        string SourcePath,
        PluginManifest Manifest,
        PluginCatalogSourceKind SourceKind);
}
