using Avalonia;

namespace LanMountainDesktop.DesktopEditing;

internal enum ComponentLibraryCollapseVisualState
{
    Expanded,
    Collapsing,
    Collapsed,
    Restoring
}

internal readonly record struct ComponentLibraryCollapseState(
    ComponentLibraryCollapseVisualState VisualState,
    Thickness ExpandedMargin,
    double ExpandedOpacity,
    bool IsChipVisible)
{
    public static ComponentLibraryCollapseState CreateExpanded(Thickness expandedMargin, double expandedOpacity)
    {
        return new(
            ComponentLibraryCollapseVisualState.Expanded,
            expandedMargin,
            expandedOpacity,
            IsChipVisible: false);
    }

    public ComponentLibraryCollapseState WithVisualState(ComponentLibraryCollapseVisualState visualState, bool isChipVisible)
    {
        return this with
        {
            VisualState = visualState,
            IsChipVisible = isChipVisible
        };
    }
}
