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
    bool IsChipVisible)
{
    public static ComponentLibraryCollapseState CreateExpanded(Thickness expandedMargin)
    {
        return new(
            ComponentLibraryCollapseVisualState.Expanded,
            expandedMargin,
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
