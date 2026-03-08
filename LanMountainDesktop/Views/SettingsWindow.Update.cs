using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views;

public partial class SettingsWindow
{
    private void InitializeUpdateSettings(AppSettingsSnapshot snapshot)
    {
        _autoCheckUpdates = snapshot.AutoCheckUpdates;
        _updateChannel = NormalizeUpdateChannel(snapshot.UpdateChannel, snapshot.IncludePrereleaseUpdates);
        _latestReleaseVersionText = "-";
        _latestReleasePublishedAt = null;
        _updateDownloadProgressPercent = 0;
        _updateDownloadProgressText = L("settings.update.download_progress_idle", "Download progress: -");
        _updateStatusText = L("settings.update.status_ready", "Ready to check for updates.");
        _latestReleaseInstallerAsset = null;
        _downloadedUpdateInstallerPath = null;

        _suppressUpdateOptionEvents = true;
        try
        {
            AutoCheckUpdatesToggleSwitch.IsChecked = _autoCheckUpdates;
            UpdateChannelChipListBox.SelectedIndex = IncludePrereleaseUpdates ? 1 : 0;
        }
        finally
        {
            _suppressUpdateOptionEvents = false;
        }

        UpdateUpdatePanelState();
    }

    private void ApplyUpdateLocalization()
    {
        UpdatePanelTitleTextBlock.Text = L("settings.update.title", "Update");
        UpdateCurrentVersionLabelTextBlock.Text = L("settings.update.current_version_label", "Current Version");
        UpdateLatestVersionLabelTextBlock.Text = L("settings.update.latest_version_label", "Latest Release");
        UpdatePublishedAtLabelTextBlock.Text = L("settings.update.published_at_label", "Published At");
        UpdateOptionsSettingsExpander.Header = L("settings.update.options_header", "Update Options");
        UpdateOptionsSettingsExpander.Description = L("settings.update.options_desc", "Configure update checks and release channel.");
        AutoCheckUpdatesToggleSwitch.Content = L("settings.update.auto_check_toggle", "Automatically check for updates on startup");
        UpdateChannelLabelTextBlock.Text = L("settings.update.channel_label", "Update Channel");
        UpdateChannelStableChipItem.Content = L("settings.update.channel_stable", "Stable");
        UpdateChannelPreviewChipItem.Content = L("settings.update.channel_preview", "Preview");
        UpdateActionsSettingsExpander.Header = L("settings.update.actions_header", "Update Actions");
        UpdateActionsSettingsExpander.Description = L("settings.update.actions_desc", "Check releases, download installer, and start update.");
        CheckForUpdatesButton.Content = L("settings.update.check_button", "Check for Updates");
        DownloadAndInstallUpdateButton.Content = L("settings.update.download_install_button", "Download & Install");
        UpdateUpdatePanelState();
    }

    private async void OnCheckForUpdatesClick(object? sender, RoutedEventArgs e)
    {
        await CheckForUpdatesAsync(false);
    }

    private async void OnDownloadAndInstallUpdateClick(object? sender, RoutedEventArgs e)
    {
        if (_isCheckingUpdates || _isDownloadingUpdate)
        {
            return;
        }

        if (_latestReleaseInstallerAsset is null)
        {
            await CheckForUpdatesAsync(false);
        }

        if (_latestReleaseInstallerAsset is not null)
        {
            await DownloadAndInstallUpdateAsync(_latestReleaseInstallerAsset);
        }
    }

    private void OnAutoCheckUpdatesToggled(object? sender, RoutedEventArgs e)
    {
        if (_suppressUpdateOptionEvents)
        {
            return;
        }

        _autoCheckUpdates = AutoCheckUpdatesToggleSwitch.IsChecked == true;
        PersistSettings();
    }

