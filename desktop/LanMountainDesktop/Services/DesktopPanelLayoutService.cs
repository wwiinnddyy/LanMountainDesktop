using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services.Json;
using LanMountainDesktop.Shared.Data;
using LanMountainDesktop.Shared.IO;

namespace LanMountainDesktop.Services;

public interface IDesktopPanelLayoutService
{
    DesktopPanelLayoutSnapshot Load();

    void Save(DesktopPanelLayoutSnapshot snapshot);

    void AddComponentPlacement(DesktopPanelPlacementSnapshot placement);

    void UpdateComponentPlacement(DesktopPanelPlacementSnapshot placement);

    void RemoveComponentPlacement(string placementId);
}

internal sealed class DesktopPanelLayoutService : IDesktopPanelLayoutService
{
    private const string LayoutFileName = "desktop_panel_layout.json";
    private const string LegacyLayoutFileName = "fused_desktop_layout.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly object _lock = new();
    private readonly string _filePath;
    private readonly IReadOnlyList<string> _legacyFilePaths;
    private DesktopPanelLayoutSnapshot? _cachedSnapshot;

    public DesktopPanelLayoutService(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(AppDataPathProvider.GetDataRoot(), LayoutFileName);
        _legacyFilePaths =
        [
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                UserDataRoot.FolderName,
                LegacyLayoutFileName),
            Path.Combine(AppDataPathProvider.GetDataRoot(), LegacyLayoutFileName)
        ];
    }

    public DesktopPanelLayoutSnapshot Load()
    {
        lock (_lock)
        {
            if (_cachedSnapshot is not null)
            {
                return _cachedSnapshot.Clone();
            }

            try
            {
                if (!File.Exists(_filePath))
                {
                    _cachedSnapshot = TryImportLegacyLayout() ?? new DesktopPanelLayoutSnapshot();
                    return _cachedSnapshot.Clone();
                }

                var json = File.ReadAllText(_filePath);
                _cachedSnapshot = Normalize(
                    JsonSerializer.Deserialize<DesktopPanelLayoutSnapshot>(json, JsonOptions));
                return _cachedSnapshot.Clone();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("DesktopPanelLayout", "Failed to load desktop panel layout.", ex);
                _cachedSnapshot = new DesktopPanelLayoutSnapshot();
                return _cachedSnapshot.Clone();
            }
        }
    }

    public void Save(DesktopPanelLayoutSnapshot snapshot)
    {
        lock (_lock)
        {
            try
            {
                var normalized = Normalize(snapshot);
                _cachedSnapshot = normalized.Clone();
                AtomicFileWriter.WriteText(
                    _filePath,
                    JsonSerializer.Serialize(normalized, JsonOptions),
                    "DesktopPanelLayout");
            }
            catch (Exception ex)
            {
                AppLogger.Warn("DesktopPanelLayout", "Failed to save desktop panel layout.", ex);
            }
        }
    }

    public void AddComponentPlacement(DesktopPanelPlacementSnapshot placement)
    {
        var snapshot = Load();
        snapshot.ComponentPlacements.RemoveAll(existing =>
            string.Equals(existing.PlacementId, placement.PlacementId, StringComparison.OrdinalIgnoreCase));
        snapshot.ComponentPlacements.Add(placement.Clone());
        Save(snapshot);
    }

    public void UpdateComponentPlacement(DesktopPanelPlacementSnapshot placement)
    {
        var snapshot = Load();
        var index = snapshot.ComponentPlacements.FindIndex(existing =>
            string.Equals(existing.PlacementId, placement.PlacementId, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return;
        }

        snapshot.ComponentPlacements[index] = placement.Clone();
        Save(snapshot);
    }

    public void RemoveComponentPlacement(string placementId)
    {
        var snapshot = Load();
        snapshot.ComponentPlacements.RemoveAll(existing =>
            string.Equals(existing.PlacementId, placementId, StringComparison.OrdinalIgnoreCase));
        Save(snapshot);
    }

    private DesktopPanelLayoutSnapshot? TryImportLegacyLayout()
    {
        foreach (var legacyPath in _legacyFilePaths)
        {
            if (!File.Exists(legacyPath))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(legacyPath));
                if (!document.RootElement.TryGetProperty("componentPlacements", out var placements) ||
                    placements.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var imported = new DesktopPanelLayoutSnapshot();
                var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var element in placements.EnumerateArray())
                {
                    if (element.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var componentId = JsonNodeReader.ReadString(element, "componentId");
                    if (string.IsNullOrWhiteSpace(componentId))
                    {
                        continue;
                    }

                    var placementId = JsonNodeReader.ReadString(element, "placementId");
                    if (string.IsNullOrWhiteSpace(placementId) || !seenIds.Add(placementId))
                    {
                        placementId = Guid.NewGuid().ToString("N");
                        seenIds.Add(placementId);
                    }

                    imported.ComponentPlacements.Add(new DesktopPanelPlacementSnapshot
                    {
                        PlacementId = placementId,
                        ComponentId = componentId,
                        Column = Math.Max(0, JsonNodeReader.ReadInt(element, "gridColumn") ?? 0),
                        Row = Math.Max(0, JsonNodeReader.ReadInt(element, "gridRow") ?? 0),
                        WidthCells = Math.Max(1, JsonNodeReader.ReadInt(element, "gridWidthCells") ?? 1),
                        HeightCells = Math.Max(1, JsonNodeReader.ReadInt(element, "gridHeightCells") ?? 1)
                    });
                }

                if (imported.ComponentPlacements.Count == 0)
                {
                    continue;
                }

                Save(imported);
                AppLogger.Info(
                    "DesktopPanelLayout",
                    $"Imported {imported.ComponentPlacements.Count} placement(s) from legacy fused desktop layout '{legacyPath}'.");
                return imported;
            }
            catch (Exception ex)
            {
                AppLogger.Warn(
                    "DesktopPanelLayout",
                    $"Failed to import legacy fused desktop layout '{legacyPath}'.",
                    ex);
            }
        }

        return null;
    }

    private static DesktopPanelLayoutSnapshot Normalize(DesktopPanelLayoutSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return new DesktopPanelLayoutSnapshot();
        }

        var normalized = new DesktopPanelLayoutSnapshot();
        if (snapshot.ComponentPlacements is null)
        {
            return normalized;
        }

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var placement in snapshot.ComponentPlacements)
        {
            if (placement is null || string.IsNullOrWhiteSpace(placement.ComponentId))
            {
                continue;
            }

            var placementId = string.IsNullOrWhiteSpace(placement.PlacementId)
                ? Guid.NewGuid().ToString("N")
                : placement.PlacementId.Trim();
            if (!seenIds.Add(placementId))
            {
                placementId = Guid.NewGuid().ToString("N");
                seenIds.Add(placementId);
            }

            normalized.ComponentPlacements.Add(new DesktopPanelPlacementSnapshot
            {
                PlacementId = placementId,
                ComponentId = placement.ComponentId.Trim(),
                Column = Math.Max(0, placement.Column),
                Row = Math.Max(0, placement.Row),
                WidthCells = Math.Max(1, placement.WidthCells),
                HeightCells = Math.Max(1, placement.HeightCells)
            });
        }

        return normalized;
    }

}

public static class DesktopPanelLayoutServiceProvider
{
    private static readonly object Gate = new();
    private static IDesktopPanelLayoutService? _instance;

    public static IDesktopPanelLayoutService GetOrCreate()
    {
        lock (Gate)
        {
            _instance ??= new DesktopPanelLayoutService();
            return _instance;
        }
    }
}
