using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using LanMontainDesktop.Services;

namespace LanMontainDesktop.Views.Components;

public partial class RecordingWidget : UserControl, IDesktopComponentWidget
{
    private const int WaveBarCount = 22;

    private readonly DispatcherTimer _uiTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(96)
    };

    private readonly IAudioRecorderService _audioRecorderService = AudioRecorderServiceFactory.CreateDefault();
    private readonly AppSettingsService _settingsService = new();
    private readonly LocalizationService _localizationService = new();
    private readonly List<Border> _waveBars = [];
    private readonly double[] _waveLevels = new double[WaveBarCount];

    private string _languageCode = "zh-CN";
    private string _lastSavedFilePath = string.Empty;
    private double _currentCellSize = 48;
    private bool _isAttached;

    public RecordingWidget()
    {
        InitializeComponent();

        _uiTimer.Tick += OnUiTick;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        SizeChanged += OnSizeChanged;

        InitializeWaveBars();
        ReloadLanguageCode();
        ApplyCellSize(_currentCellSize);
        RefreshVisual();
    }

    public void ApplyCellSize(double cellSize)
    {
        _currentCellSize = Math.Max(1, cellSize);
        var scale = ResolveScale();

        RootBorder.CornerRadius = new CornerRadius(Math.Clamp(34 * scale, 16, 56));
        RootBorder.Padding = new Thickness(Math.Clamp(10 * scale, 6, 18));
        RecorderCardBorder.CornerRadius = new CornerRadius(Math.Clamp(30 * scale, 14, 48));

        var sideButtonSize = Math.Clamp(54 * scale, 38, 72);
        DiscardButtonBorder.Width = sideButtonSize;
        DiscardButtonBorder.Height = sideButtonSize;
        DiscardButtonBorder.CornerRadius = new CornerRadius(sideButtonSize / 2d);

        SaveButtonBorder.Width = sideButtonSize;
        SaveButtonBorder.Height = sideButtonSize;
        SaveButtonBorder.CornerRadius = new CornerRadius(sideButtonSize / 2d);

        var centerButtonSize = Math.Clamp(68 * scale, 48, 86);
        RecordToggleButtonBorder.Width = centerButtonSize;
        RecordToggleButtonBorder.Height = centerButtonSize;
        RecordToggleButtonBorder.CornerRadius = new CornerRadius(centerButtonSize / 2d);

        WaveformBarsPanel.Spacing = Math.Clamp(3 * scale, 1.8, 5.4);
        TitleTextBlock.FontSize = Math.Clamp(19 * scale, 13, 26);
        TimerTextBlock.FontSize = Math.Clamp(66 * scale, 38, 84);
        HintTextBlock.FontSize = Math.Clamp(13 * scale, 9, 16);

        UpdateWaveformVisual();
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = true;
        _uiTimer.Start();
        ReloadLanguageCode();
        RefreshVisual();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        _uiTimer.Stop();
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        ApplyCellSize(_currentCellSize);
    }

    private void OnUiTick(object? sender, EventArgs e)
    {
        if (!_isAttached)
        {
            return;
        }

        RefreshVisual();
    }

    private void OnDiscardButtonPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _audioRecorderService.Discard();
        RefreshVisual();
        e.Handled = true;
    }

    private void OnRecordToggleButtonPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var snapshot = _audioRecorderService.GetSnapshot();
        if (!snapshot.IsSupported)
        {
            RefreshVisual();
            e.Handled = true;
            return;
        }

        if (snapshot.State == AudioRecorderRuntimeState.Recording)
        {
            _audioRecorderService.Pause();
        }
        else
        {
            _audioRecorderService.StartOrResume();
        }

        RefreshVisual();
        e.Handled = true;
    }

    private void OnSaveButtonPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _ = _audioRecorderService.StopAndSave();
        RefreshVisual();
        e.Handled = true;
    }

    private void RefreshVisual()
    {
        var snapshot = _audioRecorderService.GetSnapshot();

        TitleTextBlock.Text = L("recording.widget.title", "Recorder");
        TimerTextBlock.Text = FormatDuration(snapshot.Duration);

        var incomingLevel = snapshot.State == AudioRecorderRuntimeState.Recording
            ? snapshot.InputLevel
            : snapshot.State == AudioRecorderRuntimeState.Paused
                ? 0.10
                : 0;

        PushWaveLevel(incomingLevel);
        UpdateWaveformVisual();

        ApplyControlState(snapshot);
    }

    private void ApplyControlState(AudioRecorderSnapshot snapshot)
    {
        var isSupported = snapshot.IsSupported;
        var canFinalize = snapshot.State == AudioRecorderRuntimeState.Recording ||
                          snapshot.State == AudioRecorderRuntimeState.Paused;

        DiscardButtonBorder.IsHitTestVisible = isSupported && canFinalize;
        SaveButtonBorder.IsHitTestVisible = isSupported && canFinalize;
        RecordToggleButtonBorder.IsHitTestVisible = isSupported;

        DiscardButtonBorder.Opacity = DiscardButtonBorder.IsHitTestVisible ? 1 : 0.42;
        SaveButtonBorder.Opacity = SaveButtonBorder.IsHitTestVisible ? 1 : 0.42;
        RecordToggleButtonBorder.Opacity = RecordToggleButtonBorder.IsHitTestVisible ? 1 : 0.54;

        RecordDot.IsVisible = snapshot.State == AudioRecorderRuntimeState.Ready;
        PauseGlyphPath.IsVisible = snapshot.State == AudioRecorderRuntimeState.Recording;
        PlayGlyphPath.IsVisible = snapshot.State == AudioRecorderRuntimeState.Paused;

        if (!isSupported)
        {
            HintTextBlock.Text = L("recording.widget.hint.unsupported", "Microphone is unavailable");
            return;
        }

        if (snapshot.State == AudioRecorderRuntimeState.Recording)
        {
            HintTextBlock.Text = L("recording.widget.hint.recording", "Recording");
            return;
        }

        if (snapshot.State == AudioRecorderRuntimeState.Paused)
        {
            HintTextBlock.Text = L("recording.widget.hint.paused", "Paused");
            return;
        }

        if (snapshot.State == AudioRecorderRuntimeState.Error)
        {
            HintTextBlock.Text = string.IsNullOrWhiteSpace(snapshot.LastError)
                ? L("recording.widget.hint.error", "Recording failed")
                : snapshot.LastError;
            return;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.LastSavedFilePath) &&
            !string.Equals(snapshot.LastSavedFilePath, _lastSavedFilePath, StringComparison.OrdinalIgnoreCase))
        {
            _lastSavedFilePath = snapshot.LastSavedFilePath;
        }

        if (!string.IsNullOrWhiteSpace(_lastSavedFilePath))
        {
            var fileName = Path.GetFileName(_lastSavedFilePath);
            HintTextBlock.Text = string.Format(
                CultureInfo.InvariantCulture,
                L("recording.widget.hint.saved_format", "Saved {0}"),
                fileName);
            return;
        }

        HintTextBlock.Text = L("recording.widget.hint.ready", "Tap red button to record");
    }

    private void InitializeWaveBars()
    {
        if (_waveBars.Count > 0)
        {
            return;
        }

        for (var i = 0; i < WaveBarCount; i++)
        {
            var bar = new Border
            {
                Width = 3,
                Height = 6,
                CornerRadius = new CornerRadius(1.5),
                Background = CreateBrush("#121722"),
                Opacity = 0.24,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };

            _waveBars.Add(bar);
            WaveformBarsPanel.Children.Add(bar);
        }
    }

    private void PushWaveLevel(double level)
    {
        for (var i = 0; i < _waveLevels.Length - 1; i++)
        {
            _waveLevels[i] = _waveLevels[i + 1];
        }

        var previous = _waveLevels[^2];
        var target = Math.Clamp(level, 0, 1);
        _waveLevels[^1] = Math.Clamp((previous * 0.35) + (target * 0.65), 0, 1);
    }

    private void UpdateWaveformVisual()
    {
        var scale = ResolveScale();
        var barWidth = Math.Clamp(3 * scale, 2, 5);
        for (var i = 0; i < _waveBars.Count; i++)
        {
            var bar = _waveBars[i];
            var eased = Math.Pow(Math.Clamp(_waveLevels[i], 0, 1), 0.62);
            bar.Width = barWidth;
            bar.Height = Math.Clamp((4 + (eased * 30)) * scale, 3, 46);
            bar.CornerRadius = new CornerRadius(Math.Clamp(barWidth / 2d, 1, 3));
            bar.Opacity = Math.Clamp(0.20 + (eased * 0.82), 0.20, 1.0);
        }
    }

    private void ReloadLanguageCode()
    {
        try
        {
            var snapshot = _settingsService.Load();
            _languageCode = _localizationService.NormalizeLanguageCode(snapshot.LanguageCode);
        }
        catch
        {
            _languageCode = "zh-CN";
        }
    }

    private string L(string key, string fallback)
    {
        return _localizationService.GetString(_languageCode, key, fallback);
    }

    private double ResolveScale()
    {
        var cellScale = Math.Clamp(_currentCellSize / 44d, 0.60, 2.0);
        var widthScale = Bounds.Width > 1
            ? Math.Clamp(Bounds.Width / Math.Max(1, _currentCellSize * 2), 0.60, 2.0)
            : 1;
        var heightScale = Bounds.Height > 1
            ? Math.Clamp(Bounds.Height / Math.Max(1, _currentCellSize * 2), 0.60, 2.0)
            : 1;
        return Math.Clamp(Math.Min(cellScale, Math.Min(widthScale, heightScale) * 1.02), 0.58, 2.04);
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return duration.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        }

        return duration.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }

    private static IBrush CreateBrush(string colorHex)
    {
        return new SolidColorBrush(Color.Parse(colorHex));
    }
}
