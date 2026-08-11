namespace LanMountainDesktop.AirAppSdk;

public static class AirAppRuntimeModes
{
    public const string InProcess = "in-proc";
    public const string InProcessAlt = "in-process";
    public const string IsolatedBackground = "isolated-background";
    public const string IsolatedWindow = "isolated-window";

    public static bool TryParse(string? value, out AirAppRuntimeMode mode)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case null:
            case "":
            case InProcess:
            case InProcessAlt:
                mode = AirAppRuntimeMode.InProcess;
                return true;
            case IsolatedBackground:
                mode = AirAppRuntimeMode.IsolatedBackground;
                return true;
            case IsolatedWindow:
                mode = AirAppRuntimeMode.IsolatedWindow;
                return true;
            default:
                mode = default;
                return false;
        }
    }

    public static AirAppRuntimeMode Parse(string? value, string sourceName, string propertyName = "runtime.mode")
    {
        if (TryParse(value, out var mode))
        {
            return mode;
        }

        var candidate = string.IsNullOrWhiteSpace(value) ? "<empty>" : value.Trim();
        throw new InvalidOperationException(
            $"AirApp manifest '{sourceName}' declares unsupported runtime mode '{candidate}' in '{propertyName}'. " +
            $"Supported values: '{InProcess}', '{IsolatedBackground}', '{IsolatedWindow}'.");
    }

    public static string NormalizeManifestValue(string? value, string sourceName, string propertyName = "runtime.mode")
    {
        return ToManifestValue(Parse(value, sourceName, propertyName));
    }

    public static string ToManifestValue(AirAppRuntimeMode mode)
    {
        return mode switch
        {
            AirAppRuntimeMode.InProcess => InProcess,
            AirAppRuntimeMode.IsolatedBackground => IsolatedBackground,
            AirAppRuntimeMode.IsolatedWindow => IsolatedWindow,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported plugin runtime mode.")
        };
    }
}
