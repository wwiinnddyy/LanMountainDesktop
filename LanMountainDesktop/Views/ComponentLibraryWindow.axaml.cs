using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentIcons.Common;
using LanMountainDesktop.Services;
using LanMountainDesktop.ViewModels;

namespace LanMountainDesktop.Views;

public partial class ComponentLibraryWindow : Window
{
    private IComponentLibraryService? _componentLibraryService;
    private Func<double, ComponentLibraryCreateContext>? _createContextFactory;
    private Func<string, string, string>? _localize;
    private Func<ComponentLibraryComponentEntry, ComponentPreviewKey>? _previewKeyResolver;
    private Func<ComponentPreviewKey, ComponentPreviewImageEntry?>? _previewEntryResolver;
    private Action<ComponentPreviewKey>? _warmPreviewRequested;
    private Action<ComponentPreviewKey>? _renderPreviewRequested;
    private readonly ComponentLibraryWindowViewModel _viewModel = new();

    public ComponentLibraryWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    public ComponentLibraryWindow(
        IComponentLibraryService componentLibraryService,
        Func<double, ComponentLibraryCreateContext> createContextFactory,
        Func<string, string, string> localize,
        Func<ComponentLibraryComponentEntry, ComponentPreviewKey>? previewKeyResolver = null,
        Func<ComponentPreviewKey, ComponentPreviewImageEntry?>? previewEntryResolver = null,
        Action<ComponentPreviewKey>? warmPreviewRequested = null,
        Action<ComponentPreviewKey>? renderPreviewRequested = null)
        : this()
    {
        _componentLibraryService = componentLibraryService ?? throw new ArgumentNullException(nameof(componentLibraryService));
        _createContextFactory = createContextFactory ?? throw new ArgumentNullException(nameof(createContextFactory));
        _localize = localize ?? throw new ArgumentNullException(nameof(localize));
        _previewKeyResolver = previewKeyResolver;
        _previewEntryResolver = previewEntryResolver;
        _warmPreviewRequested = warmPreviewRequested;
        _renderPreviewRequested = renderPreviewRequested;
        Reload();
    }

    public event EventHandler<string>? AddComponentRequested;

    public void Reload()
    {
        if (_componentLibraryService is null || _localize is null)
        {
            return;
        }

        _viewModel.Title = _localize("component_library.title", "Widgets");
        _viewModel.Categories.Clear();
        _viewModel.Components.Clear();

        var categories = _componentLibraryService.GetDesktopCategories();
        foreach (var category in categories)
        {
            var itemModels = category.Components
                .Select(CreateComponentItem)
                .ToArray();
            _viewModel.Categories.Add(new ComponentLibraryCategoryViewModel(
                category.Id,
                GetLocalizedCategoryTitle(category.Id),
                ResolveCategoryIcon(category.Id),
                itemModels));
        }

        if (_viewModel.Categories.Count == 0)
        {
            return;
        }

        if (CategoryListBox is not null)
        {
            CategoryListBox.SelectedIndex = 0;
        }
    }

    private ComponentLibraryItemViewModel CreateComponentItem(ComponentLibraryComponentEntry entry)
    {
        var displayName = string.IsNullOrWhiteSpace(entry.DisplayNameLocalizationKey)
            ? entry.DisplayName
            : _localize?.Invoke(entry.DisplayNameLocalizationKey, entry.DisplayName) ?? entry.DisplayName;
        var previewKey = ResolvePreviewKey(entry);
        var previewEntry = _previewEntryResolver?.Invoke(previewKey);
        var item = new ComponentLibraryItemViewModel(
            entry.ComponentId,
            displayName,
            previewKey,
            description: null,
            _localize?.Invoke("component_library.preview.loading", "Loading preview...") ?? "Loading preview...",
            _localize?.Invoke("component_library.preview.unavailable", "Preview unavailable") ?? "Preview unavailable",
            previewEntry);

        if (previewEntry is null || previewEntry.State == ComponentPreviewImageState.Pending)
        {
            _warmPreviewRequested?.Invoke(previewKey);
            _renderPreviewRequested?.Invoke(previewKey);
        }

        return item;
    }

    private void OnCategorySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        _viewModel.Components.Clear();

        if (CategoryListBox?.SelectedItem is not ComponentLibraryCategoryViewModel selectedCategory)
        {
            return;
        }

