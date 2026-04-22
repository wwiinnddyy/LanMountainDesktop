namespace LanMountainDesktop.PluginSdk;

public static class PluginRuntimeModes
{
    public const string InProcess = "in-proc";
    public const string IsolatedBackground = "isolated-background";
    public const string IsolatedWindow = "isolated-window";

    public static bool TryParse(string? value, out PluginRuntimeMode mode)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case null:
            case "":
            case InProcess:
                mode = PluginRuntimeMode.InProcess;
                return true;
            case IsolatedBackground:
                mode = PluginRuntimeMode.IsolatedBackground;
                return true;
            case IsolatedWindow:
                mode = PluginRuntimeMode.IsolatedWindow;
                return true;
            default:
                mode = default;
                return false;
        }
    }

    public static PluginRuntimeMode Parse(string? value, string sourceName, string propertyName = "runtime.mode")
    {
        if (TryParse(value, out var mode))
        {
            return mode;
        }

        var candidate = string.IsNullOrWhiteSpace(value) ? "<empty>" : value.Trim();
        throw new InvalidOperationException(
            $"Plugin manifest '{sourceName}' declares unsupported runtime mode '{candidate}' in '{propertyName}'. " +
            $"Supported values: '{InProcess}', '{IsolatedBackground}', '{IsolatedWindow}'.");
    }

    public static string NormalizeManifestValue(string? value, string sourceName, string propertyName = "runtime.mode")
    {
        return ToManifestValue(Parse(value, sourceName, propertyName));
    }

    public static string ToManifestValue(PluginRuntimeMode mode)
    {
        return mode switch
        {
            PluginRuntimeMode.InProcess => InProcess,
            PluginRuntimeMode.IsolatedBackground => IsolatedBackground,
            PluginRuntimeMode.IsolatedWindow => IsolatedWindow,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported plugin runtime mode.")
        };
    }
}
