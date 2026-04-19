namespace LanMountainDesktop.Launcher.Models;

/// <summary>
/// GitHub Release 信息
/// </summary>
public sealed class ReleaseInfo
{
    public required string TagName { get; init; }
    public required string Name { get; init; }
    public required bool Prerelease { get; init; }
    public required DateTime PublishedAt { get; init; }
    public required List<ReleaseAsset> Assets { get; init; }
    public string? Body { get; init; }
    public string? VelopackFeedUrl { get; init; }
    public string? VelopackLegacyReleasesUrl { get; init; }
}

/// <summary>
/// Release 资源文件
/// </summary>
public sealed class ReleaseAsset
{
    public required string Name { get; init; }
    public required string BrowserDownloadUrl { get; init; }
    public required long Size { get; init; }
}
