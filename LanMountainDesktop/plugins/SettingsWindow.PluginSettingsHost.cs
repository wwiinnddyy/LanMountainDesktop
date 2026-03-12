using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FluentIcons.Common;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views;

public partial class SettingsWindow
{
    private readonly Dictionary<string, Control> _pluginSettingsPageHosts = new(StringComparer.OrdinalIgnoreCase);

    private void InitializePluginSettingsNavigation()
    {
        if (_pluginSettingsPageHosts.Count > 0)
        {
            return;
        }

        var runtime = (Application.Current as App)?.PluginRuntimeService;
        var contributions = runtime?.SettingsPages
            .OrderBy(contribution => contribution.Registration.SortOrder)
            .ThenBy(contribution => contribution.Plugin.Manifest.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(contribution => contribution.Registration.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (contributions is not { Length: > 0 })
        {
            SettingsPluginNavSection.IsVisible = false;
            return;
        }

        var pageCountsByPluginId = contributions
            .GroupBy(contribution => contribution.Plugin.Manifest.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        foreach (var contribution in contributions)
        {
            var tag = BuildPluginSettingsTag(contribution);
            var navigationTitle = BuildPluginSettingsNavigationTitle(contribution, pageCountsByPluginId);
            var navItem = CreateSettingsNavItem(tag, Symbol.PuzzlePiece, navigationTitle);
            ToolTip.SetTip(navItem, $"{contribution.Plugin.Manifest.Name} - {contribution.Registration.Title}");

            SettingsPluginNavHost.Children.Add(navItem);
            _pluginSettingsNavItems[tag] = navItem;

            var pageHost = CreatePluginSettingsPageHost(contribution);
            pageHost.IsVisible = false;
            SettingsContentPagesHost.Children.Add(pageHost);
            _pluginSettingsPageHosts[tag] = pageHost;
        }

        SettingsPluginNavSection.IsVisible = SettingsPluginNavHost.Children.Count > 0;
    }

    private static string BuildPluginSettingsTag(PluginSettingsPageContribution contribution)
    {
        return $"PluginPage:{contribution.Plugin.Manifest.Id}:{contribution.Registration.Id}";
    }

    private static string BuildPluginSettingsNavigationTitle(
        PluginSettingsPageContribution contribution,
        IReadOnlyDictionary<string, int> pageCountsByPluginId)
    {
        return pageCountsByPluginId.TryGetValue(contribution.Plugin.Manifest.Id, out var pageCount) && pageCount > 1
            ? $"{contribution.Plugin.Manifest.Name} - {contribution.Registration.Title}"
            : contribution.Plugin.Manifest.Name;
    }

    private Control CreatePluginSettingsPageHost(PluginSettingsPageContribution contribution)
    {
        Control content;
        try
        {
            content = contribution.Registration.ContentFactory(contribution.Plugin.Services);
        }
        catch (Exception ex)
        {
            content = CreatePluginPageErrorContent(ex);
        }

        return new StackPanel
        {
            Spacing = 16,
            Children =
            {
                new TextBlock
                {
                    Text = contribution.Registration.Title,
                    FontSize = 24,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = GetThemeBrush("AdaptiveTextPrimaryBrush")
                },
                new TextBlock
                {
                    Text = contribution.Plugin.Manifest.Name,
                    Foreground = GetThemeBrush("AdaptiveTextSecondaryBrush")
                },
                content
            }
        };
    }

    private static Control CreatePluginPageErrorContent(Exception exception)
    {
        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#332B0F16")),
            BorderBrush = new SolidColorBrush(Color.Parse("#66F97316")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(16),
            Child = new TextBlock
            {
                Text = exception.Message,
                TextWrapping = TextWrapping.Wrap
            }
        };
    }

    private void UpdatePluginSettingsPageVisibility(string? selectedTag)
    {
        foreach (var pair in _pluginSettingsPageHosts)
        {
            pair.Value.IsVisible = string.Equals(pair.Key, selectedTag, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal void RefreshPluginSettingsNavigation()
    {
        foreach (var pair in _pluginSettingsPageHosts.ToArray())
        {
            if (_pluginSettingsNavItems.TryGetValue(pair.Key, out var navItem))
            {
                SettingsPluginNavHost.Children.Remove(navItem);
            }

            SettingsContentPagesHost.Children.Remove(pair.Value);
        }

        _pluginSettingsPageHosts.Clear();
        _pluginSettingsNavItems.Clear();
        SettingsPluginNavSection.IsVisible = false;
        InitializePluginSettingsNavigation();

        if (GetSettingsNavItem(_selectedSettingsTabTag) is null)
        {
            SelectSettingsTab("Plugins", persistSelection: false);
        }
        else
        {
            SelectSettingsTab(_selectedSettingsTabTag, persistSelection: false);
        }
    }

    private string? GetSelectedSettingsTabTag()
    {
        return _selectedSettingsTabTag;
    }

    private int ResolveSelectedSettingsTabIndex()
    {
        var selectedTag = GetSelectedSettingsTabTag();
        if (string.IsNullOrWhiteSpace(selectedTag))
        {
            return 0;
        }

        var buttons = EnumerateSettingsNavItems().ToList();
        for (var i = 0; i < buttons.Count; i++)
        {
            if (string.Equals(buttons[i].Tag?.ToString(), selectedTag, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return 0;
    }

    private void RestoreSettingsTabSelection(AppSettingsSnapshot snapshot)
    {
        var buttons = EnumerateSettingsNavItems().ToList();
        if (buttons.Count == 0)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.SettingsTabTag) &&
            GetSettingsNavItem(snapshot.SettingsTabTag) is not null)
        {
            SelectSettingsTab(snapshot.SettingsTabTag, persistSelection: false);
            return;
        }

        var safeIndex = Math.Clamp(snapshot.SettingsTabIndex, 0, Math.Max(0, buttons.Count - 1));
        var button = buttons[safeIndex];
        SelectSettingsTab(button.Tag?.ToString() ?? "Wallpaper", persistSelection: false);
    }
}
