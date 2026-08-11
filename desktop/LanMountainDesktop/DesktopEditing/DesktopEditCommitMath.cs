namespace LanMountainDesktop.DesktopEditing;

internal static class DesktopEditCommitMath
{
    public static bool IsPendingCommitValid(bool isPending, int scheduledVersion, int currentVersion)
    {
        return isPending && scheduledVersion == currentVersion;
    }
}
