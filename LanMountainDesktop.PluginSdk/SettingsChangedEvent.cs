using System.Collections.Generic;

namespace LanMountainDesktop.PluginSdk;

public sealed class SettingsChangedEvent
{
    public SettingsChangedEvent(
        SettingsScope scope,
        string? subjectId = null,
        string? placementId = null,
        string? sectionId = null,
        IReadOnlyCollection<string>? changedKeys = null)
    {
        Scope = scope;
        SubjectId = string.IsNullOrWhiteSpace(subjectId) ? null : subjectId.Trim();
        PlacementId = string.IsNullOrWhiteSpace(placementId) ? null : placementId.Trim();
        SectionId = string.IsNullOrWhiteSpace(sectionId) ? null : sectionId.Trim();
        ChangedKeys = changedKeys is { Count: > 0 }
            ? changedKeys.ToArray()
            : [];
    }

    public SettingsScope Scope { get; }

    public string? SubjectId { get; }

    public string? PlacementId { get; }

    public string? SectionId { get; }

    public IReadOnlyCollection<string> ChangedKeys { get; }
}
