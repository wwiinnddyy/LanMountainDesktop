using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FluentIcons.Common;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.ViewModels;
using LanMountainDesktop.Views.Components;

namespace LanMountainDesktop.Views;

public partial class FusedDesktopComponentLibraryControl : UserControl
{
    public event EventHandler<string>? AddComponentRequested;

    private static readonly LocalizationService LocalizationService = new();

    private readonly ComponentLibraryWindowViewModel _viewModel = new();
    private readonly ISettingsFacadeService _settingsFacade = HostSettingsFacadeProvider.GetOrCreate();
    private readonly IWeatherInfoService _weatherDataService;
    private readonly TimeZoneService _timeZoneService;
    private readonly IRecommendationInfoService _recommendationInfoService = new RecommendationDataService();
    private readonly ICalculatorDataService _calculatorDataService = new CalculatorDataService();

    private List<DesktopComponentDefinition> _allDefinitions = new();
    private ComponentRegistry? _componentRegistry;
    private DesktopComponentRuntimeRegistry? _componentRuntimeRegistry;
    private Control? _selectedPreviewControl;

    public FusedDesktopComponentLibraryControl()
    {
        InitializeComponent();
        DataContext = _viewModel;

        _weatherDataService = _settingsFacade.Weather.GetWeatherInfoService();
        _timeZoneService = _settingsFacade.Region.GetTimeZoneService();

        LoadRegistry();
        LoadCategories();

        CategoryListBox.ContainerPrepared += OnCategoryListBoxContainerPrepared;
        if (_viewModel.Categories.Count > 0)
        {
            CategoryListBox.SelectedIndex = 0;
        }
    }

