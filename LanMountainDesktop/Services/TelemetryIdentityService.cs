using System;
using System.Collections.Generic;
using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services.Settings;

namespace LanMountainDesktop.Services;

public sealed class TelemetryIdentityService
{
    private static TelemetryIdentityService? _instance;

    private readonly ISettingsFacadeService _settingsFacade;
    private readonly object _syncRoot = new();

    private string _installId = string.Empty;
    private string _telemetryId = string.Empty;
    private bool _hasReportedBaseline;

    public static TelemetryIdentityService Instance =>
        _instance ?? throw new InvalidOperationException("TelemetryIdentityService not initialized.");

    private TelemetryIdentityService(ISettingsFacadeService settingsFacade)
    {
        _settingsFacade = settingsFacade ?? throw new ArgumentNullException(nameof(settingsFacade));
    }

    public static void Initialize(ISettingsFacadeService settingsFacade)
    {
        if (_instance is not null)
        {
            return;
        }

        var instance = new TelemetryIdentityService(settingsFacade);
        instance.LoadOrCreateIdentity();
        _instance = instance;
        TelemetryServices.Identity = instance;

        AppLogger.Info(
            "TelemetryIdentity",
            $"Initialized. InstallId={instance.InstallId}; TelemetryId={instance.TelemetryId}; BaselineReported={instance.HasReportedBaseline}.");
    }

    public string InstallId
    {
        get
        {
            lock (_syncRoot)
            {
                EnsureInitialized();
                return _installId;
            }
        }
    }

    public string TelemetryId
    {
        get
        {
            lock (_syncRoot)
            {
                EnsureInitialized();
                return _telemetryId;
            }
        }
    }

    public bool HasReportedBaseline
    {
        get
        {
            lock (_syncRoot)
            {
                EnsureInitialized();
                return _hasReportedBaseline;
            }
        }
    }

    public string RefreshTelemetryId()
    {
        lock (_syncRoot)
        {
            EnsureInitialized();

            var snapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
            snapshot.TelemetryId = GenerateId();
            _settingsFacade.Settings.SaveSnapshot(
                SettingsScope.App,
                snapshot,
                changedKeys: [nameof(AppSettingsSnapshot.TelemetryId)]);

            _telemetryId = snapshot.TelemetryId ?? GenerateId();
            AppLogger.Info("TelemetryIdentity", $"Telemetry id refreshed. TelemetryId={_telemetryId}");
            return _telemetryId;
        }
    }

    public bool MarkBaselineReported()
    {
        lock (_syncRoot)
        {
            EnsureInitialized();

            if (_hasReportedBaseline)
            {
                return false;
            }

            var snapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
            if (snapshot.HasReportedTelemetryBaseline)
            {
                _hasReportedBaseline = true;
                return false;
            }

            snapshot.HasReportedTelemetryBaseline = true;
            _settingsFacade.Settings.SaveSnapshot(
                SettingsScope.App,
                snapshot,
                changedKeys: [nameof(AppSettingsSnapshot.HasReportedTelemetryBaseline)]);

            _hasReportedBaseline = true;
            AppLogger.Info("TelemetryIdentity", "Marked baseline telemetry as reported.");
            return true;
        }
    }

    private void LoadOrCreateIdentity()
    {
        lock (_syncRoot)
        {
            var snapshot = _settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
            var changedKeys = new List<string>();

            if (string.IsNullOrWhiteSpace(snapshot.TelemetryInstallId))
            {
                snapshot.TelemetryInstallId = GenerateId();
                changedKeys.Add(nameof(AppSettingsSnapshot.TelemetryInstallId));
            }

            if (string.IsNullOrWhiteSpace(snapshot.TelemetryId))
            {
                snapshot.TelemetryId = GenerateId();
                changedKeys.Add(nameof(AppSettingsSnapshot.TelemetryId));
            }

            _installId = snapshot.TelemetryInstallId ?? GenerateId();
            _telemetryId = snapshot.TelemetryId ?? GenerateId();
            _hasReportedBaseline = snapshot.HasReportedTelemetryBaseline;

            if (changedKeys.Count > 0)
            {
                _settingsFacade.Settings.SaveSnapshot(
                    SettingsScope.App,
                    snapshot,
                    changedKeys: changedKeys);
            }
        }
    }

    private void EnsureInitialized()
    {
        if (!string.IsNullOrWhiteSpace(_installId) && !string.IsNullOrWhiteSpace(_telemetryId))
        {
            return;
        }

        LoadOrCreateIdentity();
    }

    private static string GenerateId()
    {
        return Guid.NewGuid().ToString("N");
    }
}
