using System;

namespace LanMountainDesktop.DesktopHost;

public sealed class DesktopStartupCoordinator
{
    private readonly Action _restoreWorkspaceState;

    public DesktopStartupCoordinator(Action restoreWorkspaceState)
    {
        _restoreWorkspaceState = restoreWorkspaceState ?? throw new ArgumentNullException(nameof(restoreWorkspaceState));
    }

    public void Restore() => _restoreWorkspaceState();
}
