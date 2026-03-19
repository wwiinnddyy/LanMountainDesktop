using System;
using System.IO;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class WhiteboardNotePersistenceServiceTests
{
    [Fact]
    public void SaveNote_ThenLoadNote_RoundTripsSnapshot()
    {
        using var sandbox = new WhiteboardNotePersistenceSandbox();
        var service = sandbox.CreateService();
        var snapshot = CreateSampleSnapshot();

        service.SaveNote("DesktopWhiteboard", "whiteboard-1", snapshot, retentionDays: 15);

        var loaded = service.LoadNote("DesktopWhiteboard", "whiteboard-1", retentionDays: 15);

        Assert.Single(loaded.Strokes);
        Assert.Equal(2, loaded.Strokes[0].Points.Count);
        Assert.Equal("#FF112233", loaded.Strokes[0].Color);
        Assert.True(loaded.SavedUtc > DateTimeOffset.MinValue);
    }

    [Fact]
    public void LoadNote_RemovesExpiredSnapshot_WhenRetentionExceeded()
    {
        using var sandbox = new WhiteboardNotePersistenceSandbox();
        var service = sandbox.CreateService();

        service.SaveNote("DesktopWhiteboard", "expired-board", CreateSampleSnapshot(), retentionDays: 7);
        sandbox.OverrideSavedTimestamp("DesktopWhiteboard", "expired-board", DateTimeOffset.UtcNow.AddDays(-10), retentionDays: 7);

        var loaded = service.LoadNote("DesktopWhiteboard", "expired-board", retentionDays: 7);

        Assert.Empty(loaded.Strokes);
        Assert.False(sandbox.Exists("DesktopWhiteboard", "expired-board"));
    }

    [Fact]
    public void DeleteExpiredNotesBatch_RemovesExpiredRows_AndKeepsFreshRows()
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
        Assert.False(sandbox.Exists("DesktopWhiteboard", "expired-a"));
        Assert.False(sandbox.Exists("DesktopWhiteboard", "expired-b"));
        Assert.True(sandbox.Exists("DesktopWhiteboard", "fresh-c"));
    }

    private static WhiteboardNoteSnapshot CreateSampleSnapshot()
    {
        return new WhiteboardNoteSnapshot
        {
            Strokes =
            [
                new WhiteboardStrokeSnapshot
                {
                    Color = "#FF112233",
                    InkThickness = 3.5d,
                    IgnorePressure = true,
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

        public WhiteboardNotePersistenceSandbox()
        {
            Directory.CreateDirectory(_directoryPath);
            _databasePath = Path.Combine(_directoryPath, "whiteboard-tests.db");
        }

        public WhiteboardNotePersistenceService CreateService()
        {
            return new WhiteboardNotePersistenceService(new AppDatabaseService(_databasePath));
        }

        public void OverrideSavedTimestamp(string componentId, string placementId, DateTimeOffset savedUtc, int retentionDays)
        {
            var expiresUtc = savedUtc.AddDays(WhiteboardNoteRetentionPolicy.NormalizeDays(retentionDays));
            using var connection = new AppDatabaseService(_databasePath).OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE whiteboard_notes
                SET saved_at_utc_ms = $savedAtUtcMs,
                    expires_at_utc_ms = $expiresAtUtcMs,
                    updated_at_utc_ms = $updatedAtUtcMs
                WHERE component_id = $componentId
                  AND placement_id = $placementId;
                """;
            command.Parameters.AddWithValue("$savedAtUtcMs", savedUtc.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$expiresAtUtcMs", expiresUtc.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$updatedAtUtcMs", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$componentId", componentId);
            command.Parameters.AddWithValue("$placementId", placementId);
            command.ExecuteNonQuery();
        }

        public bool Exists(string componentId, string placementId)
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
