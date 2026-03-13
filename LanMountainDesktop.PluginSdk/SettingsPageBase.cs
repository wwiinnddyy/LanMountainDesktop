using System;
using Avalonia.Controls;

namespace LanMountainDesktop.PluginSdk;

public abstract class SettingsPageBase : UserControl
{
    public static readonly string DialogHostIdentifier = "LanMountainDesktop.SettingsWindow";

    private ISettingsPageHostContext? _hostContext;

    public ISettingsPageHostContext? HostContext => _hostContext;

    public Uri? NavigationUri { get; set; }

    public void InitializeHostContext(ISettingsPageHostContext hostContext)
    {
        _hostContext = hostContext;
    }

    public virtual void OnNavigatedTo(object? parameter)
    {
    }

    protected void OpenDrawer(Control content, string? title = null)
    {
        _hostContext?.OpenDrawer(content, title);
    }

    protected void OpenDrawer(object content, bool usePageDataContext = false, object? dataContext = null, string? title = null)
    {
        if (content is Control control && !usePageDataContext)
        {
            control.DataContext = dataContext ?? DataContext ?? this;
            OpenDrawer(control, title);
            return;
        }

        if (content is Control drawerControl)
        {
            OpenDrawer(drawerControl, title);
        }
    }

    protected void CloseDrawer()
    {
        _hostContext?.CloseDrawer();
    }

    protected void RequestRestart(string? reason = null)
    {
        _hostContext?.RequestRestart(reason);
    }
}
