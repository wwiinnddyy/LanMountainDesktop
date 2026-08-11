namespace LanMountainDesktop.AirAppSdk;

/// <summary>
/// Describes a window entry contributed by an AirApp.
/// Produced by <c>AddAirAppWindow&lt;TWindow&gt;</c> and resolved by the AirAppHost
/// window loader to open the window content inside the host window shell.
/// </summary>
public sealed record AirAppWindowRegistration(
    string WindowId,
    string DisplayName,
    Type WindowType);
