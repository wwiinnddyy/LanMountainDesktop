using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using LanMountainDesktop.Models;
using Microsoft.Data.Sqlite;

namespace LanMountainDesktop.Services;

public sealed class StudyDataStore
{
    private const string SelectedSessionReportIdMetaKey = "study.selected_session_report_id";
    private const int DefaultNoiseSliceCapacity = 50000;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AppDatabaseService _databaseService;
    private readonly Action<string>? _logger;

    public StudyDataStore(AppDatabaseService? databaseService = null, Action<string>? logger = null)
    {
        _databaseService = databaseService ?? AppDatabaseServiceFactory.CreateDefault();
        _logger = logger;
    }

    private void Log(string message)
    {
        _logger?.Invoke($"[StudyDataStore] {message}");
    }

    public IReadOnlyList<StudySessionReport> LoadSessionReports(int limit = 120)
    {
        try
        {
            using var connection = _databaseService.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = limit > 0
                ? """
                  SELECT report_json
                  FROM study_session_reports
                  ORDER BY ended_at_utc_ms DESC
                  LIMIT $limit;
                  """
                : """
                  SELECT report_json
                  FROM study_session_reports
                  ORDER BY ended_at_utc_ms DESC;
                  """;
            if (limit > 0)
            {
                command.Parameters.AddWithValue("$limit", limit);
            }

            var reports = new List<StudySessionReport>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (reader.IsDBNull(0))
                {
                    continue;
                }

                var json = reader.GetString(0);
                if (string.IsNullOrWhiteSpace(json))
                {
                    continue;
                }

                try
                {
                    var report = JsonSerializer.Deserialize<StudySessionReport>(json, JsonOptions);
                    if (report is not null)
                    {
                        reports.Add(report);
                    }
                }
                catch (JsonException ex)
                {
                    Log($"Failed to deserialize session report: {ex.Message}");
                }
            }

            return reports;
        }
        catch (Exception ex)
        {
            Log($"Failed to load session reports: {ex.Message}");
            return Array.Empty<StudySessionReport>();
        }
    }

    public bool TryGetSessionReport(string sessionId, out StudySessionReport report)
    {
        report = null!;
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        try
        {
            using var connection = _databaseService.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT report_json
                FROM study_session_reports
                WHERE session_id = $sessionId
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$sessionId", sessionId.Trim());

            var json = command.ExecuteScalar() as string;
            if (string.IsNullOrWhiteSpace(json))
            {
                Log($"Session report not found for id: {sessionId}");
                return false;
            }

            var parsed = JsonSerializer.Deserialize<StudySessionReport>(json, JsonOptions);
            if (parsed is null)
            {
                Log($"Failed to deserialize session report for id: {sessionId}");
                return false;
            }

            report = parsed;
            return true;
        }
        catch (JsonException ex)
        {
            Log($"JSON deserialization error for session {sessionId}: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            Log($"Failed to get session report {sessionId}: {ex.Message}");
            return false;
        }
    }

    public void ReplaceSessionReports(IReadOnlyList<StudySessionReport> reports)
    {
        try
        {
            using var connection = _databaseService.OpenConnection();
            using var transaction = connection.BeginTransaction();

            using (var clearCommand = connection.CreateCommand())
            {
                clearCommand.Transaction = transaction;
                clearCommand.CommandText = "DELETE FROM study_session_reports;";
                clearCommand.ExecuteNonQuery();
            }

            for (var i = 0; i < reports.Count; i++)
            {
                InsertOrUpdateSessionReport(connection, transaction, reports[i]);
            }

            transaction.Commit();
        }
        catch (Exception ex)
        {
            Log($"Failed to replace session reports: {ex.Message}");
        }
    }

    public string? GetSelectedSessionReportId()
    {
        try
        {
            using var connection = _databaseService.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT value
                FROM app_meta
                WHERE key = $key
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$key", SelectedSessionReportIdMetaKey);
            var value = command.ExecuteScalar() as string;
            return string.IsNullOrWhiteSpace(value)
                ? null
                : value.Trim();
        }
        catch (Exception ex)
        {
            Log($"Failed to get selected session report id: {ex.Message}");
            return null;
        }
    }

    public void SetSelectedSessionReportId(string? sessionId)
    {
        try
        {
            using var connection = _databaseService.OpenConnection();
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                using var deleteCommand = connection.CreateCommand();
                deleteCommand.CommandText = "DELETE FROM app_meta WHERE key = $key;";
                deleteCommand.Parameters.AddWithValue("$key", SelectedSessionReportIdMetaKey);
                deleteCommand.ExecuteNonQuery();
                return;
            }

            using var upsertCommand = connection.CreateCommand();
            upsertCommand.CommandText = """
                INSERT INTO app_meta(key, value)
                VALUES ($key, $value)
                ON CONFLICT(key) DO UPDATE SET value = excluded.value;
                """;
            upsertCommand.Parameters.AddWithValue("$key", SelectedSessionReportIdMetaKey);
            upsertCommand.Parameters.AddWithValue("$value", sessionId.Trim());
            upsertCommand.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Log($"Failed to set selected session report id: {ex.Message}");
        }
    }

    public void AppendNoiseSlice(NoiseSliceSummary slice, string? sessionId, NoiseSliceSourceType sourceType)
    {
        try
        {
            using var connection = _databaseService.OpenConnection();
            using var transaction = connection.BeginTransaction();
            var nowUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var sliceJson = JsonSerializer.Serialize(slice, JsonOptions);

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO study_noise_slices(
                        start_at_utc_ms,
                        end_at_utc_ms,
                        source_type,
                        session_id,
                        score,
                        avg_db,
                        p95_db,
                        p50_dbfs,
                        over_ratio_dbfs,
                        segment_count,
                        slice_json,
                        created_at_utc_ms)
                    VALUES(
                        $startAtUtcMs,
                        $endAtUtcMs,
                        $sourceType,
                        $sessionId,
                        $score,
                        $avgDb,
                        $p95Db,
                        $p50Dbfs,
                        $overRatioDbfs,
                        $segmentCount,
                        $sliceJson,
                        $createdAtUtcMs);
                    """;
                command.Parameters.AddWithValue("$startAtUtcMs", slice.StartAt.ToUnixTimeMilliseconds());
                command.Parameters.AddWithValue("$endAtUtcMs", slice.EndAt.ToUnixTimeMilliseconds());
                command.Parameters.AddWithValue("$sourceType", (int)sourceType);
                command.Parameters.AddWithValue("$sessionId", string.IsNullOrWhiteSpace(sessionId) ? (object)DBNull.Value : sessionId.Trim());
                command.Parameters.AddWithValue("$score", slice.Score);
                command.Parameters.AddWithValue("$avgDb", slice.Display.AvgDb);
                command.Parameters.AddWithValue("$p95Db", slice.Display.P95Db);
                command.Parameters.AddWithValue("$p50Dbfs", slice.Raw.P50Dbfs);
                command.Parameters.AddWithValue("$overRatioDbfs", slice.Raw.OverRatioDbfs);
                command.Parameters.AddWithValue("$segmentCount", Math.Max(0, slice.Raw.SegmentCount));
                command.Parameters.AddWithValue("$sliceJson", sliceJson);
                command.Parameters.AddWithValue("$createdAtUtcMs", nowUtcMs);
                command.ExecuteNonQuery();
            }

            using (var trimCommand = connection.CreateCommand())
            {
                trimCommand.Transaction = transaction;
                trimCommand.CommandText = """
                    DELETE FROM study_noise_slices
                    WHERE timeline_id NOT IN (
                        SELECT timeline_id
                        FROM study_noise_slices
                        ORDER BY end_at_utc_ms DESC
                        LIMIT $limit
                    );
                    """;
                trimCommand.Parameters.AddWithValue("$limit", DefaultNoiseSliceCapacity);
                trimCommand.ExecuteNonQuery();
            }

            transaction.Commit();
        }
        catch (Exception ex)
        {
            Log($"Failed to append noise slice: {ex.Message}");
        }
    }

    public IReadOnlyList<NoiseSliceTimelineEntry> LoadNoiseSliceTimeline(
        DateTimeOffset? startAt = null,
        DateTimeOffset? endAt = null,
        int limit = 720,
        bool includeRealtimeSlices = true,
        bool includeSessionSlices = true)
    {
        if (!includeRealtimeSlices && !includeSessionSlices)
        {
            return Array.Empty<NoiseSliceTimelineEntry>();
        }

        try
        {
            using var connection = _databaseService.OpenConnection();
            using var command = connection.CreateCommand();
            var whereParts = new List<string>();
            if (startAt is not null)
            {
                whereParts.Add("end_at_utc_ms >= $startAtUtcMs");
                command.Parameters.AddWithValue("$startAtUtcMs", startAt.Value.ToUnixTimeMilliseconds());
            }

            if (endAt is not null)
            {
                whereParts.Add("start_at_utc_ms <= $endAtUtcMs");
                command.Parameters.AddWithValue("$endAtUtcMs", endAt.Value.ToUnixTimeMilliseconds());
            }

            if (includeRealtimeSlices != includeSessionSlices)
            {
                var sourceType = includeSessionSlices
                    ? (int)NoiseSliceSourceType.Session
                    : (int)NoiseSliceSourceType.Realtime;
                whereParts.Add("source_type = $sourceType");
                command.Parameters.AddWithValue("$sourceType", sourceType);
            }

            var whereClause = whereParts.Count == 0
                ? string.Empty
                : $"WHERE {string.Join(" AND ", whereParts)}";
            var normalizedLimit = Math.Clamp(limit, 1, DefaultNoiseSliceCapacity);

            command.CommandText = $"""
                SELECT timeline_id, source_type, session_id, slice_json
                FROM study_noise_slices
                {whereClause}
                ORDER BY end_at_utc_ms DESC
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$limit", normalizedLimit);

            var entries = new List<NoiseSliceTimelineEntry>(Math.Min(normalizedLimit, 2048));
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var timelineId = reader.GetInt64(0);
                var sourceTypeRaw = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                var sourceType = sourceTypeRaw == (int)NoiseSliceSourceType.Session
                    ? NoiseSliceSourceType.Session
                    : NoiseSliceSourceType.Realtime;
                var sessionId = reader.IsDBNull(2) ? null : reader.GetString(2);
                if (reader.IsDBNull(3))
                {
                    continue;
                }

                var sliceJson = reader.GetString(3);
                if (string.IsNullOrWhiteSpace(sliceJson))
                {
                    continue;
                }

                var slice = JsonSerializer.Deserialize<NoiseSliceSummary>(sliceJson, JsonOptions);
                if (slice is null)
                {
                    continue;
                }

                entries.Add(new NoiseSliceTimelineEntry(
                    TimelineId: timelineId,
                    SourceType: sourceType,
                    SessionId: sessionId,
                    Slice: slice));
            }

            return entries;
        }
        catch (Exception ex)
        {
            Log($"Failed to load noise slice timeline: {ex.Message}");
            return Array.Empty<NoiseSliceTimelineEntry>();
        }
    }

    public void ClearNoiseSliceTimeline(DateTimeOffset? olderThan = null)
    {
        try
        {
            using var connection = _databaseService.OpenConnection();
            using var command = connection.CreateCommand();
            if (olderThan is null)
            {
                command.CommandText = "DELETE FROM study_noise_slices;";
            }
            else
            {
                command.CommandText = "DELETE FROM study_noise_slices WHERE end_at_utc_ms <= $olderThanUtcMs;";
                command.Parameters.AddWithValue("$olderThanUtcMs", olderThan.Value.ToUnixTimeMilliseconds());
            }

            command.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Log($"Failed to clear noise slice timeline: {ex.Message}");
        }
    }

    private static void InsertOrUpdateSessionReport(
        SqliteConnection connection,
        SqliteTransaction transaction,
        StudySessionReport report)
    {
        var json = JsonSerializer.Serialize(report, JsonOptions);
        var nowUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO study_session_reports(
                session_id,
                label,
                started_at_utc_ms,
                ended_at_utc_ms,
                duration_ms,
                avg_score,
                slice_count,
                report_json,
                updated_at_utc_ms)
            VALUES (
                $sessionId,
                $label,
                $startedAtUtcMs,
                $endedAtUtcMs,
                $durationMs,
                $avgScore,
                $sliceCount,
                $reportJson,
                $updatedAtUtcMs)
            ON CONFLICT(session_id) DO UPDATE SET
                label = excluded.label,
                started_at_utc_ms = excluded.started_at_utc_ms,
                ended_at_utc_ms = excluded.ended_at_utc_ms,
                duration_ms = excluded.duration_ms,
                avg_score = excluded.avg_score,
                slice_count = excluded.slice_count,
                report_json = excluded.report_json,
                updated_at_utc_ms = excluded.updated_at_utc_ms;
            """;
        command.Parameters.AddWithValue("$sessionId", report.SessionId);
        command.Parameters.AddWithValue("$label", report.Label);
        command.Parameters.AddWithValue("$startedAtUtcMs", report.StartedAt.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$endedAtUtcMs", report.EndedAt.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$durationMs", (long)Math.Max(0, report.Duration.TotalMilliseconds));
        command.Parameters.AddWithValue("$avgScore", report.Metrics.AvgScore);
        command.Parameters.AddWithValue("$sliceCount", report.Metrics.SliceCount);
        command.Parameters.AddWithValue("$reportJson", json);
        command.Parameters.AddWithValue("$updatedAtUtcMs", nowUtcMs);
        command.ExecuteNonQuery();
    }
}
