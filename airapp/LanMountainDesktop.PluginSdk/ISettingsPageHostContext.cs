using Avalonia.Controls;

namespace LanMountainDesktop.PluginSdk;

public interface ISettingsPageHostContext
{
    void OpenDrawer(Control content, string? title = null);

    void CloseDrawer();

    void RequestRestart(string? reason = null);
}
