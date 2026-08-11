using Avalonia;
using Avalonia.Threading;

namespace LanMountainDesktop.DesktopEditing;

internal static class DesktopEditAnimationRuntime
{
    public static bool CanUseTransitions()
    {
        return Application.Current is not null && Dispatcher.UIThread.CheckAccess();
    }
}
