using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using FluentIcons.Common;

namespace LanMountainDesktop.ViewModels;

public sealed class ComponentLibraryWindowViewModel : ViewModelBase
{
    public string Title { get; set; } = "Widgets";

    public ObservableCollection<ComponentLibraryCategoryViewModel> Categories { get; } = [];

    public ObservableCollection<ComponentLibraryItemViewModel> Components { get; } = [];
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
{
    public ComponentLibraryItemViewModel(
        string componentId,
        string displayName,
        Control? previewControl)
    {
        ComponentId = componentId;
        DisplayName = displayName;
        PreviewControl = previewControl;
    }

    public string ComponentId { get; }

    public string DisplayName { get; }

    public Control? PreviewControl { get; }
}
