using System;
using System.IO;
using System.Text.Json;
using LanMountainDesktop.Models;

namespace LanMountainDesktop.Services;

public sealed class AppSettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public AppSettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var settingsDirectory = Path.Combine(appData, "LanMountainDesktop");
        _settingsPath = Path.Combine(settingsDirectory, "settings.json");
    }

    public AppSettingsSnapshot Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new AppSettingsSnapshot();
            }

            var json = File.ReadAllText(_settingsPath);
            var snapshot = JsonSerializer.Deserialize<AppSettingsSnapshot>(json, SerializerOptions);
            return snapshot ?? new AppSettingsSnapshot();
        }
        catch
        {
            return new AppSettingsSnapshot();
        }
    }

    public void Save(AppSettingsSnapshot snapshot)
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(snapshot, SerializerOptions);
            File.WriteAllText(_settingsPath, json);
        }
        catch
        {
            // Swallow persistence errors to keep UI interactions uninterrupted.
        }
    }
}

