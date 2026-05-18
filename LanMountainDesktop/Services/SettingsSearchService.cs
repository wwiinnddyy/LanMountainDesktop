using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using LanMountainDesktop.Services.Settings;

namespace LanMountainDesktop.Services;

public sealed class SettingsSearchResult
{
    public SettingsSearchResult(
        string pageId,
        string pageTitle,
        string? pageDescription,
        string displayTitle,
        string? displayDescription,
        string? targetId,
        Control? targetControl,
        bool isPageResult,
        IEnumerable<string>? keywords = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(pageTitle);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayTitle);

        PageId = pageId.Trim();
        PageTitle = pageTitle.Trim();
        PageDescription = NormalizeText(pageDescription);
        DisplayTitle = displayTitle.Trim();
        DisplayDescription = NormalizeText(displayDescription);
        TargetId = NormalizeText(targetId);
        TargetControl = targetControl;
        IsPageResult = isPageResult;
        Keywords = keywords?
            .Select(NormalizeText)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()
            ?? [];
    }

    public string PageId { get; }

    public string PageTitle { get; }

    public string? PageDescription { get; }

    public string DisplayTitle { get; }

    public string? DisplayDescription { get; }

    public string? TargetId { get; }

    public Control? TargetControl { get; }

    public bool IsPageResult { get; }

    public IReadOnlyList<string> Keywords { get; }

    public string SearchText => string.Join(
        " ",
        new[]
        {
            PageId,
            PageTitle,
            PageDescription,
            DisplayTitle,
            DisplayDescription,
            TargetId,
            string.Join(" ", Keywords)
        }.Where(static value => !string.IsNullOrWhiteSpace(value)));

    public override string ToString() => DisplayTitle;

    private static string? NormalizeText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed class SettingsSearchService
{
    private readonly Dictionary<string, List<SettingsSearchResult>> _entriesByPage = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<SettingsSearchResult> Entries =>
        _entriesByPage.Values.SelectMany(static entries => entries).ToArray();

    public void RebuildPageEntries(IEnumerable<SettingsPageDescriptor> pages)
    {
        _entriesByPage.Clear();

        foreach (var page in pages)
        {
            _entriesByPage[page.PageId] =
            [
                CreatePageResult(page)
            ];
        }
    }

    public void IndexPage(SettingsPageDescriptor descriptor, Control page)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(page);

        var results = new List<SettingsSearchResult> { CreatePageResult(descriptor) };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            descriptor.PageId
        };

        foreach (var target in page.GetVisualDescendants().OfType<Control>())
        {
            if (target is not FASettingsExpander && target is not FASettingsExpanderItem)
            {
                continue;
            }

            var title = ReadControlText(target, "Header");
            var description = ReadControlText(target, "Description");

            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(description))
            {
                continue;
            }

            var targetId = string.IsNullOrWhiteSpace(target.Name)
                ? $"{descriptor.PageId}:{results.Count}"
                : target.Name;
            var key = $"{targetId}|{title}|{description}";
            if (!seen.Add(key))
            {
                continue;
            }

            results.Add(new SettingsSearchResult(
                descriptor.PageId,
                descriptor.Title,
                descriptor.Description,
                string.IsNullOrWhiteSpace(title) ? descriptor.Title : title!,
                description,
                targetId,
                target,
                isPageResult: false,
                keywords: [descriptor.Category.ToString(), descriptor.IconKey]));
        }

        _entriesByPage[descriptor.PageId] = results;
    }

    public IReadOnlyList<SettingsSearchResult> Search(string? query, int maxResults = 24)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0)
        {
            return [];
        }

        return Entries
            .Select(entry => new
            {
                Entry = entry,
                Score = Score(entry, terms)
            })
            .Where(static item => item.Score > 0)
            .OrderByDescending(static item => item.Score)
            .ThenBy(static item => item.Entry.IsPageResult)
            .ThenBy(static item => item.Entry.PageTitle, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(static item => item.Entry.DisplayTitle, StringComparer.CurrentCultureIgnoreCase)
            .Take(Math.Max(1, maxResults))
            .Select(static item => item.Entry)
            .ToArray();
    }

    public static bool Filter(string? search, object? item)
    {
        if (item is not SettingsSearchResult result || string.IsNullOrWhiteSpace(search))
        {
            return false;
        }

        var terms = search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return terms.Length > 0 && Score(result, terms) > 0;
    }

    private static SettingsSearchResult CreatePageResult(SettingsPageDescriptor descriptor)
    {
        return new SettingsSearchResult(
            descriptor.PageId,
            descriptor.Title,
            descriptor.Description,
            descriptor.Title,
            descriptor.Description,
            descriptor.PageId,
            null,
            isPageResult: true,
            keywords:
            [
                descriptor.Category.ToString(),
                descriptor.IconKey,
                descriptor.PluginId ?? string.Empty,
                descriptor.GroupId ?? string.Empty
            ]);
    }

    private static int Score(SettingsSearchResult entry, IReadOnlyList<string> terms)
    {
        var score = 0;
        foreach (var term in terms)
        {
            if (entry.DisplayTitle.StartsWith(term, StringComparison.OrdinalIgnoreCase))
            {
                score += 100;
                continue;
            }

            if (entry.DisplayTitle.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                score += 75;
                continue;
            }

            if (entry.PageTitle.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                score += 50;
                continue;
            }

            if (entry.SearchText.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                score += 25;
                continue;
            }

            return 0;
        }

        return score + (entry.IsPageResult ? 0 : 12);
    }

    private static string? ReadControlText(Control control, string propertyName)
    {
        var value = control.GetType().GetProperty(propertyName)?.GetValue(control);
        return value switch
        {
            null => null,
            string text => string.IsNullOrWhiteSpace(text) ? null : text.Trim(),
            TextBlock textBlock => string.IsNullOrWhiteSpace(textBlock.Text) ? null : textBlock.Text.Trim(),
            _ => value.ToString()
        };
    }
}
