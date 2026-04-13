using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.PluginSdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LanMountainDesktop.Plugins;

public sealed class PluginLoader
{
    private readonly PluginLoaderOptions _options;

    public PluginLoader(PluginLoaderOptions? options = null)
    {
        _options = options ?? new PluginLoaderOptions();
    }

    public IReadOnlyList<PluginLoadResult> LoadAll(
        string pluginsRootDirectory,
        IServiceProvider? services = null,
        IReadOnlyDictionary<string, object?>? properties = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginsRootDirectory);

        if (!Directory.Exists(pluginsRootDirectory))
        {
            return Array.Empty<PluginLoadResult>();
        }

        var results = new List<PluginLoadResult>();
        var candidates = DiscoverCandidates(pluginsRootDirectory, results);
        var selectedPluginIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            if (!selectedPluginIds.Add(candidate.Manifest.Id))
            {
                results.Add(PluginLoadResult.Failure(
                    candidate.SourcePath,
                    candidate.Manifest,
                    new InvalidOperationException(
                        $"Duplicate plugin id '{candidate.Manifest.Id}' was found. Source '{candidate.SourcePath}' was ignored because a higher-priority source was already selected.")));
                continue;
            }

            results.Add(candidate.SourceKind switch
            {
                PluginSourceKind.Package => LoadFromPackage(
                    candidate.SourcePath,
                    pluginsRootDirectory,
                    candidate.Manifest,
                    services,
                    properties),
                _ => LoadFromManifest(
                    candidate.SourcePath,
                    candidate.Manifest,
                    services,
                    properties)
            });
        }

        return results;
    }

    public PluginLoadResult LoadFromManifest(
        string manifestPath,
        IServiceProvider? services = null,
        IReadOnlyDictionary<string, object?>? properties = null)
    {
        PluginManifest? manifest = null;

        try
        {
            manifest = PluginManifest.Load(manifestPath);
            return LoadFromManifest(manifestPath, manifest, services, properties);
        }
        catch (Exception ex)
        {
            return PluginLoadResult.Failure(Path.GetFullPath(manifestPath), manifest, ex);
        }
    }

    public PluginLoadResult LoadFromPackage(
        string packagePath,
        string pluginsRootDirectory,
        IServiceProvider? services = null,
        IReadOnlyDictionary<string, object?>? properties = null)
    {
        PluginManifest? manifest = null;

        try
        {
            manifest = ReadManifestFromPackage(packagePath);
            return LoadFromPackage(packagePath, pluginsRootDirectory, manifest, services, properties);
        }
        catch (Exception ex)
        {
            return PluginLoadResult.Failure(Path.GetFullPath(packagePath), manifest, ex);
        }
    }

    public PluginLoadResult LoadFromAssembly(
        string assemblyPath,
        PluginManifest manifest,
        IServiceProvider? services = null,
        IReadOnlyDictionary<string, object?>? properties = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        ArgumentNullException.ThrowIfNull(manifest);

        var fullAssemblyPath = Path.GetFullPath(assemblyPath);
        var pluginDirectory = Path.GetDirectoryName(fullAssemblyPath)
            ?? throw new InvalidOperationException($"Failed to determine the plugin directory of '{fullAssemblyPath}'.");
        var dataDirectory = Path.Combine(pluginDirectory, _options.DataDirectoryName);
        return LoadCore(fullAssemblyPath, fullAssemblyPath, pluginDirectory, dataDirectory, manifest, services, properties);
    }

    private PluginLoadResult LoadCore(
        string sourcePath,
        string assemblyPath,
        string pluginDirectory,
        string dataDirectory,
        PluginManifest manifest,
        IServiceProvider? services,
        IReadOnlyDictionary<string, object?>? properties)
    {
        PluginLoadContext? loadContext = null;
        IPlugin? plugin = null;
        PluginRuntimeContext? runtimeContext = null;
        ServiceProvider? pluginServices = null;
        IReadOnlyList<IHostedService> hostedServices = Array.Empty<IHostedService>();

        try
        {
            Directory.CreateDirectory(dataDirectory);
            ValidatePluginRuntimeAssets(manifest, assemblyPath, pluginDirectory, _options.IsDevMode);
            AppLogger.Info(
                "PluginLoader",
                $"LoadCore starting. PluginId='{manifest.Id}'; AssemblyPath='{assemblyPath}'; PluginDirectory='{pluginDirectory}'; DataDirectory='{dataDirectory}'.");

            loadContext = new PluginLoadContext(assemblyPath, _options.SharedAssemblyNames);
            var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
            AppLogger.Info("PluginLoader", $"Assembly loaded. PluginId='{manifest.Id}'; Assembly='{assembly.FullName}'.");
            var pluginType = ResolvePluginType(assembly);
            plugin = CreatePluginInstance(pluginType);
            AppLogger.Info("PluginLoader", $"Plugin instance created. PluginId='{manifest.Id}'; PluginType='{pluginType.FullName}'.");
            runtimeContext = CreateRuntimeContext(manifest, pluginDirectory, dataDirectory, properties, services);
            var serviceCollection = CreateServiceCollection(runtimeContext, services);
            var hostBuilderContext = CreateHostBuilderContext(runtimeContext);

            plugin.Initialize(hostBuilderContext, serviceCollection);
            AppLogger.Info("PluginLoader", $"Plugin Initialize completed. PluginId='{manifest.Id}'.");

            pluginServices = serviceCollection.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = false,
                ValidateOnBuild = true
            });
            AppLogger.Info("PluginLoader", $"Service provider built. PluginId='{manifest.Id}'.");
            runtimeContext.SetServices(pluginServices);

            var settingsSections = pluginServices
                .GetServices<PluginSettingsSectionRegistration>()
                .OrderBy(section => section.SortOrder)
                .ThenBy(section => section.TitleLocalizationKey, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var desktopComponents = pluginServices
                .GetServices<PluginDesktopComponentRegistration>()
                .OrderBy(component => component.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(component => component.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var desktopComponentEditors = pluginServices
                .GetServices<PluginDesktopComponentEditorRegistration>()
                .OrderBy(editor => editor.ComponentId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var exportedServices = ResolveExports(manifest, pluginServices);
            AppLogger.Info(
                "PluginLoader",
                $"Plugin contributions resolved. PluginId='{manifest.Id}'; SettingsSections={settingsSections.Length}; Widgets={desktopComponents.Length}; Editors={desktopComponentEditors.Length}; Exports={exportedServices.Count}."); 
            hostedServices = pluginServices.GetServices<IHostedService>().ToArray();
            StartHostedServices(hostedServices);
            AppLogger.Info("PluginLoader", $"Hosted services started. PluginId='{manifest.Id}'; HostedServices={hostedServices.Count}."); 

            var loadedPlugin = new LoadedPlugin(
                manifest,
                sourcePath,
                assemblyPath,
                assembly,
                plugin,
                runtimeContext,
                pluginServices,
                settingsSections,
                desktopComponents,
                desktopComponentEditors,
                exportedServices,
                hostedServices,
                loadContext);

            return PluginLoadResult.Success(sourcePath, manifest, loadedPlugin);
        }
        catch (Exception ex)
        {
            StopHostedServices(hostedServices);
            DisposeInstance(pluginServices);
            DisposeInstance(plugin);
            DisposeInstance(runtimeContext);
            loadContext?.Unload();
            return PluginLoadResult.Failure(sourcePath, manifest, ex);
        }
    }

    private PluginLoadResult LoadFromManifest(
        string manifestPath,
        PluginManifest manifest,
        IServiceProvider? services,
        IReadOnlyDictionary<string, object?>? properties)
    {
        try
        {
            var fullManifestPath = Path.GetFullPath(manifestPath);
            var assemblyPath = manifest.ResolveEntranceAssemblyPath(fullManifestPath);
            if (!File.Exists(assemblyPath))
            {
                throw new FileNotFoundException(
                    $"Plugin '{manifest.Id}' entrance assembly '{assemblyPath}' was not found.",
                    assemblyPath);
            }

            var pluginDirectory = Path.GetDirectoryName(assemblyPath)
                ?? throw new InvalidOperationException($"Failed to determine the plugin directory of '{assemblyPath}'.");
            var dataDirectory = Path.Combine(pluginDirectory, _options.DataDirectoryName);
            return LoadCore(fullManifestPath, assemblyPath, pluginDirectory, dataDirectory, manifest, services, properties);
        }
        catch (Exception ex)
        {
            return PluginLoadResult.Failure(Path.GetFullPath(manifestPath), manifest, ex);
        }
    }

    private PluginLoadResult LoadFromPackage(
        string packagePath,
        string pluginsRootDirectory,
        PluginManifest manifest,
        IServiceProvider? services,
        IReadOnlyDictionary<string, object?>? properties)
    {
        try
        {
            var fullPackagePath = Path.GetFullPath(packagePath);
            var extractionDirectory = ExtractPackage(fullPackagePath, pluginsRootDirectory);
            var extractedManifestPath = Path.Combine(extractionDirectory, _options.ManifestFileName);

            if (!File.Exists(extractedManifestPath))
            {
                throw new FileNotFoundException(
                    $"Plugin package '{fullPackagePath}' does not contain '{_options.ManifestFileName}'.",
                    extractedManifestPath);
            }

            var extractedManifest = PluginManifest.Load(extractedManifestPath);
            if (!string.Equals(extractedManifest.Id, manifest.Id, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Plugin package '{fullPackagePath}' manifest id changed after extraction. Expected '{manifest.Id}', actual '{extractedManifest.Id}'.");
            }

            var assemblyPath = extractedManifest.ResolveEntranceAssemblyPath(extractedManifestPath);
            if (!File.Exists(assemblyPath))
            {
                throw new FileNotFoundException(
                    $"Plugin '{extractedManifest.Id}' entrance assembly '{assemblyPath}' was not found after package extraction.",
                    assemblyPath);
            }

            var dataDirectory = GetPackagedDataDirectory(pluginsRootDirectory, extractedManifest);
            return LoadCore(fullPackagePath, assemblyPath, extractionDirectory, dataDirectory, extractedManifest, services, properties);
        }
        catch (Exception ex)
        {
            return PluginLoadResult.Failure(Path.GetFullPath(packagePath), manifest, ex);
        }
    }

    private PluginRuntimeContext CreateRuntimeContext(
        PluginManifest manifest,
        string pluginDirectory,
        string dataDirectory,
        IReadOnlyDictionary<string, object?>? properties,
        IServiceProvider? hostServices)
    {
        return new PluginRuntimeContext(
            manifest,
            pluginDirectory,
            dataDirectory,
            CreateReadOnlyProperties(properties),
            BuildAppearanceSnapshot(hostServices));
    }

    private ServiceCollection CreateServiceCollection(
        PluginRuntimeContext runtimeContext,
        IServiceProvider? hostServices)
    {
        var services = new ServiceCollection();
        services.AddSingleton(runtimeContext);
        services.AddSingleton<IPluginRuntimeContext>(runtimeContext);
        services.AddSingleton<IPluginAppearanceContext>(runtimeContext.Appearance);
        services.AddSingleton(runtimeContext.Manifest);
        services.AddSingleton<IReadOnlyDictionary<string, object?>>(runtimeContext.Properties);
        services.AddSingleton<IPluginMessageBus, PluginMessageBus>();
        services.AddSingleton<IPluginSettingsService>(provider =>
            new PluginScopedSettingsService(
                runtimeContext.Manifest.Id,
                provider.GetRequiredService<ISettingsService>()));

        RegisterHostService<IPluginPackageManager>(services, hostServices);
        RegisterHostService<IHostApplicationLifecycle>(services, hostServices);
        RegisterHostService<IPluginExportRegistry>(services, hostServices);
        RegisterHostService<ISettingsFacadeService>(services, hostServices);
        RegisterHostService<ISettingsService>(services, hostServices);
        RegisterHostService<ISettingsCatalog>(services, hostServices);
        RegisterHostService<IAppearanceThemeService>(services, hostServices);

        return services;
    }

    private static PluginAppearanceSnapshot BuildAppearanceSnapshot(IServiceProvider? hostServices)
    {
        var defaultSnapshot = new PluginAppearanceSnapshot(
            CornerRadiusTokens: new PluginCornerRadiusTokens(6, 12, 14, 20, 28, 32, 36, 24),
            ThemeVariant: "Unknown");

        if (hostServices?.GetService(typeof(IAppearanceThemeService)) is not IAppearanceThemeService appearanceThemeService)
        {
            return defaultSnapshot;
        }

        try
        {
            var hostSnapshot = appearanceThemeService.GetCurrent();
            return new PluginAppearanceSnapshot(
                CornerRadiusTokens: PluginCornerRadiusTokens.FromShared(hostSnapshot.CornerRadiusTokens),
                ThemeVariant: hostSnapshot.IsNightMode ? "Dark" : "Light");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("PluginLoader", "Failed to resolve host appearance snapshot for plugin runtime context.", ex);
            return defaultSnapshot;
        }
    }

    private static void RegisterHostService<TService>(IServiceCollection services, IServiceProvider? hostServices)
        where TService : class
    {
        if (hostServices?.GetService(typeof(TService)) is TService service)
        {
            services.AddSingleton(service);
        }
    }

    private static HostBuilderContext CreateHostBuilderContext(PluginRuntimeContext runtimeContext)
    {
        var hostBuilderContext = new HostBuilderContext(new Dictionary<object, object>());
        hostBuilderContext.Properties["LanMountainDesktop.PluginManifest"] = runtimeContext.Manifest;
        hostBuilderContext.Properties["LanMountainDesktop.PluginDirectory"] = runtimeContext.PluginDirectory;
        hostBuilderContext.Properties["LanMountainDesktop.PluginDataDirectory"] = runtimeContext.DataDirectory;
        hostBuilderContext.Properties["LanMountainDesktop.PluginRuntimeContext"] = runtimeContext;

        foreach (var pair in runtimeContext.Properties)
        {
            if (pair.Value is not null)
            {
                hostBuilderContext.Properties[pair.Key] = pair.Value;
            }
        }

        return hostBuilderContext;
    }

    private static IReadOnlyList<PluginServiceExportDescriptor> ResolveExports(
        PluginManifest manifest,
        IServiceProvider services)
    {
        return services
            .GetServices<PluginServiceExportRegistration>()
            .Select(registration =>
            {
                if (!IsSupportedExportContract(manifest, registration.ContractType))
                {
                    throw new InvalidOperationException(
                        $"Plugin '{manifest.Id}' exported contract '{registration.ContractType.FullName}', but export contracts must come from LanMountainDesktop.PluginSdk or a manifest-declared shared contract assembly.");
                }

                return new PluginServiceExportDescriptor(
                    manifest.Id,
                    registration.ContractType,
                    services.GetService(registration.ContractType)
                        ?? throw new InvalidOperationException(
                            $"Plugin '{manifest.Id}' exported contract '{registration.ContractType.FullName}', but no singleton service instance was registered."));
            })
            .ToArray();
    }

    private static bool IsSupportedExportContract(PluginManifest manifest, Type contractType)
    {
        if (contractType.Assembly == typeof(IPlugin).Assembly)
        {
            return true;
        }

        var assemblyFileName = contractType.Assembly.GetName().Name + ".dll";
        return manifest.SharedContracts?.Any(contract =>
            string.Equals(contract.AssemblyName, assemblyFileName, StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static void StartHostedServices(IEnumerable<IHostedService> hostedServices)
    {
        foreach (var hostedService in hostedServices)
        {
            AppLogger.Info("PluginLoader", $"Starting hosted service '{hostedService.GetType().FullName}'.");
            hostedService.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
    }

    private static void StopHostedServices(IEnumerable<IHostedService> hostedServices)
    {
        foreach (var hostedService in hostedServices.Reverse())
        {
            try
            {
                hostedService.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            catch
            {
                // Ignore best-effort shutdown during failed startup.
            }
        }
    }

    private IReadOnlyList<PluginCandidate> DiscoverCandidates(
        string pluginsRootDirectory,
        List<PluginLoadResult> preparationFailures)
    {
        var candidates = new List<PluginCandidate>();

        foreach (var packagePath in EnumerateCandidatePaths(
                     pluginsRootDirectory,
                     "*" + NormalizePackageExtension(_options.PackageFileExtension)))
        {
            try
            {
                var manifest = ReadManifestFromPackage(packagePath);
                candidates.Add(new PluginCandidate(Path.GetFullPath(packagePath), manifest, PluginSourceKind.Package));
            }
            catch (Exception ex)
            {
                preparationFailures.Add(PluginLoadResult.Failure(Path.GetFullPath(packagePath), null, ex));
            }
        }

        foreach (var manifestPath in EnumerateCandidatePaths(pluginsRootDirectory, _options.ManifestFileName))
        {
            try
            {
                var manifest = PluginManifest.Load(manifestPath);
                candidates.Add(new PluginCandidate(Path.GetFullPath(manifestPath), manifest, PluginSourceKind.Manifest));
            }
            catch (Exception ex)
            {
                preparationFailures.Add(PluginLoadResult.Failure(Path.GetFullPath(manifestPath), null, ex));
            }
        }

        return candidates
            .OrderBy(candidate => candidate.SourceKind)
            .ThenBy(candidate => candidate.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IEnumerable<string> EnumerateCandidatePaths(string pluginsRootDirectory, string searchPattern)
    {
        var runtimeRootDirectory = EnsureTrailingSeparator(GetRuntimeRootDirectory(pluginsRootDirectory));

        return Directory
            .EnumerateFiles(pluginsRootDirectory, searchPattern, SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .Where(path => !path.StartsWith(runtimeRootDirectory, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
    }

    private PluginManifest ReadManifestFromPackage(string packagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);

        var fullPackagePath = Path.GetFullPath(packagePath);
        if (!File.Exists(fullPackagePath))
        {
            throw new FileNotFoundException($"Plugin package '{fullPackagePath}' was not found.", fullPackagePath);
        }

        using var archive = ZipFile.OpenRead(fullPackagePath);
        var manifestEntries = archive.Entries
            .Where(entry =>
                !string.IsNullOrWhiteSpace(entry.Name) &&
                string.Equals(entry.Name, _options.ManifestFileName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (manifestEntries.Length == 0)
        {
            throw new InvalidOperationException(
                $"Plugin package '{fullPackagePath}' does not contain '{_options.ManifestFileName}'.");
        }

        if (manifestEntries.Length > 1)
        {
            throw new InvalidOperationException(
                $"Plugin package '{fullPackagePath}' contains multiple '{_options.ManifestFileName}' files.");
        }

        using var stream = manifestEntries[0].Open();
        return PluginManifest.Load(stream, $"{fullPackagePath}!/{manifestEntries[0].FullName}");
    }

    private string ExtractPackage(string packagePath, string pluginsRootDirectory)
    {
        var extractionDirectory = GetPackageExtractionDirectory(pluginsRootDirectory, packagePath);
        
        // 检查是否可以跳过解压（缓存有效）
        if (ShouldSkipExtraction(packagePath, extractionDirectory))
        {
            AppLogger.Info(
                "PluginLoader",
                $"Skipping extraction for '{packagePath}'. Cache is up-to-date.");
            return extractionDirectory;
        }
        
        AppLogger.Info(
            "PluginLoader",
            $"Extracting package '{packagePath}' to '{extractionDirectory}'.");
        RecreateDirectory(extractionDirectory);
        ZipFile.ExtractToDirectory(packagePath, extractionDirectory, overwriteFiles: true);
        
        // 保存解压元数据用于后续缓存检查
        SaveExtractionMetadata(packagePath, extractionDirectory);
        
        return extractionDirectory;
    }

    private string GetPackageExtractionDirectory(string pluginsRootDirectory, string packagePath)
    {
        var packageName = SanitizeDirectoryName(Path.GetFileNameWithoutExtension(packagePath));
        var packageHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(packagePath))))
            .Substring(0, 12);

        return Path.Combine(
            GetRuntimeRootDirectory(pluginsRootDirectory),
            _options.ExtractedPackagesDirectoryName,
            $"{packageName}_{packageHash}");
    }

    private string GetPackagedDataDirectory(string pluginsRootDirectory, PluginManifest manifest)
    {
        return Path.Combine(
            GetRuntimeRootDirectory(pluginsRootDirectory),
            _options.PackagedDataDirectoryName,
            SanitizeDirectoryName(manifest.Id));
    }

    private string GetRuntimeRootDirectory(string pluginsRootDirectory)
    {
        return Path.Combine(Path.GetFullPath(pluginsRootDirectory), _options.RuntimeDirectoryName);
    }

    private static void RecreateDirectory(string directoryPath)
    {
        if (Directory.Exists(directoryPath))
        {
            FileOperationRetryHelper.DeleteDirectoryWithRetry(directoryPath, recursive: true, "PluginLoader");
        }

        Directory.CreateDirectory(directoryPath);
    }

    private static string NormalizePackageExtension(string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        return extension.StartsWith(".", StringComparison.Ordinal) ? extension : "." + extension;
    }

    private static string EnsureTrailingSeparator(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return fullPath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? fullPath
            : fullPath + Path.DirectorySeparatorChar;
    }

    private static string SanitizeDirectoryName(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);

        foreach (var ch in value)
        {
            builder.Append(invalidCharacters.Contains(ch) ? '_' : ch);
        }

        return string.IsNullOrWhiteSpace(builder.ToString()) ? "_plugin" : builder.ToString().Trim();
    }

    private bool ShouldSkipExtraction(string packagePath, string extractionDirectory)
    {
        // 如果解压目录不存在，必须解压
        if (!Directory.Exists(extractionDirectory))
        {
            return false;
        }

        // 检查元数据文件是否存在
        var metadataPath = Path.Combine(extractionDirectory, ".extraction-metadata.json");
        if (!File.Exists(metadataPath))
        {
            return false;
        }

        try
        {
            var packageInfo = new FileInfo(packagePath);
            var metadata = ReadExtractionMetadata(metadataPath);

            // 如果包文件修改时间晚于解压时间，需要重新解压
            // 同时检查文件大小是否匹配
            return packageInfo.Length == metadata.PackageSize &&
                   packageInfo.LastWriteTimeUtc <= metadata.ExtractedAt;
        }
        catch (Exception ex)
        {
            AppLogger.Warn(
                "PluginLoader",
                $"Failed to read extraction metadata for '{packagePath}'. Will re-extract.",
                ex);
            return false;
        }
    }

    private void SaveExtractionMetadata(string packagePath, string extractionDirectory)
    {
        try
        {
            var packageInfo = new FileInfo(packagePath);
            var metadata = new ExtractionMetadata
            {
                PackagePath = Path.GetFullPath(packagePath),
                ExtractedAt = DateTime.UtcNow,
                PackageSize = packageInfo.Length,
                PackageLastWriteTime = packageInfo.LastWriteTimeUtc
            };

            var metadataPath = Path.Combine(extractionDirectory, ".extraction-metadata.json");
            var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(metadataPath, json);
        }
        catch (Exception ex)
        {
            AppLogger.Warn(
                "PluginLoader",
                $"Failed to save extraction metadata for '{packagePath}'.",
                ex);
        }
    }

    private static ExtractionMetadata ReadExtractionMetadata(string metadataPath)
    {
        var json = File.ReadAllText(metadataPath);
        return JsonSerializer.Deserialize<ExtractionMetadata>(json)
            ?? throw new InvalidOperationException("Failed to deserialize extraction metadata.");
    }

    private sealed class ExtractionMetadata
    {
        public string PackagePath { get; set; } = string.Empty;
        public DateTime ExtractedAt { get; set; }
        public long PackageSize { get; set; }
        public DateTime PackageLastWriteTime { get; set; }
    }

    private static ReadOnlyDictionary<string, object?> CreateReadOnlyProperties(
        IReadOnlyDictionary<string, object?>? properties)
    {
        if (properties is null || properties.Count == 0)
        {
            return new ReadOnlyDictionary<string, object?>(
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase));
        }

        var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in properties)
        {
            map[pair.Key] = pair.Value;
        }

        return new ReadOnlyDictionary<string, object?>(map);
    }

    private static void ValidatePluginRuntimeAssets(
        PluginManifest manifest,
        string assemblyPath,
        string pluginDirectory,
        bool isDevMode)
    {
        var depsFilePath = Path.ChangeExtension(assemblyPath, ".deps.json");
        if (!File.Exists(depsFilePath))
        {
            if (isDevMode)
            {
                AppLogger.Warn(
                    "PluginLoader",
                    $"Plugin '{manifest.Id}' is missing '{Path.GetFileName(depsFilePath)}'. In developer mode this is allowed, but dependency resolution may fail at runtime.");
            }
            else
            {
                throw new InvalidOperationException(
                    $"Plugin '{manifest.Id}' targets API {PluginSdkInfo.ApiVersion} and must include '{Path.GetFileName(depsFilePath)}' next to its main assembly.");
            }
        }

        var runtimesDirectory = Path.Combine(pluginDirectory, "runtimes");
        if (Directory.Exists(runtimesDirectory) &&
            !Directory.EnumerateFiles(runtimesDirectory, "*", SearchOption.AllDirectories).Any())
        {
            throw new InvalidOperationException(
                $"Plugin '{manifest.Id}' contains an empty 'runtimes' directory. Native/runtime assets must be packaged together with the plugin.");
        }
    }

    private static Type ResolvePluginType(Assembly assembly)
    {
        var candidateTypes = GetLoadableTypes(assembly)
            .Where(type =>
                typeof(IPlugin).IsAssignableFrom(type) &&
                !type.IsAbstract &&
                !type.IsInterface &&
                !type.ContainsGenericParameters)
            .ToArray();

        if (candidateTypes.Length == 0)
        {
            throw new InvalidOperationException(
                $"Assembly '{assembly.Location}' does not contain a concrete type implementing '{nameof(IPlugin)}'.");
        }

        var attributedTypes = candidateTypes
            .Where(type => type.IsDefined(typeof(PluginEntranceAttribute), inherit: false))
            .ToArray();

        if (attributedTypes.Length == 1)
        {
            return attributedTypes[0];
        }

        if (attributedTypes.Length > 1)
        {
            throw new InvalidOperationException(
                $"Assembly '{assembly.Location}' contains multiple plugin entrance types. Mark only one type with '{nameof(PluginEntranceAttribute)}'.");
        }

        if (candidateTypes.Length == 1)
        {
            return candidateTypes[0];
        }

        throw new InvalidOperationException(
            $"Assembly '{assembly.Location}' contains multiple '{nameof(IPlugin)}' implementations. Mark the intended entrance type with '{nameof(PluginEntranceAttribute)}'.");
    }

    private static IPlugin CreatePluginInstance(Type pluginType)
    {
        if (pluginType.GetConstructor(Type.EmptyTypes) is null)
        {
            throw new InvalidOperationException(
                $"Plugin type '{pluginType.FullName}' must expose a public parameterless constructor.");
        }

        if (Activator.CreateInstance(pluginType) is not IPlugin plugin)
        {
            throw new InvalidOperationException(
                $"Failed to create plugin instance of type '{pluginType.FullName}'.");
        }

        return plugin;
    }

    private static void DisposeInstance(object? instance)
    {
        if (instance is null)
        {
            return;
        }

        try
        {
            if (instance is IAsyncDisposable asyncDisposable)
            {
                asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
                return;
            }

            if (instance is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        catch (Exception disposeError)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[PluginLoader] Disposal of '{instance.GetType().FullName}' failed: {disposeError}");
        }
    }

    private static Type[] GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            var loaderMessages = ex.LoaderExceptions
                .Where(exception => exception is not null)
                .Select(exception => exception!.Message)
                .ToArray();

            var detail = loaderMessages.Length == 0
                ? "No additional loader diagnostics were provided."
                : string.Join(Environment.NewLine, loaderMessages);

            throw new InvalidOperationException(
                $"Failed to inspect plugin assembly '{assembly.Location}'.{Environment.NewLine}{detail}",
                ex);
        }
    }

    private sealed class PluginRuntimeContext : IPluginRuntimeContext
    {
        private readonly PluginAppearanceContext _appearanceContext;

        public PluginRuntimeContext(
            PluginManifest manifest,
            string pluginDirectory,
            string dataDirectory,
            IReadOnlyDictionary<string, object?> properties,
            PluginAppearanceSnapshot appearanceSnapshot)
        {
            Manifest = manifest;
            PluginDirectory = pluginDirectory;
            DataDirectory = dataDirectory;
            Properties = properties;
            _appearanceContext = new PluginAppearanceContext(appearanceSnapshot);
            Appearance = _appearanceContext;
            Services = NullServiceProvider.Instance;
        }

        public PluginManifest Manifest { get; }

        public string PluginDirectory { get; }

        public string DataDirectory { get; }

        public IServiceProvider Services { get; private set; }

        public IReadOnlyDictionary<string, object?> Properties { get; }

        public IPluginAppearanceContext Appearance { get; }

        public T? GetService<T>()
        {
            return (T?)Services.GetService(typeof(T));
        }

        public bool TryGetProperty<T>(string key, out T? value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);

            if (Properties.TryGetValue(key, out var rawValue) && rawValue is T typedValue)
            {
                value = typedValue;
                return true;
            }

            value = default;
            return false;
        }

        public void SetServices(IServiceProvider services)
        {
            Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        /// <summary>
        /// 更新外观快照并通知插件。
        /// </summary>
        internal void UpdateAppearanceSnapshot(PluginAppearanceSnapshot newSnapshot, IReadOnlyCollection<AppearanceProperty> changedProperties)
        {
            _appearanceContext.UpdateSnapshot(newSnapshot, changedProperties);
        }
    }

    private sealed class PluginMessageBus : IPluginMessageBus, IDisposable
    {
        private readonly Dictionary<Type, List<Subscription>> _subscriptions = [];
        private readonly object _gate = new();
        private int _disposed;

        public IDisposable Subscribe<TMessage>(Action<TMessage> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(PluginMessageBus));
            }

            var subscription = new Subscription(this, typeof(TMessage), message => handler((TMessage)message!));
            lock (_gate)
            {
                if (!_subscriptions.TryGetValue(subscription.MessageType, out var handlers))
                {
                    handlers = [];
                    _subscriptions[subscription.MessageType] = handlers;
                }

                handlers.Add(subscription);
            }

            return subscription;
        }

        public void Publish<TMessage>(TMessage message)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            Subscription[] handlers;
            lock (_gate)
            {
                if (!_subscriptions.TryGetValue(typeof(TMessage), out var subscriptions) || subscriptions.Count == 0)
                {
                    return;
                }

                handlers = subscriptions.ToArray();
            }

            foreach (var handler in handlers)
            {
                try
                {
                    handler.Invoke(message);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[PluginMessageBus] Handler for '{typeof(TMessage).FullName}' failed: {ex}");
                }
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            lock (_gate)
            {
                _subscriptions.Clear();
            }
        }

        private void Unsubscribe(Subscription subscription)
        {
            lock (_gate)
            {
                if (!_subscriptions.TryGetValue(subscription.MessageType, out var handlers))
                {
                    return;
                }

                handlers.Remove(subscription);
                if (handlers.Count == 0)
                {
                    _subscriptions.Remove(subscription.MessageType);
                }
            }
        }

        private sealed class Subscription : IDisposable
        {
            private readonly PluginMessageBus _owner;
            private int _disposed;

            public Subscription(PluginMessageBus owner, Type messageType, Action<object?> handler)
            {
                _owner = owner;
                MessageType = messageType;
                Handler = handler;
            }

            public Type MessageType { get; }

            public Action<object?> Handler { get; }

            public void Invoke(object? message)
            {
                if (_disposed != 0)
                {
                    return;
                }

                Handler(message);
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                {
                    return;
                }

                _owner.Unsubscribe(this);
            }
        }
    }

    private sealed class NullServiceProvider : IServiceProvider
    {
        public static NullServiceProvider Instance { get; } = new();

        private NullServiceProvider()
        {
        }

        public object? GetService(Type serviceType)
        {
            return null;
        }
    }

    private enum PluginSourceKind
    {
        Package = 0,
        Manifest = 1
    }

    private sealed record PluginCandidate(
        string SourcePath,
        PluginManifest Manifest,
        PluginSourceKind SourceKind);
}
