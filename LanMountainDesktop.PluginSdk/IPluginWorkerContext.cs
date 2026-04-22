using LanMountainDesktop.PluginIsolation.Contracts;

namespace LanMountainDesktop.PluginSdk;

public interface IPluginWorkerContext
{
    string PluginId { get; }

    PluginManifest Manifest { get; }

    PluginRuntimeMode RuntimeMode { get; }

    string SessionId { get; }

    string HostPipeName { get; }

    string ProtocolVersion { get; }

    string PluginDirectory { get; }

    string DataDirectory { get; }

    IReadOnlyList<PluginCapabilityDeclaration> GrantedCapabilities { get; }

    IReadOnlyDictionary<string, string> StartupProperties { get; }
}