    private void OnCategoryListBoxContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        _ = sender;
        if (e.Container is ListBoxItem listBoxItem)
        {
            listBoxItem.Classes.Add("category-item");
        }
    }

    private void LoadRegistry()
    {
        var pluginRuntimeService = (Application.Current as App)?.PluginRuntimeService;
        _componentRegistry = DesktopComponentRegistryFactory.Create(pluginRuntimeService);
        _componentRuntimeRegistry = DesktopComponentRegistryFactory.CreateRuntimeRegistry(
            _componentRegistry,
            pluginRuntimeService,
            _settingsFacade);

        _allDefinitions = _componentRegistry.GetAll()
            .Where(static definition => definition.AllowDesktopPlacement)
            .ToList();
    }

    private void LoadCategories()
    {
        _viewModel.Categories.Clear();

        var languageCode = _settingsFacade.Region.Get().LanguageCode;
        _viewModel.Categories.Add(new ComponentLibraryCategoryViewModel(
            "all",
            L(languageCode, "component_category.all", "All"),
            Symbol.Apps,
            Array.Empty<ComponentLibraryItemViewModel>()));

        var usedCategories = _allDefinitions
            .Select(static definition => definition.Category)
            .Where(static category => !string.IsNullOrWhiteSpace(category))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var category in usedCategories)
        {
            var categoryComponents = _allDefinitions
                .Where(definition => string.Equals(definition.Category, category, StringComparison.OrdinalIgnoreCase))
                .OrderBy(static definition => definition.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(CreateComponentItem)
                .ToArray();

            _viewModel.Categories.Add(new ComponentLibraryCategoryViewModel(
                category,
                GetLocalizedCategoryTitle(languageCode, category),
                ResolveCategoryIcon(category),
                categoryComponents));
        }
    }

    private static Symbol ResolveCategoryIcon(string categoryId)
    {
        if (string.Equals(categoryId, "Clock", StringComparison.OrdinalIgnoreCase)) return Symbol.Clock;
        if (string.Equals(categoryId, "Date", StringComparison.OrdinalIgnoreCase)) return Symbol.CalendarDate;
        if (string.Equals(categoryId, "Weather", StringComparison.OrdinalIgnoreCase)) return Symbol.WeatherSunny;
        if (string.Equals(categoryId, "Board", StringComparison.OrdinalIgnoreCase)) return Symbol.Edit;
        if (string.Equals(categoryId, "Media", StringComparison.OrdinalIgnoreCase)) return Symbol.Play;
        if (string.Equals(categoryId, "Info", StringComparison.OrdinalIgnoreCase)) return Symbol.Apps;
        if (string.Equals(categoryId, "Calculator", StringComparison.OrdinalIgnoreCase)) return Symbol.Calculator;
        if (string.Equals(categoryId, "Study", StringComparison.OrdinalIgnoreCase)) return Symbol.Hourglass;
        if (string.Equals(categoryId, "File", StringComparison.OrdinalIgnoreCase)) return Symbol.Folder;
        return Symbol.Apps;
    }

    private string GetLocalizedCategoryTitle(string languageCode, string categoryId)
    {
        if (string.Equals(categoryId, "Clock", StringComparison.OrdinalIgnoreCase)) return L(languageCode, "component_category.clock", "Clock");
        if (string.Equals(categoryId, "Date", StringComparison.OrdinalIgnoreCase)) return L(languageCode, "component_category.date", "Calendar");
        if (string.Equals(categoryId, "Weather", StringComparison.OrdinalIgnoreCase)) return L(languageCode, "component_category.weather", "Weather");
        if (string.Equals(categoryId, "Board", StringComparison.OrdinalIgnoreCase)) return L(languageCode, "component_category.board", "Board");
        if (string.Equals(categoryId, "Media", StringComparison.OrdinalIgnoreCase)) return L(languageCode, "component_category.media", "Media");
        if (string.Equals(categoryId, "Info", StringComparison.OrdinalIgnoreCase)) return L(languageCode, "component_category.info", "Info");
        if (string.Equals(categoryId, "Calculator", StringComparison.OrdinalIgnoreCase)) return L(languageCode, "component_category.calculator", "Calculator");
        if (string.Equals(categoryId, "Study", StringComparison.OrdinalIgnoreCase)) return L(languageCode, "component_category.study", "Study");
        if (string.Equals(categoryId, "File", StringComparison.OrdinalIgnoreCase)) return L(languageCode, "component_category.file", "File");
        return categoryId;
    }

    private string L(string languageCode, string key, string fallback)
    {
        return LocalizationService.GetString(languageCode, key, fallback);
    }

    private static ComponentLibraryItemViewModel CreateComponentItem(DesktopComponentDefinition definition)
    {
        return new ComponentLibraryItemViewModel(definition.Id, definition.DisplayName);
    }

    private void OnCategorySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        UpdateSelectedComponent();
    }

    private void UpdateSelectedComponent()
    {
        if (CategoryListBox.SelectedItem is not ComponentLibraryCategoryViewModel selectedCategory)
        {
            _viewModel.SelectedComponent = null;
            SetSelectedPreviewControl(null);
            return;
        }

        var filtered = selectedCategory.Id == "all"
            ? _allDefinitions.OrderBy(static definition => definition.DisplayName, StringComparer.OrdinalIgnoreCase)
            : _allDefinitions
                .Where(definition => string.Equals(definition.Category, selectedCategory.Id, StringComparison.OrdinalIgnoreCase))
                .OrderBy(static definition => definition.DisplayName, StringComparer.OrdinalIgnoreCase);

        var firstComponent = filtered.FirstOrDefault();
        if (firstComponent is null)
        {
            _viewModel.SelectedComponent = null;
            SetSelectedPreviewControl(null);
            return;
        }

        _viewModel.SelectedComponent = selectedCategory.Components.FirstOrDefault(component => component.ComponentId == firstComponent.Id)
            ?? CreateComponentItem(firstComponent);
        SetSelectedPreviewControl(CreateStaticPreviewControl(firstComponent));
    }

    private Control? CreateStaticPreviewControl(DesktopComponentDefinition definition)
    {
        if (_componentRuntimeRegistry is null ||
            !_componentRuntimeRegistry.TryGetDescriptor(definition.Id, out var descriptor))
        {
            return null;
        }

        try
        {
            var control = descriptor.CreateControl(
                ResolvePreviewCellSize(definition),
                _timeZoneService,
                _weatherDataService,
                _recommendationInfoService,
                _calculatorDataService,
                _settingsFacade,
                placementId: null,
                renderMode: DesktopComponentRenderMode.LibraryPreview);
            ComponentPreviewRuntimeQuiescer.Attach(control);
            return control;
        }
        catch (Exception ex) when (!UiExceptionGuard.IsFatalException(ex))
        {
            AppLogger.Warn(
                "ComponentLibrary",
                $"Failed to create static fused preview for component '{definition.Id}'.",
                ex);
            return null;
        }
    }

    private static double ResolvePreviewCellSize(DesktopComponentDefinition definition)
    {
        const double maxWidth = 360d;
        const double maxHeight = 240d;
        return Math.Clamp(
            Math.Min(
                maxWidth / Math.Max(1, definition.MinWidthCells),
                maxHeight / Math.Max(1, definition.MinHeightCells)),
            32d,
            96d);
    }

    private void SetSelectedPreviewControl(Control? control)
    {
        DisposeSelectedPreviewControl();
        _selectedPreviewControl = control;
        if (SelectedComponentPreviewHost is not null)
        {
            SelectedComponentPreviewHost.Content = control;
        }
    }

    private void DisposeSelectedPreviewControl()
    {
        if (_selectedPreviewControl is null)
        {
            return;
        }

        ComponentPreviewRuntimeQuiescer.Detach(_selectedPreviewControl);
        if (_selectedPreviewControl is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _selectedPreviewControl = null;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        DisposeSelectedPreviewControl();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnAddComponentClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string componentId)
        {
            AddComponentRequested?.Invoke(this, componentId);
        }
    }

    private void OnFindMoreComponentsClick(object? sender, RoutedEventArgs e)
    {
        if (Application.Current is App app)
        {
            app.OpenIndependentSettingsModule("FusedDesktopComponentLibrary", "plugin-catalog");
        }

        this.FindAncestorOfType<Window>()?.Close();
    }
}
