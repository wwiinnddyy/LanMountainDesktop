using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace LanMountainDesktop.Services;

public static class AppDatabaseServiceFactory
{
    private static readonly Lazy<AppDatabaseService> SharedService = new(
        () => new AppDatabaseService(),
        isThreadSafe: true);

    public static AppDatabaseService CreateDefault()
    {
        return SharedService.Value;
    }
}

public sealed class AppDatabaseService
{
    private readonly object _schemaSyncRoot = new();
    private readonly string _databasePath;
    private bool _schemaInitialized;

    public AppDatabaseService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dataDirectory = Path.Combine(appData, "LanMountainDesktop");
        _databasePath = Path.Combine(dataDirectory, "app.db");
    }

    public AppDatabaseService(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Database path cannot be null or whitespace.", nameof(databasePath));
        }

        _databasePath = databasePath;
    }

    public SqliteConnection OpenConnection()
    {
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var connection = new SqliteConnection($"Data Source={_databasePath};Cache=Shared;Mode=ReadWriteCreate");
        connection.Open();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 3000;";
            command.ExecuteNonQuery();
        }

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
                PRAGMA journal_mode = WAL;
                PRAGMA synchronous = NORMAL;

                CREATE TABLE IF NOT EXISTS app_meta (
                    key TEXT NOT NULL PRIMARY KEY,
                    value TEXT NULL
                );

                CREATE TABLE IF NOT EXISTS study_session_reports (
                    session_id TEXT NOT NULL PRIMARY KEY,
                    label TEXT NOT NULL,
                    started_at_utc_ms INTEGER NOT NULL,
                    ended_at_utc_ms INTEGER NOT NULL,
                    duration_ms INTEGER NOT NULL,
                    avg_score REAL NOT NULL,
                    slice_count INTEGER NOT NULL,
                    report_json TEXT NOT NULL,
                    updated_at_utc_ms INTEGER NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_study_session_reports_ended_at
                    ON study_session_reports(ended_at_utc_ms DESC);

                CREATE TABLE IF NOT EXISTS study_noise_slices (
                    timeline_id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    start_at_utc_ms INTEGER NOT NULL,
                    end_at_utc_ms INTEGER NOT NULL,
                    source_type INTEGER NOT NULL,
                    session_id TEXT NULL,
                    score REAL NOT NULL,
                    avg_db REAL NOT NULL,
                    p95_db REAL NOT NULL,
                    p50_dbfs REAL NOT NULL,
                    over_ratio_dbfs REAL NOT NULL,
                    segment_count INTEGER NOT NULL,
                    slice_json TEXT NOT NULL,
                    created_at_utc_ms INTEGER NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_study_noise_slices_end_at
                    ON study_noise_slices(end_at_utc_ms DESC);

                CREATE INDEX IF NOT EXISTS idx_study_noise_slices_source_time
                    ON study_noise_slices(source_type, end_at_utc_ms DESC);

                CREATE TABLE IF NOT EXISTS attendance_sessions (
                    session_id TEXT NOT NULL PRIMARY KEY,
                    label TEXT NOT NULL,
                    started_at_utc_ms INTEGER NOT NULL,
                    ended_at_utc_ms INTEGER NULL,
                    status TEXT NOT NULL,
                    score REAL NULL,
                    payload_json TEXT NULL,
                    created_at_utc_ms INTEGER NOT NULL,
                    updated_at_utc_ms INTEGER NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_attendance_sessions_started_at
                    ON attendance_sessions(started_at_utc_ms DESC);

                CREATE TABLE IF NOT EXISTS attendance_events (
                    event_id TEXT NOT NULL PRIMARY KEY,
                    session_id TEXT NOT NULL,
                    event_type TEXT NOT NULL,
                    occurred_at_utc_ms INTEGER NOT NULL,
                    payload_json TEXT NULL,
                    created_at_utc_ms INTEGER NOT NULL,
                    FOREIGN KEY(session_id) REFERENCES attendance_sessions(session_id) ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS idx_attendance_events_session_time
                    ON attendance_events(session_id, occurred_at_utc_ms DESC);
                """;
            command.ExecuteNonQuery();
            _schemaInitialized = true;
        }
    }
}
