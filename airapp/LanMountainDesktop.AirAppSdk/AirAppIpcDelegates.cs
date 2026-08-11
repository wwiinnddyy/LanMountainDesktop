using System.Text.Json;

namespace LanMountainDesktop.AirAppIsolation.Ipc;

public delegate Task<JsonElement?> AirAppIpcRequestDispatcher(
    string route,
    JsonElement? payload,
    CancellationToken cancellationToken);

public delegate Task AirAppIpcNotificationDispatcher(
    string route,
    JsonElement? payload,
    CancellationToken cancellationToken);
