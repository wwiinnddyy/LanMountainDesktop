using System;
using Avalonia.Threading;
using LanMountainDesktop.Views;

namespace LanMountainDesktop.Services;

internal sealed class IndependentSettingsModuleService
{
    private SettingsWindow? _window;

    public void ShowOrActivate(string source, string? pageTag = null)
    {
        AppLogger.Info("IndependentSettingsModule", $"OpenRequested; Source='{source}'; PageTag='{pageTag ?? "<default>"}'.");

        void ShowCore()
        {
            try
            {
                if (_window is not { } window)
                {
                    AppLogger.Info("IndependentSettingsModule", $"WindowConstructionStarted; Source='{source}'.");
                    window = new SettingsWindow();
                    AppLogger.Info("IndependentSettingsModule", $"WindowConstructionCompleted; Source='{source}'.");
                    window.Closed += (_, _) =>
                    {
                        if (ReferenceEquals(_window, window))
                        {
                            _window = null;
                        }

                        AppLogger.Info("IndependentSettingsModule", "WindowClosed.");
                    };
                    _window = window;
                }

                window.Open(pageTag);
                AppLogger.Info(
                    "IndependentSettingsModule",
                    $"WindowActivated; Source='{source}'; ReusedExisting={ReferenceEquals(_window, window)}; WasVisible={window.IsVisible}; PageTag='{pageTag ?? "<default>"}'.");
            }
            catch (Exception ex)
            {
                AppLogger.Warn("IndependentSettingsModule", $"Failed to open independent settings module window. Source='{source}'.", ex);
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            ShowCore();
            return;
        }

        Dispatcher.UIThread.Post(ShowCore, DispatcherPriority.Normal);
    }

    public void CloseIfOpen()
    {
        void CloseCore()
        {
            if (_window is null)
            {
                return;
            }

            try
            {
                _window.PrepareForForceClose();
                _window.Close();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("IndependentSettingsModule", "Failed to close independent settings module window during shutdown.", ex);
            }
            finally
            {
                _window = null;
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            CloseCore();
            return;
        }

        Dispatcher.UIThread.Post(CloseCore, DispatcherPriority.Send);
    }
}
