using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using LanMountainDesktop.Models;
using Microsoft.Data.Sqlite;

namespace LanMountainDesktop.Services.Settings;

public interface IComponentLayoutStore
{
    DesktopLayoutSettingsSnapshot LoadLayout();

    void SaveLayout(DesktopLayoutSettingsSnapshot snapshot);
}

public interface IComponentStateStore
{
    ComponentSettingsSnapshot LoadState(string componentId, string? placementId);

    void SaveState(string componentId, string? placementId, ComponentSettingsSnapshot snapshot);

    void DeleteState(string componentId, string? placementId);
}

public interface IComponentMessageStore
{
    T LoadSection<T>(string componentId, string? placementId, string sectionId) where T : new();

    void SaveSection<T>(string componentId, string? placementId, string sectionId, T section);

    void DeleteSection(string componentId, string? placementId, string sectionId);
}

internal static class ComponentDomainStorageProvider
{
    private static readonly object Gate = new();
    private static SqliteComponentDomainStorage? _instance;

    public static SqliteComponentDomainStorage Instance
    {
        get
        {
            lock (Gate)
            {
                _instance ??= new SqliteComponentDomainStorage();
                return _instance;
            }
        }
    }
}

internal sealed class SqliteComponentDomainStorage :
    IComponentLayoutStore,
    IComponentStateStore,
    IComponentMessageStore
{
    private const string MigrationMarkerKey = "component_domain_v1";
    private const string DefaultInstanceKey = "__default__";
    private const string LegacySectionId = "__legacy__";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly object _gate = new();
    private readonly string _settingsRoot;
    private readonly string _dbPath;
    private readonly string _layoutJsonPath;
    private readonly string _componentJsonPath;

    public SqliteComponentDomainStorage(string? settingsRoot = null)
    {
        _settingsRoot = string.IsNullOrWhiteSpace(settingsRoot)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LanMountainDesktop")
            : settingsRoot.Trim();
        _dbPath = Path.Combine(_settingsRoot, "component-state.db");
        _layoutJsonPath = Path.Combine(_settingsRoot, "desktop-layout-settings.json");
        _componentJsonPath = Path.Combine(_settingsRoot, "component-settings.json");

        Directory.CreateDirectory(_settingsRoot);
        InitializeDatabase();
    }

    public DesktopLayoutSettingsSnapshot LoadLayout()
    {
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                                  SELECT desktop_page_count, current_desktop_surface_index
                                  FROM component_layout
                                  WHERE id = 1;
                                  """;
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return new DesktopLayoutSettingsSnapshot();
            }

            return new DesktopLayoutSettingsSnapshot
            {
                DesktopPageCount = Math.Max(1, reader.GetInt32(0)),
                CurrentDesktopSurfaceIndex = Math.Max(0, reader.GetInt32(1)),
                DesktopComponentPlacements = LoadPlacements(connection)
            };
        }
    }

    public void SaveLayout(DesktopLayoutSettingsSnapshot snapshot)
    {
        var normalized = snapshot?.Clone() ?? new DesktopLayoutSettingsSnapshot();
        normalized.DesktopPageCount = Math.Max(1, normalized.DesktopPageCount);
        normalized.CurrentDesktopSurfaceIndex = Math.Max(0, normalized.CurrentDesktopSurfaceIndex);

        lock (_gate)
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                                      INSERT INTO component_layout(id, desktop_page_count, current_desktop_surface_index, updated_utc)
                                      VALUES(1, $count, $index, $updated)
                                      ON CONFLICT(id) DO UPDATE SET
                                        desktop_page_count = excluded.desktop_page_count,
                                        current_desktop_surface_index = excluded.current_desktop_surface_index,
                                        updated_utc = excluded.updated_utc;
                                      """;
                command.Parameters.AddWithValue("$count", normalized.DesktopPageCount);
                command.Parameters.AddWithValue("$index", normalized.CurrentDesktopSurfaceIndex);
                command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                command.ExecuteNonQuery();
            }

            using (var deleteCommand = connection.CreateCommand())
            {
                deleteCommand.Transaction = transaction;
                deleteCommand.CommandText = "DELETE FROM component_placement;";
                deleteCommand.ExecuteNonQuery();
            }

            if (normalized.DesktopComponentPlacements is { Count: > 0 })
            {
                foreach (var placement in normalized.DesktopComponentPlacements)
                {
                    if (placement is null || string.IsNullOrWhiteSpace(placement.PlacementId))
                    {
                        continue;
                    }

                    using var insertCommand = connection.CreateCommand();
                    insertCommand.Transaction = transaction;
                    insertCommand.CommandText = """
                                                INSERT INTO component_placement(
                                                    placement_id, page_index, component_id, row_index, column_index, width_cells, height_cells, updated_utc)
                                                VALUES($placementId, $page, $componentId, $row, $column, $width, $height, $updated);
                                                """;
                    insertCommand.Parameters.AddWithValue("$placementId", placement.PlacementId.Trim());
                    insertCommand.Parameters.AddWithValue("$page", Math.Max(0, placement.PageIndex));
                    insertCommand.Parameters.AddWithValue("$componentId", placement.ComponentId?.Trim() ?? string.Empty);
                    insertCommand.Parameters.AddWithValue("$row", Math.Max(0, placement.Row));
                    insertCommand.Parameters.AddWithValue("$column", Math.Max(0, placement.Column));
                    insertCommand.Parameters.AddWithValue("$width", Math.Max(1, placement.WidthCells));
                    insertCommand.Parameters.AddWithValue("$height", Math.Max(1, placement.HeightCells));
                    insertCommand.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                    insertCommand.ExecuteNonQuery();
                }
            }

            transaction.Commit();
        }
    }

    public ComponentSettingsSnapshot LoadState(string componentId, string? placementId)
    {
        var instanceKey = BuildInstanceKey(componentId, placementId);
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                                  SELECT state_json
                                  FROM component_state
                                  WHERE instance_key = $instanceKey
                                  LIMIT 1;
                                  """;
            command.Parameters.AddWithValue("$instanceKey", instanceKey);
            var json = command.ExecuteScalar() as string;
            if (string.IsNullOrWhiteSpace(json))
            {
                if (string.Equals(instanceKey, DefaultInstanceKey, StringComparison.OrdinalIgnoreCase))
                {
                    return new ComponentSettingsSnapshot();
                }

                return LoadDefaultState(connection);
            }

            return DeserializeState(json);
        }
    }

    public void SaveState(string componentId, string? placementId, ComponentSettingsSnapshot snapshot)
    {
        var instanceKey = BuildInstanceKey(componentId, placementId);
        var normalizedComponentId = NormalizeKey(componentId);
        var normalizedPlacementId = NormalizePlacement(placementId);
        var json = JsonSerializer.Serialize(snapshot ?? new ComponentSettingsSnapshot(), SerializerOptions);

        lock (_gate)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                                  INSERT INTO component_state(instance_key, component_id, placement_id, state_json, updated_utc)
                                  VALUES($instanceKey, $componentId, $placementId, $stateJson, $updated)
                                  ON CONFLICT(instance_key) DO UPDATE SET
                                    component_id = excluded.component_id,
                                    placement_id = excluded.placement_id,
                                    state_json = excluded.state_json,
                                    updated_utc = excluded.updated_utc;
                                  """;
            command.Parameters.AddWithValue("$instanceKey", instanceKey);
            command.Parameters.AddWithValue("$componentId", normalizedComponentId);
            command.Parameters.AddWithValue("$placementId", normalizedPlacementId);
            command.Parameters.AddWithValue("$stateJson", json);
            command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
        }
    }

    public void DeleteState(string componentId, string? placementId)
    {
        var instanceKey = BuildInstanceKey(componentId, placementId);
        if (string.Equals(instanceKey, DefaultInstanceKey, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        lock (_gate)
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            using (var stateDelete = connection.CreateCommand())
            {
                stateDelete.Transaction = transaction;
                stateDelete.CommandText = "DELETE FROM component_state WHERE instance_key = $instanceKey;";
                stateDelete.Parameters.AddWithValue("$instanceKey", instanceKey);
                stateDelete.ExecuteNonQuery();
            }

            using (var messageDelete = connection.CreateCommand())
            {
                messageDelete.Transaction = transaction;
                messageDelete.CommandText = "DELETE FROM component_message WHERE instance_key = $instanceKey;";
                messageDelete.Parameters.AddWithValue("$instanceKey", instanceKey);
                messageDelete.ExecuteNonQuery();
            }

            transaction.Commit();
        }
    }

    public T LoadSection<T>(string componentId, string? placementId, string sectionId) where T : new()
    {
        var instanceKey = BuildInstanceKey(componentId, placementId);
        var normalizedSectionId = NormalizeSection(sectionId);

        lock (_gate)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                                  SELECT message_json
                                  FROM component_message
                                  WHERE instance_key = $instanceKey
                                    AND section_id = $sectionId
                                  LIMIT 1;
                                  """;
            command.Parameters.AddWithValue("$instanceKey", instanceKey);
            command.Parameters.AddWithValue("$sectionId", normalizedSectionId);
            var json = command.ExecuteScalar() as string;
            if (string.IsNullOrWhiteSpace(json))
            {
                return new T();
            }

            try
            {
                return JsonSerializer.Deserialize<T>(json, SerializerOptions) ?? new T();
            }
            catch
            {
                return new T();
            }
        }
    }

    public void SaveSection<T>(string componentId, string? placementId, string sectionId, T section)
    {
        var instanceKey = BuildInstanceKey(componentId, placementId);
        var normalizedComponentId = NormalizeKey(componentId);
        var normalizedPlacementId = NormalizePlacement(placementId);
        var normalizedSectionId = NormalizeSection(sectionId);
        var json = JsonSerializer.Serialize(section, SerializerOptions);

        lock (_gate)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                                  INSERT INTO component_message(instance_key, component_id, placement_id, section_id, message_json, updated_utc)
                                  VALUES($instanceKey, $componentId, $placementId, $sectionId, $messageJson, $updated)
                                  ON CONFLICT(instance_key, section_id) DO UPDATE SET
                                    component_id = excluded.component_id,
                                    placement_id = excluded.placement_id,
                                    message_json = excluded.message_json,
                                    updated_utc = excluded.updated_utc;
                                  """;
            command.Parameters.AddWithValue("$instanceKey", instanceKey);
            command.Parameters.AddWithValue("$componentId", normalizedComponentId);
            command.Parameters.AddWithValue("$placementId", normalizedPlacementId);
            command.Parameters.AddWithValue("$sectionId", normalizedSectionId);
            command.Parameters.AddWithValue("$messageJson", json);
            command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
        }
    }

    public void DeleteSection(string componentId, string? placementId, string sectionId)
    {
        var instanceKey = BuildInstanceKey(componentId, placementId);
        var normalizedSectionId = NormalizeSection(sectionId);

        lock (_gate)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                                  DELETE FROM component_message
                                  WHERE instance_key = $instanceKey
                                    AND section_id = $sectionId;
                                  """;
            command.Parameters.AddWithValue("$instanceKey", instanceKey);
            command.Parameters.AddWithValue("$sectionId", normalizedSectionId);
            command.ExecuteNonQuery();
        }
    }

    public T LoadLegacyMessage<T>(string componentId, string? placementId) where T : new()
    {
        return LoadSection<T>(componentId, placementId, LegacySectionId);
    }

    public void SaveLegacyMessage<T>(string componentId, string? placementId, T section)
    {
        SaveSection(componentId, placementId, LegacySectionId, section);
    }

    public void DeleteLegacyMessage(string componentId, string? placementId)
    {
        DeleteSection(componentId, placementId, LegacySectionId);
    }

    private void InitializeDatabase()
    {
        lock (_gate)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                                  CREATE TABLE IF NOT EXISTS settings_meta(
                                      key TEXT PRIMARY KEY,
                                      value TEXT NOT NULL
                                  );

                                  CREATE TABLE IF NOT EXISTS component_layout(
                                      id INTEGER PRIMARY KEY CHECK(id = 1),
                                      desktop_page_count INTEGER NOT NULL,
                                      current_desktop_surface_index INTEGER NOT NULL,
                                      updated_utc TEXT NOT NULL
                                  );

                                  CREATE TABLE IF NOT EXISTS component_placement(
                                      placement_id TEXT PRIMARY KEY,
                                      page_index INTEGER NOT NULL,
                                      component_id TEXT NOT NULL,
                                      row_index INTEGER NOT NULL,
                                      column_index INTEGER NOT NULL,
                                      width_cells INTEGER NOT NULL,
                                      height_cells INTEGER NOT NULL,
                                      updated_utc TEXT NOT NULL
                                  );

                                  CREATE TABLE IF NOT EXISTS component_state(
                                      instance_key TEXT PRIMARY KEY,
                                      component_id TEXT NOT NULL,
                                      placement_id TEXT NOT NULL,
                                      state_json TEXT NOT NULL,
                                      updated_utc TEXT NOT NULL
                                  );

                                  CREATE TABLE IF NOT EXISTS component_message(
                                      instance_key TEXT NOT NULL,
                                      component_id TEXT NOT NULL,
                                      placement_id TEXT NOT NULL,
                                      section_id TEXT NOT NULL,
                                      message_json TEXT NOT NULL,
                                      updated_utc TEXT NOT NULL,
                                      PRIMARY KEY(instance_key, section_id)
                                  );
                                  """;
            command.ExecuteNonQuery();

            if (!IsMigrationApplied(connection))
            {
                ApplyInitialMigration(connection);
            }
        }
    }

    private bool IsMigrationApplied(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT value
                              FROM settings_meta
                              WHERE key = $key
                              LIMIT 1;
                              """;
        command.Parameters.AddWithValue("$key", MigrationMarkerKey);
        var raw = command.ExecuteScalar() as string;
        return string.Equals(raw, "applied", StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyInitialMigration(SqliteConnection connection)
    {
        AppLogger.Info("ComponentDomainStorage", "Starting one-shot migration from legacy JSON files to SQLite.");
        using var transaction = connection.BeginTransaction();
        try
        {
            if (TryLoadLegacyLayout(out var layout))
            {
                PersistLayout(connection, transaction, layout);
            }

            if (TryLoadLegacyComponentDocument(out var document))
            {
                PersistComponentDocument(connection, transaction, document);
            }

            using var markerCommand = connection.CreateCommand();
            markerCommand.Transaction = transaction;
            markerCommand.CommandText = """
                                        INSERT INTO settings_meta(key, value)
                                        VALUES($key, 'applied')
                                        ON CONFLICT(key) DO UPDATE SET value = 'applied';
                                        """;
            markerCommand.Parameters.AddWithValue("$key", MigrationMarkerKey);
            markerCommand.ExecuteNonQuery();

            transaction.Commit();
            BackupLegacyFile(_layoutJsonPath);
            BackupLegacyFile(_componentJsonPath);
            AppLogger.Info("ComponentDomainStorage", "Legacy JSON migration completed.");
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            AppLogger.Error("ComponentDomainStorage", "Legacy JSON migration failed. SQLite writes are blocked for this session.", ex);
            throw;
        }
    }

    private void PersistLayout(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DesktopLayoutSettingsSnapshot snapshot)
    {
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                                  INSERT INTO component_layout(id, desktop_page_count, current_desktop_surface_index, updated_utc)
                                  VALUES(1, $count, $index, $updated)
                                  ON CONFLICT(id) DO UPDATE SET
                                    desktop_page_count = excluded.desktop_page_count,
                                    current_desktop_surface_index = excluded.current_desktop_surface_index,
                                    updated_utc = excluded.updated_utc;
                                  """;
            command.Parameters.AddWithValue("$count", Math.Max(1, snapshot.DesktopPageCount));
            command.Parameters.AddWithValue("$index", Math.Max(0, snapshot.CurrentDesktopSurfaceIndex));
            command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
        }

        if (snapshot.DesktopComponentPlacements is not { Count: > 0 })
        {
            return;
        }

        foreach (var placement in snapshot.DesktopComponentPlacements)
        {
            if (placement is null || string.IsNullOrWhiteSpace(placement.PlacementId))
            {
                continue;
            }

            using var placementCommand = connection.CreateCommand();
            placementCommand.Transaction = transaction;
            placementCommand.CommandText = """
                                           INSERT INTO component_placement(
                                               placement_id, page_index, component_id, row_index, column_index, width_cells, height_cells, updated_utc)
                                           VALUES($placementId, $page, $componentId, $row, $column, $width, $height, $updated)
                                           ON CONFLICT(placement_id) DO UPDATE SET
                                               page_index = excluded.page_index,
                                               component_id = excluded.component_id,
                                               row_index = excluded.row_index,
                                               column_index = excluded.column_index,
                                               width_cells = excluded.width_cells,
                                               height_cells = excluded.height_cells,
                                               updated_utc = excluded.updated_utc;
                                           """;
            placementCommand.Parameters.AddWithValue("$placementId", placement.PlacementId.Trim());
            placementCommand.Parameters.AddWithValue("$page", Math.Max(0, placement.PageIndex));
            placementCommand.Parameters.AddWithValue("$componentId", placement.ComponentId?.Trim() ?? string.Empty);
            placementCommand.Parameters.AddWithValue("$row", Math.Max(0, placement.Row));
            placementCommand.Parameters.AddWithValue("$column", Math.Max(0, placement.Column));
            placementCommand.Parameters.AddWithValue("$width", Math.Max(1, placement.WidthCells));
            placementCommand.Parameters.AddWithValue("$height", Math.Max(1, placement.HeightCells));
            placementCommand.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            placementCommand.ExecuteNonQuery();
        }
    }

    private void PersistComponentDocument(
        SqliteConnection connection,
        SqliteTransaction transaction,
        LegacyComponentDocument document)
    {
        PersistComponentState(connection, transaction, DefaultInstanceKey, "__default__", string.Empty, document.DefaultSettings ?? new ComponentSettingsSnapshot());

        if (document.InstanceSettings is not null)
        {
            foreach (var pair in document.InstanceSettings)
            {
                if (!TrySplitInstanceKey(pair.Key, out var componentId, out var placementId))
                {
                    continue;
                }

                PersistComponentState(connection, transaction, pair.Key.Trim(), componentId, placementId, pair.Value ?? new ComponentSettingsSnapshot());
            }
        }

        if (document.PluginSettings is null)
        {
            return;
        }

        foreach (var pair in document.PluginSettings)
        {
            if (!TrySplitInstanceKey(pair.Key, out var componentId, out var placementId))
            {
                continue;
            }

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                                  INSERT INTO component_message(instance_key, component_id, placement_id, section_id, message_json, updated_utc)
                                  VALUES($instanceKey, $componentId, $placementId, $sectionId, $json, $updated)
                                  ON CONFLICT(instance_key, section_id) DO UPDATE SET
                                    component_id = excluded.component_id,
                                    placement_id = excluded.placement_id,
                                    message_json = excluded.message_json,
                                    updated_utc = excluded.updated_utc;
                                  """;
            command.Parameters.AddWithValue("$instanceKey", pair.Key.Trim());
            command.Parameters.AddWithValue("$componentId", componentId);
            command.Parameters.AddWithValue("$placementId", placementId);
            command.Parameters.AddWithValue("$sectionId", LegacySectionId);
            command.Parameters.AddWithValue("$json", pair.Value.GetRawText());
            command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
        }
    }

    private static void PersistComponentState(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string instanceKey,
        string componentId,
        string placementId,
        ComponentSettingsSnapshot snapshot)
    {
        var json = JsonSerializer.Serialize(snapshot ?? new ComponentSettingsSnapshot(), SerializerOptions);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
                              INSERT INTO component_state(instance_key, component_id, placement_id, state_json, updated_utc)
                              VALUES($instanceKey, $componentId, $placementId, $stateJson, $updated)
                              ON CONFLICT(instance_key) DO UPDATE SET
                                component_id = excluded.component_id,
                                placement_id = excluded.placement_id,
                                state_json = excluded.state_json,
                                updated_utc = excluded.updated_utc;
                              """;
        command.Parameters.AddWithValue("$instanceKey", instanceKey);
        command.Parameters.AddWithValue("$componentId", componentId);
        command.Parameters.AddWithValue("$placementId", placementId);
        command.Parameters.AddWithValue("$stateJson", json);
        command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    private bool TryLoadLegacyLayout(out DesktopLayoutSettingsSnapshot snapshot)
    {
        snapshot = new DesktopLayoutSettingsSnapshot();
        if (!File.Exists(_layoutJsonPath))
        {
            return false;
        }

        try
        {
            var json = File.ReadAllText(_layoutJsonPath);
            snapshot = JsonSerializer.Deserialize<DesktopLayoutSettingsSnapshot>(json, SerializerOptions) ?? new DesktopLayoutSettingsSnapshot();
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("ComponentDomainStorage", $"Failed to read legacy layout file '{_layoutJsonPath}'.", ex);
            return false;
        }
    }

    private bool TryLoadLegacyComponentDocument(out LegacyComponentDocument document)
    {
        document = new LegacyComponentDocument();
        if (!File.Exists(_componentJsonPath))
        {
            return false;
        }

        try
        {
            var json = File.ReadAllText(_componentJsonPath);
            using var parsed = JsonDocument.Parse(json);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var hasDocumentShape = false;
            foreach (var property in parsed.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, "defaultSettings", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(property.Name, "instanceSettings", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(property.Name, "pluginSettings", StringComparison.OrdinalIgnoreCase))
                {
                    hasDocumentShape = true;
                    break;
                }
            }

            if (hasDocumentShape)
            {
                document = JsonSerializer.Deserialize<LegacyComponentDocument>(json, SerializerOptions) ?? new LegacyComponentDocument();
                document.DefaultSettings ??= new ComponentSettingsSnapshot();
                document.InstanceSettings ??= new Dictionary<string, ComponentSettingsSnapshot>(StringComparer.OrdinalIgnoreCase);
                document.PluginSettings ??= new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                return true;
            }

            var legacySingle = JsonSerializer.Deserialize<ComponentSettingsSnapshot>(json, SerializerOptions) ?? new ComponentSettingsSnapshot();
            document = new LegacyComponentDocument
            {
                DefaultSettings = legacySingle
            };
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("ComponentDomainStorage", $"Failed to read legacy component settings file '{_componentJsonPath}'.", ex);
            return false;
        }
    }

    private static void BackupLegacyFile(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var backupPath = $"{path}.migrated.bak";
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }

            File.Move(path, backupPath);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("ComponentDomainStorage", $"Failed to backup migrated legacy file '{path}'.", ex);
        }
    }

    private static bool TrySplitInstanceKey(string key, out string componentId, out string placementId)
    {
        componentId = string.Empty;
        placementId = string.Empty;
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var normalized = key.Trim();
        var parts = normalized.Split("::", 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            string.IsNullOrWhiteSpace(parts[0]) ||
            string.IsNullOrWhiteSpace(parts[1]))
        {
            return false;
        }

        componentId = parts[0];
        placementId = parts[1];
        return true;
    }

    private static string BuildInstanceKey(string componentId, string? placementId)
    {
        var normalizedComponentId = NormalizeKey(componentId);
        var normalizedPlacementId = NormalizePlacement(placementId);
        if (string.IsNullOrWhiteSpace(normalizedComponentId) ||
            string.IsNullOrWhiteSpace(normalizedPlacementId))
        {
            return DefaultInstanceKey;
        }

        return $"{normalizedComponentId}::{normalizedPlacementId}";
    }

    private static string NormalizeKey(string? key)
    {
        return key?.Trim() ?? string.Empty;
    }

    private static string NormalizePlacement(string? placementId)
    {
        return placementId?.Trim() ?? string.Empty;
    }

    private static string NormalizeSection(string? sectionId)
    {
        return string.IsNullOrWhiteSpace(sectionId) ? LegacySectionId : sectionId.Trim();
    }

    private static ComponentSettingsSnapshot DeserializeState(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ComponentSettingsSnapshot>(json, SerializerOptions) ?? new ComponentSettingsSnapshot();
        }
        catch
        {
            return new ComponentSettingsSnapshot();
        }
    }

    private static ComponentSettingsSnapshot LoadDefaultState(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT state_json
                              FROM component_state
                              WHERE instance_key = $instanceKey
                              LIMIT 1;
                              """;
        command.Parameters.AddWithValue("$instanceKey", DefaultInstanceKey);
        var json = command.ExecuteScalar() as string;
        return string.IsNullOrWhiteSpace(json) ? new ComponentSettingsSnapshot() : DeserializeState(json);
    }

    private static List<DesktopComponentPlacementSnapshot> LoadPlacements(SqliteConnection connection)
    {
        var placements = new List<DesktopComponentPlacementSnapshot>();
        using var command = connection.CreateCommand();
        command.CommandText = """
                              SELECT placement_id, page_index, component_id, row_index, column_index, width_cells, height_cells
                              FROM component_placement
                              ORDER BY page_index, row_index, column_index;
                              """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            placements.Add(new DesktopComponentPlacementSnapshot
            {
                PlacementId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                PageIndex = reader.IsDBNull(1) ? 0 : Math.Max(0, reader.GetInt32(1)),
                ComponentId = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                Row = reader.IsDBNull(3) ? 0 : Math.Max(0, reader.GetInt32(3)),
                Column = reader.IsDBNull(4) ? 0 : Math.Max(0, reader.GetInt32(4)),
                WidthCells = reader.IsDBNull(5) ? 1 : Math.Max(1, reader.GetInt32(5)),
                HeightCells = reader.IsDBNull(6) ? 1 : Math.Max(1, reader.GetInt32(6))
            });
        }

        return placements;
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection($"Data Source={_dbPath};Mode=ReadWriteCreate;Cache=Shared");
        connection.Open();
        return connection;
    }

    private sealed class LegacyComponentDocument
    {
        public ComponentSettingsSnapshot? DefaultSettings { get; set; } = new();

        public Dictionary<string, ComponentSettingsSnapshot>? InstanceSettings { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, JsonElement>? PluginSettings { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }
}
