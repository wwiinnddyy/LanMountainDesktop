using System;
using System.Collections.Generic;
using System.Linq;

namespace LanMountainDesktop.Services;

internal sealed record TelemetryEvent(
    string EventName,
    string DistinctId,
    string InstallId,
    string TelemetryId,
    string SessionId,
    long Sequence,
    DateTimeOffset Timestamp,
    IReadOnlyDictionary<string, object?> Payload,
    IReadOnlyDictionary<string, object?>? StateBefore = null,
    IReadOnlyDictionary<string, object?>? StateAfter = null)
{
    public Dictionary<string, object?> ToPostHogProperties()
    {
        var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["install_id"] = InstallId,
            ["telemetry_id"] = TelemetryId,
            ["session_id"] = SessionId,
            ["sequence"] = Sequence,
            ["timestamp_utc"] = Timestamp.ToString("o"),
            ["app_version"] = TelemetryEnvironmentInfo.GetAppVersion(),
            ["os_name"] = TelemetryEnvironmentInfo.GetOsName(),
            ["os_version"] = TelemetryEnvironmentInfo.GetOsVersion(),
            ["device_model"] = TelemetryEnvironmentInfo.GetDeviceModel(),
            ["device_arch"] = TelemetryEnvironmentInfo.GetDeviceArchitecture(),
            ["runtime_version"] = TelemetryEnvironmentInfo.GetRuntimeVersion(),
            ["language"] = TelemetryEnvironmentInfo.GetSystemLanguage(),
            ["payload"] = Copy(Payload)
        };

        if (StateBefore is not null && StateBefore.Count > 0)
        {
            properties["state_before"] = Copy(StateBefore);
        }

        if (StateAfter is not null && StateAfter.Count > 0)
        {
            properties["state_after"] = Copy(StateAfter);
        }

        return properties;
    }

    private static Dictionary<string, object?> Copy(IReadOnlyDictionary<string, object?> source)
    {
        return source.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);
    }
}
