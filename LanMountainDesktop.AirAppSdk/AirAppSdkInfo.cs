namespace LanMountainDesktop.AirAppSdk;

/// <summary>
/// AirApp SDK information.
/// </summary>
public static class AirAppSdkInfo
{
    /// <summary>
    /// Current SDK version.
    /// </summary>
    public const string SdkVersion = "6.0.0";

    /// <summary>
    /// Current API version.
    /// AirApps must target this major version to be compatible.
    /// </summary>
    public const string ApiVersion = "6.0.0";

    /// <summary>
    /// Gets the SDK display name.
    /// </summary>
    public static string DisplayName => "LanMountainDesktop AirApp SDK";

    /// <summary>
    /// Gets the default manifest file name.
    /// </summary>
    public const string ManifestFileName = "airapp.json";

    /// <summary>
    /// Gets the package file extension.
    /// </summary>
    public const string PackageExtension = ".laapp";
}
