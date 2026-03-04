namespace LanMountainDesktop.Models;

public sealed class StartMenuAppEntry
{
    public required string DisplayName { get; init; }

    public required string FilePath { get; init; }

    public required string RelativePath { get; init; }

    public byte[]? IconPngBytes { get; init; }
}
