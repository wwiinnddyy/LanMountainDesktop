using LanMountainDesktop.AirAppIsolation.Contracts;

namespace LanMountainDesktop.AirAppIsolation.Ipc;

public static class AirAppIpcRoutedNotifyIds
{
    public const string SessionReady = AirAppIpcRoutes.Session.Ready;
    public const string LifecycleStateChanged = AirAppIpcRoutes.Lifecycle.StateChanged;
    public const string SettingsChanged = AirAppIpcRoutes.Settings.Changed;
    public const string AppearanceChanged = AirAppIpcRoutes.Appearance.Changed;
    public const string UiDetach = AirAppIpcRoutes.Ui.Detach;
    public const string UiStateChanged = AirAppIpcRoutes.Ui.StateChanged;
    public const string HeartbeatPing = AirAppIpcRoutes.Heartbeat.Ping;
    public const string HeartbeatPong = AirAppIpcRoutes.Heartbeat.Pong;
    public const string LogWrite = AirAppIpcRoutes.Log.Write;
    public const string FaultReport = AirAppIpcRoutes.Fault.Report;
}
