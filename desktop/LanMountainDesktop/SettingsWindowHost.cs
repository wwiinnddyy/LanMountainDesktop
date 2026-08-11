using System;

namespace LanMountainDesktop.DesktopHost;

public sealed class SettingsWindowHost
{
    private readonly Action<string, string?> _openSettingsWindow;

    public SettingsWindowHost(Action<string, string?> openSettingsWindow)
    {
        _openSettingsWindow = openSettingsWindow ?? throw new ArgumentNullException(nameof(openSettingsWindow));
    }

    public void Open(string source, string? pageId = null)
    {
        _openSettingsWindow(source, pageId);
    }
}
