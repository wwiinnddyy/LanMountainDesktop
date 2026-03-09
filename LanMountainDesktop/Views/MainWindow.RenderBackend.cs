using System;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views;

public partial class MainWindow
{
    private void UpdateCurrentRenderBackendStatus()
    {
        var backendInfo = AppRenderBackendDiagnostics.Detect();
        var localizedBackend = GetLocalizedRenderBackendName(backendInfo.ActualBackend);

        CurrentRenderBackendLabelTextBlock.Text = L(
            "settings.about.render_mode.current_label",
            "Current actual backend");
        CurrentRenderBackendValueTextBlock.Text = Lf(
            "settings.about.render_mode.current_format",
            "Current backend: {0}",
            localizedBackend);
        CurrentRenderBackendImplementationTextBlock.Text = string.IsNullOrWhiteSpace(backendInfo.ImplementationTypeName)
            ? L(
                "settings.about.render_mode.impl_unavailable",
                "Runtime implementation is unavailable.")
            : Lf(
                "settings.about.render_mode.impl_format",
                "Runtime implementation: {0}",
                backendInfo.ImplementationTypeName);
    }

    private string GetLocalizedRenderBackendName(string renderBackend)
    {
        return renderBackend switch
        {
            AppRenderingModeHelper.Default => L("settings.about.render_mode.default", "Default"),
            AppRenderingModeHelper.Software => L("settings.about.render_mode.software", "Software"),
            AppRenderingModeHelper.AngleEgl => L("settings.about.render_mode.angle_egl", "angleEgl"),
            AppRenderingModeHelper.Wgl => L("settings.about.render_mode.wgl", "WGL"),
            AppRenderingModeHelper.Vulkan => L("settings.about.render_mode.vulkan", "Vulkan"),
            _ => L("settings.about.render_mode.unknown", "Unknown")
        };
    }
}
