namespace LanMountainDesktop.Launcher.Models;

/// <summary>
/// 更新检查结果
/// </summary>
public sealed class UpdateCheckResult
{
    public bool HasUpdate { get; init; }
    public string? LatestVersion { get; init; }
    public string? CurrentVersion { get; init; }
    public ReleaseInfo? Release { get; init; }
    public string? ErrorMessage { get; init; }
}
