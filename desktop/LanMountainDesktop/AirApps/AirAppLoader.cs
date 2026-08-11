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
using LanMountainDesktop.AirAppSdk;
using LanMountainDesktop.Shared.IPC;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using dotnetCampus.Ipc.CompilerServices.Attributes;

namespace LanMountainDesktop.AirApps;

public sealed class AirAppLoader
{
    private readonly AirAppLoaderOptions _options;

    public AirAppLoader(AirAppLoaderOptions? options = null)
    {
        _options = options ?? new AirAppLoaderOptions();
    }

    public IReadOnlyList<AirAppLoadResult> LoadAll(
        string pluginsRootDirectory,
        IServiceProvider? services = null,
        IReadOnlyDictionary<string, object?>? properties = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginsRootDirectory);

        if (!Directory.Exists(pluginsRootDirectory))
        {
            return Array.Empty<AirAppLoadResult>();
        }

        var results = new List<AirAppLoadResult>();
        var candidates = DiscoverCandidates(pluginsRootDirectory, results);
        var selectedAirAppIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            if (!selectedAirAppIds.Add(candidate.Manifest.Id))
            {
                results.Add(AirAppLoadResult.Failure(
                    candidate.SourcePath,
                    candidate.Manifest,
                    new InvalidOperationException(
                        $"Duplicate plugin id '{candidate.Manifest.Id}' was found. Source '{candidate.SourcePath}' was ignored because a higher-priority source was already selected.")));
                continue;
            }

