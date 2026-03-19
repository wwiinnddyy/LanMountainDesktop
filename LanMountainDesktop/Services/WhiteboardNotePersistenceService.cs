using System;
using System.Text.Json;
using LanMountainDesktop.Models;
using Microsoft.Data.Sqlite;

namespace LanMountainDesktop.Services;

public sealed class WhiteboardNotePersistenceService : IWhiteboardNotePersistenceService
{
    private const int DefaultCleanupBatchSize = 256;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly object _schemaSyncRoot = new();
    private readonly AppDatabaseService _databaseService;
    private bool _schemaInitialized;

    public WhiteboardNotePersistenceService(AppDatabaseService? databaseService = null)
    {
        _databaseService = databaseService ?? AppDatabaseServiceFactory.CreateDefault();
    }

    public WhiteboardNoteSnapshot LoadNote(string componentId, string? placementId, int retentionDays)
    {
        if (!TryNormalizeKeys(componentId, placementId, out var normalizedComponentId, out var normalizedPlacementId))
        {
            return new WhiteboardNoteSnapshot();
        }

        try
        {
            using var connection = OpenConnection();
            DeleteExpiredInternal(
                connection,
                normalizedComponentId,
                normalizedPlacementId,
                WhiteboardNoteRetentionPolicy.NormalizeDays(retentionDays),
                DateTimeOffset.UtcNow);

            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT note_json, saved_at_utc_ms
                FROM whiteboard_notes
                WHERE component_id = $componentId
                  AND placement_id = $placementId
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$componentId", normalizedComponentId);
            command.Parameters.AddWithValue("$placementId", normalizedPlacementId);

            using var reader = command.ExecuteReader();
            if (!reader.Read() || reader.IsDBNull(0))
            {
                return new WhiteboardNoteSnapshot();
            }

            var json = reader.GetString(0);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new WhiteboardNoteSnapshot();
            }

            var snapshot = JsonSerializer.Deserialize<WhiteboardNoteSnapshot>(json, JsonOptions) ?? new WhiteboardNoteSnapshot();
            if (!reader.IsDBNull(1))
            {
                snapshot.SavedUtc = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1));
            }

            if (IsExpired(snapshot, retentionDays))
            {
                DeleteNote(normalizedComponentId, normalizedPlacementId);
                return new WhiteboardNoteSnapshot();
            }

            return snapshot.Clone();
        }
        catch
        {
            return new WhiteboardNoteSnapshot();
        }
    }

    public void SaveNote(string componentId, string? placementId, WhiteboardNoteSnapshot snapshot, int retentionDays)
    {
        if (!TryNormalizeKeys(componentId, placementId, out var normalizedComponentId, out var normalizedPlacementId))
        {
            return;
        }

        try
        {
            var nowUtc = DateTimeOffset.UtcNow;
            var persistedSnapshot = snapshot?.Clone() ?? new WhiteboardNoteSnapshot();
            persistedSnapshot.SavedUtc = nowUtc;
            var expiresUtc = GetExpirationUtc(persistedSnapshot, retentionDays) ?? nowUtc.AddDays(WhiteboardNoteRetentionPolicy.DefaultDays);
            var json = JsonSerializer.Serialize(persistedSnapshot, JsonOptions);

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO whiteboard_notes(
                    component_id,
                    placement_id,
                    note_json,
                    saved_at_utc_ms,
                    expires_at_utc_ms,
                    updated_at_utc_ms)
                VALUES(
                    $componentId,
                    $placementId,
                    $noteJson,
                    $savedAtUtcMs,
                    $expiresAtUtcMs,
                    $updatedAtUtcMs)
                ON CONFLICT(component_id, placement_id) DO UPDATE SET
                    note_json = excluded.note_json,
                    saved_at_utc_ms = excluded.saved_at_utc_ms,
                    expires_at_utc_ms = excluded.expires_at_utc_ms,
                    updated_at_utc_ms = excluded.updated_at_utc_ms;
                """;
            command.Parameters.AddWithValue("$componentId", normalizedComponentId);
            command.Parameters.AddWithValue("$placementId", normalizedPlacementId);
            command.Parameters.AddWithValue("$noteJson", json);
            command.Parameters.AddWithValue("$savedAtUtcMs", persistedSnapshot.SavedUtc.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$expiresAtUtcMs", expiresUtc.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$updatedAtUtcMs", nowUtc.ToUnixTimeMilliseconds());
            command.ExecuteNonQuery();
        }
        catch
        {
            // Keep whiteboard usable even when persistence is unavailable.
        }
    }

    public bool DeleteNote(string componentId, string? placementId)
    {
        if (!TryNormalizeKeys(componentId, placementId, out var normalizedComponentId, out var normalizedPlacementId))
        {
            return false;
        }

        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM whiteboard_notes
                WHERE component_id = $componentId
                  AND placement_id = $placementId;
                """;
            command.Parameters.AddWithValue("$componentId", normalizedComponentId);
            command.Parameters.AddWithValue("$placementId", normalizedPlacementId);
            return command.ExecuteNonQuery() > 0;
        }
        catch
        {
            return false;
        }
    }

    public bool TryDeleteExpiredNote(string componentId, string? placementId, int retentionDays)
    {
        if (!TryNormalizeKeys(componentId, placementId, out var normalizedComponentId, out var normalizedPlacementId))
        {
            return false;
        }

        try
        {
            using var connection = OpenConnection();
            return DeleteExpiredInternal(
                connection,
                normalizedComponentId,
                normalizedPlacementId,
                WhiteboardNoteRetentionPolicy.NormalizeDays(retentionDays),
                DateTimeOffset.UtcNow);
        }
        catch
        {
            return false;
        }
    }

    public int DeleteExpiredNotesBatch(int batchSize = DefaultCleanupBatchSize, DateTimeOffset? now = null)
    {
        try
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM whiteboard_notes
                WHERE rowid IN (
                    SELECT rowid
                    FROM whiteboard_notes
                    WHERE expires_at_utc_ms <= $nowUtcMs
                    ORDER BY expires_at_utc_ms ASC
                    LIMIT $batchSize
                );
                """;
            command.Parameters.AddWithValue("$nowUtcMs", (now ?? DateTimeOffset.UtcNow).ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$batchSize", NormalizeBatchSize(batchSize));
            return command.ExecuteNonQuery();
        }
        catch
        {
            return 0;
        }
    }

    public bool IsExpired(WhiteboardNoteSnapshot snapshot, int retentionDays, DateTimeOffset? now = null)
    {
        if (snapshot is null)
        {
            return false;
        }

        var expirationUtc = GetExpirationUtc(snapshot, retentionDays);
        if (!expirationUtc.HasValue)
        {
            return false;
        }

        return expirationUtc.Value <= (now ?? DateTimeOffset.UtcNow);
    }

    public DateTimeOffset? GetExpirationUtc(WhiteboardNoteSnapshot snapshot, int retentionDays)
    {
        if (snapshot is null || snapshot.SavedUtc == default)
        {
            return null;
        }

        return snapshot.SavedUtc.AddDays(WhiteboardNoteRetentionPolicy.NormalizeDays(retentionDays));
    }

    private SqliteConnection OpenConnection()
    {
        var connection = _databaseService.OpenConnection();
        EnsureSchema(connection);
        return connection;
    }

    private void EnsureSchema(SqliteConnection connection)
    {
        if (_schemaInitialized)
        {
            return;
        }

        lock (_schemaSyncRoot)
        {
            if (_schemaInitialized)
            {
                return;
            }

            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS whiteboard_notes (
                    component_id TEXT NOT NULL,
                    placement_id TEXT NOT NULL,
                    note_json TEXT NOT NULL,
                    saved_at_utc_ms INTEGER NOT NULL,
                    expires_at_utc_ms INTEGER NOT NULL,
                    updated_at_utc_ms INTEGER NOT NULL,
                    PRIMARY KEY (component_id, placement_id)
                );

                CREATE INDEX IF NOT EXISTS idx_whiteboard_notes_expires_at
                    ON whiteboard_notes(expires_at_utc_ms);
                """;
            command.ExecuteNonQuery();
            _schemaInitialized = true;
        }
    }

    private static bool DeleteExpiredInternal(
        SqliteConnection connection,
        string componentId,
        string placementId,
        int retentionDays,
        DateTimeOffset nowUtc)
    {
        using var selectCommand = connection.CreateCommand();
        selectCommand.CommandText = """
            SELECT saved_at_utc_ms
            FROM whiteboard_notes
            WHERE component_id = $componentId
              AND placement_id = $placementId
            LIMIT 1;
            """;
        selectCommand.Parameters.AddWithValue("$componentId", componentId);
        selectCommand.Parameters.AddWithValue("$placementId", placementId);

        var scalar = selectCommand.ExecuteScalar();
        if (scalar is not long savedAtUtcMs)
        {
            return false;
        }

        var savedUtc = DateTimeOffset.FromUnixTimeMilliseconds(savedAtUtcMs);
        var expiresUtc = savedUtc.AddDays(WhiteboardNoteRetentionPolicy.NormalizeDays(retentionDays));
        if (expiresUtc > nowUtc)
        {
            return false;
        }

        using var deleteCommand = connection.CreateCommand();
        deleteCommand.CommandText = """
            DELETE FROM whiteboard_notes
            WHERE component_id = $componentId
              AND placement_id = $placementId;
            """;
        deleteCommand.Parameters.AddWithValue("$componentId", componentId);
        deleteCommand.Parameters.AddWithValue("$placementId", placementId);
        return deleteCommand.ExecuteNonQuery() > 0;
    }

    private static bool TryNormalizeKeys(
        string componentId,
        string? placementId,
        out string normalizedComponentId,
        out string normalizedPlacementId)
    {
        normalizedComponentId = componentId?.Trim() ?? string.Empty;
        normalizedPlacementId = placementId?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(normalizedComponentId);
    }

    private static int NormalizeBatchSize(int batchSize)
    {
        return batchSize <= 0
            ? DefaultCleanupBatchSize
            : Math.Clamp(batchSize, 1, 4096);
    }
}
