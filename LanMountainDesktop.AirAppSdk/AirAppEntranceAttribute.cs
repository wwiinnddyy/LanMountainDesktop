namespace LanMountainDesktop.AirAppSdk;

/// <summary>
/// Marks a class as the entry point for an AirApp.
/// The marked class must inherit from AirAppBase or implement IAirApp.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AirAppEntranceAttribute : Attribute
{
}
