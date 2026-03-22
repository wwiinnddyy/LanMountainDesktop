using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace LanMountainDesktop.DesktopEditing;

internal sealed class DesktopEditGhostView : Border
{
    private static readonly TimeSpan FastDuration = TimeSpan.FromMilliseconds(120);
    private static readonly Easing StandardEasing = new CubicEaseOut();

    private readonly Border _accentDot;
    private readonly TextBlock _titleTextBlock;
    private readonly TextBlock _detailTextBlock;
    private readonly Border _badgeBorder;
    private readonly TextBlock _badgeTextBlock;
    private readonly ScaleTransform _scaleTransform = new(1, 1);

    private readonly SolidColorBrush _normalBackgroundBrush = new(Color.Parse("#F11B2430"));
    private readonly SolidColorBrush _normalBorderBrush = new(Color.Parse("#4D8AA3C1"));
    private readonly SolidColorBrush _normalAccentBrush = new(Color.Parse("#FF4F8EF7"));
    private readonly SolidColorBrush _normalTextBrush = new(Color.Parse("#FFF5F7FA"));
    private readonly SolidColorBrush _normalMutedTextBrush = new(Color.Parse("#BDE2E8F0"));
    private readonly SolidColorBrush _normalBadgeBackgroundBrush = new(Color.Parse("#245E86D6"));
    private readonly SolidColorBrush _normalBadgeBorderBrush = new(Color.Parse("#557EA7E6"));
    private readonly SolidColorBrush _invalidBackgroundBrush = new(Color.Parse("#F01B1022"));
    private readonly SolidColorBrush _invalidBorderBrush = new(Color.Parse("#FFE25555"));
    private readonly SolidColorBrush _invalidAccentBrush = new(Color.Parse("#FFFF6B6B"));
    private readonly SolidColorBrush _invalidBadgeBackgroundBrush = new(Color.Parse("#33FF4D4D"));
    private readonly SolidColorBrush _invalidBadgeBorderBrush = new(Color.Parse("#88FF7676"));

