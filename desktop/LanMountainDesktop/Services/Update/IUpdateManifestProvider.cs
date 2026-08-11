using LanMountainDesktop.Shared.Contracts.Update;

namespace LanMountainDesktop.Services.Update;

public interface IUpdateManifestProvider
{
    string ProviderName { get; }

    Task<UpdateManifest?> GetLatestAsync(
        string channel,
        string platform,
        Version currentVersion,
        CancellationToken ct);

    Task<UpdateManifest?> GetByVersionAsync(
        string version,
        string channel,
        string platform,
        CancellationToken ct);

    Task<IReadOnlyList<UpdateManifest>> GetIncrementalChainAsync(
        string channel,
        string platform,
        Version fromVersion,
        Version toVersion,
        CancellationToken ct);
}
