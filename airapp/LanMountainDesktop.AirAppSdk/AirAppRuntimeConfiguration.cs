namespace LanMountainDesktop.AirAppSdk;

public sealed record AirAppRuntimeConfiguration(string Mode = AirAppRuntimeModes.InProcess)
{
    public AirAppRuntimeMode RuntimeMode =>
        AirAppRuntimeModes.TryParse(Mode, out var mode) ? mode : AirAppRuntimeMode.InProcess;

    internal AirAppRuntimeConfiguration NormalizeAndValidate(string manifestPath)
    {
        return this with
        {
            Mode = AirAppRuntimeModes.NormalizeManifestValue(Mode, manifestPath)
        };
    }
}
