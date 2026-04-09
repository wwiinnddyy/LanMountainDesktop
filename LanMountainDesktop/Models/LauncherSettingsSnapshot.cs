using System.Collections.Generic;

namespace LanMountainDesktop.Models;

public sealed class LauncherSettingsSnapshot
{
    public List<string> HiddenLauncherFolderPaths { get; set; } = [];

    public List<string> HiddenLauncherAppPaths { get; set; } = [];

    public bool ShowTileBackground { get; set; } = true;

    public LauncherSettingsSnapshot Clone()
    {
        var clone = (LauncherSettingsSnapshot)MemberwiseClone();
        clone.HiddenLauncherFolderPaths = HiddenLauncherFolderPaths is { Count: > 0 }
            ? new List<string>(HiddenLauncherFolderPaths)
            : [];
        clone.HiddenLauncherAppPaths = HiddenLauncherAppPaths is { Count: > 0 }
            ? new List<string>(HiddenLauncherAppPaths)
            : [];
        return clone;
    }
}
