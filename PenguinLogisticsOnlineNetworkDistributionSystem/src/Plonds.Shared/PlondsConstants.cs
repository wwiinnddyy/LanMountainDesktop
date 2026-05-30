namespace Plonds.Shared;

public static class PlondsConstants
{
    public const string ProtocolName = "PLONDS";
    public const string ProtocolVersion = "2.0";
    public const string FormatVersion = "2.0";

    public const string ActionAdd = "add";
    public const string ActionReplace = "replace";
    public const string ActionReuse = "reuse";
    public const string ActionDelete = "delete";

    public const string HashAlgorithmSha256 = "sha256";
    public const string HashAlgorithmMd5 = "md5";

    public const string DefaultLauncherRelativePath = "LanMountainDesktop.Launcher.exe";

    public const string CompareMethodFileCompare = "file-compare";
    public const string CompareMethodCommitAnalyze = "commit-analyze";

    public static readonly string[] SupportedActions =
    [
        ActionAdd,
        ActionReplace,
        ActionReuse,
        ActionDelete
    ];

    public static readonly string[] SupportedHashAlgorithms =
    [
        HashAlgorithmSha256,
        HashAlgorithmMd5
    ];

    public static readonly string[] DefaultSourceDirs =
    [
        "LanMountainDesktop",
        "LanMountainDesktop.Launcher",
        "LanMountainDesktop.Shared.Contracts",
        "LanMountainDesktop.PluginSdk",
        "LanMountainDesktop.Appearance",
        "LanMountainDesktop.Settings.Core",
        "LanMountainDesktop.ComponentSystem"
    ];
}
