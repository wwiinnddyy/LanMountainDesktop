using LanMountainDesktop.Launcher.Models;

namespace LanMountainDesktop.Launcher.Update;

internal static class UpdateEngineResults
{
    public static LauncherResult Failed(string stage, string code, string message)
    {
        return new LauncherResult
        {
            Success = false,
            Stage = stage,
            Code = code,
            Message = message,
            ErrorMessage = message
        };
    }
}
