using LanMountainDesktop.Models;
using LanMountainDesktop.Services.Settings;

namespace LanMountainDesktop.Services;

public sealed class DesktopLayoutSettingsService
{
    private readonly IComponentLayoutStore _layoutStore = ComponentDomainStorageProvider.Instance;

    public DesktopLayoutSettingsSnapshot Load()
    {
        return _layoutStore.LoadLayout();
    }

    public void Save(DesktopLayoutSettingsSnapshot snapshot)
    {
        _layoutStore.SaveLayout(snapshot ?? new DesktopLayoutSettingsSnapshot());
    }
}
