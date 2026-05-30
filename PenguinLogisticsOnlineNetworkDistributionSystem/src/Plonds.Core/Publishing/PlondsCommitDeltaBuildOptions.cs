namespace Plonds.Core.Publishing;

public sealed record PlondsCommitDeltaBuildOptions(
    string Platform,
    string CurrentVersion,
    string CurrentPayloadZip,
    string OutputRoot,
    string Channel,
    string BaselineTag,
    string CurrentTag,
    string? FallbackBaselineZip = null,
    string? BaselineVersion = null,
    string LauncherRelativePath = "LanMountainDesktop.Launcher.exe",
    string HashAlgorithm = "sha256");