            results.Add(candidate.SourceKind switch
            {
                AirAppSourceKind.Package => LoadFromPackage(
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

    public AirAppLoadResult LoadFromManifest(
        string manifestPath,
        IServiceProvider? services = null,
        IReadOnlyDictionary<string, object?>? properties = null)
    {
        AirAppManifest? manifest = null;

        try
        {
            manifest = AirAppManifest.Load(manifestPath);
            return LoadFromManifest(manifestPath, manifest, services, properties);
        }
        catch (Exception ex)
        {
            return AirAppLoadResult.Failure(Path.GetFullPath(manifestPath), manifest, ex);
        }
    }

    public AirAppLoadResult LoadFromPackage(
        string packagePath,
        string pluginsRootDirectory,
        IServiceProvider? services = null,
        IReadOnlyDictionary<string, object?>? properties = null)
    {
        AirAppManifest? manifest = null;

        try
        {
            manifest = ReadManifestFromPackage(packagePath);
            return LoadFromPackage(packagePath, pluginsRootDirectory, manifest, services, properties);
        }
        catch (Exception ex)
        {
            return AirAppLoadResult.Failure(Path.GetFullPath(packagePath), manifest, ex);
        }
    }

    public AirAppLoadResult LoadFromAssembly(
        string assemblyPath,
        AirAppManifest manifest,
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

    private AirAppLoadResult LoadCore(
        string sourcePath,
        string assemblyPath,
        string pluginDirectory,
        string dataDirectory,
        AirAppManifest manifest,
        IServiceProvider? services,
        IReadOnlyDictionary<string, object?>? properties)
    {
        AirAppLoadContext? loadContext = null;
        IAirApp? plugin = null;
        AirAppRuntimeContext? runtimeContext = null;
        ServiceProvider? pluginServices = null;
        IReadOnlyList<IHostedService> hostedServices = Array.Empty<IHostedService>();

        try
        {
            Directory.CreateDirectory(dataDirectory);
            ValidateAirAppRuntimeAssets(manifest, assemblyPath, pluginDirectory, _options.IsDevMode);
            AppLogger.Info(
                "AirAppLoader",
                $"LoadCore starting. AirAppId='{manifest.Id}'; AssemblyPath='{assemblyPath}'; AirAppDirectory='{pluginDirectory}'; DataDirectory='{dataDirectory}'.");

            loadContext = new AirAppLoadContext(assemblyPath, _options.SharedAssemblyNames);
            var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
            AppLogger.Info("AirAppLoader", $"Assembly loaded. AirAppId='{manifest.Id}'; Assembly='{assembly.FullName}'.");
            var pluginType = ResolveAirAppType(assembly);
            plugin = CreateAirAppInstance(pluginType);
            AppLogger.Info("AirAppLoader", $"AirApp instance created. AirAppId='{manifest.Id}'; AirAppType='{pluginType.FullName}'.");
            runtimeContext = CreateRuntimeContext(manifest, pluginDirectory, dataDirectory, properties, services);
            var serviceCollection = CreateServiceCollection(runtimeContext, services);
            var hostBuilderContext = CreateHostBuilderContext(runtimeContext);

            plugin.Initialize(hostBuilderContext, serviceCollection);
            AppLogger.Info("AirAppLoader", $"AirApp Initialize completed. AirAppId='{manifest.Id}'.");

            pluginServices = serviceCollection.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = false,
                ValidateOnBuild = true
            });
            AppLogger.Info("AirAppLoader", $"Service provider built. AirAppId='{manifest.Id}'.");
            runtimeContext.SetServices(pluginServices);
            plugin.OnStartedAsync(runtimeContext).GetAwaiter().GetResult();
            AppLogger.Info("AirAppLoader", $"AirApp OnStartedAsync completed. AirAppId='{manifest.Id}'.");

            var settingsSections = pluginServices
                .GetServices<AirAppSettingsSectionRegistration>()
                .OrderBy(section => section.SortOrder)
                .ThenBy(section => section.TitleLocalizationKey, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var desktopComponents = pluginServices
                .GetServices<AirAppComponentRegistration>()
                .OrderBy(component => component.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(component => component.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var desktopComponentEditors = pluginServices
                .GetServices<AirAppComponentEditorRegistration>()
                .OrderBy(editor => editor.ComponentId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var exportedServices = ResolveExports(manifest, pluginServices);
            var publicIpcServices = ResolvePublicIpcServices(manifest, pluginServices);
            AppLogger.Info(
                "AirAppLoader",
                $"AirApp contributions resolved. AirAppId='{manifest.Id}'; SettingsSections={settingsSections.Length}; Widgets={desktopComponents.Length}; Editors={desktopComponentEditors.Length}; Exports={exportedServices.Count}; PublicIpcServices={publicIpcServices.Count}."); 
            hostedServices = pluginServices.GetServices<IHostedService>().ToArray();
            StartHostedServices(hostedServices);
            AppLogger.Info("AirAppLoader", $"Hosted services started. AirAppId='{manifest.Id}'; HostedServices={hostedServices.Count}."); 

            var loadedAirApp = new LoadedAirApp(
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
                publicIpcServices,
                hostedServices,
                loadContext);

            return AirAppLoadResult.Success(sourcePath, manifest, loadedAirApp);
        }
        catch (Exception ex)
        {
            StopHostedServices(hostedServices);
            DisposeInstance(pluginServices);
            DisposeInstance(plugin);
            DisposeInstance(runtimeContext);
            loadContext?.Unload();
            return AirAppLoadResult.Failure(sourcePath, manifest, ex);
        }
    }

    private AirAppLoadResult LoadFromManifest(
        string manifestPath,
        AirAppManifest manifest,
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
                    $"AirApp '{manifest.Id}' entrance assembly '{assemblyPath}' was not found.",
                    assemblyPath);
            }

            var pluginDirectory = Path.GetDirectoryName(assemblyPath)
                ?? throw new InvalidOperationException($"Failed to determine the plugin directory of '{assemblyPath}'.");
            var dataDirectory = Path.Combine(pluginDirectory, _options.DataDirectoryName);
            return LoadCore(fullManifestPath, assemblyPath, pluginDirectory, dataDirectory, manifest, services, properties);
        }
        catch (Exception ex)
        {
            return AirAppLoadResult.Failure(Path.GetFullPath(manifestPath), manifest, ex);
        }
    }

    private AirAppLoadResult LoadFromPackage(
        string packagePath,
        string pluginsRootDirectory,
        AirAppManifest manifest,
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
                    $"AirApp package '{fullPackagePath}' does not contain '{_options.ManifestFileName}'.",
                    extractedManifestPath);
            }

            var extractedManifest = AirAppManifest.Load(extractedManifestPath);
            if (!string.Equals(extractedManifest.Id, manifest.Id, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"AirApp package '{fullPackagePath}' manifest id changed after extraction. Expected '{manifest.Id}', actual '{extractedManifest.Id}'.");
            }

            var assemblyPath = extractedManifest.ResolveEntranceAssemblyPath(extractedManifestPath);
            if (!File.Exists(assemblyPath))
            {
                throw new FileNotFoundException(
                    $"AirApp '{extractedManifest.Id}' entrance assembly '{assemblyPath}' was not found after package extraction.",
                    assemblyPath);
            }

            var dataDirectory = GetPackagedDataDirectory(pluginsRootDirectory, extractedManifest);
            return LoadCore(fullPackagePath, assemblyPath, extractionDirectory, dataDirectory, extractedManifest, services, properties);
        }
        catch (Exception ex)
        {
            return AirAppLoadResult.Failure(Path.GetFullPath(packagePath), manifest, ex);
        }
    }

