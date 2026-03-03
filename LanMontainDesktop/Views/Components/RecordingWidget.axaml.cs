using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
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
        var rawScale = ResolveScale();
        var chromeScale = Math.Clamp(rawScale, 0.62, 2.0);
        var contentScale = Math.Clamp(rawScale, 0.74, 1.0);

        var rootRadius = Math.Clamp(34 * chromeScale, 16, 56);
        RootBorder.CornerRadius = new CornerRadius(rootRadius);
        RootBorder.Padding = new Thickness(0);
        RecorderCardBorder.CornerRadius = new CornerRadius(rootRadius);
        RecorderContentGrid.Margin = new Thickness(
            Math.Clamp(24 * contentScale, 14, 26),
            Math.Clamp(18 * contentScale, 10, 22),
            Math.Clamp(24 * contentScale, 14, 26),
            Math.Clamp(18 * contentScale, 10, 24));

        var sideButtonSize = Math.Clamp(54 * contentScale, 34, 58);
        DiscardButtonBorder.Width = sideButtonSize;
        DiscardButtonBorder.Height = sideButtonSize;
        DiscardButtonBorder.CornerRadius = new CornerRadius(sideButtonSize / 2d);
        DiscardIcon.FontSize = Math.Clamp(20 * contentScale, 14, 22);

        SaveButtonBorder.Width = sideButtonSize;
        SaveButtonBorder.Height = sideButtonSize;
        SaveButtonBorder.CornerRadius = new CornerRadius(sideButtonSize / 2d);
        SaveIcon.FontSize = Math.Clamp(22 * contentScale, 15, 24);

        var centerButtonSize = Math.Clamp(68 * contentScale, 42, 72);
        RecordToggleButtonBorder.Width = centerButtonSize;
        RecordToggleButtonBorder.Height = centerButtonSize;
        RecordToggleButtonBorder.CornerRadius = new CornerRadius(centerButtonSize / 2d);
        var centerIconSize = Math.Clamp(20 * contentScale, 14, 24);
        PauseGlyphIcon.FontSize = centerIconSize;
        PlayGlyphIcon.FontSize = centerIconSize;
        var recordDotSize = Math.Clamp(15 * contentScale, 10, 16);
        RecordDot.Width = recordDotSize;
        RecordDot.Height = recordDotSize;

        WaveformRowGrid.Margin = new Thickness(0, Math.Clamp(12 * contentScale, 6, 16), 0, 0);
        CenterNeedle.Height = Math.Clamp(32 * contentScale, 18, 34);
        FutureLine.Margin = new Thickness(Math.Clamp(4 * contentScale, 2, 6), 0, 0, 0);
        FutureLine.Height = Math.Clamp(2 * contentScale, 1, 3);
        ControlButtonsGrid.Margin = new Thickness(0, Math.Clamp(16 * contentScale, 8, 20), 0, 0);
        ControlButtonsGrid.ColumnSpacing = Math.Clamp(16 * contentScale, 8, 16);
        HintTextBlock.Margin = new Thickness(0, Math.Clamp(8 * contentScale, 4, 10), 0, 0);

        WaveformBarsPanel.Spacing = Math.Clamp(3 * contentScale, 1.6, 3.4);
        TitleTextBlock.FontSize = Math.Clamp(19 * contentScale, 12, 20);
        TimerTextBlock.FontSize = Math.Clamp(66 * contentScale, 34, 66);
        HintTextBlock.FontSize = Math.Clamp(13 * contentScale, 9, 13);

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

    private async void OnSaveButtonPointerPressed(object? sender, PointerPressedEventArgs e)
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

        var (wasCancelled, outputPath) = await PickSavePathAsync();
        if (wasCancelled)
        {
            RefreshVisual();
            e.Handled = true;
            return;
        }

        _ = _audioRecorderService.StopAndSave(outputPath);
        RefreshVisual();
        e.Handled = true;
    }

    private void RefreshVisual()
    {
        var snapshot = _audioRecorderService.GetSnapshot();

        TitleTextBlock.Text = L("recording.widget.title", "Recorder");
        TimerTextBlock.Text = FormatDuration(snapshot.Duration);

        if (snapshot.State == AudioRecorderRuntimeState.Recording)
        {
            PushWaveLevel(snapshot.InputLevel);
        }
        else if (snapshot.State != AudioRecorderRuntimeState.Paused)
        {
            ClearWaveLevels();
        }

        UpdateWaveformVisual();

        ApplyControlState(snapshot);
    }

    private void ApplyControlState(AudioRecorderSnapshot snapshot)
    {
        var isSupported = snapshot.IsSupported;
        var canFinalize = snapshot.State == AudioRecorderRuntimeState.Recording ||
                          snapshot.State == AudioRecorderRuntimeState.Paused;
        var isReady = snapshot.State == AudioRecorderRuntimeState.Ready;

        TitleTextBlock.IsVisible = false;
        DiscardButtonBorder.IsVisible = canFinalize;
        SaveButtonBorder.IsVisible = canFinalize;
        DiscardButtonBorder.IsHitTestVisible = isSupported && canFinalize;
        SaveButtonBorder.IsHitTestVisible = isSupported && canFinalize;
        RecordToggleButtonBorder.IsHitTestVisible = isSupported;

        DiscardButtonBorder.Opacity = DiscardButtonBorder.IsHitTestVisible ? 1 : 0.42;
        SaveButtonBorder.Opacity = SaveButtonBorder.IsHitTestVisible ? 1 : 0.42;
        RecordToggleButtonBorder.Opacity = RecordToggleButtonBorder.IsHitTestVisible ? 1 : 0.54;

        TimerTextBlock.Foreground = CreateBrush(!isSupported
            ? "#B2B7C0"
            : isReady
                ? "#A4A9B2"
                : "#151922");
        HintTextBlock.IsVisible = !isReady || !isSupported;

        RecordDot.IsVisible = snapshot.State == AudioRecorderRuntimeState.Ready;
        PauseGlyphIcon.IsVisible = snapshot.State == AudioRecorderRuntimeState.Recording;
        PlayGlyphIcon.IsVisible = snapshot.State == AudioRecorderRuntimeState.Paused;

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

    private void ClearWaveLevels()
    {
        Array.Fill(_waveLevels, 0);
    }

    private void UpdateWaveformVisual()
    {
        var scale = Math.Clamp(ResolveScale(), 0.74, 1.0);
        var barWidth = Math.Clamp(3 * scale, 1.8, 3.2);
        for (var i = 0; i < _waveBars.Count; i++)
        {
            var bar = _waveBars[i];
            var eased = Math.Pow(Math.Clamp(_waveLevels[i], 0, 1), 0.62);
            bar.Width = barWidth;
            bar.Height = Math.Clamp((4 + (eased * 24)) * scale, 3, 30);
            bar.CornerRadius = new CornerRadius(Math.Clamp(barWidth / 2d, 1, 2));
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

    private async Task<(bool WasCancelled, string? OutputPath)> PickSavePathAsync()
    {
        var suggestedName = $"recording_{DateTime.Now:yyyyMMdd_HHmmss}.wav";
        var topLevel = TopLevel.GetTopLevel(this);
        var storageProvider = topLevel?.StorageProvider;
        if (storageProvider is null)
        {
            return (false, null);
        }

        var saveFile = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = L("recording.widget.save_picker_title", "Save recording"),
            SuggestedFileName = suggestedName,
            DefaultExtension = "wav",
            FileTypeChoices =
            [
                new FilePickerFileType(L("recording.widget.save_picker_type", "WAV audio"))
                {
                    Patterns = ["*.wav"],
                    MimeTypes = ["audio/wav", "audio/x-wav"]
                }
            ]
        });

        if (saveFile is null)
        {
            return (true, null);
        }

        var path = saveFile.Path;
        if (path is null || !path.IsFile)
        {
            return (true, null);
        }

        return (false, path.LocalPath);
    }
}
