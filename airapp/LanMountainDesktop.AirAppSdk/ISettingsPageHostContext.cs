using Avalonia.Controls;

namespace LanMountainDesktop.AirAppSdk;

public interface ISettingsPageHostContext
{
    void OpenDrawer(Control content, string? title = null);

    void CloseDrawer();

    void RequestRestart(string? reason = null);
}
