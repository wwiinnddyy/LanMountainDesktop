using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using FluentIcons.Avalonia.Fluent;
using FluentIcons.Common;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views;

public partial class MainWindow
{
    private readonly Dictionary<string, Control> _pluginSettingsPageHosts = new(StringComparer.OrdinalIgnoreCase);

    private void InitializePluginSettingsNavigation()
    {
        if (_pluginSettingsPageHosts.Count > 0 || SettingsNavView?.MenuItems is null)
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
            return;
        }

        var pageCountsByPluginId = contributions
            .GroupBy(contribution => contribution.Plugin.Manifest.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        var insertIndex = SettingsNavView.MenuItems.IndexOf(SettingsNavPluginsItem) + 1;
        foreach (var contribution in contributions)
        {
            var tag = BuildPluginSettingsTag(contribution);
            var navigationTitle = BuildPluginSettingsNavigationTitle(contribution, pageCountsByPluginId);
            var navItem = new NavigationViewItem
            {
                Content = navigationTitle,
                Tag = tag,
                IconSource = new FluentIcons.Avalonia.Fluent.SymbolIconSource
                {
                    Symbol = FluentIcons.Common.Symbol.PuzzlePiece,
                    IconVariant = FluentIcons.Common.IconVariant.Regular
                }
            };

            ToolTip.SetTip(navItem, $"{contribution.Plugin.Manifest.Name} - {contribution.Registration.Title}");

            SettingsNavView.MenuItems.Insert(insertIndex++, navItem);

            var pageHost = CreatePluginSettingsPageHost(contribution);
            pageHost.IsVisible = false;
            SettingsContentPagesHost.Children.Add(pageHost);
            _pluginSettingsPageHosts[tag] = pageHost;
        }
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
            content = contribution.Registration.ContentFactory();
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

    private Control CreatePluginPageErrorContent(Exception exception)
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
        if (SettingsNavView?.MenuItems is null)
        {
            return;
        }

        foreach (var pair in _pluginSettingsPageHosts.ToArray())
        {
            var navItem = SettingsNavView.MenuItems
                .OfType<NavigationViewItem>()
                .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), pair.Key, StringComparison.OrdinalIgnoreCase));
            if (navItem is not null)
            {
                SettingsNavView.MenuItems.Remove(navItem);
            }

            SettingsContentPagesHost.Children.Remove(pair.Value);
        }

        _pluginSettingsPageHosts.Clear();
        InitializePluginSettingsNavigation();
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