    private AirAppRuntimeContext CreateRuntimeContext(
        AirAppManifest manifest,
        string pluginDirectory,
        string dataDirectory,
        IReadOnlyDictionary<string, object?>? properties,
        IServiceProvider? hostServices)
    {
        return new AirAppRuntimeContext(
            manifest,
            pluginDirectory,
            dataDirectory,
            CreateReadOnlyProperties(properties),
            BuildAppearanceSnapshot(hostServices));
    }

    private ServiceCollection CreateServiceCollection(
        AirAppRuntimeContext runtimeContext,
        IServiceProvider? hostServices)
    {
        var services = new ServiceCollection();
        services.AddSingleton(runtimeContext);
        services.AddSingleton<IAirAppRuntimeContext>(runtimeContext);
        services.AddSingleton<IAirAppAppearanceContext>(runtimeContext.Appearance);
        services.AddSingleton(runtimeContext.Manifest);
        services.AddSingleton<IReadOnlyDictionary<string, object?>>(runtimeContext.Properties);
        services.AddSingleton<IAirAppMessageBus, AirAppMessageBus>();
        services.AddSingleton<IAirAppSettingsService>(provider =>
            new AirAppScopedSettingsService(
                runtimeContext.Manifest.Id,
                provider.GetRequiredService<ISettingsService>()));

        RegisterHostService<IAirAppPackageManager>(services, hostServices);
        RegisterHostService<IHostApplicationLifecycle>(services, hostServices);
        RegisterHostService<IAirAppExportRegistry>(services, hostServices);
        RegisterHostService<ISettingsFacadeService>(services, hostServices);
        RegisterHostService<ISettingsService>(services, hostServices);
        RegisterHostService<ISettingsCatalog>(services, hostServices);
        // Legacy compatibility only. Normal plugin appearance snapshots come from IMaterialColorService.
        RegisterHostService<IAppearanceThemeService>(services, hostServices);
        RegisterHostService<IMaterialColorService>(services, hostServices);
        RegisterHostService<IExternalIpcNotificationPublisher>(services, hostServices);

        return services;
    }

    private static AirAppAppearanceSnapshot BuildAppearanceSnapshot(IServiceProvider? hostServices)
    {
        var defaultSnapshot = CreateDefaultAppearanceSnapshot();

        try
        {
            if (TryBuildAppearanceSnapshotFromMaterialColorService(hostServices, out var snapshot))
            {
                return snapshot;
            }

            if (TryBuildCompatibilityAppearanceSnapshotFromAppearanceThemeService(hostServices, out snapshot))
            {
                return snapshot;
            }

            return defaultSnapshot;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("AirAppLoader", "Failed to resolve host appearance snapshot for plugin runtime context.", ex);
            return defaultSnapshot;
        }
    }

    private static bool TryBuildAppearanceSnapshotFromMaterialColorService(
        IServiceProvider? hostServices,
        out AirAppAppearanceSnapshot snapshot)
    {
        snapshot = default!;
        if (hostServices?.GetService(typeof(IMaterialColorService)) is not IMaterialColorService materialColorService)
        {
            return false;
        }

        snapshot = AirAppAppearanceSnapshotMapper.FromMaterialColorSnapshot(materialColorService.GetMaterialColorSnapshot());
        return true;
    }

