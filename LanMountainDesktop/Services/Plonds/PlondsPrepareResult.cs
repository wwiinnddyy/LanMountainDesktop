namespace LanMountainDesktop.Services.Plonds;

internal sealed record PlondsPrepareResult(
    bool Success,
    PlondsPreparedPackage? Package,
    string? ErrorMessage,
    bool RequiresUiHandling)
{
    public static PlondsPrepareResult Prepared(PlondsPreparedPackage package) => new(true, package, null, false);

    public static PlondsPrepareResult FailedForUi(string message) => new(false, null, message, true);
}