        foreach (var component in selectedCategory.Components)
        {
            _viewModel.Components.Add(component);
        }

        RequestPreviewWarmup(selectedCategory.Components);
    }

    private void OnAddComponentClick(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is not Button button ||
            button.Tag is not string componentId ||
            string.IsNullOrWhiteSpace(componentId))
        {
            return;
        }

        AddComponentRequested?.Invoke(this, componentId);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        Hide();
    }

    public void UpdatePreviewImage(ComponentPreviewImageEntry previewImageEntry)
    {
        ArgumentNullException.ThrowIfNull(previewImageEntry);

        foreach (var category in _viewModel.Categories)
        {
            foreach (var component in category.Components)
            {
                if (component.PreviewKey.Equals(previewImageEntry.Key))
                {
                    component.UpdatePreviewImageEntry(previewImageEntry);
                }
            }
        }
    }

    private ComponentPreviewKey ResolvePreviewKey(ComponentLibraryComponentEntry entry)
    {
        if (_previewKeyResolver is not null)
        {
            return _previewKeyResolver(entry);
        }

        return ComponentPreviewKey.ForComponentType(entry.ComponentId, entry.MinWidthCells, entry.MinHeightCells);
    }

    private void RequestPreviewWarmup(IEnumerable<ComponentLibraryItemViewModel> components)
    {
        if (_warmPreviewRequested is null && _renderPreviewRequested is null)
        {
            return;
        }

        foreach (var component in components)
        {
            if (!component.IsPreviewPending)
            {
                continue;
            }

            _warmPreviewRequested?.Invoke(component.PreviewKey);
            _renderPreviewRequested?.Invoke(component.PreviewKey);
        }
    }

    private Symbol ResolveCategoryIcon(string categoryId)
    {
        if (string.Equals(categoryId, "Clock", StringComparison.OrdinalIgnoreCase))
        {
            return Symbol.Clock;
        }

        if (string.Equals(categoryId, "Date", StringComparison.OrdinalIgnoreCase))
        {
            return Symbol.CalendarDate;
        }

        if (string.Equals(categoryId, "Weather", StringComparison.OrdinalIgnoreCase))
        {
            return Symbol.WeatherSunny;
        }

        if (string.Equals(categoryId, "Board", StringComparison.OrdinalIgnoreCase))
        {
            return Symbol.Edit;
        }

        if (string.Equals(categoryId, "Media", StringComparison.OrdinalIgnoreCase))
        {
            return Symbol.Play;
        }

        if (string.Equals(categoryId, "Info", StringComparison.OrdinalIgnoreCase))
        {
            return Symbol.Info;
        }

        if (string.Equals(categoryId, "Calculator", StringComparison.OrdinalIgnoreCase))
        {
            return Symbol.Calculator;
        }

        if (string.Equals(categoryId, "Study", StringComparison.OrdinalIgnoreCase))
        {
            return Symbol.Hourglass;
        }

        return Symbol.Apps;
    }

    private string GetLocalizedCategoryTitle(string categoryId)
    {
        if (_localize is null)
        {
            return categoryId;
        }

        if (string.Equals(categoryId, "Clock", StringComparison.OrdinalIgnoreCase))
        {
            return _localize("component_category.clock", "Clock");
        }

        if (string.Equals(categoryId, "Date", StringComparison.OrdinalIgnoreCase))
        {
            return _localize("component_category.date", "Calendar");
        }

        if (string.Equals(categoryId, "Weather", StringComparison.OrdinalIgnoreCase))
        {
            return _localize("component_category.weather", "Weather");
        }

        if (string.Equals(categoryId, "Board", StringComparison.OrdinalIgnoreCase))
        {
            return _localize("component_category.board", "Board");
        }

        if (string.Equals(categoryId, "Media", StringComparison.OrdinalIgnoreCase))
        {
            return _localize("component_category.media", "Media");
        }

        if (string.Equals(categoryId, "Info", StringComparison.OrdinalIgnoreCase))
        {
            return _localize("component_category.info", "Info");
        }

        if (string.Equals(categoryId, "Calculator", StringComparison.OrdinalIgnoreCase))
        {
            return _localize("component_category.calculator", "Calculator");
        }

        if (string.Equals(categoryId, "Study", StringComparison.OrdinalIgnoreCase))
        {
            return _localize("component_category.study", "Study");
        }

        return categoryId;
    }
}
