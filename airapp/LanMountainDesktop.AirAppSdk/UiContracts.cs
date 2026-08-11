using System.Text.Json;

namespace LanMountainDesktop.AirAppIsolation.Contracts;

public sealed record AirAppUiSurfaceDescriptor(
    string SurfaceId,
    string SurfaceKind,
    string Title,
    string? ComponentId = null);

public static class AirAppUiSurfaceKinds
{
    public const string DesktopComponent = "desktop-component";
    public const string ComponentEditor = "component-editor";
    public const string SettingsPage = "settings-page";
    public const string Window = "window";
}

public sealed record AirAppUiAttachRequest(
    string SurfaceId,
    string SurfaceKind,
    string? InstanceId = null,
    JsonElement? InitialState = null);

public sealed record AirAppUiAttachResponse(
    bool Accepted,
    JsonElement? InitialState = null,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public sealed record AirAppUiDetachNotification(
    string SurfaceId,
    string SurfaceKind,
    string? InstanceId = null);

public sealed record AirAppUiCommandRequest(
    string SurfaceId,
    string CommandName,
    string? InstanceId = null,
    JsonElement? Payload = null);

public sealed record AirAppUiCommandResponse(
    bool Accepted,
    JsonElement? Payload = null,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public sealed record AirAppUiStateChangedNotification(
    string SurfaceId,
    string SurfaceKind,
    string? InstanceId = null,
    JsonElement? State = null);
