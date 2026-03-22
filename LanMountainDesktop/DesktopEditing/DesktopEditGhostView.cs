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

    private readonly Image _previewImage;
    private readonly Border _previewOverlay;
    private readonly Border _fallbackCard;
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

    private bool _hasPreviewImage;
    private bool _isInvalid;

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
            CreateOpacityTransition(FastDuration)
        };
        _scaleTransform.Transitions = new Transitions
        {
            CreateScaleTransition(ScaleTransform.ScaleXProperty, FastDuration),
            CreateScaleTransition(ScaleTransform.ScaleYProperty, FastDuration)
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

        _previewImage = new Image
        {
            Stretch = Stretch.UniformToFill,
            IsVisible = false
        };

        _previewOverlay = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#1A000000")),
            IsVisible = false
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

        var fallbackGrid = new Grid
        {
            RowDefinitions = new RowDefinitions
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            },
            RowSpacing = 8
        };
        fallbackGrid.Children.Add(contentPanel);
        fallbackGrid.Children.Add(_badgeBorder);
        Grid.SetRow(contentPanel, 0);
        Grid.SetRow(_badgeBorder, 1);
        _badgeBorder.Margin = new Thickness(0, 2, 0, 0);

        _fallbackCard = new Border
        {
            Background = Brushes.Transparent,
            Child = fallbackGrid
        };

        Child = new Grid
        {
            Children =
            {
                _previewImage,
                _previewOverlay,
                _fallbackCard
            }
        };

        UpdatePreviewMetrics(180, 120);
        UpdateContent(null, null, null);
        ApplyShellChrome();
    }

    public void UpdateContent(string? title, string? detail, string? badgeText)
    {
        _titleTextBlock.Text = string.IsNullOrWhiteSpace(title) ? "Component" : title;
        _detailTextBlock.Text = string.IsNullOrWhiteSpace(detail) ? string.Empty : detail;
        _detailTextBlock.IsVisible = !string.IsNullOrWhiteSpace(detail);
        _badgeTextBlock.Text = string.IsNullOrWhiteSpace(badgeText) ? string.Empty : badgeText;
        _badgeBorder.IsVisible = !string.IsNullOrWhiteSpace(badgeText);
    }

    public void SetPreviewImage(IImage? image)
    {
        _previewImage.Source = image;
        _hasPreviewImage = image is not null;
        _previewImage.IsVisible = _hasPreviewImage;
        _previewOverlay.IsVisible = false;
        _fallbackCard.IsVisible = !_hasPreviewImage;
        ApplyShellChrome();
    }

    public void UpdatePreviewMetrics(double width, double height)
    {
        var normalizedWidth = Math.Max(1, width);
        var normalizedHeight = Math.Max(1, height);
        var minSide = Math.Max(1, Math.Min(normalizedWidth, normalizedHeight));

        CornerRadius = _hasPreviewImage
            ? new CornerRadius(Math.Clamp(minSide * 0.14, 14, 24))
            : new CornerRadius(Math.Clamp(minSide * 0.16, 16, 28));
        Padding = _hasPreviewImage
            ? new Thickness(
                Math.Clamp(minSide * 0.02, 1, 4),
                Math.Clamp(minSide * 0.02, 1, 4),
                Math.Clamp(minSide * 0.02, 1, 4),
                Math.Clamp(minSide * 0.02, 1, 4))
            : new Thickness(
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
        _isInvalid = isInvalid;

        if (isInvalid)
        {
            _accentDot.Background = _invalidAccentBrush;
            _badgeBorder.Background = _invalidBadgeBackgroundBrush;
            _badgeBorder.BorderBrush = _invalidBadgeBorderBrush;
            _titleTextBlock.Foreground = _invalidBorderBrush;
            _detailTextBlock.Foreground = _invalidBorderBrush;
            _badgeTextBlock.Foreground = _invalidBorderBrush;
            if (!_hasPreviewImage)
            {
                Background = _invalidBackgroundBrush;
                BorderBrush = _invalidBorderBrush;
                BorderThickness = new Thickness(1);
                Opacity = 0.9;
            }
            else
            {
                ApplyShellChrome();
            }
            return;
        }

        _accentDot.Background = _normalAccentBrush;
        _badgeBorder.Background = _normalBadgeBackgroundBrush;
        _badgeBorder.BorderBrush = _normalBadgeBorderBrush;
        _titleTextBlock.Foreground = _normalTextBrush;
        _detailTextBlock.Foreground = _normalMutedTextBrush;
        _badgeTextBlock.Foreground = _normalTextBrush;
        if (!_hasPreviewImage)
        {
            Background = _normalBackgroundBrush;
            BorderBrush = _normalBorderBrush;
            BorderThickness = new Thickness(1);
            Opacity = 1.0;
        }
        else
        {
            ApplyShellChrome();
        }
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

    internal bool HasPreviewImage => _hasPreviewImage;

    internal void SetScaleTransitionDuration(TimeSpan duration)
    {
        _scaleTransform.Transitions = new Transitions
        {
            CreateScaleTransition(ScaleTransform.ScaleXProperty, duration),
            CreateScaleTransition(ScaleTransform.ScaleYProperty, duration)
        };
    }

    internal void SetOpacityTransitionDuration(TimeSpan duration)
    {
        Transitions = new Transitions
        {
            CreateOpacityTransition(duration)
        };
    }

    private void ApplyShellChrome()
    {
        if (_hasPreviewImage)
        {
            Background = Brushes.Transparent;
            BorderBrush = Brushes.Transparent;
            BorderThickness = new Thickness(0);
            BoxShadow = BoxShadows.Parse("0 14 32 #1A000000");
            Opacity = 1.0;
            return;
        }

        BoxShadow = default;
        if (_isInvalid)
        {
            Background = _invalidBackgroundBrush;
            BorderBrush = _invalidBorderBrush;
            BorderThickness = new Thickness(1);
            Opacity = 0.9;
            return;
        }

        Background = _normalBackgroundBrush;
        BorderBrush = _normalBorderBrush;
        BorderThickness = new Thickness(1);
        Opacity = 1.0;
    }

    private static DoubleTransition CreateScaleTransition(AvaloniaProperty property, TimeSpan duration) =>
        new()
        {
            Property = property,
            Duration = duration,
            Easing = StandardEasing
        };

    private static DoubleTransition CreateOpacityTransition(TimeSpan duration) =>
        new()
        {
            Property = Visual.OpacityProperty,
            Duration = duration,
            Easing = StandardEasing
        };
}
