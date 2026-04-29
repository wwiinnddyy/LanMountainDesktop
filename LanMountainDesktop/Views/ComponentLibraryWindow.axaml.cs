using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LanMountainDesktop.ComponentSystem;
using FluentIcons.Common;
using LanMountainDesktop.Services;
using LanMountainDesktop.ViewModels;

namespace LanMountainDesktop.Views;

public partial class ComponentLibraryWindow : Window
{
    private IComponentLibraryService? _componentLibraryService;
    private Func<double, ComponentLibraryCreateContext>? _createContextFactory;
    private Func<string, string, string>? _localize;
    private readonly ComponentLibraryWindowViewModel _viewModel = new();

    public ComponentLibraryWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    public ComponentLibraryWindow(
        IComponentLibraryService componentLibraryService,
        Func<double, ComponentLibraryCreateContext> createContextFactory,
        Func<string, string, string> localize)
        : this()
    {
        _componentLibraryService = componentLibraryService ?? throw new ArgumentNullException(nameof(componentLibraryService));
        _createContextFactory = createContextFactory ?? throw new ArgumentNullException(nameof(createContextFactory));
        _localize = localize ?? throw new ArgumentNullException(nameof(localize));
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
        DisposePreviewControls(_viewModel.Categories.SelectMany(static category => category.Components));
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
        var previewControl = CreateStaticPreviewControl(entry);
        return new ComponentLibraryItemViewModel(
            entry.ComponentId,
            displayName,
            description: null,
            previewControl);
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

        ComponentPreviewRuntimeQuiescer.Quiesce(this);
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

    private Control? CreateStaticPreviewControl(ComponentLibraryComponentEntry entry)
    {
        if (_componentLibraryService is null || _createContextFactory is null)
        {
            return null;
        }

        var cellSize = ResolvePreviewCellSize(entry);
        var context = _createContextFactory(cellSize) with
        {
            PlacementId = null,
            RenderMode = DesktopComponentRenderMode.LibraryPreview
        };

        if (!_componentLibraryService.TryCreateControl(entry.ComponentId, context, out var control, out _))
        {
            return null;
        }

        if (control is not null)
        {
            ComponentPreviewRuntimeQuiescer.Attach(control);
        }

        return control;
    }

    private static double ResolvePreviewCellSize(ComponentLibraryComponentEntry entry)
    {
        var maxWidth = 180d;
        var maxHeight = 120d;
        return Math.Clamp(
            Math.Min(
                maxWidth / Math.Max(1, entry.MinWidthCells),
                maxHeight / Math.Max(1, entry.MinHeightCells)),
            24d,
            72d);
    }

    private static void DisposePreviewControls(IEnumerable<ComponentLibraryItemViewModel> components)
    {
        foreach (var control in components.Select(static component => component.PreviewControl).OfType<Control>())
        {
            ComponentPreviewRuntimeQuiescer.Detach(control);
            if (control is IDisposable disposable)
            {
                disposable.Dispose();
            }
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
