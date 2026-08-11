using LanMountainDesktop.Shared.Contracts.Update;

namespace LanMountainDesktop.Services.Update;

internal sealed class CompositeManifestProvider : IUpdateManifestProvider
{
    private readonly IUpdateManifestProvider _primary;
    private readonly IUpdateManifestProvider _fallback;

    public string ProviderName => $"{_primary.ProviderName}+{_fallback.ProviderName}";

    public CompositeManifestProvider(IUpdateManifestProvider primary, IUpdateManifestProvider fallback)
    {
        _primary = primary ?? throw new ArgumentNullException(nameof(primary));
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
    }

    public async Task<UpdateManifest?> GetLatestAsync(
        string channel,
        string platform,
        Version currentVersion,
        CancellationToken ct)
    {
        try
        {
            var result = await _primary.GetLatestAsync(channel, platform, currentVersion, ct);
            if (result is not null)
            {
                return result;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Update", $"{_primary.ProviderName} GetLatestAsync failed: {ex.Message}", ex);
        }

        AppLogger.Info("Update", $"Falling back to {_fallback.ProviderName} for GetLatestAsync");
        return await _fallback.GetLatestAsync(channel, platform, currentVersion, ct);
    }

    public async Task<UpdateManifest?> GetByVersionAsync(
        string version,
        string channel,
        string platform,
        CancellationToken ct)
    {
        try
        {
            var result = await _primary.GetByVersionAsync(version, channel, platform, ct);
            if (result is not null)
            {
                return result;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Update", $"{_primary.ProviderName} GetByVersionAsync failed: {ex.Message}", ex);
        }

        AppLogger.Info("Update", $"Falling back to {_fallback.ProviderName} for GetByVersionAsync");
        return await _fallback.GetByVersionAsync(version, channel, platform, ct);
    }

    public async Task<IReadOnlyList<UpdateManifest>> GetIncrementalChainAsync(
        string channel,
        string platform,
        Version fromVersion,
        Version toVersion,
        CancellationToken ct)
    {
        try
        {
            var result = await _primary.GetIncrementalChainAsync(channel, platform, fromVersion, toVersion, ct);
            if (result is not null && result.Count > 0)
            {
                return result;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Update", $"{_primary.ProviderName} GetIncrementalChainAsync failed: {ex.Message}", ex);
        }

        AppLogger.Info("Update", $"Falling back to {_fallback.ProviderName} for GetIncrementalChainAsync");
        return await _fallback.GetIncrementalChainAsync(channel, platform, fromVersion, toVersion, ct);
    }
}
