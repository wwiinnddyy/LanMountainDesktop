namespace LanMountainDesktop.Launcher;

internal static class LauncherRuntimeContext
{
    public static CommandContext Current { get; set; } = CommandContext.FromArgs([]);
}
