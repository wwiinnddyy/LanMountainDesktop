using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.ClockAirApp;

namespace LanMountainDesktop.AirAppHost;

public sealed partial class ClockAirAppView : UserControl
{
    private sealed class WorldClockRowVisual
    {
        public required TimeZoneInfo TimeZone { get; init; }

        public required TextBlock TimeTextBlock { get; init; }

        public required TextBlock DateTextBlock { get; init; }

        public required TextBlock OffsetTextBlock { get; init; }
    }

    private readonly DispatcherTimer _clockTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(250)
    };

    private readonly AirAppLaunchOptions _options;
    private readonly ClockAirAppSettingsStore _settingsStore = new();
    private readonly LocalizationService _localizationService = new();
    private readonly ClockAirAppStopwatchState _stopwatchState = new();
    private readonly ClockAirAppTimerState _timerState = new();
    private readonly List<TimeZoneInfo> _allTimeZones;
    private readonly List<WorldClockRowVisual> _worldClockRows = [];

    private ClockAirAppSettingsSnapshot _settings = ClockAirAppSettingsSnapshot.Normalize(null);
    private CultureInfo _culture = CultureInfo.CurrentCulture;
    private string _languageCode = "zh-CN";
    private string _selectedTab = ClockAirAppTabIds.WorldClock;
    private bool _suppressSettingsEvents;

    public ClockAirAppView()
        : this(AirAppLaunchOptions.Parse([]))
    {
    }

    public ClockAirAppView(AirAppLaunchOptions options)
    {
        _options = options;
        _allTimeZones = TimeZoneInfo.GetSystemTimeZones()
            .OrderBy(static zone => zone.GetUtcOffset(DateTime.UtcNow))
            .ThenBy(static zone => zone.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        InitializeComponent();
        LoadLanguage();
        LoadSettings();
        ApplyLocalizedText();
        PopulateSettingsControls();
        PopulateTimeZoneCombo();
        RebuildWorldClockRows();
        SelectStartupTab();
        UpdateAll();

        _clockTimer.Tick += OnClockTimerTick;
        AttachedToVisualTree += (_, _) =>
        {
            UpdateAll();
            _clockTimer.Start();
        };
        DetachedFromVisualTree += (_, _) => _clockTimer.Stop();
    }

    private void LoadLanguage()
    {
        try
        {
            var appSettings = new AppSettingsService().Load();
            _languageCode = _localizationService.NormalizeLanguageCode(appSettings.LanguageCode);
            _culture = CultureInfo.GetCultureInfo(_languageCode);
        }
        catch
        {
            _languageCode = "zh-CN";
            _culture = CultureInfo.GetCultureInfo("zh-CN");
        }
    }

    private void LoadSettings()
    {
        _settings = _settingsStore.Load();
        _timerState.SetDuration(TimeSpan.FromMinutes(5));
    }

    private void ApplyLocalizedText()
    {
        HeaderTitleTextBlock.Text = L("clockairapp.title", "Clock");
        HeaderSubtitleTextBlock.Text = L("clockairapp.subtitle", "World clock, stopwatch and timer");

        WorldTabButton.Content = L("clockairapp.tab.world", "World");
        StopwatchTabButton.Content = L("clockairapp.tab.stopwatch", "Stopwatch");
        TimerTabButton.Content = L("clockairapp.tab.timer", "Timer");
        SettingsTabButton.Content = L("clockairapp.tab.settings", "Settings");

        LocalLabelTextBlock.Text = L("clockairapp.world.local", "Local time");
        AddCityButton.Content = L("clockairapp.world.add", "Add");
        TimeZoneSearchTextBox.PlaceholderText = L("clockairapp.world.search", "Search city or time zone");

        StopwatchHintTextBlock.Text = L("clockairapp.stopwatch.hint", "Lap timing stays in this window session.");
        StopwatchStartPauseButton.Content = L("clockairapp.action.start", "Start");
        StopwatchLapButton.Content = L("clockairapp.stopwatch.lap", "Lap");
        StopwatchResetButton.Content = L("clockairapp.action.reset", "Reset");

        TimerHintTextBlock.Text = L("clockairapp.timer.hint", "Choose a preset or enter custom minutes.");
        TimerApplyButton.Content = L("clockairapp.timer.apply", "Apply");
        TimerStartPauseButton.Content = L("clockairapp.action.start", "Start");
        TimerResetButton.Content = L("clockairapp.action.reset", "Reset");
        TimerMinutesTextBox.PlaceholderText = L("clockairapp.timer.minutes", "Minutes");

        SettingsHeaderTextBlock.Text = L("clockairapp.settings.title", "Clock settings");
        TimeFormatLabelTextBlock.Text = L("clockairapp.settings.time_format", "Time format");
        StartupTabLabelTextBlock.Text = L("clockairapp.settings.startup_tab", "Startup page");
        ShowSecondsCheckBox.Content = L("clockairapp.settings.show_seconds", "Show seconds");
        ActivateOnTimerFinishedCheckBox.Content = L("clockairapp.settings.activate_timer", "Activate window when timer finishes");
    }

    private void PopulateSettingsControls()
    {
        _suppressSettingsEvents = true;
        try
        {
            SetComboItems(
                TimeFormatComboBox,
                [
                    (ClockAirAppTimeFormatMode.System, L("clockairapp.settings.time_format.system", "Follow system")),
                    (ClockAirAppTimeFormatMode.TwentyFourHour, L("clockairapp.settings.time_format.24h", "24-hour")),
                    (ClockAirAppTimeFormatMode.TwelveHour, L("clockairapp.settings.time_format.12h", "12-hour"))
                ],
                _settings.TimeFormatMode);
            SetComboItems(
                StartupTabComboBox,
                [
                    (ClockAirAppTabIds.Last, L("clockairapp.settings.startup.last", "Last used")),
                    (ClockAirAppTabIds.WorldClock, L("clockairapp.tab.world", "World")),
                    (ClockAirAppTabIds.Stopwatch, L("clockairapp.tab.stopwatch", "Stopwatch")),
                    (ClockAirAppTabIds.Timer, L("clockairapp.tab.timer", "Timer"))
                ],
                _settings.StartupTab);
            ShowSecondsCheckBox.IsChecked = _settings.ShowSeconds;
            ActivateOnTimerFinishedCheckBox.IsChecked = _settings.ActivateOnTimerFinished;
        }
        finally
        {
            _suppressSettingsEvents = false;
        }
    }

    private static void SetComboItems(ComboBox comboBox, IEnumerable<(string Id, string Text)> items, string selectedId)
    {
        comboBox.Items.Clear();
        foreach (var item in items)
        {
            comboBox.Items.Add(new ComboBoxItem
            {
                Tag = item.Id,
                Content = item.Text
            });
        }

        comboBox.SelectedItem = comboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, selectedId, StringComparison.OrdinalIgnoreCase))
            ?? comboBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
    }

    private void SelectStartupTab()
    {
        var startupTab = ClockAirAppTabIds.Normalize(_settings.StartupTab, ClockAirAppTabIds.Last);
        var tab = string.Equals(startupTab, ClockAirAppTabIds.Last, StringComparison.OrdinalIgnoreCase)
            ? ClockAirAppTabIds.Normalize(_settings.LastSelectedTab)
            : ClockAirAppTabIds.Normalize(startupTab);
        SelectTab(tab, save: false);
    }

    private void OnClockTimerTick(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        UpdateAll();
    }

    private void UpdateAll()
    {
        var now = DateTimeOffset.Now;
        UpdateWorldClock(now);
        UpdateStopwatch(now);
        UpdateTimer(now);
    }

    private void UpdateWorldClock(DateTimeOffset now)
    {
        var localNow = now.LocalDateTime;
        LocalTimeTextBlock.Text = ClockAirAppTimeFormatter.FormatTime(localNow, _settings, _culture);
        LocalDateTextBlock.Text = localNow.ToString("yyyy-MM-dd dddd", _culture);
        LocalTimeZoneTextBlock.Text = TimeZoneInfo.Local.DisplayName;
        WorldSummaryTextBlock.Text = Lf("clockairapp.world.count", "{0} cities", _settings.WorldClockTimeZoneIds.Count);

        var utcNow = now.UtcDateTime;
        foreach (var row in _worldClockRows)
        {
            var zonedTime = TimeZoneInfo.ConvertTimeFromUtc(utcNow, row.TimeZone);
            row.TimeTextBlock.Text = ClockAirAppTimeFormatter.FormatTime(zonedTime, _settings, _culture);
            row.DateTextBlock.Text = $"{ResolveRelativeDayLabel((zonedTime.Date - localNow.Date).Days)} - {zonedTime.ToString("yyyy-MM-dd", _culture)}";
            row.OffsetTextBlock.Text = ClockAirAppTimeFormatter.FormatUtcOffset(row.TimeZone.GetUtcOffset(utcNow));
        }
    }

    private void UpdateStopwatch(DateTimeOffset now)
    {
        StopwatchElapsedTextBlock.Text = ClockAirAppTimeFormatter.FormatDuration(_stopwatchState.GetElapsed(now), includeMilliseconds: true);
        StopwatchStartPauseButton.Content = _stopwatchState.IsRunning
            ? L("clockairapp.action.pause", "Pause")
            : L("clockairapp.action.start", "Start");
        StopwatchLapButton.IsEnabled = _stopwatchState.GetElapsed(now) > TimeSpan.Zero;
        StopwatchResetButton.IsEnabled = _stopwatchState.GetElapsed(now) > TimeSpan.Zero || _stopwatchState.Laps.Count > 0;
    }

    private void UpdateTimer(DateTimeOffset now)
    {
        if (_timerState.Update(now))
        {
            TimerStatusTextBlock.Text = L("clockairapp.timer.finished", "Timer finished");
            if (_settings.ActivateOnTimerFinished && VisualRoot is Window window)
            {
                window.Activate();
            }
        }

        TimerRemainingTextBlock.Text = ClockAirAppTimeFormatter.FormatDuration(_timerState.GetRemaining(now));
        TimerStartPauseButton.Content = _timerState.IsRunning
            ? L("clockairapp.action.pause", "Pause")
            : L("clockairapp.action.start", "Start");
        TimerResetButton.IsEnabled = _timerState.GetRemaining(now) < _timerState.Duration || _timerState.IsCompleted;
        if (!_timerState.IsCompleted && string.IsNullOrWhiteSpace(TimerStatusTextBlock.Text))
        {
            TimerStatusTextBlock.Text = Lf("clockairapp.timer.duration_status", "Duration {0}", ClockAirAppTimeFormatter.FormatDuration(_timerState.Duration));
        }
    }

    private void OnTabButtonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton button && button.Tag is string tab)
        {
            SelectTab(tab, save: true);
        }
    }

    private void SelectTab(string tab, bool save)
    {
        _selectedTab = ClockAirAppTabIds.Normalize(tab);
        WorldPage.IsVisible = string.Equals(_selectedTab, ClockAirAppTabIds.WorldClock, StringComparison.OrdinalIgnoreCase);
        StopwatchPage.IsVisible = string.Equals(_selectedTab, ClockAirAppTabIds.Stopwatch, StringComparison.OrdinalIgnoreCase);
        TimerPage.IsVisible = string.Equals(_selectedTab, ClockAirAppTabIds.Timer, StringComparison.OrdinalIgnoreCase);
        SettingsPage.IsVisible = string.Equals(_selectedTab, ClockAirAppTabIds.Settings, StringComparison.OrdinalIgnoreCase);

        WorldTabButton.IsChecked = WorldPage.IsVisible;
        StopwatchTabButton.IsChecked = StopwatchPage.IsVisible;
        TimerTabButton.IsChecked = TimerPage.IsVisible;
        SettingsTabButton.IsChecked = SettingsPage.IsVisible;

        if (save)
        {
            _settings.LastSelectedTab = _selectedTab;
            _settingsStore.Save(_settings);
        }
    }

    private void OnTimeZoneSearchChanged(object? sender, TextChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        PopulateTimeZoneCombo();
    }

    private void PopulateTimeZoneCombo()
    {
        var query = TimeZoneSearchTextBox.Text?.Trim() ?? string.Empty;
        var zones = _allTimeZones
            .Where(zone => MatchesTimeZoneQuery(zone, query))
            .Take(80)
            .ToList();

        TimeZoneComboBox.Items.Clear();
        foreach (var zone in zones)
        {
            TimeZoneComboBox.Items.Add(new ComboBoxItem
            {
                Tag = zone.Id,
                Content = FormatTimeZoneOption(zone)
            });
        }

        TimeZoneComboBox.SelectedItem = TimeZoneComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
    }

    private bool MatchesTimeZoneQuery(TimeZoneInfo zone, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var cityName = ClockAirAppTimeFormatter.ResolveCityName(zone, _languageCode);
        return zone.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               zone.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               zone.StandardName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               cityName.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private string FormatTimeZoneOption(TimeZoneInfo zone)
    {
        return $"{ClockAirAppTimeFormatter.FormatUtcOffset(zone.GetUtcOffset(DateTime.UtcNow))} | {ClockAirAppTimeFormatter.ResolveCityName(zone, _languageCode)} | {zone.StandardName}";
    }

    private void OnAddCityClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (TimeZoneComboBox.SelectedItem is not ComboBoxItem item || item.Tag is not string zoneId)
        {
            return;
        }

        if (_settings.WorldClockTimeZoneIds.Any(existing => string.Equals(existing, zoneId, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        _settings.WorldClockTimeZoneIds.Add(zoneId);
        SaveWorldClockSettings();
    }

    private void RebuildWorldClockRows()
    {
        _worldClockRows.Clear();
        WorldClockRowsPanel.Children.Clear();
        for (var index = 0; index < _settings.WorldClockTimeZoneIds.Count; index++)
        {
            var timeZone = WorldClockTimeZoneCatalog.ResolveTimeZoneOrLocal(_settings.WorldClockTimeZoneIds[index]);
            AddWorldClockRow(timeZone, index);
        }
    }

    private void AddWorldClockRow(TimeZoneInfo timeZone, int index)
    {
        var cityText = new TextBlock
        {
            Text = ClockAirAppTimeFormatter.ResolveCityName(timeZone, _languageCode),
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Foreground = TryGetBrush("AirAppTitleTextBrush", "#FF171A20")
        };
        var timeText = new TextBlock
        {
            FontSize = 24,
            FontWeight = FontWeight.SemiBold,
            LetterSpacing = 0,
            Foreground = TryGetBrush("AirAppTitleTextBrush", "#FF171A20"),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var dateText = new TextBlock
        {
            FontSize = 12,
            Foreground = TryGetBrush("AirAppSecondaryTextBrush", "#FF657080")
        };
        var offsetText = new TextBlock
        {
            FontSize = 12,
            Foreground = TryGetBrush("AirAppSecondaryTextBrush", "#FF657080"),
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var upButton = CreateIconButton("↑", L("clockairapp.action.move_up", "Move up"));
        upButton.IsEnabled = index > 0;
        upButton.Click += (_, _) => MoveWorldClock(index, -1);

        var downButton = CreateIconButton("↓", L("clockairapp.action.move_down", "Move down"));
        downButton.IsEnabled = index < _settings.WorldClockTimeZoneIds.Count - 1;
        downButton.Click += (_, _) => MoveWorldClock(index, 1);

        var removeButton = CreateIconButton("×", L("clockairapp.action.remove", "Remove"));
        removeButton.IsEnabled = _settings.WorldClockTimeZoneIds.Count > 1;
        removeButton.Click += (_, _) => RemoveWorldClock(index);

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto"),
            ColumnSpacing = 8
        };
        var leftStack = new StackPanel
        {
            Spacing = 3,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                cityText,
                dateText
            }
        };
        var timeStack = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                timeText,
                offsetText
            }
        };
        row.Children.Add(leftStack);
        row.Children.Add(timeStack);
        row.Children.Add(upButton);
        row.Children.Add(downButton);
        row.Children.Add(removeButton);
        Grid.SetColumn(timeStack, 1);
        Grid.SetColumn(upButton, 2);
        Grid.SetColumn(downButton, 3);
        Grid.SetColumn(removeButton, 4);

        WorldClockRowsPanel.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.Parse("#0A000000")),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(12, 10),
            Child = row
        });

        _worldClockRows.Add(new WorldClockRowVisual
        {
            TimeZone = timeZone,
            TimeTextBlock = timeText,
            DateTextBlock = dateText,
            OffsetTextBlock = offsetText
        });
    }

    private Button CreateIconButton(string text, string tooltip)
    {
        var button = new Button
        {
            Content = text,
            Classes = { "clock-icon-command" }
        };
        ToolTip.SetTip(button, tooltip);
        return button;
    }

    private void MoveWorldClock(int index, int delta)
    {
        var nextIndex = index + delta;
        if (index < 0 || nextIndex < 0 || index >= _settings.WorldClockTimeZoneIds.Count || nextIndex >= _settings.WorldClockTimeZoneIds.Count)
        {
            return;
        }

        (_settings.WorldClockTimeZoneIds[index], _settings.WorldClockTimeZoneIds[nextIndex]) =
            (_settings.WorldClockTimeZoneIds[nextIndex], _settings.WorldClockTimeZoneIds[index]);
        SaveWorldClockSettings();
    }

    private void RemoveWorldClock(int index)
    {
        if (_settings.WorldClockTimeZoneIds.Count <= 1 || index < 0 || index >= _settings.WorldClockTimeZoneIds.Count)
        {
            return;
        }

        _settings.WorldClockTimeZoneIds.RemoveAt(index);
        SaveWorldClockSettings();
    }

    private void SaveWorldClockSettings()
    {
        _settings = ClockAirAppSettingsSnapshot.Normalize(_settings);
        _settingsStore.Save(_settings);
        RebuildWorldClockRows();
        UpdateWorldClock(DateTimeOffset.Now);
    }

    private void OnStopwatchStartPauseClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        var now = DateTimeOffset.Now;
        if (_stopwatchState.IsRunning)
        {
            _stopwatchState.Pause(now);
        }
        else
        {
            _stopwatchState.StartOrResume(now);
        }

        UpdateStopwatch(now);
    }

    private void OnStopwatchLapClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        _ = _stopwatchState.AddLap(DateTimeOffset.Now);
        RebuildStopwatchLaps();
    }

    private void OnStopwatchResetClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        _stopwatchState.Reset();
        RebuildStopwatchLaps();
        UpdateStopwatch(DateTimeOffset.Now);
    }

    private void RebuildStopwatchLaps()
    {
        StopwatchLapsPanel.Children.Clear();
        for (var index = 0; index < _stopwatchState.Laps.Count; index++)
        {
            var lap = _stopwatchState.Laps[index];
            StopwatchLapsPanel.Children.Add(new TextBlock
            {
                Text = Lf("clockairapp.stopwatch.lap_format", "Lap {0}  {1}", _stopwatchState.Laps.Count - index, ClockAirAppTimeFormatter.FormatDuration(lap, includeMilliseconds: true)),
                Foreground = TryGetBrush("AirAppSecondaryTextBrush", "#FF657080"),
                FontSize = 13
            });
        }
    }

    private void OnTimerPresetClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button &&
            button.Tag is string minutesText &&
            int.TryParse(minutesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes))
        {
            SetTimerDuration(minutes);
        }
    }

    private void OnTimerApplyClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (!int.TryParse(TimerMinutesTextBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var minutes))
        {
            TimerStatusTextBlock.Text = L("clockairapp.timer.invalid", "Enter a valid minute value.");
            return;
        }

        SetTimerDuration(minutes);
    }

    private void SetTimerDuration(int minutes)
    {
        minutes = Math.Clamp(minutes, 1, 24 * 60);
        TimerMinutesTextBox.Text = minutes.ToString(CultureInfo.CurrentCulture);
        _timerState.SetDuration(TimeSpan.FromMinutes(minutes));
        TimerStatusTextBlock.Text = Lf("clockairapp.timer.duration_status", "Duration {0}", ClockAirAppTimeFormatter.FormatDuration(_timerState.Duration));
        UpdateTimer(DateTimeOffset.Now);
    }

    private void OnTimerStartPauseClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        var now = DateTimeOffset.Now;
        if (_timerState.IsRunning)
        {
            _timerState.Pause(now);
        }
        else
        {
            _timerState.StartOrResume(now);
            TimerStatusTextBlock.Text = string.Empty;
        }

        UpdateTimer(now);
    }

    private void OnTimerResetClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        _timerState.Reset();
        TimerStatusTextBlock.Text = Lf("clockairapp.timer.duration_status", "Duration {0}", ClockAirAppTimeFormatter.FormatDuration(_timerState.Duration));
        UpdateTimer(DateTimeOffset.Now);
    }

    private void OnSettingsChanged(object? sender, SelectionChangedEventArgs e)
    {
        _ = e;
        SaveSettingsFromControls(sender);
    }

    private void OnSettingsChanged(object? sender, RoutedEventArgs e)
    {
        _ = e;
        SaveSettingsFromControls(sender);
    }

    private void SaveSettingsFromControls(object? sender)
    {
        _ = sender;
        if (_suppressSettingsEvents)
        {
            return;
        }

        _settings.TimeFormatMode = TimeFormatComboBox.SelectedItem is ComboBoxItem timeFormatItem && timeFormatItem.Tag is string timeFormat
            ? timeFormat
            : ClockAirAppTimeFormatMode.System;
        _settings.StartupTab = StartupTabComboBox.SelectedItem is ComboBoxItem startupItem && startupItem.Tag is string startupTab
            ? startupTab
            : ClockAirAppTabIds.Last;
        _settings.ShowSeconds = ShowSecondsCheckBox.IsChecked == true;
        _settings.ActivateOnTimerFinished = ActivateOnTimerFinishedCheckBox.IsChecked == true;
        _settingsStore.Save(_settings);
        UpdateAll();
    }

    private string ResolveRelativeDayLabel(int dayDelta)
    {
        if (dayDelta < 0)
        {
            return L("worldclock.widget.yesterday", "Yesterday");
        }

        if (dayDelta > 0)
        {
            return L("worldclock.widget.tomorrow", "Tomorrow");
        }

        return L("worldclock.widget.today", "Today");
    }

    private IBrush TryGetBrush(string resourceKey, string fallbackColor)
    {
        return this.TryFindResource(resourceKey, out var value) && value is IBrush brush
            ? brush
            : new SolidColorBrush(Color.Parse(fallbackColor));
    }

    private string L(string key, string fallback)
    {
        return _localizationService.GetString(_languageCode, key, fallback);
    }

    private string Lf(string key, string fallback, params object[] args)
    {
        return string.Format(_culture, L(key, fallback), args);
    }
}
