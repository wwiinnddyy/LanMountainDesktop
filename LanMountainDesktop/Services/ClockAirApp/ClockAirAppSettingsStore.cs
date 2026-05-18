using System;
using System.IO;
using System.Text.Json;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Services.ClockAirApp;

public sealed class ClockAirAppSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public ClockAirAppSettingsStore()
        : this(Path.Combine(AppDataPathProvider.GetDataRoot(), "AirApps", "Clock", "settings.json"))
    {
    }

    public ClockAirAppSettingsStore(string settingsPath)
    {
        _settingsPath = settingsPath;
    }

    public string SettingsPath => _settingsPath;

    public ClockAirAppSettingsSnapshot Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return ClockAirAppSettingsSnapshot.Normalize(null);
            }

            var json = File.ReadAllText(_settingsPath);
            var snapshot = JsonSerializer.Deserialize<ClockAirAppSettingsSnapshot>(json, SerializerOptions);
            return ClockAirAppSettingsSnapshot.Normalize(snapshot);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("ClockAirApp", $"Failed to load clock Air APP settings from '{_settingsPath}'.", ex);
            return ClockAirAppSettingsSnapshot.Normalize(null);
        }
    }

    public void Save(ClockAirAppSettingsSnapshot snapshot)
    {
        var normalized = ClockAirAppSettingsSnapshot.Normalize(snapshot);
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(normalized, SerializerOptions));
        }
        catch (Exception ex)
        {
            AppLogger.Warn("ClockAirApp", $"Failed to save clock Air APP settings to '{_settingsPath}'.", ex);
        }
    }
}
