using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LanMountainDesktop.Models;
using Microsoft.Data.Sqlite;

namespace LanMountainDesktop.Services;

public sealed class WhiteboardNotePersistenceService : IWhiteboardNotePersistenceService
{
    private const int DefaultCleanupBatchSize = 256;
    private const int CurrentSnapshotVersion = 2;
    private const string Category = "WhiteboardPersistence";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly object _legacySchemaSyncRoot = new();
    private readonly string _whiteboardsRootDirectory;
    private readonly AppDatabaseService _legacyDatabaseService;
    private bool _legacySchemaInitialized;

    public WhiteboardNotePersistenceService()
        : this(Path.Combine(AppDataPathProvider.GetDataRoot(), "Whiteboards"), AppDatabaseServiceFactory.CreateDefault())
    {
    }

    public WhiteboardNotePersistenceService(AppDatabaseService? legacyDatabaseService)
        : this(Path.Combine(AppDataPathProvider.GetDataRoot(), "Whiteboards"), legacyDatabaseService)
    {
    }

    public WhiteboardNotePersistenceService(string whiteboardsRootDirectory, AppDatabaseService? legacyDatabaseService = null)
    {
        if (string.IsNullOrWhiteSpace(whiteboardsRootDirectory))
        {
            throw new ArgumentException("Whiteboard root directory cannot be null or whitespace.", nameof(whiteboardsRootDirectory));
        }

        _whiteboardsRootDirectory = Path.GetFullPath(whiteboardsRootDirectory);
        _legacyDatabaseService = legacyDatabaseService ?? AppDatabaseServiceFactory.CreateDefault();
    }

    public WhiteboardNoteSnapshot LoadNote(string componentId, string? placementId, int retentionDays)
    {
        if (!TryNormalizeKeys(componentId, placementId, out var normalizedComponentId, out var normalizedPlacementId))
        {
            return new WhiteboardNoteSnapshot();
        }

        var notePath = GetNoteFilePath(normalizedComponentId, normalizedPlacementId);
        var normalizedRetentionDays = WhiteboardNoteRetentionPolicy.NormalizeDays(retentionDays);

        try
        {
            if (File.Exists(notePath))
            {
                var snapshot = ReadSnapshot(notePath);
                if (IsExpired(snapshot, normalizedRetentionDays))
                {
                    TryDeleteFile(notePath);
                    return new WhiteboardNoteSnapshot();
                }

                return snapshot.Clone();
            }

            var legacySnapshot = TryLoadLegacyNote(normalizedComponentId, normalizedPlacementId, normalizedRetentionDays);
            if (legacySnapshot.Strokes.Count == 0 && legacySnapshot.SavedUtc == default)
            {
                return new WhiteboardNoteSnapshot();
            }

            if (!IsExpired(legacySnapshot, normalizedRetentionDays))
            {
                if (SaveNote(normalizedComponentId, normalizedPlacementId, legacySnapshot, normalizedRetentionDays))
                {
                    _ = TryDeleteLegacyNote(normalizedComponentId, normalizedPlacementId);
                }

                return legacySnapshot.Clone();
            }

            _ = TryDeleteLegacyNote(normalizedComponentId, normalizedPlacementId);
        }
        catch (Exception ex)
        {
            AppLogger.Warn(
                Category,
                $"Failed to load whiteboard note. ComponentId='{normalizedComponentId}'; PlacementId='{normalizedPlacementId}'.",
                ex);
        }

        return new WhiteboardNoteSnapshot();
    }

