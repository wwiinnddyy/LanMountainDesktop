using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace LanMountainDesktop.Views;

public partial class SettingsWindow
{
    private void UpdateComponentsSettingsSummary()
    {
        if (ComponentsSettingsHubPanel is null)
        {
            return;
        }

        var definitions = _componentRegistry
            .GetAll()
            .OrderBy(definition => definition.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(definition => definition.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var runtime = (Application.Current as App)?.PluginRuntimeService;
        var pluginComponentIds = runtime?.DesktopComponents
            .Select(contribution => contribution.Registration.ComponentId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];

        var pluginCount = definitions.Count(definition => pluginComponentIds.Contains(definition.Id));
        var builtInCount = definitions.Count - pluginCount;
        var desktopCount = definitions.Count(definition => definition.AllowDesktopPlacement);
        var statusBarCount = definitions.Count(definition => definition.AllowStatusBarPlacement);

        ComponentsSettingsHubPanel.ComponentsSummaryTextBlock.Text = Lf(
            "settings.components.summary_format",
            "Available components: {0}. Built-in: {1}. Plugin-provided: {2}. Desktop: {3}. Status bar: {4}.",
            definitions.Count,
            builtInCount,
            pluginCount,
            desktopCount,
            statusBarCount);

        ComponentsSettingsHubPanel.ComponentCategoryItemsPanel.Children.Clear();
        foreach (var group in definitions
                     .GroupBy(definition => definition.Category, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            ComponentsSettingsHubPanel.ComponentCategoryItemsPanel.Children.Add(new Border
            {
                Margin = new Thickness(0, 4, 0, 0),
                Padding = new Thickness(12, 10),
                Background = GetThemeBrush("LayerFillColorDefaultBrush"),
                BorderBrush = GetThemeBrush("CardStrokeColorDefaultBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock
                        {
                            FontWeight = FontWeight.SemiBold,
                            Text = group.Key
                        },
                        new TextBlock
                        {
                            Foreground = GetThemeBrush("TextFillColorSecondaryBrush"),
                            Text = Lf("settings.components.category_count_format", "{0} item(s)", group.Count())
                        }
                    }
                }
            });
        }
    }
}
