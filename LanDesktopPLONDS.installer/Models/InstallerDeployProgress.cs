namespace LanDesktopPLONDS.Installer.Models;

public sealed record InstallerDeployProgress(
    string Stage,
    string? TargetVersion,
    double DownloadProgress,
    double InstallProgress,
    string? CurrentFile,
    long BytesDownloaded,
    long? TotalBytes);
