namespace LanMountainDesktop.Services.Plonds;

internal sealed record PlondsInstallResult(
    bool Success,
    string? ErrorMessage,
    string? ErrorCode = null);