    public DesktopEditGhostView()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        Padding = new Thickness(14);
        Background = _normalBackgroundBrush;
        BorderBrush = _normalBorderBrush;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(22);
        ClipToBounds = true;
        RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        RenderTransform = _scaleTransform;
        Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = Visual.OpacityProperty,
                Duration = FastDuration,
                Easing = StandardEasing
            }
        };
        _scaleTransform.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = ScaleTransform.ScaleXProperty,
                Duration = FastDuration,
                Easing = StandardEasing
            },
            new DoubleTransition
            {
                Property = ScaleTransform.ScaleYProperty,
                Duration = FastDuration,
                Easing = StandardEasing
            }
        };

        _accentDot = new Border
        {
            Width = 10,
            Height = 10,
            CornerRadius = new CornerRadius(999),
            Background = _normalAccentBrush,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center
        };

        _titleTextBlock = new TextBlock
        {
            Foreground = _normalTextBrush,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            MaxLines = 1
        };

        _detailTextBlock = new TextBlock
        {
            Foreground = _normalMutedTextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            MaxLines = 1
        };

        _badgeTextBlock = new TextBlock
        {
            Foreground = _normalTextBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            MaxLines = 1
        };

        _badgeBorder = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Padding = new Thickness(9, 4),
            CornerRadius = new CornerRadius(999),
            Background = _normalBadgeBackgroundBrush,
            BorderBrush = _normalBadgeBorderBrush,
            BorderThickness = new Thickness(1),
            Child = _badgeTextBlock
        };

        var headerPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                _accentDot,
                _titleTextBlock
            }
        };

        var contentPanel = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                headerPanel,
                _detailTextBlock
            }
        };

        var rootGrid = new Grid
        {
            RowDefinitions = new RowDefinitions
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            },
            RowSpacing = 8
        };
        rootGrid.Children.Add(contentPanel);
        rootGrid.Children.Add(_badgeBorder);
        Grid.SetRow(contentPanel, 0);
        Grid.SetRow(_badgeBorder, 1);
        _badgeBorder.Margin = new Thickness(0, 2, 0, 0);

        Child = rootGrid;

        UpdatePreviewMetrics(180, 120);
        UpdateContent(null, null, null);
    }

    public void UpdateContent(string? title, string? detail, string? badgeText)
    {
        _titleTextBlock.Text = string.IsNullOrWhiteSpace(title) ? "Component" : title;
        _detailTextBlock.Text = string.IsNullOrWhiteSpace(detail) ? string.Empty : detail;
        _detailTextBlock.IsVisible = !string.IsNullOrWhiteSpace(detail);
        _badgeTextBlock.Text = string.IsNullOrWhiteSpace(badgeText) ? string.Empty : badgeText;
        _badgeBorder.IsVisible = !string.IsNullOrWhiteSpace(badgeText);
    }

    public void UpdatePreviewMetrics(double width, double height)
    {
        var normalizedWidth = Math.Max(1, width);
        var normalizedHeight = Math.Max(1, height);
        var minSide = Math.Max(1, Math.Min(normalizedWidth, normalizedHeight));

        CornerRadius = new CornerRadius(Math.Clamp(minSide * 0.16, 16, 28));
        Padding = new Thickness(
            Math.Clamp(minSide * 0.10, 10, 18),
            Math.Clamp(minSide * 0.10, 10, 18),
            Math.Clamp(minSide * 0.10, 10, 18),
            Math.Clamp(minSide * 0.09, 10, 16));

        var titleFontSize = Math.Clamp(minSide * 0.12, 12, 18);
        var detailFontSize = Math.Clamp(minSide * 0.085, 10, 13);
        var badgeFontSize = Math.Clamp(minSide * 0.08, 9, 12);
        var dotSize = Math.Clamp(minSide * 0.07, 8, 12);
        var badgeHorizontalPadding = Math.Clamp(minSide * 0.07, 8, 14);
        var badgeVerticalPadding = Math.Clamp(minSide * 0.035, 3, 6);

        _accentDot.Width = dotSize;
        _accentDot.Height = dotSize;
        _titleTextBlock.FontSize = titleFontSize;
        _detailTextBlock.FontSize = detailFontSize;
        _badgeTextBlock.FontSize = badgeFontSize;
        _badgeBorder.Padding = new Thickness(badgeHorizontalPadding, badgeVerticalPadding);
    }

    public void SetInvalid(bool isInvalid)
    {
        if (isInvalid)
        {
            Background = _invalidBackgroundBrush;
            BorderBrush = _invalidBorderBrush;
            _accentDot.Background = _invalidAccentBrush;
            _badgeBorder.Background = _invalidBadgeBackgroundBrush;
            _badgeBorder.BorderBrush = _invalidBadgeBorderBrush;
            _titleTextBlock.Foreground = _invalidBorderBrush;
            _detailTextBlock.Foreground = _invalidBorderBrush;
            _badgeTextBlock.Foreground = _invalidBorderBrush;
            Opacity = 0.9;
            return;
        }

        Background = _normalBackgroundBrush;
        BorderBrush = _normalBorderBrush;
        _accentDot.Background = _normalAccentBrush;
        _badgeBorder.Background = _normalBadgeBackgroundBrush;
        _badgeBorder.BorderBrush = _normalBadgeBorderBrush;
        _titleTextBlock.Foreground = _normalTextBrush;
        _detailTextBlock.Foreground = _normalMutedTextBrush;
        _badgeTextBlock.Foreground = _normalTextBrush;
        Opacity = 1.0;
    }

    public void SetRestingScale(double scale)
    {
        var clampedScale = Math.Clamp(scale, 0.85, 1.12);
        _scaleTransform.ScaleX = clampedScale;
        _scaleTransform.ScaleY = clampedScale;
    }

    public void AnimateToScale(double scale)
    {
        var clampedScale = Math.Clamp(scale, 0.85, 1.12);
        _scaleTransform.ScaleX = clampedScale;
        _scaleTransform.ScaleY = clampedScale;
    }
}
