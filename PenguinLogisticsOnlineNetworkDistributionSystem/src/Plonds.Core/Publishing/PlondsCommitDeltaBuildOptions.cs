namespace Plonds.Core.Publishing;

public sealed record PlondsCommitDeltaBuildOptions(
    string Platform,
    string CurrentVersion,
    string CurrentPayloadZip,
    string OutputRoot,
    string Channel,
    string BaselineTag,
    string CurrentTag,
    string HashAlgorithm = "sha256",
    string? SourceDirs = null,
    string? FallbackBaselineZip = null,
    string LauncherRelativePath = "LanMountainDesktop.Launcher.exe");