    private static bool TryBuildCompatibilityAppearanceSnapshotFromAppearanceThemeService(
        IServiceProvider? hostServices,
        out AirAppAppearanceSnapshot snapshot)
    {
        snapshot = default!;
        if (hostServices?.GetService(typeof(IAppearanceThemeService)) is not IAppearanceThemeService appearanceThemeService)
        {
            return false;
        }

        snapshot = AirAppAppearanceSnapshotMapper.FromCompatibilityAppearanceSnapshot(appearanceThemeService.GetCurrent());
        return true;
    }

    private static AirAppAppearanceSnapshot CreateDefaultAppearanceSnapshot()
    {
        return new AirAppAppearanceSnapshot(
            CornerRadiusTokens: new AirAppCornerRadiusTokens(6, 12, 14, 20, 28, 32, 36, 24),
            ThemeVariant: "Unknown");
    }

    private static void RegisterHostService<TService>(IServiceCollection services, IServiceProvider? hostServices)
        where TService : class
    {
        if (hostServices?.GetService(typeof(TService)) is TService service)
        {
            services.AddSingleton(service);
        }
    }

    private static HostBuilderContext CreateHostBuilderContext(AirAppRuntimeContext runtimeContext)
    {
        var hostBuilderContext = new HostBuilderContext(new Dictionary<object, object>());
        hostBuilderContext.Properties["LanMountainDesktop.AirAppManifest"] = runtimeContext.Manifest;
        hostBuilderContext.Properties["LanMountainDesktop.AirAppDirectory"] = runtimeContext.AirAppDirectory;
        hostBuilderContext.Properties["LanMountainDesktop.AirAppDataDirectory"] = runtimeContext.DataDirectory;
        hostBuilderContext.Properties["LanMountainDesktop.AirAppRuntimeContext"] = runtimeContext;

        foreach (var pair in runtimeContext.Properties)
        {
            if (pair.Value is not null)
            {
                hostBuilderContext.Properties[pair.Key] = pair.Value;
            }
        }

        return hostBuilderContext;
    }

    private static IReadOnlyList<AirAppServiceExportDescriptor> ResolveExports(
        AirAppManifest manifest,
        IServiceProvider services)
    {
        return services
            .GetServices<AirAppServiceExportRegistration>()
            .Select(registration =>
            {
                if (!IsSupportedExportContract(manifest, registration.ContractType))
                {
                    throw new InvalidOperationException(
                        $"AirApp '{manifest.Id}' exported contract '{registration.ContractType.FullName}', but export contracts must come from LanMountainDesktop.AirAppSdk or a manifest-declared shared contract assembly.");
                }

                return new AirAppServiceExportDescriptor(
                    manifest.Id,
                    registration.ContractType,
                    services.GetService(registration.ContractType)
                        ?? throw new InvalidOperationException(
                            $"AirApp '{manifest.Id}' exported contract '{registration.ContractType.FullName}', but no singleton service instance was registered."));
            })
            .ToArray();
    }

    private static IReadOnlyList<AirAppPublicIpcServiceDescriptor> ResolvePublicIpcServices(
        AirAppManifest manifest,
        IServiceProvider services)
    {
        var descriptors = new List<AirAppPublicIpcServiceDescriptor>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var registration in services.GetServices<AirAppPublicIpcServiceRegistration>())
        {
            var implementation = services.GetService(registration.ContractType)
                ?? throw new InvalidOperationException(
                    $"AirApp '{manifest.Id}' registered public IPC contract '{registration.ContractType.FullName}', but no singleton service instance was found.");

            AddDescriptor(registration.ContractType, implementation, registration.ObjectId, registration.NotifyIds);
        }

        var builder = new RuntimeAirAppPublicIpcBuilder(services, AddDescriptor);
        foreach (var contributor in services.GetServices<IAirAppPublicIpcContributor>())
        {
            contributor.ConfigurePublicIpc(builder);
        }

        return descriptors;

