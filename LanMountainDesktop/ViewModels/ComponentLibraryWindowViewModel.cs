using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using LanMountainDesktop.Services;
using FluentIcons.Common;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LanMountainDesktop.ViewModels;

public sealed class ComponentLibraryWindowViewModel : ViewModelBase
{
    private string _title = "Widgets";
    private ComponentLibraryItemViewModel? _selectedComponent;

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public ObservableCollection<ComponentLibraryCategoryViewModel> Categories { get; } = [];

    public ObservableCollection<ComponentLibraryItemViewModel> Components { get; } = [];

    public ComponentLibraryItemViewModel? SelectedComponent
    {
        get => _selectedComponent;
        set => SetProperty(ref _selectedComponent, value);
    }
}

public sealed class ComponentLibraryCategoryViewModel
{
    public ComponentLibraryCategoryViewModel(
        string id,
        string title,
        Symbol icon,
        IReadOnlyList<ComponentLibraryItemViewModel> components)
    {
        Id = id;
        Title = title;
        Icon = icon;
        Components = components;
    }

    public string Id { get; }

    public string Title { get; }

    public Symbol Icon { get; }

    public IReadOnlyList<ComponentLibraryItemViewModel> Components { get; }
}

public sealed class ComponentLibraryItemViewModel
    : ObservableObject
{
    private readonly string _loadingPreviewText;
    private readonly string _previewUnavailableText;
    private string _displayName;
    private string? _description;
    private ComponentPreviewKey _previewKey;
    private ComponentPreviewImageEntry? _previewImageEntry;
    private ComponentPreviewImageState _previewState;
    private string? _previewErrorMessage;
    private string _previewStatusText;

    public ComponentLibraryItemViewModel(
        string componentId,
        string displayName,
        ComponentPreviewKey previewKey,
        string? description = null,
        string loadingPreviewText = "Loading preview...",
        string previewUnavailableText = "Preview unavailable",
        ComponentPreviewImageEntry? previewImageEntry = null)
    {
        ComponentId = componentId;
        _displayName = displayName;
        _description = description;
        _previewKey = previewKey;
        _loadingPreviewText = loadingPreviewText;
        _previewUnavailableText = previewUnavailableText;
        _previewStatusText = loadingPreviewText;
        UpdatePreviewImageEntry(previewImageEntry, raiseEntryChanged: false);
    }

    public string ComponentId { get; }

    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }

    public string? Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    public ComponentPreviewKey PreviewKey
    {
        get => _previewKey;
        set => SetProperty(ref _previewKey, value);
    }

    public ComponentPreviewImageEntry? PreviewImageEntry => _previewImageEntry;

    public object? PreviewBitmap => _previewImageEntry?.Bitmap;

    public ComponentPreviewImageState PreviewState => _previewState;

    public bool IsPreviewPending => _previewState == ComponentPreviewImageState.Pending;

    public bool IsPreviewReady => _previewState == ComponentPreviewImageState.Ready && _previewImageEntry?.Bitmap is not null;

    public bool IsPreviewFailed => _previewState == ComponentPreviewImageState.Failed;

    public string? PreviewErrorMessage => _previewErrorMessage;

    public string PreviewStatusText => _previewStatusText;

    public void UpdatePreviewImageEntry(ComponentPreviewImageEntry? previewImageEntry)
    {
        UpdatePreviewImageEntry(previewImageEntry, raiseEntryChanged: true);
    }

    private void UpdatePreviewImageEntry(ComponentPreviewImageEntry? previewImageEntry, bool raiseEntryChanged)
    {
        if (raiseEntryChanged && ReferenceEquals(_previewImageEntry, previewImageEntry))
        {
            return;
        }

        if (_previewImageEntry is not null)
        {
            _previewImageEntry.PropertyChanged -= OnPreviewImageEntryPropertyChanged;
        }

        _previewImageEntry = previewImageEntry;
        _previewState = previewImageEntry?.State ?? ComponentPreviewImageState.Pending;
        _previewErrorMessage = previewImageEntry?.ErrorMessage;

        _previewStatusText = _previewState switch
        {
            ComponentPreviewImageState.Ready => string.Empty,
            ComponentPreviewImageState.Failed => string.IsNullOrWhiteSpace(_previewErrorMessage)
                ? _previewUnavailableText
                : _previewErrorMessage!,
            _ => _loadingPreviewText
        };

        if (_previewImageEntry is not null)
        {
            _previewImageEntry.PropertyChanged += OnPreviewImageEntryPropertyChanged;
        }

        RaisePreviewDependentProperties();
    }

    private void OnPreviewImageEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _ = sender;
        if (string.IsNullOrWhiteSpace(e.PropertyName) ||
            e.PropertyName is nameof(ComponentPreviewImageEntry.Bitmap) or
                nameof(ComponentPreviewImageEntry.State) or
                nameof(ComponentPreviewImageEntry.ErrorMessage))
        {
            _previewState = _previewImageEntry?.State ?? ComponentPreviewImageState.Pending;
            _previewErrorMessage = _previewImageEntry?.ErrorMessage;
            _previewStatusText = _previewState switch
            {
                ComponentPreviewImageState.Ready => string.Empty,
                ComponentPreviewImageState.Failed => string.IsNullOrWhiteSpace(_previewErrorMessage)
                    ? _previewUnavailableText
                    : _previewErrorMessage!,
                _ => _loadingPreviewText
            };

            RaisePreviewDependentProperties();
        }
    }

    private void RaisePreviewDependentProperties()
    {
        OnPropertyChanged(nameof(PreviewImageEntry));
        OnPropertyChanged(nameof(PreviewBitmap));
        OnPropertyChanged(nameof(PreviewState));
        OnPropertyChanged(nameof(IsPreviewPending));
        OnPropertyChanged(nameof(IsPreviewReady));
        OnPropertyChanged(nameof(IsPreviewFailed));
        OnPropertyChanged(nameof(PreviewErrorMessage));
        OnPropertyChanged(nameof(PreviewStatusText));
    }
}
