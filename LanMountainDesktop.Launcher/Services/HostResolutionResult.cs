namespace LanMountainDesktop.Launcher.Services;

internal sealed class HostResolutionResult
{
    public bool Success { get; init; }

    public string? ResolvedHostPath { get; init; }

    public string? ResolutionSource { get; init; }

    public string AppRoot { get; init; } = string.Empty;

    public string? ExplicitAppRoot { get; init; }

    public bool DevModeConfigIgnored { get; init; }

    public List<string> SearchedPaths { get; init; } = [];
}
