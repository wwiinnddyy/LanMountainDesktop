namespace LanMountainDesktop.AirAppSdk;

/// <summary>
/// Provides appearance and theme context.
/// </summary>
public interface IAirAppAppearanceContext
{
    /// <summary>
    /// Gets the current appearance snapshot.
    /// </summary>
    AirAppAppearanceSnapshot CurrentSnapshot { get; }

    /// <summary>
    /// Subscribe to appearance changes.
    /// </summary>
    /// <param name="handler">Change handler</param>
    /// <returns>Subscription token</returns>
    IDisposable SubscribeToChanges(Action<AirAppAppearanceSnapshot> handler);
}
