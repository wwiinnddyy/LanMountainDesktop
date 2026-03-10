using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using LanMountainDesktop.Services;
using LanMountainDesktop.PluginSdk;

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
        PluginContext? context = null;

        try
        {
            loadContext = new PluginLoadContext(assemblyPath, _options.SharedAssemblyNames);
            var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
            var pluginType = ResolvePluginType(assembly);
            plugin = CreatePluginInstance(pluginType);
            context = CreateContext(manifest, pluginDirectory, dataDirectory, services, properties);

            plugin.Initialize(context);
            var settingsPages = context.GetSettingsPagesSnapshot();
            var desktopComponents = context.GetDesktopComponentsSnapshot();

            var loadedPlugin = new LoadedPlugin(
                manifest,
                sourcePath,
                assemblyPath,
                assembly,
                plugin,
                context,
                settingsPages,
                desktopComponents,
                loadContext);

            return PluginLoadResult.Success(sourcePath, manifest, loadedPlugin);
        }
        catch (Exception ex)
        {
            DisposeInstance(plugin);
            DisposeInstance(context);
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

    private PluginContext CreateContext(
        PluginManifest manifest,
        string pluginDirectory,
        string dataDirectory,
        IServiceProvider? services,
        IReadOnlyDictionary<string, object?>? properties)
    {
        Directory.CreateDirectory(dataDirectory);

        return new PluginContext(
            manifest,
            pluginDirectory,
            dataDirectory,
            services ?? NullServiceProvider.Instance,
            CreateReadOnlyProperties(properties));
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
        AppLogger.Info(
            "PluginLoader",
            $"Extracting package '{packagePath}' to '{extractionDirectory}'.");
        RecreateDirectory(extractionDirectory);
        ZipFile.ExtractToDirectory(packagePath, extractionDirectory, overwriteFiles: true);
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

    private sealed class PluginContext : IPluginContext, IDisposable, IAsyncDisposable
    {
        private readonly Dictionary<string, PluginSettingsPageRegistration> _settingsPages =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PluginDesktopComponentRegistration> _desktopComponents =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<Type, object> _registeredServices = [];
        private readonly List<object> _serviceRegistrationOrder = [];
        private readonly object _serviceGate = new();
        private readonly IServiceProvider _hostServices;
        private int _disposed;

        public PluginContext(
            PluginManifest manifest,
            string pluginDirectory,
            string dataDirectory,
            IServiceProvider services,
            IReadOnlyDictionary<string, object?> properties)
        {
            Manifest = manifest;
            PluginDirectory = pluginDirectory;
            DataDirectory = dataDirectory;
            _hostServices = services;
            Services = new PluginCompositeServiceProvider(this);
            Properties = properties;

            RegisterBuiltInService<IPluginContext>(this);
            RegisterBuiltInService<IPluginMessageBus>(new PluginMessageBus());
        }

        public PluginManifest Manifest { get; }

        public string PluginDirectory { get; }

        public string DataDirectory { get; }

        public IServiceProvider Services { get; }

        public IReadOnlyDictionary<string, object?> Properties { get; }

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

        public void RegisterService<TService>(TService service)
            where TService : class
        {
            RegisterServiceCore(typeof(TService), service, allowOverride: false);
        }

        public void RegisterSettingsPage(PluginSettingsPageRegistration registration)
        {
            ArgumentNullException.ThrowIfNull(registration);
            ThrowIfDisposed();

            if (!_settingsPages.TryAdd(registration.Id, registration))
            {
                throw new InvalidOperationException(
                    $"Plugin '{Manifest.Id}' already registered a settings page with id '{registration.Id}'.");
            }
        }

        public void RegisterDesktopComponent(PluginDesktopComponentRegistration registration)
        {
            ArgumentNullException.ThrowIfNull(registration);
            ThrowIfDisposed();

            if (!_desktopComponents.TryAdd(registration.ComponentId, registration))
            {
                throw new InvalidOperationException(
                    $"Plugin '{Manifest.Id}' already registered a desktop component with id '{registration.ComponentId}'.");
            }
        }

        public IReadOnlyList<PluginSettingsPageRegistration> GetSettingsPagesSnapshot()
        {
            ThrowIfDisposed();
            return _settingsPages.Values
                .OrderBy(page => page.SortOrder)
                .ThenBy(page => page.Title, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public IReadOnlyList<PluginDesktopComponentRegistration> GetDesktopComponentsSnapshot()
        {
            ThrowIfDisposed();
            return _desktopComponents.Values
                .OrderBy(component => component.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(component => component.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        internal object? ResolveService(Type serviceType)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return null;
            }

            if (serviceType == typeof(IServiceProvider))
            {
                return Services;
            }

            lock (_serviceGate)
            {
                if (_registeredServices.TryGetValue(serviceType, out var service))
                {
                    return service;
                }

                foreach (var registeredService in _registeredServices.Values)
                {
                    if (serviceType.IsInstanceOfType(registeredService))
                    {
                        return registeredService;
                    }
                }
            }

            return _hostServices.GetService(serviceType);
        }

        private void RegisterBuiltInService<TService>(TService service)
            where TService : class
        {
            RegisterServiceCore(typeof(TService), service, allowOverride: true);
        }

        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            object[] services;
            lock (_serviceGate)
            {
                services = _serviceRegistrationOrder.ToArray();
                _registeredServices.Clear();
                _serviceRegistrationOrder.Clear();
            }

            _settingsPages.Clear();
            _desktopComponents.Clear();

            var disposedServices = new HashSet<object>(ReferenceEqualityComparer.Instance);
            for (var i = services.Length - 1; i >= 0; i--)
            {
                var service = services[i];
                if (ReferenceEquals(service, this) || !disposedServices.Add(service))
                {
                    continue;
                }

                if (service is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync();
                }
                else if (service is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }

        private void RegisterServiceCore(Type serviceType, object service, bool allowOverride)
        {
            ArgumentNullException.ThrowIfNull(serviceType);
            ArgumentNullException.ThrowIfNull(service);
            ThrowIfDisposed();

            if (!serviceType.IsInstanceOfType(service))
            {
                throw new InvalidOperationException(
                    $"Service instance '{service.GetType().FullName}' is not assignable to '{serviceType.FullName}'.");
            }

            lock (_serviceGate)
            {
                if (!allowOverride && _registeredServices.ContainsKey(serviceType))
                {
                    throw new InvalidOperationException(
                        $"Plugin '{Manifest.Id}' already registered a service for '{serviceType.FullName}'.");
                }

                _registeredServices[serviceType] = service;
                _serviceRegistrationOrder.Add(service);
            }
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(PluginContext));
            }
        }
    }

    private sealed class PluginCompositeServiceProvider : IServiceProvider
    {
        private readonly PluginContext _context;

        public PluginCompositeServiceProvider(PluginContext context)
        {
            _context = context;
        }

        public object? GetService(Type serviceType)
        {
            ArgumentNullException.ThrowIfNull(serviceType);
            return _context.ResolveService(serviceType);
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