    private void OnUpdateChannelSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressUpdateOptionEvents)
        {
            return;
        }

        var selectedChannel = UpdateChannelChipListBox.SelectedIndex == 1 ? UpdateChannelPreview : UpdateChannelStable;
        if (string.Equals(_updateChannel, selectedChannel, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _updateChannel = selectedChannel;
        _latestReleaseInstallerAsset = null;
        _latestReleaseVersionText = "-";
        _latestReleasePublishedAt = null;
        _downloadedUpdateInstallerPath = null;
        _updateStatusText = Lf("settings.update.status_channel_changed_format", "Update channel switched to {0}. Please check again.", GetLocalizedUpdateChannelName(_updateChannel));
        PersistSettings();
        UpdateUpdatePanelState();
    }

    private async Task CheckForUpdatesAsync(bool silentWhenNoUpdate)
    {
        if (_isCheckingUpdates || _isDownloadingUpdate)
        {
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            _updateStatusText = L("settings.update.status_windows_only", "Automatic installer update is currently available only on Windows.");
            UpdateUpdatePanelState();
            return;
        }

        _isCheckingUpdates = true;
        _updateStatusText = L("settings.update.status_checking", "Checking GitHub releases...");
        _updateDownloadProgressPercent = 0;
        _updateDownloadProgressText = L("settings.update.download_progress_idle", "Download progress: -");
        UpdateUpdatePanelState();

        try
        {
            if (!Version.TryParse(GetAppVersionText(), out var currentVersion))
            {
                currentVersion = new Version(0, 0, 0);
            }

            var result = await _releaseUpdateService.CheckForUpdatesAsync(currentVersion, IncludePrereleaseUpdates);
            if (!result.Success)
            {
                _latestReleaseInstallerAsset = null;
                _latestReleaseVersionText = "-";
                _latestReleasePublishedAt = null;
                _downloadedUpdateInstallerPath = null;
                _updateStatusText = Lf("settings.update.status_check_failed_format", "Update check failed: {0}", result.ErrorMessage ?? L("common.unknown", "Unknown error"));
                return;
            }

            _latestReleaseInstallerAsset = result.PreferredAsset;
            _latestReleaseVersionText = result.LatestVersionText;
            _latestReleasePublishedAt = result.Release?.PublishedAt;
            _downloadedUpdateInstallerPath = null;

            if (!result.IsUpdateAvailable)
            {
                _latestReleaseInstallerAsset = null;
                _updateStatusText = silentWhenNoUpdate
                    ? L("settings.update.status_up_to_date", "You are already on the latest version.")
                    : L("settings.update.status_up_to_date", "You are already on the latest version.");
                return;
            }

            if (_latestReleaseInstallerAsset is null)
            {
                _updateStatusText = L("settings.update.status_asset_missing", "A new release is available, but no compatible installer was found.");
                return;
            }

            _updateStatusText = Lf("settings.update.status_available_format", "New version {0} is available. Click Download & Install.", _latestReleaseVersionText);
        }
        catch (Exception ex)
        {
            _updateStatusText = Lf("settings.update.status_check_failed_format", "Update check failed: {0}", ex.Message);
        }
        finally
        {
            _isCheckingUpdates = false;
            UpdateUpdatePanelState();
        }
    }

    private async Task DownloadAndInstallUpdateAsync(GitHubReleaseAsset asset)
    {
        if (_isCheckingUpdates || _isDownloadingUpdate)
        {
            return;
        }

        _isDownloadingUpdate = true;
        _updateStatusText = L("settings.update.status_downloading", "Downloading installer...");
        _updateDownloadProgressPercent = 0;
        _updateDownloadProgressText = Lf("settings.update.download_progress_format", "Download progress: {0:F0}%", _updateDownloadProgressPercent);
        UpdateUpdatePanelState();

        try
        {
            var destinationPath = BuildUpdateInstallerPath(asset.Name);
            var progress = new Progress<double>(value =>
            {
                _updateDownloadProgressPercent = Math.Clamp(value * 100d, 0d, 100d);
                _updateDownloadProgressText = Lf("settings.update.download_progress_format", "Download progress: {0:F0}%", _updateDownloadProgressPercent);
                UpdateUpdatePanelState();
            });

            var result = await _releaseUpdateService.DownloadAssetAsync(asset, destinationPath, progress);
            if (!result.Success || string.IsNullOrWhiteSpace(result.FilePath))
            {
                _updateStatusText = Lf("settings.update.status_download_failed_format", "Download failed: {0}", result.ErrorMessage ?? L("common.unknown", "Unknown error"));
                return;
            }

            _downloadedUpdateInstallerPath = result.FilePath;
            _updateDownloadProgressPercent = 100;
            _updateDownloadProgressText = Lf("settings.update.download_progress_format", "Download progress: {0:F0}%", _updateDownloadProgressPercent);
            _updateStatusText = L("settings.update.status_launching_installer", "Download complete. Launching installer...");
            UpdateUpdatePanelState();
            LaunchInstallerAndExit(_downloadedUpdateInstallerPath);
        }
        catch (Exception ex)
        {
            _updateStatusText = Lf("settings.update.status_download_failed_format", "Download failed: {0}", ex.Message);
        }
        finally
        {
            _isDownloadingUpdate = false;
            UpdateUpdatePanelState();
        }
    }

    private void LaunchInstallerAndExit(string installerPath)
    {
        if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath))
        {
            _updateStatusText = L("settings.update.status_installer_missing", "Installer file was not found after download.");
            UpdateUpdatePanelState();
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                WorkingDirectory = Path.GetDirectoryName(installerPath) ?? Environment.CurrentDirectory,
                UseShellExecute = true
            });

            _updateStatusText = L("settings.update.status_installer_started", "Installer started. The app will close for update.");
            UpdateUpdatePanelState();

            Dispatcher.UIThread.Post(() =>
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.Shutdown();
                }
                else
                {
                    Close();
                }
            }, DispatcherPriority.Background);
        }
        catch (Exception ex)
        {
            _updateStatusText = Lf("settings.update.status_launch_failed_format", "Failed to start installer: {0}", ex.Message);
            UpdateUpdatePanelState();
        }
    }

    private void UpdateUpdatePanelState()
    {
        UpdateCurrentVersionValueTextBlock.Text = GetAppVersionText();
        UpdateLatestVersionValueTextBlock.Text = string.IsNullOrWhiteSpace(_latestReleaseVersionText) ? "-" : _latestReleaseVersionText;
        UpdatePublishedAtValueTextBlock.Text = _latestReleasePublishedAt.HasValue && _latestReleasePublishedAt.Value != DateTimeOffset.MinValue
            ? _latestReleasePublishedAt.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm")
            : "-";
        UpdateStatusTextBlock.Text = string.IsNullOrWhiteSpace(_updateStatusText) ? L("settings.update.status_ready", "Ready to check for updates.") : _updateStatusText;
        UpdateDownloadProgressTextBlock.Text = string.IsNullOrWhiteSpace(_updateDownloadProgressText) ? L("settings.update.download_progress_idle", "Download progress: -") : _updateDownloadProgressText;
        UpdateDownloadProgressBar.IsVisible = _isDownloadingUpdate;
        UpdateDownloadProgressBar.Value = Math.Clamp(_updateDownloadProgressPercent, 0d, 100d);
        CheckForUpdatesButton.IsEnabled = !_isCheckingUpdates && !_isDownloadingUpdate;
        DownloadAndInstallUpdateButton.IsEnabled = !_isCheckingUpdates && !_isDownloadingUpdate && _latestReleaseInstallerAsset is not null;
    }

    private static string NormalizeUpdateChannel(string? channel, bool includePrereleaseFallback)
    {
        if (string.Equals(channel, UpdateChannelPreview, StringComparison.OrdinalIgnoreCase))
        {
            return UpdateChannelPreview;
        }

        if (string.Equals(channel, UpdateChannelStable, StringComparison.OrdinalIgnoreCase))
        {
            return UpdateChannelStable;
        }

        return includePrereleaseFallback ? UpdateChannelPreview : UpdateChannelStable;
    }

    private string GetLocalizedUpdateChannelName(string channel)
    {
        return string.Equals(channel, UpdateChannelPreview, StringComparison.OrdinalIgnoreCase)
            ? L("settings.update.channel_preview", "Preview")
            : L("settings.update.channel_stable", "Stable");
    }

    private static string BuildUpdateInstallerPath(string assetName)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var updatesDirectory = Path.Combine(appData, "LanMountainDesktop", "Updates");
        Directory.CreateDirectory(updatesDirectory);

        var safeName = SanitizeFileName(assetName);
        return Path.Combine(updatesDirectory, safeName);
    }

    private static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return $"LanMountainDesktop-Update-{DateTime.Now:yyyyMMddHHmmss}.exe";
        }

        var sanitized = fileName;
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            sanitized = sanitized.Replace(c, '_');
        }

        return sanitized;
    }
}
