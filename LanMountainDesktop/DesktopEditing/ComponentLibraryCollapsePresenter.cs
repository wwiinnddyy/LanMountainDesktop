using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace LanMountainDesktop.DesktopEditing;

internal sealed class ComponentLibraryCollapsePresenter
{
    private static readonly TimeSpan TransitionDuration = TimeSpan.FromMilliseconds(150);
    private static readonly Easing TransitionEasing = new CubicEaseOut();

    private readonly Border _componentLibraryWindow;
    private readonly Border _collapsedChipHost;
    private readonly TextBlock _collapsedChipTextBlock;
    private readonly Control? _collapsedChipIcon;
    private readonly TranslateTransform _windowTranslate = new();
    private readonly TranslateTransform _chipTranslate = new();
    private readonly ScaleTransform _chipScale = new(1, 1);

    private ComponentLibraryCollapseState _state;
    private int _transitionVersion;

    public ComponentLibraryCollapsePresenter(
        Border componentLibraryWindow,
        Border collapsedChipHost,
        TextBlock collapsedChipTextBlock,
        Control? collapsedChipIcon = null)
    {
        _componentLibraryWindow = componentLibraryWindow ?? throw new ArgumentNullException(nameof(componentLibraryWindow));
        _collapsedChipHost = collapsedChipHost ?? throw new ArgumentNullException(nameof(collapsedChipHost));
        _collapsedChipTextBlock = collapsedChipTextBlock ?? throw new ArgumentNullException(nameof(collapsedChipTextBlock));
        _collapsedChipIcon = collapsedChipIcon;

        EnsureTransforms();
        _state = ComponentLibraryCollapseState.CreateExpanded(_componentLibraryWindow.Margin);
        ApplyExpandedSnapshot();
        _collapsedChipHost.IsVisible = false;
        _collapsedChipHost.IsHitTestVisible = false;
        _collapsedChipHost.Opacity = 0;
    }

    public bool IsCollapsed => _state.VisualState is ComponentLibraryCollapseVisualState.Collapsing or ComponentLibraryCollapseVisualState.Collapsed;

    public ComponentLibraryCollapseVisualState VisualState => _state.VisualState;

    public void SyncExpandedState(Thickness margin)
    {
        _state = _state with
        {
            ExpandedMargin = margin
        };

        if (_state.VisualState is ComponentLibraryCollapseVisualState.Expanded or ComponentLibraryCollapseVisualState.Restoring)
        {
            ApplyExpandedSnapshot();
        }
    }

    public void Collapse(string title)
    {
        _collapsedChipTextBlock.Text = string.IsNullOrWhiteSpace(title) ? "Widgets" : title;

        if (_state.VisualState is ComponentLibraryCollapseVisualState.Collapsing or ComponentLibraryCollapseVisualState.Collapsed)
        {
            ShowCollapsedChip(_transitionVersion);
            return;
        }

        var version = ++_transitionVersion;
        _state = _state.WithVisualState(ComponentLibraryCollapseVisualState.Collapsing, isChipVisible: true);

        ApplyExpandedSnapshot();
        ShowCollapsedChip(version);
        SetCollapsedWindowTargets();

        DispatcherTimer.RunOnce(
            () =>
            {
                if (version != _transitionVersion)
                {
                    return;
                }

                _state = _state.WithVisualState(ComponentLibraryCollapseVisualState.Collapsed, isChipVisible: true);
                _componentLibraryWindow.IsVisible = false;
                _componentLibraryWindow.IsHitTestVisible = false;
            },
            TransitionDuration);
    }

