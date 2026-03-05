using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

public partial class WorldClockWidget : UserControl, IDesktopComponentWidget, ITimeZoneAwareComponentWidget
{
    private const int BaseWidthCells = 4;
    private const int BaseHeightCells = 2;
    private const double BaseCellSize = 48;
    private const double DialDesignSize = 100;
    private const double DialCenter = DialDesignSize / 2d;

    private static readonly FontFamily MiSansFontFamily =
        new("MiSans VF, avares://LanMountainDesktop/Assets/Fonts#MiSans");

    private static readonly IReadOnlyDictionary<string, string> ZhCityNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["China Standard Time"] = "北京",
            ["Asia/Shanghai"] = "北京",
            ["GMT Standard Time"] = "伦敦",
            ["Europe/London"] = "伦敦",
            ["AUS Eastern Standard Time"] = "悉尼",
            ["Australia/Sydney"] = "悉尼",
            ["Eastern Standard Time"] = "纽约",
            ["America/New_York"] = "纽约",
            ["Tokyo Standard Time"] = "东京",
            ["Asia/Tokyo"] = "东京",
            ["UTC"] = "协调世界时",
            ["Etc/UTC"] = "协调世界时"
        };

    private static readonly IReadOnlyDictionary<string, string> EnCityNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["China Standard Time"] = "Beijing",
            ["Asia/Shanghai"] = "Beijing",
            ["GMT Standard Time"] = "London",
            ["Europe/London"] = "London",
            ["AUS Eastern Standard Time"] = "Sydney",
            ["Australia/Sydney"] = "Sydney",
            ["Eastern Standard Time"] = "New York",
            ["America/New_York"] = "New York",
            ["Tokyo Standard Time"] = "Tokyo",
            ["Asia/Tokyo"] = "Tokyo",
            ["UTC"] = "UTC",
            ["Etc/UTC"] = "UTC"
        };

    private sealed class ClockEntryVisual
    {
        public required StackPanel Host { get; init; }

        public required Border DialBorder { get; init; }

        public required Canvas TickCanvas { get; init; }

        public required Canvas NumberCanvas { get; init; }

        public required Line HourHand { get; init; }

        public required Line MinuteHand { get; init; }

        public required Line SecondHand { get; init; }

        public required Ellipse CenterOuter { get; init; }

        public required TextBlock CityTextBlock { get; init; }

        public required TextBlock DayTextBlock { get; init; }

        public required TextBlock OffsetTextBlock { get; init; }

        public bool? IsNightApplied { get; set; }
    }

    private readonly DispatcherTimer _clockTimer = new()
    {
        Interval = TimeSpan.FromSeconds(1)
    };

    private readonly AppSettingsService _settingsService = new();
    private readonly LocalizationService _localizationService = new();
    private readonly ClockEntryVisual[] _entryVisuals = new ClockEntryVisual[WorldClockTimeZoneCatalog.ClockCount];
    private readonly TimeZoneInfo[] _entryTimeZones = new TimeZoneInfo[WorldClockTimeZoneCatalog.ClockCount];

    private TimeZoneService? _timeZoneService;
    private string _languageCode = "zh-CN";
    private double _currentCellSize = BaseCellSize;
    private DateTime _nextLanguageProbeUtc = DateTime.MinValue;
    private string _secondHandMode = ClockSecondHandMode.Tick;

    public WorldClockWidget()
    {
        InitializeComponent();

        BuildClockEntryVisuals();
        LoadFromSettings();
        ApplySecondHandTimerInterval();
        ApplyCellSize(_currentCellSize);
        UpdateClockVisuals();

        _clockTimer.Tick += OnClockTimerTick;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        SizeChanged += OnSizeChanged;
    }

    public void SetTimeZoneService(TimeZoneService timeZoneService)
    {
        ClearTimeZoneService();
        _timeZoneService = timeZoneService;
        _timeZoneService.TimeZoneChanged += OnTimeZoneChanged;
        UpdateClockVisuals();
    }

    public void ClearTimeZoneService()
    {
        if (_timeZoneService is null)
        {
            return;
        }

        _timeZoneService.TimeZoneChanged -= OnTimeZoneChanged;
        _timeZoneService = null;
    }

    public void RefreshFromSettings()
    {
        LoadFromSettings();
        ApplySecondHandTimerInterval();
        UpdateClockVisuals();
    }

    public void ApplyCellSize(double cellSize)
    {
        _currentCellSize = Math.Max(1, cellSize);
        var scale = ResolveScale();

        var totalWidth = Bounds.Width > 1 ? Bounds.Width : _currentCellSize * BaseWidthCells;
        var totalHeight = Bounds.Height > 1 ? Bounds.Height : _currentCellSize * BaseHeightCells;

        var horizontalPadding = Math.Clamp(10 * scale, 4, 26);
        var verticalPadding = Math.Clamp(8 * scale, 3, 22);
        RootBorder.Padding = new Thickness(horizontalPadding, verticalPadding);
        RootBorder.CornerRadius = new CornerRadius(Math.Clamp(24 * scale, 10, 46));

        var usableWidth = Math.Max(48, totalWidth - horizontalPadding * 2);
        var usableHeight = Math.Max(28, totalHeight - verticalPadding * 2);

        var columnSpacing = Math.Clamp(usableWidth * 0.015, 2, 14);
        ClockHostGrid.ColumnSpacing = columnSpacing;
        var widthPerClock = Math.Max(18, (usableWidth - columnSpacing * 3) / WorldClockTimeZoneCatalog.ClockCount);

        var secondaryFont = Math.Clamp(10.5 * scale * (widthPerClock / 46d), 7, 18);
        var cityFont = Math.Clamp(secondaryFont * 1.42, 9, 24);
        var textSpacing = Math.Clamp(2.8 * scale, 1, 7);

        var estimatedTextHeight = cityFont * 1.2 + secondaryFont * 2.35 + textSpacing * 3;
        var dialSize = Math.Clamp(Math.Min(widthPerClock, usableHeight - estimatedTextHeight), 18, 108);
        if (dialSize < 18)
        {
            dialSize = Math.Clamp(Math.Min(widthPerClock, usableHeight * 0.56), 16, 108);
        }

        foreach (var entry in _entryVisuals)
        {
            entry.Host.Spacing = textSpacing;
            entry.DialBorder.Width = dialSize;
            entry.DialBorder.Height = dialSize;
            entry.DialBorder.CornerRadius = new CornerRadius(dialSize / 2d);

            entry.CityTextBlock.FontSize = cityFont;
            entry.DayTextBlock.FontSize = secondaryFont;
            entry.OffsetTextBlock.FontSize = secondaryFont;

            var maxTextWidth = Math.Max(16, widthPerClock + 10);
            entry.CityTextBlock.MaxWidth = maxTextWidth;
            entry.DayTextBlock.MaxWidth = maxTextWidth;
            entry.OffsetTextBlock.MaxWidth = maxTextWidth;
        }
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        LoadFromSettings();
        ApplySecondHandTimerInterval();
        UpdateClockVisuals();
        _clockTimer.Start();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _clockTimer.Stop();
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        ApplyCellSize(_currentCellSize);
    }

    private void OnTimeZoneChanged(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        UpdateClockVisuals();
    }

    private void OnClockTimerTick(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        UpdateClockVisuals();
    }

    private void BuildClockEntryVisuals()
    {
        ClockHostGrid.Children.Clear();
        for (var index = 0; index < WorldClockTimeZoneCatalog.ClockCount; index++)
        {
            var entry = CreateClockEntryVisual();
            _entryVisuals[index] = entry;
            ClockHostGrid.Children.Add(entry.Host);
            Grid.SetColumn(entry.Host, index);
            Grid.SetRow(entry.Host, 0);
        }
    }

    private ClockEntryVisual CreateClockEntryVisual()
    {
        var tickCanvas = new Canvas
        {
            Width = DialDesignSize,
            Height = DialDesignSize,
            IsHitTestVisible = false
        };
        var numberCanvas = new Canvas
        {
            Width = DialDesignSize,
            Height = DialDesignSize,
            IsHitTestVisible = false
        };
        var handsCanvas = new Canvas
        {
            Width = DialDesignSize,
            Height = DialDesignSize,
            IsHitTestVisible = false
        };

        var hourHand = CreateHandLine("#2B3242", 5.0);
        var minuteHand = CreateHandLine("#40495E", 3.2);
        var secondHand = CreateHandLine("#1A74F2", 2.2);
        handsCanvas.Children.Add(hourHand);
        handsCanvas.Children.Add(minuteHand);
        handsCanvas.Children.Add(secondHand);

        var centerOuter = new Ellipse
        {
            Width = 11,
            Height = 11,
            Fill = CreateBrush("#4F7BC0"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var centerInner = new Ellipse
        {
            Width = 4.5,
            Height = 4.5,
            Fill = CreateBrush("#1A74F2"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var dialRoot = new Grid
        {
            Width = DialDesignSize,
            Height = DialDesignSize
        };
        dialRoot.Children.Add(tickCanvas);
        dialRoot.Children.Add(numberCanvas);
        dialRoot.Children.Add(handsCanvas);
        dialRoot.Children.Add(centerOuter);
        dialRoot.Children.Add(centerInner);

        var dialBorder = new Border
        {
            Width = 56,
            Height = 56,
            CornerRadius = new CornerRadius(28),
            BorderThickness = new Thickness(1),
            Background = CreateBrush("#FAFBFD"),
            BorderBrush = CreateBrush("#DADFE8"),
            ClipToBounds = true,
            Child = new Viewbox
            {
                Stretch = Stretch.Uniform,
                Child = dialRoot
            }
        };

        var cityTextBlock = new TextBlock
        {
            Text = string.Empty,
            FontFamily = MiSansFontFamily,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = CreateBrush("#20232A"),
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var dayTextBlock = new TextBlock
        {
            Text = string.Empty,
            FontFamily = MiSansFontFamily,
            FontSize = 10.5,
            FontWeight = FontWeight.Medium,
            Foreground = CreateBrush("#646C79"),
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var offsetTextBlock = new TextBlock
        {
            Text = string.Empty,
            FontFamily = MiSansFontFamily,
            FontSize = 10.5,
            FontWeight = FontWeight.Medium,
            Foreground = CreateBrush("#7A7F89"),
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var host = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 3,
            Children =
            {
                dialBorder,
                cityTextBlock,
                dayTextBlock,
                offsetTextBlock
            }
        };

        var entry = new ClockEntryVisual
        {
            Host = host,
            DialBorder = dialBorder,
            TickCanvas = tickCanvas,
            NumberCanvas = numberCanvas,
            HourHand = hourHand,
            MinuteHand = minuteHand,
            SecondHand = secondHand,
            CenterOuter = centerOuter,
            CityTextBlock = cityTextBlock,
            DayTextBlock = dayTextBlock,
            OffsetTextBlock = offsetTextBlock
        };

        ApplyDialTheme(entry, isNight: false);
        return entry;
    }

    private static void BuildDialTicks(ClockEntryVisual entry, bool isNight)
    {
        entry.TickCanvas.Children.Clear();
        var majorColor = isNight ? "#E3E7F2" : "#2D3341";
        var minorColor = isNight ? "#9EA7B8" : "#9AA4B3";

        for (var i = 0; i < 60; i++)
        {
            var isMajor = i % 5 == 0;
            var angle = (i * 6 - 90) * Math.PI / 180d;
            var outerRadius = DialCenter - 6.5;
            var innerRadius = outerRadius - (isMajor ? 9 : 4.5);

            var x1 = DialCenter + Math.Cos(angle) * innerRadius;
            var y1 = DialCenter + Math.Sin(angle) * innerRadius;
            var x2 = DialCenter + Math.Cos(angle) * outerRadius;
            var y2 = DialCenter + Math.Sin(angle) * outerRadius;

            entry.TickCanvas.Children.Add(new Line
            {
                StartPoint = new Point(x1, y1),
                EndPoint = new Point(x2, y2),
                Stroke = CreateBrush(isMajor ? majorColor : minorColor),
                StrokeThickness = isMajor ? 1.9 : 0.8,
                StrokeLineCap = PenLineCap.Round
            });
        }
    }

    private static void BuildDialNumbers(ClockEntryVisual entry, bool isNight)
    {
        entry.NumberCanvas.Children.Clear();
        var numberColor = isNight ? "#F2F5FB" : "#1B202A";
        var radius = 36;
        for (var number = 1; number <= 12; number++)
        {
            var angle = (number * 30 - 90) * Math.PI / 180d;
            var x = DialCenter + Math.Cos(angle) * radius;
            var y = DialCenter + Math.Sin(angle) * radius;
            var text = number.ToString(CultureInfo.InvariantCulture);
            var isDoubleDigit = number >= 10;
            var width = isDoubleDigit ? 14 : 10;
            var height = 12;
            var numberText = new TextBlock
            {
                Text = text,
                Width = width,
                Height = height,
                FontFamily = MiSansFontFamily,
                FontSize = 9,
                FontWeight = FontWeight.SemiBold,
                Foreground = CreateBrush(numberColor),
                TextAlignment = TextAlignment.Center
            };

            Canvas.SetLeft(numberText, x - width / 2d);
            Canvas.SetTop(numberText, y - height / 2d);
            entry.NumberCanvas.Children.Add(numberText);
        }
    }

    private void LoadFromSettings()
    {
        var snapshot = _settingsService.Load();
        _languageCode = _localizationService.NormalizeLanguageCode(snapshot.LanguageCode);

        var ids = WorldClockTimeZoneCatalog.NormalizeTimeZoneIds(snapshot.WorldClockTimeZoneIds);
        for (var index = 0; index < WorldClockTimeZoneCatalog.ClockCount; index++)
        {
            var resolvedId = ids[index];
            _entryTimeZones[index] = WorldClockTimeZoneCatalog.ResolveTimeZoneOrLocal(resolvedId);
        }

        _secondHandMode = ClockSecondHandMode.Normalize(snapshot.WorldClockSecondHandMode);
    }

    private void ApplySecondHandTimerInterval()
    {
        _clockTimer.Interval = ClockSecondHandMode.IsSweep(_secondHandMode)
            ? TimeSpan.FromMilliseconds(16)
            : TimeSpan.FromSeconds(1);
    }

    private void UpdateClockVisuals()
    {
        var utcNow = DateTime.UtcNow;
        ProbeLanguageCodeIfNeeded(utcNow);

        var baseZone = _timeZoneService?.CurrentTimeZone ?? TimeZoneInfo.Local;
        var baseNow = TimeZoneInfo.ConvertTimeFromUtc(utcNow, baseZone);
        var baseOffset = baseZone.GetUtcOffset(utcNow);

        for (var index = 0; index < WorldClockTimeZoneCatalog.ClockCount; index++)
        {
            var entry = _entryVisuals[index];
            var zone = _entryTimeZones[index] ?? TimeZoneInfo.Local;
            var zonedNow = TimeZoneInfo.ConvertTimeFromUtc(utcNow, zone);
            var isNight = IsNightForLocalTime(zonedNow);
            ApplyDialTheme(entry, isNight);

            var secondValue = ClockSecondHandMode.IsSweep(_secondHandMode)
                ? zonedNow.Second + zonedNow.Millisecond / 1000d
                : zonedNow.Second;
            var minuteValue = zonedNow.Minute + secondValue / 60d;
            var hourValue = (zonedNow.Hour % 12) + minuteValue / 60d;

            var hourAngle = hourValue * 30d;
            var minuteAngle = minuteValue * 6d;
            var secondAngle = secondValue * 6d;

            SetHandGeometry(entry.HourHand, hourAngle, forwardLength: 24, backwardLength: 4.8);
            SetHandGeometry(entry.MinuteHand, minuteAngle, forwardLength: 33, backwardLength: 6);
            SetHandGeometry(entry.SecondHand, secondAngle, forwardLength: 37, backwardLength: 8.5);

            entry.CityTextBlock.Text = ResolveCityName(zone);
            entry.DayTextBlock.Text = ResolveRelativeDayLabel((zonedNow.Date - baseNow.Date).Days);

            var offsetDelta = zone.GetUtcOffset(utcNow) - baseOffset;
            entry.OffsetTextBlock.Text = ResolveOffsetLabel(offsetDelta);
        }
    }

    private static void ApplyDialTheme(ClockEntryVisual entry, bool isNight)
    {
        if (entry.IsNightApplied.HasValue && entry.IsNightApplied.Value == isNight)
        {
            return;
        }

        entry.IsNightApplied = isNight;
        entry.DialBorder.Background = CreateBrush(isNight ? "#2D313A" : "#FAFBFD");
        entry.DialBorder.BorderBrush = CreateBrush(isNight ? "#262A33" : "#DADFE8");
        entry.HourHand.Stroke = CreateBrush(isNight ? "#F5F8FF" : "#2B3242");
        entry.MinuteHand.Stroke = CreateBrush(isNight ? "#DDE4F0" : "#40495E");
        entry.SecondHand.Stroke = CreateBrush("#1A74F2");
        entry.CenterOuter.Fill = CreateBrush(isNight ? "#97B4EA" : "#4F7BC0");

        BuildDialTicks(entry, isNight);
        BuildDialNumbers(entry, isNight);
    }

    private void ProbeLanguageCodeIfNeeded(DateTime utcNow)
    {
        if (utcNow < _nextLanguageProbeUtc)
        {
            return;
        }

        _nextLanguageProbeUtc = utcNow.AddSeconds(25);
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

    private string ResolveCityName(TimeZoneInfo timeZone)
    {
        var cityNames = string.Equals(_languageCode, "zh-CN", StringComparison.OrdinalIgnoreCase)
            ? ZhCityNames
            : EnCityNames;
        if (cityNames.TryGetValue(timeZone.Id, out var cityName))
        {
            return cityName;
        }

        var normalized = timeZone.Id;
        var slashIndex = normalized.LastIndexOf('/');
        if (slashIndex >= 0 && slashIndex < normalized.Length - 1)
        {
            normalized = normalized[(slashIndex + 1)..];
        }

        normalized = normalized.Replace('_', ' ').Trim();
        normalized = normalized
            .Replace("Standard Time", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Daylight Time", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Time", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();

        return string.IsNullOrWhiteSpace(normalized) ? timeZone.Id : normalized;
    }

    private string ResolveRelativeDayLabel(int dayDelta)
    {
        if (dayDelta < 0)
        {
            return L("worldclock.widget.yesterday", "昨天");
        }

        if (dayDelta > 0)
        {
            return L("worldclock.widget.tomorrow", "明天");
        }

        return L("worldclock.widget.today", "今天");
    }

    private string ResolveOffsetLabel(TimeSpan delta)
    {
        var totalMinutes = (int)Math.Round(delta.TotalMinutes);
        if (totalMinutes == 0)
        {
            return L("worldclock.widget.offset_same", "0 小时");
        }

        var absMinutes = Math.Abs(totalMinutes);
        var hours = absMinutes / 60;
        var minutes = absMinutes % 60;
        var isAhead = totalMinutes > 0;

        if (minutes == 0)
        {
            return isAhead
                ? Lf("worldclock.widget.offset_ahead_hours", "早 {0} 小时", hours)
                : Lf("worldclock.widget.offset_behind_hours", "晚 {0} 小时", hours);
        }

        return isAhead
            ? Lf("worldclock.widget.offset_ahead_hm", "早 {0} 小时 {1} 分", hours, minutes)
            : Lf("worldclock.widget.offset_behind_hm", "晚 {0} 小时 {1} 分", hours, minutes);
    }

    private string L(string key, string fallback)
    {
        return _localizationService.GetString(_languageCode, key, fallback);
    }

    private string Lf(string key, string fallback, params object[] args)
    {
        var template = L(key, fallback);
        return string.Format(template, args);
    }

    private double ResolveScale()
    {
        var cellScale = Math.Clamp(_currentCellSize / BaseCellSize, 0.56, 2.5);
        var widthScale = Bounds.Width > 1
            ? Math.Clamp(Bounds.Width / Math.Max(1, _currentCellSize * BaseWidthCells), 0.52, 2.4)
            : 1;
        var heightScale = Bounds.Height > 1
            ? Math.Clamp(Bounds.Height / Math.Max(1, _currentCellSize * BaseHeightCells), 0.52, 2.4)
            : 1;
        return Math.Clamp(Math.Min(cellScale, Math.Min(widthScale, heightScale)), 0.50, 2.4);
    }

    private static bool IsNightForLocalTime(DateTime localTime)
    {
        var hour = localTime.Hour + localTime.Minute / 60d;
        return hour < 6 || hour >= 18;
    }

    private static void SetHandGeometry(Line hand, double angleDeg, double forwardLength, double backwardLength)
    {
        var radians = (angleDeg - 90) * Math.PI / 180d;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);

        hand.StartPoint = new Point(
            DialCenter - cos * backwardLength,
            DialCenter - sin * backwardLength);
        hand.EndPoint = new Point(
            DialCenter + cos * forwardLength,
            DialCenter + sin * forwardLength);
    }

    private static Line CreateHandLine(string colorHex, double thickness)
    {
        return new Line
        {
            StartPoint = new Point(DialCenter, DialCenter),
            EndPoint = new Point(DialCenter, DialCenter - 32),
            Stroke = CreateBrush(colorHex),
            StrokeThickness = thickness,
            StrokeLineCap = PenLineCap.Round
        };
    }

    private static IBrush CreateBrush(string colorHex)
    {
        return new SolidColorBrush(Color.Parse(colorHex));
    }
}