        void AddDescriptor(Type contractType, object implementation, string? objectId, IEnumerable<string>? notifyIds)
        {
            EnsurePublicIpcContract(manifest, contractType);

            var normalizedObjectId = objectId ?? string.Empty;
            var dedupeKey = $"{contractType.AssemblyQualifiedName}::{normalizedObjectId}";
            if (!seenKeys.Add(dedupeKey))
            {
                throw new InvalidOperationException(
                    $"AirApp '{manifest.Id}' registered duplicate public IPC contract '{contractType.FullName}' with object id '{normalizedObjectId}'.");
            }

            descriptors.Add(new AirAppPublicIpcServiceDescriptor(
                contractType,
                implementation,
                string.IsNullOrEmpty(normalizedObjectId) ? null : normalizedObjectId,
                notifyIds?
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray() ?? []));
        }
    }

    private static void EnsurePublicIpcContract(AirAppManifest manifest, Type contractType)
    {
        if (!contractType.IsInterface)
        {
            throw new InvalidOperationException(
                $"AirApp '{manifest.Id}' public IPC contract '{contractType.FullName}' must be an interface.");
        }

        if (!Attribute.IsDefined(contractType, typeof(IpcPublicAttribute), inherit: false))
        {
            throw new InvalidOperationException(
                $"AirApp '{manifest.Id}' public IPC contract '{contractType.FullName}' must be marked with '{nameof(IpcPublicAttribute)}'.");
        }
    }

    private static bool IsSupportedExportContract(AirAppManifest manifest, Type contractType)
    {
        if (contractType.Assembly == typeof(IAirApp).Assembly)
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
            AppLogger.Info("AirAppLoader", $"Starting hosted service '{hostedService.GetType().FullName}'.");
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

    private IReadOnlyList<AirAppCandidate> DiscoverCandidates(
        string pluginsRootDirectory,
        List<AirAppLoadResult> preparationFailures)
    {
        var candidates = new List<AirAppCandidate>();

        foreach (var packagePath in EnumerateCandidatePaths(
                     pluginsRootDirectory,
                     "*" + NormalizePackageExtension(_options.PackageFileExtension)))
        {
            try
            {
                var manifest = ReadManifestFromPackage(packagePath);
                candidates.Add(new AirAppCandidate(Path.GetFullPath(packagePath), manifest, AirAppSourceKind.Package));
            }
            catch (Exception ex)
            {
                preparationFailures.Add(AirAppLoadResult.Failure(Path.GetFullPath(packagePath), null, ex));
            }
        }

        foreach (var manifestPath in EnumerateCandidatePaths(pluginsRootDirectory, _options.ManifestFileName))
        {
            try
            {
                var manifest = AirAppManifest.Load(manifestPath);
                candidates.Add(new AirAppCandidate(Path.GetFullPath(manifestPath), manifest, AirAppSourceKind.Manifest));
            }
            catch (Exception ex)
            {
                preparationFailures.Add(AirAppLoadResult.Failure(Path.GetFullPath(manifestPath), null, ex));
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

    private AirAppManifest ReadManifestFromPackage(string packagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);

        var fullPackagePath = Path.GetFullPath(packagePath);
        if (!File.Exists(fullPackagePath))
        {
            throw new FileNotFoundException($"AirApp package '{fullPackagePath}' was not found.", fullPackagePath);
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
                $"AirApp package '{fullPackagePath}' does not contain '{_options.ManifestFileName}'.");
        }

        if (manifestEntries.Length > 1)
        {
            throw new InvalidOperationException(
                $"AirApp package '{fullPackagePath}' contains multiple '{_options.ManifestFileName}' files.");
        }

        using var stream = manifestEntries[0].Open();
        return AirAppManifest.Load(stream, $"{fullPackagePath}!/{manifestEntries[0].FullName}");
    }

    private string ExtractPackage(string packagePath, string pluginsRootDirectory)
    {
        var extractionDirectory = GetPackageExtractionDirectory(pluginsRootDirectory, packagePath);
        
        // 检查是否可以跳过解压（缓存有效）
        if (ShouldSkipExtraction(packagePath, extractionDirectory))
        {
            AppLogger.Info(
                "AirAppLoader",
                $"Skipping extraction for '{packagePath}'. Cache is up-to-date.");
            return extractionDirectory;
        }
        
        AppLogger.Info(
            "AirAppLoader",
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

    private string GetPackagedDataDirectory(string pluginsRootDirectory, AirAppManifest manifest)
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
            FileOperationRetryHelper.DeleteDirectoryWithRetry(directoryPath, recursive: true, "AirAppLoader");
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
                "AirAppLoader",
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
                "AirAppLoader",
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

    private static void ValidateAirAppRuntimeAssets(
        AirAppManifest manifest,
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
                    "AirAppLoader",
                    $"AirApp '{manifest.Id}' is missing '{Path.GetFileName(depsFilePath)}'. In developer mode this is allowed, but dependency resolution may fail at runtime.");
            }
            else
            {
                throw new InvalidOperationException(
                    $"AirApp '{manifest.Id}' targets API {AirAppSdkInfo.ApiVersion} and must include '{Path.GetFileName(depsFilePath)}' next to its main assembly.");
            }
        }

        var runtimesDirectory = Path.Combine(pluginDirectory, "runtimes");
        if (Directory.Exists(runtimesDirectory) &&
            !Directory.EnumerateFiles(runtimesDirectory, "*", SearchOption.AllDirectories).Any())
        {
            throw new InvalidOperationException(
                $"AirApp '{manifest.Id}' contains an empty 'runtimes' directory. Native/runtime assets must be packaged together with the plugin.");
        }
    }

    private static Type ResolveAirAppType(Assembly assembly)
    {
        var candidateTypes = GetLoadableTypes(assembly)
            .Where(type =>
                typeof(IAirApp).IsAssignableFrom(type) &&
                !type.IsAbstract &&
                !type.IsInterface &&
                !type.ContainsGenericParameters)
            .ToArray();

        if (candidateTypes.Length == 0)
        {
            throw new InvalidOperationException(
                $"Assembly '{assembly.Location}' does not contain a concrete type implementing '{nameof(IAirApp)}'.");
        }

        var attributedTypes = candidateTypes
            .Where(type => type.IsDefined(typeof(AirAppEntranceAttribute), inherit: false))
            .ToArray();

        if (attributedTypes.Length == 1)
        {
            return attributedTypes[0];
        }

        if (attributedTypes.Length > 1)
        {
            throw new InvalidOperationException(
                $"Assembly '{assembly.Location}' contains multiple plugin entrance types. Mark only one type with '{nameof(AirAppEntranceAttribute)}'.");
        }

        if (candidateTypes.Length == 1)
        {
            return candidateTypes[0];
        }

        throw new InvalidOperationException(
            $"Assembly '{assembly.Location}' contains multiple '{nameof(IAirApp)}' implementations. Mark the intended entrance type with '{nameof(AirAppEntranceAttribute)}'.");
    }

    private static IAirApp CreateAirAppInstance(Type pluginType)
    {
        if (pluginType.GetConstructor(Type.EmptyTypes) is null)
        {
            throw new InvalidOperationException(
                $"AirApp type '{pluginType.FullName}' must expose a public parameterless constructor.");
        }

        if (Activator.CreateInstance(pluginType) is not IAirApp plugin)
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
                $"[AirAppLoader] Disposal of '{instance.GetType().FullName}' failed: {disposeError}");
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

    private sealed class AirAppRuntimeContext : IAirAppRuntimeContext
    {
        private readonly AirAppAppearanceContext _appearanceContext;
        private readonly AirAppMessageBus _messageBus;
        private readonly IAirAppLogger _logger;
        private readonly List<AirAppComponentOptions> _runtimeComponents = [];
        private readonly List<AirAppWindowRegistration> _runtimeWindows = [];

        public AirAppRuntimeContext(
            AirAppManifest manifest,
            string pluginDirectory,
            string dataDirectory,
            IReadOnlyDictionary<string, object?> properties,
            AirAppAppearanceSnapshot appearanceSnapshot)
        {
            Manifest = manifest;
            AirAppDirectory = pluginDirectory;
            DataDirectory = dataDirectory;
            CacheDirectory = Path.Combine(pluginDirectory, "Cache");
            Properties = properties;
            _appearanceContext = new AirAppAppearanceContext(appearanceSnapshot);
            Appearance = _appearanceContext;
            _messageBus = new AirAppMessageBus();
            _logger = new AirAppRuntimeLogger(manifest.Id);
            MessageBus = _messageBus;
            Logger = _logger;
            Lifetime = NoOpHostApplicationLifetime.Instance;
            Services = NullServiceProvider.Instance;
        }

        public AirAppManifest Manifest { get; }

        public string AirAppDirectory { get; }

        public string DataDirectory { get; }

        public string CacheDirectory { get; }

        public IServiceProvider Services { get; private set; }

        public IReadOnlyDictionary<string, object?> Properties { get; }

        public IHostApplicationLifetime Lifetime { get; }

        public IAirAppMessageBus MessageBus { get; }

        public IAirAppAppearanceContext Appearance { get; }

        public IAirAppLogger Logger { get; }

        /// <summary>
        /// 由宿主注入的窗口打开处理器（AirAppHost 第三方窗口链路，Stage 3 接入）。
        /// 未设置时 <see cref="OpenWindowAsync"/> 会抛出 NotSupportedException。
        /// </summary>
        internal Func<string, Task<IAirAppWindow>>? OpenWindowHandler { get; set; }

        /// <summary>
        /// 由宿主注入的窗口关闭处理器。
        /// </summary>
        internal Action<string>? CloseWindowHandler { get; set; }

        internal IReadOnlyList<AirAppComponentOptions> RuntimeComponents => _runtimeComponents;

        internal IReadOnlyList<AirAppWindowRegistration> RuntimeWindows => _runtimeWindows;

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

        public async Task<IAirAppWindow> OpenWindowAsync(string windowId)
        {
            var handler = OpenWindowHandler;
            if (handler is null)
            {
                throw new NotSupportedException(
                    $"AirApp '{Manifest.Id}' requested to open window '{windowId}', but the host does not support in-process window opening. Use an isolated window AirApp instead.");
            }

            return await handler(windowId).ConfigureAwait(false);
        }

        public void CloseWindow(string windowId)
        {
            var closer = CloseWindowHandler;
            if (closer is null)
            {
                throw new NotSupportedException(
                    $"AirApp '{Manifest.Id}' requested to close window '{windowId}', but the host does not support in-process window closing.");
            }

            closer(windowId);
        }

        public void RegisterComponent(AirAppComponentOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            _runtimeComponents.Add(options);
        }

        public void RegisterWindow(string id, string name, Type windowType)
        {
            _runtimeWindows.Add(new AirAppWindowRegistration(id, name, windowType));
        }

        public void RegisterService<TService, TImplementation>()
            where TService : class
            where TImplementation : class, TService
        {
            throw new InvalidOperationException(
                $"AirApp '{Manifest.Id}' called RegisterService after startup. Register services in Initialize via IServiceCollection instead.");
        }

        public void SetServices(IServiceProvider services)
        {
            Services = services ?? throw new ArgumentNullException(nameof(services));
        }

        /// <summary>
        /// 更新外观快照并通知插件。
        /// </summary>
        internal void UpdateAppearanceSnapshot(AirAppAppearanceSnapshot newSnapshot, IReadOnlyCollection<AppearanceProperty> changedProperties)
        {
            _appearanceContext.UpdateSnapshot(newSnapshot, changedProperties);
        }
    }

    private sealed class AirAppRuntimeLogger : IAirAppLogger
    {
        private readonly string _category;

        public AirAppRuntimeLogger(string airAppId)
        {
            _category = $"AirApp:{airAppId}";
        }

        public void Debug(string message) => AppLogger.Info(_category, message);

        public void Info(string message) => AppLogger.Info(_category, message);

        public void Warn(string message) => AppLogger.Warn(_category, message);

        public void Warn(string message, Exception exception) => AppLogger.Warn(_category, message, exception);

        public void Error(string message) => AppLogger.Error(_category, message);

        public void Error(string message, Exception exception) => AppLogger.Error(_category, message, exception);
    }

    private sealed class NoOpHostApplicationLifetime : IHostApplicationLifetime
    {
        public static NoOpHostApplicationLifetime Instance { get; } = new();

        private NoOpHostApplicationLifetime()
        {
        }

        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
        }
    }

    private sealed class AirAppMessageBus : IAirAppMessageBus, IDisposable
    {
        private readonly Dictionary<Type, List<Subscription>> _subscriptions = [];
        private readonly Dictionary<string, List<Subscription>> _topicSubscriptions = [];
        private readonly object _gate = new();
        private int _disposed;

        public IDisposable Subscribe<TMessage>(Action<TMessage> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(AirAppMessageBus));
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
                        $"[AirAppMessageBus] Handler for '{typeof(TMessage).FullName}' failed: {ex}");
                }
            }
        }

        public void Publish(string topic, object? payload = null)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            Subscription[] handlers;
            lock (_gate)
            {
                if (!_topicSubscriptions.TryGetValue(topic, out var subscriptions) || subscriptions.Count == 0)
                {
                    return;
                }

                handlers = subscriptions.ToArray();
            }

            foreach (var handler in handlers)
            {
                try
                {
                    handler.Invoke(payload);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[AirAppMessageBus] Handler for topic '{topic}' failed: {ex}");
                }
            }
        }

        public IDisposable Subscribe(string topic, Action<object?> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(AirAppMessageBus));
            }

            var subscription = new Subscription(this, typeof(object), handler, topic);
            lock (_gate)
            {
                if (!_topicSubscriptions.TryGetValue(topic, out var handlers))
                {
                    handlers = [];
                    _topicSubscriptions[topic] = handlers;
                }

                handlers.Add(subscription);
            }

            return subscription;
        }

        public IDisposable Subscribe<T>(string topic, Action<T?> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            return Subscribe(topic, message => handler(message is T typed ? typed : default));
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
                _topicSubscriptions.Clear();
            }
        }

        private void Unsubscribe(Subscription subscription)
        {
            lock (_gate)
            {
                if (subscription.Topic is not null)
                {
                    if (_topicSubscriptions.TryGetValue(subscription.Topic, out var topicHandlers))
                    {
                        topicHandlers.Remove(subscription);
                        if (topicHandlers.Count == 0)
                        {
                            _topicSubscriptions.Remove(subscription.Topic);
                        }
                    }

                    return;
                }

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
            private readonly AirAppMessageBus _owner;
            private int _disposed;

            public Subscription(AirAppMessageBus owner, Type messageType, Action<object?> handler, string? topic = null)
            {
                _owner = owner;
                _topic = topic;
                MessageType = messageType;
                Handler = handler;
            }

            private readonly string? _topic;

            public string? Topic => _topic;

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

    private enum AirAppSourceKind
    {
        Package = 0,
        Manifest = 1
    }

    private sealed record AirAppCandidate(
        string SourcePath,
        AirAppManifest Manifest,
        AirAppSourceKind SourceKind);

    private sealed class RuntimeAirAppPublicIpcBuilder : IAirAppPublicIpcBuilder
    {
        private readonly IServiceProvider _services;
        private readonly Action<Type, object, string?, IEnumerable<string>?> _register;

        public RuntimeAirAppPublicIpcBuilder(
            IServiceProvider services,
            Action<Type, object, string?, IEnumerable<string>?> register)
        {
            _services = services;
            _register = register;
        }

        public IAirAppPublicIpcBuilder AddService<TContract>(
            string? objectId = null,
            IEnumerable<string>? notifyIds = null)
            where TContract : class
        {
            var implementation = _services.GetService(typeof(TContract))
                ?? throw new InvalidOperationException(
                    $"AirApp public IPC contributor requested contract '{typeof(TContract).FullName}', but no singleton service was registered.");
            _register(typeof(TContract), implementation, objectId, notifyIds);
            return this;
        }

        public IAirAppPublicIpcBuilder AddService(
            Type contractType,
            object implementation,
            string? objectId = null,
            IEnumerable<string>? notifyIds = null)
        {
            ArgumentNullException.ThrowIfNull(contractType);
            ArgumentNullException.ThrowIfNull(implementation);
            _register(contractType, implementation, objectId, notifyIds);
            return this;
        }
    }
}