    public void Restore()
    {
        if (_state.VisualState is ComponentLibraryCollapseVisualState.Expanded)
        {
            ApplyExpandedSnapshot();
            _collapsedChipHost.IsVisible = false;
            _collapsedChipHost.IsHitTestVisible = false;
            _collapsedChipHost.Opacity = 0;
            return;
        }

        var version = ++_transitionVersion;
        _state = _state.WithVisualState(ComponentLibraryCollapseVisualState.Restoring, isChipVisible: false);

        PrepareRestoringWindow();
        HideCollapsedChip(version);
        Dispatcher.UIThread.Post(
            () =>
            {
                if (version != _transitionVersion)
                {
                    return;
                }

                _componentLibraryWindow.Opacity = 1;
                _windowTranslate.Y = 0;
            },
            DispatcherPriority.Background);

        DispatcherTimer.RunOnce(
            () =>
            {
                if (version != _transitionVersion)
                {
                    return;
                }

                _state = _state.WithVisualState(ComponentLibraryCollapseVisualState.Expanded, isChipVisible: false);
                _componentLibraryWindow.IsVisible = true;
                _componentLibraryWindow.IsHitTestVisible = true;
            },
            TransitionDuration);
    }

    private void EnsureTransforms()
    {
        _componentLibraryWindow.RenderTransform = _windowTranslate;
        _windowTranslate.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = TranslateTransform.YProperty,
                Duration = TransitionDuration,
                Easing = TransitionEasing
            }
        };

        _collapsedChipHost.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        _collapsedChipHost.RenderTransform = new TransformGroup
        {
            Children =
            {
                _chipTranslate,
                _chipScale
            }
        };
        _chipTranslate.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = TranslateTransform.YProperty,
                Duration = TransitionDuration,
                Easing = TransitionEasing
            }
        };
        _chipScale.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = ScaleTransform.ScaleXProperty,
                Duration = TransitionDuration,
                Easing = TransitionEasing
            },
            new DoubleTransition
            {
                Property = ScaleTransform.ScaleYProperty,
                Duration = TransitionDuration,
                Easing = TransitionEasing
            }
        };
    }

    private void ApplyExpandedSnapshot()
    {
        _componentLibraryWindow.Margin = _state.ExpandedMargin;
        _componentLibraryWindow.Opacity = 1;
        _componentLibraryWindow.IsVisible = true;
        _componentLibraryWindow.IsHitTestVisible = true;
        _windowTranslate.Y = 0;
    }

    private void SetCollapsedWindowTargets()
    {
        _componentLibraryWindow.Opacity = 0;
        _windowTranslate.Y = 28;
    }

    private void ShowCollapsedChip(int version)
    {
        _collapsedChipHost.IsVisible = true;
        _collapsedChipHost.IsHitTestVisible = false;
        _collapsedChipTextBlock.IsVisible = true;
        if (_collapsedChipIcon is not null)
        {
            _collapsedChipIcon.IsVisible = true;
        }

        _collapsedChipHost.Opacity = 0;
        _chipTranslate.Y = 8;
        _chipScale.ScaleX = 0.96;
        _chipScale.ScaleY = 0.96;

        Dispatcher.UIThread.Post(
            () =>
            {
                if (version != _transitionVersion)
                {
                    return;
                }

                _collapsedChipHost.Opacity = 1;
                _chipTranslate.Y = 0;
                _chipScale.ScaleX = 1;
                _chipScale.ScaleY = 1;
            },
            DispatcherPriority.Background);
    }

    private void HideCollapsedChip(int version)
    {
        _collapsedChipHost.IsVisible = true;
        _collapsedChipHost.IsHitTestVisible = false;
        _collapsedChipHost.Opacity = 0;
        _chipTranslate.Y = 8;
        _chipScale.ScaleX = 0.96;
        _chipScale.ScaleY = 0.96;

        DispatcherTimer.RunOnce(
            () =>
            {
                if (version != _transitionVersion)
                {
                    return;
                }

                _collapsedChipHost.IsVisible = false;
            },
            TransitionDuration);
    }

    private void PrepareRestoringWindow()
    {
        _componentLibraryWindow.IsVisible = true;
        _componentLibraryWindow.IsHitTestVisible = true;
        _componentLibraryWindow.Margin = _state.ExpandedMargin;
        _componentLibraryWindow.Opacity = 0;
        _windowTranslate.Y = 28;
    }
}
