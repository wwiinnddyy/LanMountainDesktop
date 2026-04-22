using System.Text.Json;

namespace LanMountainDesktop.PluginIsolation.Contracts;

public sealed record PluginUiSurfaceDescriptor(
    string SurfaceId,
    string SurfaceKind,
    string Title,
    string? ComponentId = null);

public static class PluginUiSurfaceKinds
{
    public const string DesktopComponent = "desktop-component";
    public const string ComponentEditor = "component-editor";
    public const string SettingsPage = "settings-page";
    public const string Window = "window";
}

public sealed record PluginUiAttachRequest(
    string SurfaceId,
    string SurfaceKind,
    string? InstanceId = null,
    JsonElement? InitialState = null);

public sealed record PluginUiAttachResponse(
    bool Accepted,
    JsonElement? InitialState = null,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public sealed record PluginUiDetachNotification(
    string SurfaceId,
    string SurfaceKind,
    string? InstanceId = null);

public sealed record PluginUiCommandRequest(
    string SurfaceId,
    string CommandName,
    string? InstanceId = null,
    JsonElement? Payload = null);

public sealed record PluginUiCommandResponse(
    bool Accepted,
    JsonElement? Payload = null,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public sealed record PluginUiStateChangedNotification(
    string SurfaceId,
    string SurfaceKind,
    string? InstanceId = null,
    JsonElement? State = null);
