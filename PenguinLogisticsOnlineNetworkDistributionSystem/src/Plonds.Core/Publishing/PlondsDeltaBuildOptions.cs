namespace Plonds.Core.Publishing;

public sealed record PlondsDeltaBuildOptions(
    string Platform,
    string CurrentVersion,
    string CurrentPayloadZip,
    string OutputRoot,
    string Channel = "stable",
    string? BaselineVersion = null,
    string? BaselinePayloadZip = null,
    string LauncherRelativePath = "LanMountainDesktop.Launcher.exe",
    string HashAlgorithm = "sha256");
