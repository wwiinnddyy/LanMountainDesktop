using System;
using System.Collections.Generic;
using LanMountainDesktop.Models;

namespace LanMountainDesktop.Services;

public sealed class AttendanceDataStore
{
    private readonly AppDatabaseService _databaseService;

    public AttendanceDataStore(AppDatabaseService? databaseService = null)
    {
        _databaseService = databaseService ?? AppDatabaseServiceFactory.CreateDefault();
    }

    public IReadOnlyList<AttendanceSessionRecord> LoadSessions(int limit = 200)
    {
        try
        {
            using var connection = _databaseService.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = limit > 0
                ? """
                  SELECT session_id, label, started_at_utc_ms, ended_at_utc_ms, status, score, payload_json
                  FROM attendance_sessions
                  ORDER BY started_at_utc_ms DESC
                  LIMIT $limit;
                  """
                : """
                  SELECT session_id, label, started_at_utc_ms, ended_at_utc_ms, status, score, payload_json
                  FROM attendance_sessions
                  ORDER BY started_at_utc_ms DESC;
                  """;
            if (limit > 0)
            {
                command.Parameters.AddWithValue("$limit", limit);
            }

            var sessions = new List<AttendanceSessionRecord>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var sessionId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    continue;
                }

                var label = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                var startedAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(2));
                DateTimeOffset? endedAt = reader.IsDBNull(3)
                    ? null
                    : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(3));
                var status = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
                double? score = reader.IsDBNull(5) ? null : reader.GetDouble(5);
                var payload = reader.IsDBNull(6) ? null : reader.GetString(6);

                sessions.Add(new AttendanceSessionRecord(
                    SessionId: sessionId,
                    Label: label,
                    StartedAt: startedAt,
                    EndedAt: endedAt,
                    Status: status,
                    Score: score,
                    PayloadJson: payload));
            }

            return sessions;
        }
        catch
        {
            return Array.Empty<AttendanceSessionRecord>();
        }
    }

    public void UpsertSession(AttendanceSessionRecord record)
    {
        try
        {
            using var connection = _databaseService.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO attendance_sessions(
                    session_id,
                    label,
                    started_at_utc_ms,
                    ended_at_utc_ms,
                    status,
                    score,
                    payload_json,
                    created_at_utc_ms,
                    updated_at_utc_ms)
                VALUES(
                    $sessionId,
                    $label,
                    $startedAtUtcMs,
                    $endedAtUtcMs,
                    $status,
                    $score,
                    $payloadJson,
                    $createdAtUtcMs,
                    $updatedAtUtcMs)
                ON CONFLICT(session_id) DO UPDATE SET
                    label = excluded.label,
                    started_at_utc_ms = excluded.started_at_utc_ms,
                    ended_at_utc_ms = excluded.ended_at_utc_ms,
                    status = excluded.status,
                    score = excluded.score,
                    payload_json = excluded.payload_json,
                    updated_at_utc_ms = excluded.updated_at_utc_ms;
                """;
            var nowUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            command.Parameters.AddWithValue("$sessionId", record.SessionId);
            command.Parameters.AddWithValue("$label", record.Label);
            command.Parameters.AddWithValue("$startedAtUtcMs", record.StartedAt.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$endedAtUtcMs", record.EndedAt?.ToUnixTimeMilliseconds() ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$status", record.Status);
            command.Parameters.AddWithValue("$score", record.Score ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$payloadJson", record.PayloadJson ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$createdAtUtcMs", nowUtcMs);
            command.Parameters.AddWithValue("$updatedAtUtcMs", nowUtcMs);
            command.ExecuteNonQuery();
        }
        catch
        {
            // Keep runtime resilient when persistence is unavailable.
        }
    }
}

