namespace LanMountainDesktop.Launcher.Models;

internal sealed class VelopackReleaseFeed
{
    public List<VelopackReleaseAsset> Assets { get; set; } = [];
}

internal sealed class VelopackReleaseAsset
{
    public string PackageId { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string? SHA1 { get; set; }

    public string? SHA256 { get; set; }

    public long Size { get; set; }
}
