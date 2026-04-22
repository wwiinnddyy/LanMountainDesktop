namespace LanMountainDesktop.PluginSdk;

public sealed record PluginRuntimeConfiguration(string Mode = PluginRuntimeModes.InProcess)
{
    public PluginRuntimeMode RuntimeMode =>
        PluginRuntimeModes.TryParse(Mode, out var mode) ? mode : PluginRuntimeMode.InProcess;

    internal PluginRuntimeConfiguration NormalizeAndValidate(string manifestPath)
    {
        return this with
        {
            Mode = PluginRuntimeModes.NormalizeManifestValue(Mode, manifestPath)
        };
    }
}
