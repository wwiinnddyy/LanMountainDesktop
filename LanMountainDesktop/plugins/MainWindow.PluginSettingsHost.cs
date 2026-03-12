using System;
using System.Linq;
using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using LanMountainDesktop.Models;

namespace LanMountainDesktop.Views;

public partial class MainWindow
{
    private void InitializePluginSettingsNavigation()
    {
        // Legacy plugin settings pages are removed in API-only settings mode.
    }

    private void UpdatePluginSettingsPageVisibility(string? selectedTag)
    {
        _ = selectedTag;
    }

    internal void RefreshPluginSettingsNavigation()
    {
        // Legacy plugin settings pages are removed in API-only settings mode.
    }

    private string? GetSelectedSettingsTabTag()
    {
        return (SettingsNavView?.SelectedItem as NavigationViewItem)?.Tag?.ToString();
    }

    private int ResolveSelectedSettingsTabIndex()
    {
        if (SettingsNavView?.SelectedItem is null || SettingsNavView.MenuItems is null)
        {
            return 0;
        }

        for (var i = 0; i < SettingsNavView.MenuItems.Count; i++)
        {
            if (ReferenceEquals(SettingsNavView.MenuItems[i], SettingsNavView.SelectedItem))
            {
                return i;
            }
        }

        return 0;
    }

    private void RestoreSettingsTabSelection(AppSettingsSnapshot snapshot)
    {
        if (SettingsNavView?.MenuItems is null || SettingsNavView.MenuItems.Count == 0)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.SettingsTabTag))
        {
            var taggedItem = SettingsNavView.MenuItems
                .OfType<NavigationViewItem>()
                .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), snapshot.SettingsTabTag, StringComparison.OrdinalIgnoreCase));
            if (taggedItem is not null)
            {
                SettingsNavView.SelectedItem = taggedItem;
                return;
            }
        }

        var safeIndex = Math.Clamp(snapshot.SettingsTabIndex, 0, Math.Max(0, SettingsNavView.MenuItems.Count - 1));
        if (SettingsNavView.MenuItems[safeIndex] is NavigationViewItem navItem)
        {
            SettingsNavView.SelectedItem = navItem;
        }
    }
}
