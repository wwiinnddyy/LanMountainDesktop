using System;
using LanMountainDesktop.Models;

namespace LanMountainDesktop.Services;

public interface IWhiteboardNotePersistenceService
{
    WhiteboardNoteSnapshot LoadNote(string componentId, string? placementId, int retentionDays);

    void SaveNote(string componentId, string? placementId, WhiteboardNoteSnapshot snapshot, int retentionDays);

    bool DeleteNote(string componentId, string? placementId);

    bool TryDeleteExpiredNote(string componentId, string? placementId, int retentionDays);

    bool IsExpired(WhiteboardNoteSnapshot snapshot, int retentionDays, DateTimeOffset? now = null);

    DateTimeOffset? GetExpirationUtc(WhiteboardNoteSnapshot snapshot, int retentionDays);
}
