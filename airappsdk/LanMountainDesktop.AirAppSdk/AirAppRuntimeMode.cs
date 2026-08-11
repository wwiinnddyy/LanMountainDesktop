namespace LanMountainDesktop.AirAppSdk;

/// <summary>
/// Runtime mode for AirApps.
/// </summary>
public enum AirAppRuntimeMode
{
    /// <summary>
    /// Run in the host process (best performance, shared memory).
    /// </summary>
    InProcess = 0,

    /// <summary>
    /// Run in an isolated background process (safer, separate memory).
    /// </summary>
    IsolatedBackground = 1,

    /// <summary>
    /// Run in an isolated window process (full isolation).
    /// </summary>
    IsolatedWindow = 2
}

/// <summary>
/// Helper for parsing runtime modes.
/// </summary>
public static class AirAppRuntimeModes
{
    public static bool TryParse(string? mode, out AirAppRuntimeMode result)
    {
        result = AirAppRuntimeMode.InProcess;

        if (string.IsNullOrWhiteSpace(mode))
        {
            return false;
        }

        var normalized = mode.Trim().ToLowerInvariant();
        return normalized switch
        {
            "in-process" => SetResult(AirAppRuntimeMode.InProcess, out result),
            "isolated-background" => SetResult(AirAppRuntimeMode.IsolatedBackground, out result),
            "isolated-window" => SetResult(AirAppRuntimeMode.IsolatedWindow, out result),
            _ => false
        };
    }

    private static bool SetResult(AirAppRuntimeMode mode, out AirAppRuntimeMode result)
    {
        result = mode;
        return true;
    }
}
