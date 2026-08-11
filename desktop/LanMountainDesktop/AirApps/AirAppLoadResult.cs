using System;

using LanMountainDesktop.AirAppSdk;

namespace LanMountainDesktop.AirApps;

public sealed record AirAppLoadResult(
    string SourcePath,
    AirAppManifest? Manifest,
    LoadedAirApp? LoadedAirApp,
    Exception? Error)
{
    public bool IsSuccess => LoadedAirApp is not null && Error is null;

    public static AirAppLoadResult Success(string sourcePath, AirAppManifest manifest, LoadedAirApp loadedAirApp)
    {
        return new AirAppLoadResult(sourcePath, manifest, loadedAirApp, null);
    }

    public static AirAppLoadResult Failure(string sourcePath, AirAppManifest? manifest, Exception error)
    {
        return new AirAppLoadResult(sourcePath, manifest, null, error);
    }
}
