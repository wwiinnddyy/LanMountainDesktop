using LanMountainDesktop.PluginIsolation.Contracts;

namespace LanMountainDesktop.PluginIsolation.Ipc;

public static class PluginIpcRoutedNotifyIds
{
    public const string SessionReady = PluginIpcRoutes.Session.Ready;
    public const string LifecycleStateChanged = PluginIpcRoutes.Lifecycle.StateChanged;
    public const string SettingsChanged = PluginIpcRoutes.Settings.Changed;
    public const string AppearanceChanged = PluginIpcRoutes.Appearance.Changed;
    public const string UiDetach = PluginIpcRoutes.Ui.Detach;
    public const string UiStateChanged = PluginIpcRoutes.Ui.StateChanged;
    public const string HeartbeatPing = PluginIpcRoutes.Heartbeat.Ping;
    public const string HeartbeatPong = PluginIpcRoutes.Heartbeat.Pong;
    public const string LogWrite = PluginIpcRoutes.Log.Write;
    public const string FaultReport = PluginIpcRoutes.Fault.Report;
}
