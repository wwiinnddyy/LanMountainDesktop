using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using LanMountainDesktop.Theme;

namespace LanMountainDesktop.DesktopEditing;

internal enum DesktopEditGhostVisualStyle
{
    StandardLift = 0,
    ElevatedFromLibrary
}

internal sealed class DesktopEditOverlayPresenter
{
    private static readonly TimeSpan FastDuration = FluttermotionToken.Fast;
    private static readonly Easing StandardEasing = new CubicEaseOut();

    private readonly Canvas _root;
    private readonly DesktopEditGhostView _ghostView;
    private readonly Border _candidateOutline;
    private readonly ScaleTransform _candidateScale = new(1, 1);

    private Rect? _previewRect;
    private Rect? _candidateRect;
    private bool _isInvalid;
    private bool _isVisible;
    private int _dismissVersion;

    private readonly SolidColorBrush _candidateBrush = new(Color.Parse("#FF4F8EF7"));
    private readonly SolidColorBrush _candidateInvalidBrush = new(Color.Parse("#FFFF6B6B"));
    private readonly SolidColorBrush _candidateFillBrush = new(Color.Parse("#224F8EF7"));
    private readonly SolidColorBrush _candidateInvalidFillBrush = new(Color.Parse("#22FF6B6B"));

    public DesktopEditOverlayPresenter()
    {
        _ghostView = new DesktopEditGhostView
        {
            IsHitTestVisible = false,
            Opacity = 1
        };

        _candidateOutline = new Border
        {
            IsHitTestVisible = false,
            Background = _candidateFillBrush,
            BorderBrush = _candidateBrush,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(22),
            Opacity = 0,
            RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RenderTransform = _candidateScale,
            Transitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = Visual.OpacityProperty,
                    Duration = FastDuration,
                    Easing = StandardEasing
                }
            }
        };
        _candidateScale.Transitions = new Transitions
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

        _candidateOutline.SetValue(Panel.ZIndexProperty, 0);
        _ghostView.SetValue(Panel.ZIndexProperty, 1);

        _root = new Canvas
        {
            IsHitTestVisible = false,
            ClipToBounds = false,
            Opacity = 0,
            IsVisible = false,
            Children =
            {
                _candidateOutline,
                _ghostView
            }
        };

        _root.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = Visual.OpacityProperty,
                Duration = FastDuration,
                Easing = StandardEasing
            }
        };
    }

    public Control Root => _root;

    public void SetViewportSize(Size size)
    {
        _root.Width = Math.Max(1, size.Width);
        _root.Height = Math.Max(1, size.Height);
    }

    public void SetPreviewRect(Rect rect)
    {
        _previewRect = Normalize(rect);
        ApplyPreviewRect();
    }

    public void SetCandidateRect(Rect? rect)
    {
        _candidateRect = rect is null ? null : Normalize(rect.Value);
        ApplyCandidateRect();
    }

    public void UpdateGhostContent(string? title, string? detail = null, string? badge = null)
    {
        _ghostView.UpdateContent(title, detail, badge);
    }

    public void SetInvalid(bool isInvalid)
    {
        _isInvalid = isInvalid;
        _ghostView.SetInvalid(isInvalid);
        UpdateCandidateAppearance();
    }

    public void Show(DesktopEditGhostVisualStyle visualStyle = DesktopEditGhostVisualStyle.StandardLift)
    {
        _dismissVersion++;
        _isVisible = true;
        _root.IsVisible = true;
        _root.Opacity = 0;
        _ghostView.Opacity = 0;
        var initialGhostScale = visualStyle == DesktopEditGhostVisualStyle.ElevatedFromLibrary ? 1.02 : 0.985;
        var targetGhostScale = visualStyle == DesktopEditGhostVisualStyle.ElevatedFromLibrary ? 1.06 : 1;
        _ghostView.SetRestingScale(initialGhostScale);
        _candidateOutline.Opacity = 0;
        _candidateScale.ScaleX = 0.96;
        _candidateScale.ScaleY = 0.96;

        Dispatcher.UIThread.Post(() =>
        {
            if (!_isVisible)
            {
                return;
            }

            _root.Opacity = 1;
            _ghostView.Opacity = 1;
            _ghostView.SetRestingScale(targetGhostScale);
            if (_candidateRect.HasValue)
            {
                _candidateOutline.Opacity = 1;
                _candidateScale.ScaleX = 1;
                _candidateScale.ScaleY = 1;
            }
        }, DispatcherPriority.Background);
    }

    public void Hide()
    {
        _dismissVersion++;
        _isVisible = false;
        _root.Opacity = 0;
        _ghostView.Opacity = 0;
        _candidateOutline.Opacity = 0;
        _candidateScale.ScaleX = 0.96;
        _candidateScale.ScaleY = 0.96;
        _ghostView.SetRestingScale(0.96);
        _root.IsVisible = false;
    }

    public void Commit()
    {
        BeginDismiss(isCancel: false);
    }

    public void Cancel()
    {
        BeginDismiss(isCancel: true);
    }

    private void BeginDismiss(bool isCancel)
    {
        if (!_isVisible)
        {
            return;
        }

        var version = ++_dismissVersion;
        _isVisible = false;
        _candidateOutline.Opacity = 0;
        _ghostView.Opacity = 0;
        _root.Opacity = 0;

        var targetScale = isCancel ? 0.96 : 1.04;
        _ghostView.AnimateToScale(targetScale);
        _candidateScale.ScaleX = targetScale;
        _candidateScale.ScaleY = targetScale;

        DispatcherTimer.RunOnce(
            () =>
            {
                if (version != _dismissVersion)
                {
                    return;
                }

                _root.IsVisible = false;
            },
            FastDuration);
    }

    private void ApplyPreviewRect()
    {
        if (!_previewRect.HasValue)
        {
            return;
        }

        var rect = _previewRect.Value;
        _ghostView.Width = Math.Max(1, rect.Width);
        _ghostView.Height = Math.Max(1, rect.Height);
        Canvas.SetLeft(_ghostView, rect.X);
        Canvas.SetTop(_ghostView, rect.Y);
        _ghostView.UpdatePreviewMetrics(rect.Width, rect.Height);
    }

    private void ApplyCandidateRect()
    {
        if (!_candidateRect.HasValue)
        {
            _candidateOutline.IsVisible = false;
            _candidateOutline.Opacity = 0;
            return;
        }

        var rect = _candidateRect.Value;
        _candidateOutline.IsVisible = true;
        _candidateOutline.Width = Math.Max(1, rect.Width);
        _candidateOutline.Height = Math.Max(1, rect.Height);
        Canvas.SetLeft(_candidateOutline, rect.X);
        Canvas.SetTop(_candidateOutline, rect.Y);

        var cornerRadius = Math.Clamp(Math.Min(rect.Width, rect.Height) * 0.12, 14, 28);
        _candidateOutline.CornerRadius = new CornerRadius(cornerRadius);
        _candidateOutline.BorderBrush = _isInvalid ? _candidateInvalidBrush : _candidateBrush;
        _candidateOutline.Background = _isInvalid ? _candidateInvalidFillBrush : _candidateFillBrush;
        _candidateOutline.Opacity = _isVisible ? 1 : 0;
        _candidateScale.ScaleX = _isVisible ? 1 : 0.96;
        _candidateScale.ScaleY = _isVisible ? 1 : 0.96;
        UpdateCandidateAppearance();
    }

    private void UpdateCandidateAppearance()
    {
        if (!_candidateRect.HasValue)
        {
            return;
        }

        _candidateOutline.BorderBrush = _isInvalid ? _candidateInvalidBrush : _candidateBrush;
        _candidateOutline.Background = _isInvalid ? _candidateInvalidFillBrush : _candidateFillBrush;
    }

    private static Rect Normalize(Rect rect)
    {
        var width = Math.Max(1, rect.Width);
        var height = Math.Max(1, rect.Height);
        return new Rect(rect.X, rect.Y, width, height);
    }
}
