using Avalonia;
using LanMountainDesktop.DesktopEditing;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class ComponentLibraryCollapseStateTests
{
    [Fact]
    public void CreateExpanded_InitializesExpandedStateAndHidesChip()
    {
        var margin = new Thickness(24, 24, 24, 100);
        var state = ComponentLibraryCollapseState.CreateExpanded(margin, 0.75);

        Assert.Equal(ComponentLibraryCollapseVisualState.Expanded, state.VisualState);
        Assert.Equal(margin, state.ExpandedMargin);
        Assert.Equal(0.75, state.ExpandedOpacity, 3);
        Assert.False(state.IsChipVisible);
    }

    [Fact]
    public void WithVisualState_PreservesStableExpandedSnapshotAcrossTransitions()
    {
        var margin = new Thickness(20, 18, 20, 96);
        var expanded = ComponentLibraryCollapseState.CreateExpanded(margin, 1);

        var collapsing = expanded.WithVisualState(ComponentLibraryCollapseVisualState.Collapsing, isChipVisible: true);
        var collapsed = collapsing.WithVisualState(ComponentLibraryCollapseVisualState.Collapsed, isChipVisible: true);
        var restoring = collapsed.WithVisualState(ComponentLibraryCollapseVisualState.Restoring, isChipVisible: false);

        Assert.Equal(ComponentLibraryCollapseVisualState.Collapsing, collapsing.VisualState);
        Assert.Equal(ComponentLibraryCollapseVisualState.Collapsed, collapsed.VisualState);
        Assert.Equal(ComponentLibraryCollapseVisualState.Restoring, restoring.VisualState);

        Assert.Equal(margin, collapsing.ExpandedMargin);
        Assert.Equal(margin, collapsed.ExpandedMargin);
        Assert.Equal(margin, restoring.ExpandedMargin);

        Assert.Equal(1, collapsing.ExpandedOpacity, 3);
        Assert.Equal(1, collapsed.ExpandedOpacity, 3);
        Assert.Equal(1, restoring.ExpandedOpacity, 3);

        Assert.True(collapsing.IsChipVisible);
        Assert.True(collapsed.IsChipVisible);
        Assert.False(restoring.IsChipVisible);
    }

    [Fact]
    public void CreateExpanded_ProducesRestorableSnapshotEvenWhenOriginalOpacityIsLow()
    {
        var margin = new Thickness(18, 22, 18, 88);
        var expanded = ComponentLibraryCollapseState.CreateExpanded(margin, 0.15);
        var restored = expanded.WithVisualState(ComponentLibraryCollapseVisualState.Expanded, isChipVisible: false);

        Assert.Equal(margin, restored.ExpandedMargin);
        Assert.Equal(0.15, restored.ExpandedOpacity, 3);
        Assert.Equal(ComponentLibraryCollapseVisualState.Expanded, restored.VisualState);
        Assert.False(restored.IsChipVisible);
    }
}
