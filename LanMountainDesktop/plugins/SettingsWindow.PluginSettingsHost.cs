using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using FluentIcons.Common;
using FluentAvalonia.UI.Controls;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views;

public partial class SettingsWindow
{
    private readonly Dictionary<string, Control> _pluginSettingsPageHosts = new(StringComparer.OrdinalIgnoreCase);

    private void InitializePluginSettingsNavigation()
    {
        _pluginSettingsPageHosts.Clear();
        _pluginSettingsNavItems.Clear();
    }

    private void RegisterPluginSettingsDefinitions()
    {
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

        for (var i = 0; i < contributions.Length; i++)
        {
            var contribution = contributions[i];
            var tag = BuildPluginSettingsTag(contribution);
            _pluginSettingsPageHosts[tag] = CreatePluginSettingsPageHost(contribution);

            RegisterSettingsPageDefinition(new IndependentSettingsPageDefinition(
                tag,
                BuildPluginSettingsNavigationTitle(contribution, pageCountsByPluginId),
                BuildPluginSettingsPageDescription(contribution),
                FluentIcons.Common.Symbol.PuzzlePiece,
                IndependentSettingsPageCategory.External,
                200 + i,
                $"{contribution.Plugin.Manifest.Name} - {contribution.Registration.Title}"));
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

    private string BuildPluginSettingsPageDescription(PluginSettingsPageContribution contribution)
    {
        return Lf(
            "settings.page_desc.plugin_contributed_format",
            "Settings page '{0}' is provided by plugin '{1}'.",
            contribution.Registration.Title,
            contribution.Plugin.Manifest.Name);
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
            MaxWidth = 920,
            Children =
            {
                new TextBlock
                {
                    Text = contribution.Registration.Title,
                    FontSize = 24,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = GetThemeBrush("TextFillColorPrimaryBrush")
                },
                new TextBlock
                {
                    Text = contribution.Plugin.Manifest.Name,
                    Foreground = GetThemeBrush("TextFillColorSecondaryBrush")
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

    internal void RefreshPluginSettingsNavigation()
    {
        var preferredTag = NormalizeSettingsPageTag(_selectedSettingsTabTag);
        InitializeSettingsNavigation();
        SelectSettingsTab(
            _settingsPageDefinitions.ContainsKey(preferredTag) ? preferredTag : "Plugins",
            persistSelection: false);
        PluginSettingsPanel?.RefreshFromRuntime();
    }

    private string? GetSelectedSettingsTabTag()
    {
        return NormalizeSettingsPageTag(_selectedSettingsTabTag);
    }

    private int ResolveSelectedSettingsTabIndex()
    {
        if (SettingsNavView?.MenuItems is null)
        {
            return 0;
        }

        var items = SettingsNavView.MenuItems.OfType<NavigationViewItem>().ToList();
        for (var i = 0; i < items.Count; i++)
        {
            if (string.Equals(items[i].Tag?.ToString(), NormalizeSettingsPageTag(_selectedSettingsTabTag), StringComparison.OrdinalIgnoreCase))
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

        var items = SettingsNavView.MenuItems.OfType<NavigationViewItem>().ToList();
        if (items.Count == 0)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.SettingsTabTag))
        {
            var normalizedTag = NormalizeSettingsPageTag(snapshot.SettingsTabTag);
            var taggedItem = items
                .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), normalizedTag, StringComparison.OrdinalIgnoreCase));
            if (taggedItem is not null)
            {
                _selectedSettingsTabTag = normalizedTag;
                SettingsNavView.SelectedItem = taggedItem;
                return;
            }
        }

        var safeIndex = Math.Clamp(snapshot.SettingsTabIndex, 0, Math.Max(0, items.Count - 1));
        _selectedSettingsTabTag = items[safeIndex].Tag?.ToString() ?? _selectedSettingsTabTag;
        SettingsNavView.SelectedItem = items[safeIndex];
    }
}
