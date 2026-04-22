using System.Text.Json;

namespace LanMountainDesktop.PluginIsolation.Ipc;

public delegate Task<JsonElement?> PluginIpcRequestDispatcher(
    string route,
    JsonElement? payload,
    CancellationToken cancellationToken);

public delegate Task PluginIpcNotificationDispatcher(
    string route,
    JsonElement? payload,
    CancellationToken cancellationToken);
