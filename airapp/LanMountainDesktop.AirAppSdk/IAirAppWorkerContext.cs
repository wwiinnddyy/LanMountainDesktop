using LanMountainDesktop.AirAppIsolation.Contracts;

namespace LanMountainDesktop.AirAppSdk;

public interface IAirAppWorkerContext
{
    string AirAppId { get; }

    AirAppManifest Manifest { get; }

    AirAppRuntimeMode RuntimeMode { get; }

    string SessionId { get; }

    string HostPipeName { get; }

    string ProtocolVersion { get; }

    string AirAppDirectory { get; }

    string DataDirectory { get; }

    IReadOnlyList<AirAppCapabilityDeclaration> GrantedCapabilities { get; }

    IReadOnlyDictionary<string, string> StartupProperties { get; }
}
