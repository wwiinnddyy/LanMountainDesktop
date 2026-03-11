using System.Threading.Tasks;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views;

public partial class SettingsWindow
{
    private bool _isRestartPromptVisible;

    private void OnPendingRestartStateChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            UpdatePendingRestartDock();
            return;
        }

        Dispatcher.UIThread.Post(UpdatePendingRestartDock);
    }

    private void UpdatePendingRestartDock()
    {
        PendingRestartDock.IsVisible = PendingRestartStateService.HasPendingRestart;
        PendingRestartDockTitleTextBlock.Text = L("settings.restart_dock.title", "Restart required");
        PendingRestartDockDescriptionTextBlock.Text = L(
            "settings.restart_dock.description",
            "Some changes will take effect after restarting the app.");
        PendingRestartDockButtonTextBlock.Text = L("settings.restart_dock.button", "Restart app");
    }

    private async void OnPendingRestartDockButtonClick(object? sender, RoutedEventArgs e)
    {
        await ShowGenericRestartPromptAsync();
    }

    private Task ShowRenderModeRestartPromptAsync(string selectedMode)
    {
        var message = Lf(
            "settings.restart_dialog.render_mode_message",
            "Restart the app to switch the rendering mode from \"{0}\" to \"{1}\". Restart now?",
            GetLocalizedAppRenderModeDisplayName(_runningAppRenderMode),
            GetLocalizedAppRenderModeDisplayName(selectedMode));

        return ShowRestartPromptCoreAsync(message);
    }

    private Task ShowGenericRestartPromptAsync()
    {
        return ShowRestartPromptCoreAsync(L(
            "settings.restart_dock.description",
            "Some changes will take effect after restarting the app."));
    }

    private async Task ShowRestartPromptCoreAsync(string message)
    {
        if (_isRestartPromptVisible)
        {
            return;
        }

        _isRestartPromptVisible = true;

        try
        {
            var dialog = new ContentDialog
            {
                Title = L("settings.restart_dialog.title", "Restart required"),
                Content = message,
                PrimaryButtonText = L("settings.restart_dialog.restart", "Restart now"),
                CloseButtonText = L("settings.restart_dialog.cancel", "Cancel"),
                DefaultButton = ContentDialogButton.Primary
            };

            var result = await dialog.ShowAsync(this);
            if (result == ContentDialogResult.Primary)
            {
                if (App.CurrentHostApplicationLifecycle?.TryRestart(new HostApplicationLifecycleRequest(
                        Source: nameof(SettingsWindow),
                        Reason: "User confirmed a pending restart prompt from settings.")) != true)
                {
                    UpdatePendingRestartDock();
                }

                return;
            }

            UpdatePendingRestartDock();
        }
        finally
        {
            _isRestartPromptVisible = false;
        }
    }

    private string GetLocalizedAppRenderModeDisplayName(string renderMode)
    {
        if (renderMode == AppRenderBackendDiagnostics.Unknown)
        {
            return L("settings.about.render_mode.unknown", "Unknown");
        }

        return AppRenderingModeHelper.Normalize(renderMode) switch
        {
            AppRenderingModeHelper.Software => L("settings.about.render_mode.software", "Software"),
            AppRenderingModeHelper.AngleEgl => L("settings.about.render_mode.angle_egl", "angleEgl"),
            AppRenderingModeHelper.Wgl => L("settings.about.render_mode.wgl", "WGL"),
            AppRenderingModeHelper.Vulkan => L("settings.about.render_mode.vulkan", "Vulkan"),
            _ => L("settings.about.render_mode.default", "Default")
        };
    }
}
