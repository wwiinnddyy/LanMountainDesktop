using Avalonia.Controls;
using Avalonia.Media;

namespace LanMontainDesktop.Services;

public static class GlassEffectService
{
    public static void ApplyGlassResources(IResourceDictionary resources, bool isLightBackground)
    {
        if (isLightBackground)
        {
            resources["AdaptiveButtonBackgroundBrush"] = new SolidColorBrush(Color.Parse("#80FFFFFF"));
            resources["AdaptiveButtonBorderBrush"] = new SolidColorBrush(Color.Parse("#80475569"));
            resources["AdaptiveButtonHoverBackgroundBrush"] = new SolidColorBrush(Color.Parse("#B3FFFFFF"));
            resources["AdaptiveButtonPressedBackgroundBrush"] = new SolidColorBrush(Color.Parse("#D9F8FAFC"));
            resources["AdaptiveGlassPanelBackgroundBrush"] = new SolidColorBrush(Color.Parse("#A6FFFFFF"));
            resources["AdaptiveGlassPanelBorderBrush"] = new SolidColorBrush(Color.Parse("#80475569"));
            resources["AdaptiveGlassStrongBackgroundBrush"] = new SolidColorBrush(Color.Parse("#CCFFFFFF"));
            resources["AdaptiveGlassStrongBorderBrush"] = new SolidColorBrush(Color.Parse("#80475569"));
            return;
        }

        resources["AdaptiveButtonBackgroundBrush"] = new SolidColorBrush(Color.Parse("#66334155"));
        resources["AdaptiveButtonBorderBrush"] = new SolidColorBrush(Color.Parse("#80E2E8F0"));
        resources["AdaptiveButtonHoverBackgroundBrush"] = new SolidColorBrush(Color.Parse("#88475A74"));
        resources["AdaptiveButtonPressedBackgroundBrush"] = new SolidColorBrush(Color.Parse("#AA2A3B55"));
        resources["AdaptiveGlassPanelBackgroundBrush"] = new SolidColorBrush(Color.Parse("#70233448"));
        resources["AdaptiveGlassPanelBorderBrush"] = new SolidColorBrush(Color.Parse("#70475569"));
        resources["AdaptiveGlassStrongBackgroundBrush"] = new SolidColorBrush(Color.Parse("#A01E293B"));
        resources["AdaptiveGlassStrongBorderBrush"] = new SolidColorBrush(Color.Parse("#80475569"));
    }
}
