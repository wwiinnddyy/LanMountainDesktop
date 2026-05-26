using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.Shared.Contracts.Update;

namespace LanMountainDesktop.Services.Update;

internal sealed class SettingsUpdateManifestProvider : IUpdateManifestProvider
{
    private readonly ISettingsFacadeService _settingsFacade;
    private readonly IUpdateManifestProvider _plondsWithFallback;
    private readonly IUpdateManifestProvider _github;

    public SettingsUpdateManifestProvider(
        ISettingsFacadeService settingsFacade,
        IUpdateManifestProvider plonds,
        IUpdateManifestProvider github)
    {
        _settingsFacade = settingsFacade ?? throw new ArgumentNullException(nameof(settingsFacade));
        _github = github ?? throw new ArgumentNullException(nameof(github));
        _plondsWithFallback = new CompositeManifestProvider(plonds ?? throw new ArgumentNullException(nameof(plonds)), _github);
    }

    public string ProviderName => "settings-selected-update-source";

    public Task<UpdateManifest?> GetLatestAsync(
        string channel,
        string platform,
        Version currentVersion,
        CancellationToken ct)
    {
        return SelectProvider().GetLatestAsync(channel, platform, currentVersion, ct);
    }

    public Task<UpdateManifest?> GetByVersionAsync(
        string version,
        string channel,
        string platform,
        CancellationToken ct)
    {
        return SelectProvider().GetByVersionAsync(version, channel, platform, ct);
    }

    public Task<IReadOnlyList<UpdateManifest>> GetIncrementalChainAsync(
        string channel,
        string platform,
        Version fromVersion,
        Version toVersion,
        CancellationToken ct)
    {
        return SelectProvider().GetIncrementalChainAsync(channel, platform, fromVersion, toVersion, ct);
    }

    private IUpdateManifestProvider SelectProvider()
    {
        var source = UpdateSettingsValues.NormalizeDownloadSource(_settingsFacade.Update.Get().UpdateDownloadSource);
        return string.Equals(source, UpdateSettingsValues.DownloadSourceGitHub, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(source, UpdateSettingsValues.DownloadSourceGhProxy, StringComparison.OrdinalIgnoreCase)
            ? _github
            : _plondsWithFallback;
    }
}
