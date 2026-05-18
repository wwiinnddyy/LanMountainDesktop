using System;
using System.IO;
using System.Text.Json;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class WhiteboardNotePersistenceServiceTests
{
    [Fact]
    public void SaveNote_ThenLoadNote_RoundTripsFileSnapshot()
    {
        using var sandbox = new WhiteboardNotePersistenceSandbox();
        var service = sandbox.CreateService();
        var snapshot = CreateSampleSnapshot();

        var saved = service.SaveNote("DesktopWhiteboard", "whiteboard-1", snapshot, retentionDays: 15);
        var loaded = service.LoadNote("DesktopWhiteboard", "whiteboard-1", retentionDays: 15);

        Assert.True(saved);
        Assert.True(File.Exists(sandbox.GetNoteFilePath("DesktopWhiteboard", "whiteboard-1")));
        Assert.Equal(2, loaded.Version);
        Assert.Single(loaded.Strokes);
        Assert.Equal(2, loaded.Strokes[0].Points.Count);
        Assert.Equal("M 0 0 L 12 12", loaded.Strokes[0].PathSvgData);
        Assert.Equal("#FF112233", loaded.Strokes[0].Color);
        Assert.Equal(1.75d, loaded.ViewportZoom);
        Assert.Equal(-24d, loaded.ViewportOffsetX);
        Assert.Equal(-36d, loaded.ViewportOffsetY);
        Assert.True(loaded.SavedUtc > DateTimeOffset.MinValue);
        Assert.True(loaded.ExpiresUtc > loaded.SavedUtc);
    }

    [Fact]
    public void SaveNote_WithReadOnlyExistingFile_ReturnsFalseAndKeepsOldFile()
    {
        using var sandbox = new WhiteboardNotePersistenceSandbox();
        var service = sandbox.CreateService();
        var notePath = sandbox.GetNoteFilePath("DesktopWhiteboard", "read-only-board");

        Assert.True(service.SaveNote("DesktopWhiteboard", "read-only-board", CreateSampleSnapshot("#FF112233"), retentionDays: 15));
        File.SetAttributes(notePath, File.GetAttributes(notePath) | FileAttributes.ReadOnly);

        try
        {
            var saved = service.SaveNote("DesktopWhiteboard", "read-only-board", CreateSampleSnapshot("#FF445566"), retentionDays: 15);
            var loaded = service.LoadNote("DesktopWhiteboard", "read-only-board", retentionDays: 15);

            Assert.False(saved);
            Assert.Equal("#FF112233", loaded.Strokes[0].Color);
        }
        finally
        {
            File.SetAttributes(notePath, FileAttributes.Normal);
        }
    }

    [Fact]
    public void SaveNote_WithEmptySnapshot_OverwritesOldContent()
    {
        using var sandbox = new WhiteboardNotePersistenceSandbox();
        var service = sandbox.CreateService();

        Assert.True(service.SaveNote("DesktopWhiteboard", "clear-board", CreateSampleSnapshot(), retentionDays: 15));
        Assert.True(service.SaveNote("DesktopWhiteboard", "clear-board", new WhiteboardNoteSnapshot
        {
            CanvasWidth = 320,
            CanvasHeight = 180,
            ViewportZoom = 2d,
            ViewportOffsetX = -40d,
            ViewportOffsetY = -20d
        }, retentionDays: 15));

        var loaded = service.LoadNote("DesktopWhiteboard", "clear-board", retentionDays: 15);

        Assert.Empty(loaded.Strokes);
        Assert.Equal(2d, loaded.ViewportZoom);
        Assert.Equal(-40d, loaded.ViewportOffsetX);
        Assert.Equal(-20d, loaded.ViewportOffsetY);
        Assert.True(File.Exists(sandbox.GetNoteFilePath("DesktopWhiteboard", "clear-board")));
    }

    [Fact]
    public void LoadNote_WithOldJsonWithoutViewport_UsesDefaultViewport()
    {
        using var sandbox = new WhiteboardNotePersistenceSandbox();
        var service = sandbox.CreateService();
        sandbox.WriteRawNoteJson("DesktopWhiteboard", "old-json-board", """
            {
              "version": 2,
              "canvasWidth": 320,
              "canvasHeight": 180,
              "backgroundColor": "#FFFFFFFF",
              "strokes": []
            }
            """);

        var loaded = service.LoadNote("DesktopWhiteboard", "old-json-board", retentionDays: 15);

        Assert.Equal(1d, loaded.ViewportZoom);
        Assert.Equal(0d, loaded.ViewportOffsetX);
        Assert.Equal(0d, loaded.ViewportOffsetY);
    }

    [Fact]
    public void LoadNote_RemovesExpiredFile_WhenRetentionExceeded()
    {
        using var sandbox = new WhiteboardNotePersistenceSandbox();
        var service = sandbox.CreateService();

        service.SaveNote("DesktopWhiteboard", "expired-board", CreateSampleSnapshot(), retentionDays: 7);
        sandbox.OverrideSavedTimestamp("DesktopWhiteboard", "expired-board", DateTimeOffset.UtcNow.AddDays(-10), retentionDays: 7);

        var loaded = service.LoadNote("DesktopWhiteboard", "expired-board", retentionDays: 7);

        Assert.Empty(loaded.Strokes);
        Assert.False(File.Exists(sandbox.GetNoteFilePath("DesktopWhiteboard", "expired-board")));
    }

    [Fact]
    public void DeleteExpiredNotesBatch_RemovesExpiredFiles_AndKeepsFreshFiles()
    {
        using var sandbox = new WhiteboardNotePersistenceSandbox();
        var service = sandbox.CreateService();

        service.SaveNote("DesktopWhiteboard", "expired-a", CreateSampleSnapshot(), retentionDays: 7);
        service.SaveNote("DesktopWhiteboard", "expired-b", CreateSampleSnapshot(), retentionDays: 7);
        service.SaveNote("DesktopWhiteboard", "fresh-c", CreateSampleSnapshot(), retentionDays: 15);

        sandbox.OverrideSavedTimestamp("DesktopWhiteboard", "expired-a", DateTimeOffset.UtcNow.AddDays(-9), retentionDays: 7);
        sandbox.OverrideSavedTimestamp("DesktopWhiteboard", "expired-b", DateTimeOffset.UtcNow.AddDays(-8), retentionDays: 7);
        sandbox.OverrideSavedTimestamp("DesktopWhiteboard", "fresh-c", DateTimeOffset.UtcNow.AddDays(-2), retentionDays: 15);

        var deletedCount = service.DeleteExpiredNotesBatch(batchSize: 10);

        Assert.Equal(2, deletedCount);
        Assert.False(File.Exists(sandbox.GetNoteFilePath("DesktopWhiteboard", "expired-a")));
        Assert.False(File.Exists(sandbox.GetNoteFilePath("DesktopWhiteboard", "expired-b")));
        Assert.True(File.Exists(sandbox.GetNoteFilePath("DesktopWhiteboard", "fresh-c")));
    }

    [Fact]
    public void LoadNote_MigratesLegacyDatabaseSnapshot_WhenFileMissing()
    {
        using var sandbox = new WhiteboardNotePersistenceSandbox();
        sandbox.SaveLegacyNote("DesktopWhiteboard", "legacy-board", CreateSampleSnapshot("#FF778899"), retentionDays: 15);
        var service = sandbox.CreateService();

        var loaded = service.LoadNote("DesktopWhiteboard", "legacy-board", retentionDays: 15);

        Assert.Single(loaded.Strokes);
        Assert.Equal("#FF778899", loaded.Strokes[0].Color);
        Assert.True(File.Exists(sandbox.GetNoteFilePath("DesktopWhiteboard", "legacy-board")));
        Assert.False(sandbox.LegacyExists("DesktopWhiteboard", "legacy-board"));
    }

    [Fact]
    public void DeleteNote_RemovesFileAndLegacyRow()
    {
        using var sandbox = new WhiteboardNotePersistenceSandbox();
        sandbox.SaveLegacyNote("DesktopWhiteboard", "delete-board", CreateSampleSnapshot(), retentionDays: 15);
        var service = sandbox.CreateService();
        service.SaveNote("DesktopWhiteboard", "delete-board", CreateSampleSnapshot(), retentionDays: 15);

        var deleted = service.DeleteNote("DesktopWhiteboard", "delete-board");

        Assert.True(deleted);
        Assert.False(File.Exists(sandbox.GetNoteFilePath("DesktopWhiteboard", "delete-board")));
        Assert.False(sandbox.LegacyExists("DesktopWhiteboard", "delete-board"));
    }

    private static WhiteboardNoteSnapshot CreateSampleSnapshot(string color = "#FF112233")
    {
        return new WhiteboardNoteSnapshot
        {
            CanvasWidth = 320,
            CanvasHeight = 180,
            BackgroundColor = "#FFFFFFFF",
            ViewportZoom = 1.75d,
            ViewportOffsetX = -24d,
            ViewportOffsetY = -36d,
            Strokes =
            [
                new WhiteboardStrokeSnapshot
                {
                    Color = color,
                    InkThickness = 3.5d,
                    IgnorePressure = true,
                    PathSvgData = "M 0 0 L 12 12",
                    Points =
                    [
                        new WhiteboardStylusPointSnapshot { X = 12, Y = 34, Pressure = 0.4d, Width = 2, Height = 2 },
                        new WhiteboardStylusPointSnapshot { X = 48, Y = 64, Pressure = 0.7d, Width = 2, Height = 2 }
                    ]
                }
            ]
        };
    }

    private sealed class WhiteboardNotePersistenceSandbox : IDisposable
    {
        private readonly string _directoryPath = Path.Combine(
            Path.GetTempPath(),
            "LanMountainDesktop.WhiteboardNoteTests",
            Guid.NewGuid().ToString("N"));

        private readonly string _databasePath;
        private readonly string _whiteboardsRootDirectory;

        public WhiteboardNotePersistenceSandbox()
        {
            Directory.CreateDirectory(_directoryPath);
            _databasePath = Path.Combine(_directoryPath, "whiteboard-tests.db");
            _whiteboardsRootDirectory = Path.Combine(_directoryPath, "Whiteboards");
        }

        public WhiteboardNotePersistenceService CreateService()
        {
            return new WhiteboardNotePersistenceService(
                _whiteboardsRootDirectory,
                new AppDatabaseService(_databasePath));
        }

        public string GetNoteFilePath(string componentId, string placementId)
        {
            return CreateService().GetNoteFilePathForTests(componentId, placementId);
        }

        public void OverrideSavedTimestamp(string componentId, string placementId, DateTimeOffset savedUtc, int retentionDays)
        {
            var notePath = GetNoteFilePath(componentId, placementId);
            var snapshot = JsonSerializer.Deserialize<WhiteboardNoteSnapshot>(
                File.ReadAllText(notePath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new WhiteboardNoteSnapshot();
            snapshot.SavedUtc = savedUtc;
            snapshot.ExpiresUtc = savedUtc.AddDays(WhiteboardNoteRetentionPolicy.NormalizeDays(retentionDays));
            File.WriteAllText(notePath, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
        }

        public void SaveLegacyNote(string componentId, string placementId, WhiteboardNoteSnapshot snapshot, int retentionDays)
        {
            var nowUtc = DateTimeOffset.UtcNow;
            var expiresUtc = nowUtc.AddDays(WhiteboardNoteRetentionPolicy.NormalizeDays(retentionDays));
            using var connection = new AppDatabaseService(_databasePath).OpenConnection();
            using (var schemaCommand = connection.CreateCommand())
            {
                schemaCommand.CommandText = """
                    CREATE TABLE IF NOT EXISTS whiteboard_notes (
                        component_id TEXT NOT NULL,
                        placement_id TEXT NOT NULL,
                        note_json TEXT NOT NULL,
                        saved_at_utc_ms INTEGER NOT NULL,
                        expires_at_utc_ms INTEGER NOT NULL,
                        updated_at_utc_ms INTEGER NOT NULL,
                        PRIMARY KEY (component_id, placement_id)
                    );
                    """;
                schemaCommand.ExecuteNonQuery();
            }

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
                    $updatedAtUtcMs);
                """;
            command.Parameters.AddWithValue("$componentId", componentId);
            command.Parameters.AddWithValue("$placementId", placementId);
            command.Parameters.AddWithValue("$noteJson", JsonSerializer.Serialize(snapshot));
            command.Parameters.AddWithValue("$savedAtUtcMs", nowUtc.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$expiresAtUtcMs", expiresUtc.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$updatedAtUtcMs", nowUtc.ToUnixTimeMilliseconds());
            command.ExecuteNonQuery();
        }

        public void WriteRawNoteJson(string componentId, string placementId, string json)
        {
            var notePath = GetNoteFilePath(componentId, placementId);
            Directory.CreateDirectory(Path.GetDirectoryName(notePath)!);
            File.WriteAllText(notePath, json);
        }

        public bool LegacyExists(string componentId, string placementId)
        {
            using var connection = new AppDatabaseService(_databasePath).OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT COUNT(1)
                FROM whiteboard_notes
                WHERE component_id = $componentId
                  AND placement_id = $placementId;
                """;
            command.Parameters.AddWithValue("$componentId", componentId);
            command.Parameters.AddWithValue("$placementId", placementId);
            return Convert.ToInt32(command.ExecuteScalar()) > 0;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_directoryPath))
                {
                    Directory.Delete(_directoryPath, true);
                }
            }
            catch
            {
                // Temporary test directories are best-effort cleanup.
            }
        }
    }
}
