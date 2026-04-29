using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Controls;
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
    private string _displayName;
    private string? _description;
    private Control? _previewControl;

    public ComponentLibraryItemViewModel(
        string componentId,
        string displayName,
        string? description = null,
        Control? previewControl = null)
    {
        ComponentId = componentId;
        _displayName = displayName;
        _description = description;
        _previewControl = previewControl;
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

    public Control? PreviewControl
    {
        get => _previewControl;
        set => SetProperty(ref _previewControl, value);
    }

}