    public bool SaveNote(string componentId, string? placementId, WhiteboardNoteSnapshot snapshot, int retentionDays)
    {
        if (!TryNormalizeKeys(componentId, placementId, out var normalizedComponentId, out var normalizedPlacementId))
        {
            return false;
        }

        var notePath = GetNoteFilePath(normalizedComponentId, normalizedPlacementId);
        var tempPath = $"{notePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            var nowUtc = DateTimeOffset.UtcNow;
            var persistedSnapshot = snapshot?.Clone() ?? new WhiteboardNoteSnapshot();
            persistedSnapshot.Version = CurrentSnapshotVersion;
            persistedSnapshot.SavedUtc = nowUtc;
            persistedSnapshot.ExpiresUtc = nowUtc.AddDays(WhiteboardNoteRetentionPolicy.NormalizeDays(retentionDays));

            var directory = Path.GetDirectoryName(notePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(persistedSnapshot, JsonOptions);
            File.WriteAllText(tempPath, json, Encoding.UTF8);
            File.Move(tempPath, notePath, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            TryDeleteFile(tempPath);
            AppLogger.Warn(
                Category,
                $"Failed to save whiteboard note. ComponentId='{normalizedComponentId}'; PlacementId='{normalizedPlacementId}'; StrokeCount={snapshot?.Strokes.Count ?? 0}.",
                ex);
            return false;
        }
    }

    public bool DeleteNote(string componentId, string? placementId)
    {
        if (!TryNormalizeKeys(componentId, placementId, out var normalizedComponentId, out var normalizedPlacementId))
        {
            return false;
        }

        var deleted = TryDeleteFile(GetNoteFilePath(normalizedComponentId, normalizedPlacementId));
        deleted |= TryDeleteLegacyNote(normalizedComponentId, normalizedPlacementId);
        return deleted;
    }

    public bool TryDeleteExpiredNote(string componentId, string? placementId, int retentionDays)
    {
        if (!TryNormalizeKeys(componentId, placementId, out var normalizedComponentId, out var normalizedPlacementId))
        {
            return false;
        }

        var notePath = GetNoteFilePath(normalizedComponentId, normalizedPlacementId);
        try
        {
            if (File.Exists(notePath))
            {
                var snapshot = ReadSnapshot(notePath);
                if (IsExpired(snapshot, retentionDays))
                {
                    return TryDeleteFile(notePath);
                }

                return false;
            }

            return TryDeleteExpiredLegacyNote(normalizedComponentId, normalizedPlacementId, retentionDays);
        }
        catch (Exception ex)
        {
            AppLogger.Warn(
                Category,
                $"Failed to delete expired whiteboard note. ComponentId='{normalizedComponentId}'; PlacementId='{normalizedPlacementId}'.",
                ex);
            return false;
        }
    }

    public int DeleteExpiredNotesBatch(int batchSize = DefaultCleanupBatchSize, DateTimeOffset? now = null)
    {
        var deletedCount = 0;
        var normalizedBatchSize = NormalizeBatchSize(batchSize);
        var nowUtc = now ?? DateTimeOffset.UtcNow;

        try
        {
            if (Directory.Exists(_whiteboardsRootDirectory))
            {
                foreach (var notePath in Directory.EnumerateFiles(_whiteboardsRootDirectory, "*.json", SearchOption.AllDirectories))
                {
                    if (deletedCount >= normalizedBatchSize)
                    {
                        break;
                    }

                    try
                    {
                        var snapshot = ReadSnapshot(notePath);
                        if (IsExpired(snapshot, WhiteboardNoteRetentionPolicy.DefaultDays, nowUtc) &&
                            TryDeleteFile(notePath))
                        {
                            deletedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warn(Category, $"Failed to inspect whiteboard note file '{notePath}'.", ex);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn(Category, $"Failed to scan whiteboard note directory '{_whiteboardsRootDirectory}'.", ex);
        }

        deletedCount += DeleteExpiredLegacyNotesBatch(Math.Max(0, normalizedBatchSize - deletedCount), nowUtc);
        return deletedCount;
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
        if (snapshot is null)
        {
            return null;
        }

        if (snapshot.ExpiresUtc.HasValue)
        {
            return snapshot.ExpiresUtc.Value;
        }

        if (snapshot.SavedUtc == default)
        {
            return null;
        }

        return snapshot.SavedUtc.AddDays(WhiteboardNoteRetentionPolicy.NormalizeDays(retentionDays));
    }

    internal string GetNoteFilePathForTests(string componentId, string? placementId)
    {
        if (!TryNormalizeKeys(componentId, placementId, out var normalizedComponentId, out var normalizedPlacementId))
        {
            return string.Empty;
        }

        return GetNoteFilePath(normalizedComponentId, normalizedPlacementId);
    }

    private string GetNoteFilePath(string normalizedComponentId, string normalizedPlacementId)
    {
        return Path.Combine(
            _whiteboardsRootDirectory,
            SanitizePathSegment(normalizedComponentId),
            $"{SanitizePathSegment(normalizedPlacementId)}.json");
    }

    private static WhiteboardNoteSnapshot ReadSnapshot(string notePath)
    {
        var json = File.ReadAllText(notePath, Encoding.UTF8);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new WhiteboardNoteSnapshot();
        }

        return JsonSerializer.Deserialize<WhiteboardNoteSnapshot>(json, JsonOptions) ?? new WhiteboardNoteSnapshot();
    }

    private WhiteboardNoteSnapshot TryLoadLegacyNote(string componentId, string placementId, int retentionDays)
    {
        try
        {
            using var connection = OpenLegacyConnection();
            TryDeleteExpiredLegacyNote(connection, componentId, placementId, retentionDays, DateTimeOffset.UtcNow);

            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT note_json, saved_at_utc_ms, expires_at_utc_ms
                FROM whiteboard_notes
                WHERE component_id = $componentId
                  AND placement_id = $placementId
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$componentId", componentId);
            command.Parameters.AddWithValue("$placementId", placementId);

            using var reader = command.ExecuteReader();
            if (!reader.Read() || reader.IsDBNull(0))
            {
                return new WhiteboardNoteSnapshot();
            }

            var snapshot = JsonSerializer.Deserialize<WhiteboardNoteSnapshot>(reader.GetString(0), JsonOptions) ??
                           new WhiteboardNoteSnapshot();
            if (!reader.IsDBNull(1))
            {
                snapshot.SavedUtc = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1));
            }

            if (!reader.IsDBNull(2))
            {
                snapshot.ExpiresUtc = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(2));
            }

            return snapshot;
        }
        catch (Exception ex)
        {
            AppLogger.Warn(
                Category,
                $"Failed to load legacy whiteboard note. ComponentId='{componentId}'; PlacementId='{placementId}'.",
                ex);
            return new WhiteboardNoteSnapshot();
        }
    }

    private bool TryDeleteLegacyNote(string componentId, string placementId)
    {
        try
        {
            using var connection = OpenLegacyConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM whiteboard_notes
                WHERE component_id = $componentId
                  AND placement_id = $placementId;
                """;
            command.Parameters.AddWithValue("$componentId", componentId);
            command.Parameters.AddWithValue("$placementId", placementId);
            return command.ExecuteNonQuery() > 0;
        }
        catch
        {
            return false;
        }
    }

    private bool TryDeleteExpiredLegacyNote(string componentId, string placementId, int retentionDays)
    {
        try
        {
            using var connection = OpenLegacyConnection();
            return TryDeleteExpiredLegacyNote(
                connection,
                componentId,
                placementId,
                WhiteboardNoteRetentionPolicy.NormalizeDays(retentionDays),
                DateTimeOffset.UtcNow);
        }
        catch
        {
            return false;
        }
    }

    private int DeleteExpiredLegacyNotesBatch(int batchSize, DateTimeOffset nowUtc)
    {
        if (batchSize <= 0)
        {
            return 0;
        }

        try
        {
            using var connection = OpenLegacyConnection();
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
            command.Parameters.AddWithValue("$nowUtcMs", nowUtc.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$batchSize", NormalizeBatchSize(batchSize));
            return command.ExecuteNonQuery();
        }
        catch
        {
            return 0;
        }
    }

    private SqliteConnection OpenLegacyConnection()
    {
        var connection = _legacyDatabaseService.OpenConnection();
        EnsureLegacySchema(connection);
        return connection;
    }

    private void EnsureLegacySchema(SqliteConnection connection)
    {
        if (_legacySchemaInitialized)
        {
            return;
        }

        lock (_legacySchemaSyncRoot)
        {
            if (_legacySchemaInitialized)
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
            _legacySchemaInitialized = true;
        }
    }

    private static bool TryDeleteExpiredLegacyNote(
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
        return !string.IsNullOrWhiteSpace(normalizedComponentId) &&
               !string.IsNullOrWhiteSpace(normalizedPlacementId);
    }

    private static string SanitizePathSegment(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Trim())
        {
            builder.Append(Array.IndexOf(invalidChars, ch) >= 0 ? '_' : ch);
        }

        var safe = builder.ToString();
        if (string.IsNullOrWhiteSpace(safe))
        {
            return "_";
        }

        if (safe.Length <= 120)
        {
            return safe;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(safe)))[..12].ToLowerInvariant();
        return $"{safe[..100]}-{hash}";
    }

    private static bool TryDeleteFile(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
                return true;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn(Category, $"Failed to delete whiteboard note file '{path}'.", ex);
        }

        return false;
    }

    private static int NormalizeBatchSize(int batchSize)
    {
        return batchSize <= 0
            ? DefaultCleanupBatchSize
            : Math.Clamp(batchSize, 1, 4096);
    }
}
