using Avalonia.Controls;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.ComponentEditors;

public abstract class ComponentEditorViewBase : UserControl
{
    protected ComponentEditorViewBase(DesktopComponentEditorContext? context)
    {
        Context = context;
    }

    protected DesktopComponentEditorContext? Context { get; }

    protected LocalizationService LocalizationService { get; } = new();

    protected string LanguageCode =>
        LocalizationService.NormalizeLanguageCode(Context?.SettingsFacade.Region.Get().LanguageCode);

    protected string L(string key, string fallback)
    {
        return LocalizationService.GetString(LanguageCode, key, fallback);
    }

    protected ComponentSettingsSnapshot LoadSnapshot()
    {
        return Context?.ComponentSettingsAccessor.LoadSnapshot<ComponentSettingsSnapshot>() ?? new ComponentSettingsSnapshot();
    }

    protected void SaveSnapshot(ComponentSettingsSnapshot snapshot, params string[] changedKeys)
    {
        if (Context is null)
        {
            return;
        }

        Context.ComponentSettingsAccessor.SaveSnapshot(
            snapshot,
            changedKeys.Length == 0 ? null : changedKeys);
        Context.HostContext.RequestRefresh();
    }
}
