using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using LanMountainDesktop.Launcher.Models;
using LanMountainDesktop.Launcher.Services;

namespace LanMountainDesktop.Launcher.Views;

public partial class OobeWindow : Window
{
    private const int AnimationDurationMs = 300;
    private const int TypingDelayMs = 100;

    private readonly TaskCompletionSource<bool> _completionSource = new();
    private readonly DataLocationResolver _resolver;
    private bool _isTransitioning;
    private bool _isDebugMode;
    private int _currentStep = 1;
    
    // 数据位置选择
    private DataLocationMode _selectedDataLocationMode = DataLocationMode.System;
    private bool _migrateExistingData;
    
    // 主题选择
    private Services.ThemeMode _selectedThemeMode = Services.ThemeMode.Light;
    private string _selectedAccentColor = "#0078D4";
    private MonetSource _selectedMonetSource = MonetSource.Wallpaper;

    public OobeWindow()
    {
        AvaloniaXamlLoader.Load(this);
        Loaded += OnWindowLoaded;
        Opened += OnWindowOpened;

        var appRoot = AppDomain.CurrentDomain.BaseDirectory;
        _resolver = new DataLocationResolver(appRoot);
    }

    public void SetDebugMode(bool isDebugMode)
    {
        _isDebugMode = isDebugMode;
    }

    public Task WaitForEnterAsync() => _completionSource.Task;

    private void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        InitializeDataLocationStep();
        SetupEventHandlers();
    }

    private void SetupEventHandlers()
    {
        // 步骤 1: 开始按钮
        if (this.FindControl<Button>("StartButton") is { } startButton)
        {
            startButton.Click += OnStartButtonClick;
        }

        // 步骤 2: 主题选择页面
        if (this.FindControl<Button>("ThemeBackButton") is { } themeBackButton)
        {
            themeBackButton.Click += OnThemeBackClick;
        }

        if (this.FindControl<Button>("ThemeNextButton") is { } themeNextButton)
        {
            themeNextButton.Click += OnThemeNextClick;
        }

        // 浅色/深色模式选择
        if (this.FindControl<Border>("LightModeOption") is { } lightModeOption)
        {
            lightModeOption.PointerPressed += (s, e) => SelectThemeMode(Services.ThemeMode.Light);
        }

        if (this.FindControl<Border>("DarkModeOption") is { } darkModeOption)
        {
            darkModeOption.PointerPressed += (s, e) => SelectThemeMode(Services.ThemeMode.Dark);
        }

        if (this.FindControl<RadioButton>("LightModeRadio") is { } lightModeRadio)
        {
            lightModeRadio.IsCheckedChanged += (s, e) =>
            {
                if (lightModeRadio.IsChecked == true) SelectThemeMode(Services.ThemeMode.Light);
            };
        }

        if (this.FindControl<RadioButton>("DarkModeRadio") is { } darkModeRadio)
        {
            darkModeRadio.IsCheckedChanged += (s, e) =>
            {
                if (darkModeRadio.IsChecked == true) SelectThemeMode(Services.ThemeMode.Dark);
            };
        }

        // 主题色选择
        SetupAccentColorHandlers();

        // 莫奈取色来源选择
        if (this.FindControl<Border>("MonetFromWallpaperOption") is { } monetWallpaperOption)
        {
            monetWallpaperOption.PointerPressed += (s, e) => SelectMonetSource(MonetSource.Wallpaper);
        }

        if (this.FindControl<Border>("MonetFromCustomOption") is { } monetCustomOption)
        {
            monetCustomOption.PointerPressed += (s, e) => SelectMonetSource(MonetSource.Custom);
        }

        if (this.FindControl<Border>("MonetDisabledOption") is { } monetDisabledOption)
        {
            monetDisabledOption.PointerPressed += (s, e) => SelectMonetSource(MonetSource.Disabled);
        }

        if (this.FindControl<RadioButton>("MonetFromWallpaperRadio") is { } monetWallpaperRadio)
        {
            monetWallpaperRadio.IsCheckedChanged += (s, e) =>
            {
                if (monetWallpaperRadio.IsChecked == true) SelectMonetSource(MonetSource.Wallpaper);
            };
        }

        if (this.FindControl<RadioButton>("MonetFromCustomRadio") is { } monetCustomRadio)
        {
            monetCustomRadio.IsCheckedChanged += (s, e) =>
            {
                if (monetCustomRadio.IsChecked == true) SelectMonetSource(MonetSource.Custom);
            };
        }

        if (this.FindControl<RadioButton>("MonetDisabledRadio") is { } monetDisabledRadio)
        {
            monetDisabledRadio.IsCheckedChanged += (s, e) =>
            {
                if (monetDisabledRadio.IsChecked == true) SelectMonetSource(MonetSource.Disabled);
            };
        }

        // 步骤 3: 数据位置选择页面
        if (this.FindControl<Button>("DataLocationBackButton") is { } dataLocationBackButton)
        {
            dataLocationBackButton.Click += OnDataLocationBackClick;
        }

        if (this.FindControl<Button>("DataLocationNextButton") is { } dataLocationNextButton)
        {
            dataLocationNextButton.Click += OnDataLocationNextClick;
        }

        if (this.FindControl<Border>("SystemOptionBorder") is { } systemOption)
        {
            systemOption.PointerPressed += (s, e) => SelectDataLocationMode(DataLocationMode.System);
        }

        if (this.FindControl<Border>("PortableOptionBorder") is { } portableOption)
        {
            portableOption.PointerPressed += (s, e) => SelectDataLocationMode(DataLocationMode.Portable);
        }

        if (this.FindControl<RadioButton>("SystemRadio") is { } systemRadio)
        {
            systemRadio.IsCheckedChanged += (s, e) =>
            {
                if (systemRadio.IsChecked == true) SelectDataLocationMode(DataLocationMode.System);
            };
        }

        if (this.FindControl<RadioButton>("PortableRadio") is { } portableRadio)
        {
            portableRadio.IsCheckedChanged += (s, e) =>
            {
                if (portableRadio.IsChecked == true) SelectDataLocationMode(DataLocationMode.Portable);
            };
        }

        // 步骤 4: 欢迎完成页面
        if (this.FindControl<Button>("EnterButton") is { } enterButton)
        {
            enterButton.Click += OnEnterClick;
        }
    }

    private void SetupAccentColorHandlers()
    {
        var colorMap = new Dictionary<string, string>
        {
            { "BlueColor", "#0078D4" },
            { "PurpleColor", "#7B68EE" },
            { "GreenColor", "#107C10" },
            { "OrangeColor", "#D83B01" },
            { "PinkColor", "#E3008C" },
            { "TealColor", "#008080" }
        };

        foreach (var (name, color) in colorMap)
        {
            if (this.FindControl<Border>(name) is { } colorBorder)
            {
                colorBorder.PointerPressed += (s, e) => SelectAccentColor(name, color);
            }
        }
    }

    private async void OnWindowOpened(object? sender, EventArgs e)
    {
        await PlayTypingAnimationAsync();
    }

    private async Task PlayTypingAnimationAsync()
    {
        var typingTextBlock = this.FindControl<TextBlock>("TypingTextBlock");
        var cursorBorder = this.FindControl<Border>("CursorBorder");
        var subtitlePanel = this.FindControl<StackPanel>("SubtitlePanel");
        var buttonAnimationArea = this.FindControl<Grid>("ButtonAnimationArea");
        var startButton = this.FindControl<Button>("StartButton");
        var mouseCursor = this.FindControl<Canvas>("MouseCursor");

        if (typingTextBlock == null || cursorBorder == null) return;

        // 打字机效果：阑山桌面 LanMountain Desktop（在同一行）
        var fullText = "阑山桌面 LanMountain Desktop";
        for (int i = 0; i <= fullText.Length; i++)
        {
            typingTextBlock.Text = fullText.Substring(0, i);
            await Task.Delay(TypingDelayMs);
        }

        // 停顿一下
        await Task.Delay(500);

        // 隐藏光标
        cursorBorder.IsVisible = false;

        // 显示副标题（打字机效果：下一代 互动信息看板）
        if (subtitlePanel != null)
        {
            subtitlePanel.IsVisible = true;
            subtitlePanel.Opacity = 1;
            await PlaySubtitleTypingAnimationAsync();
        }

        // 停顿一下再显示按钮
        await Task.Delay(400);

        // 显示按钮动画区域
        if (buttonAnimationArea != null)
        {
            buttonAnimationArea.IsVisible = true;
        }

        // 鼠标拖拽按钮入场
        if (mouseCursor != null && startButton != null)
        {
            await AnimateMouseDragButtonAsync(mouseCursor, startButton);
        }
    }

    private async Task AnimateMouseDragButtonAsync(Canvas mouseCursor, Button button)
    {
        // 初始处于画面外部的 X 坐标
        var startX = -400.0;
        var endX = 0.0;
        
        button.IsVisible = true;
        button.Opacity = 1;
        button.RenderTransform = new TranslateTransform(startX, 0);
        
        // 鼠标位于按钮上，比如偏移 (100, 30) 的位置
        var mouseOffsetX = 100.0;
        var mouseOffsetY = 30.0;
        mouseCursor.Margin = new Thickness(startX + mouseOffsetX, mouseOffsetY, 0, 0);
        mouseCursor.IsVisible = true;
        
        await Task.Delay(300);

        var duration = 800;
        var steps = 40;
        var delay = duration / steps;

        for (int i = 0; i <= steps; i++)
        {
            var progress = (double)i / steps;
            var eased = EaseOutBack(progress); // 使用 EaseOutBack 营造“拖拽到位”的清脆回弹感

            var currentX = startX + (endX - startX) * eased;

            button.RenderTransform = new TranslateTransform(currentX, 0);
            mouseCursor.Margin = new Thickness(currentX + mouseOffsetX, mouseOffsetY, 0, 0);

            await Task.Delay(delay);
        }
        
        await Task.Delay(200);

        // 隐藏鼠标光标
        await AnimateOpacityAsync(mouseCursor, 1, 0, 200);
        mouseCursor.IsVisible = false;
    }

    private async Task PlaySubtitleTypingAnimationAsync()
    {
        var nextGenTextBlock = this.FindControl<TextBlock>("NextGenTextBlock");
        var dashboardTextBlock = this.FindControl<TextBlock>("DashboardTextBlock");
        var subtitleCursorBorder = this.FindControl<Border>("SubtitleCursorBorder");

        if (nextGenTextBlock == null || dashboardTextBlock == null) return;

        // 获取渐变画刷
        var gradientBrush = nextGenTextBlock.Foreground as LinearGradientBrush;

        // 启动渐变色流动动画
        if (gradientBrush != null)
        {
            _ = AnimateGradientFlowAsync(gradientBrush);
        }

        // 显示光标
        if (subtitleCursorBorder != null)
        {
            subtitleCursorBorder.IsVisible = true;
        }

        // 打字机效果：下一代
        var nextGenText = "下一代";
        for (int i = 0; i <= nextGenText.Length; i++)
        {
            nextGenTextBlock.Text = nextGenText.Substring(0, i);
            await Task.Delay(TypingDelayMs);
        }

        // 停顿一下
        await Task.Delay(200);

        // 换行，光标移到第二行
        if (subtitleCursorBorder != null)
        {
            subtitleCursorBorder.IsVisible = false;
        }

        // 打字机效果：互动信息看板
        var dashboardText = "互动信息看板";
        for (int i = 0; i <= dashboardText.Length; i++)
        {
            dashboardTextBlock.Text = dashboardText.Substring(0, i);
            await Task.Delay(TypingDelayMs);
        }

        // 停顿一下后隐藏光标
        await Task.Delay(300);
    }

    private async Task AnimateGradientFlowAsync(LinearGradientBrush? gradientBrush)
    {
        if (gradientBrush == null) return;

        var stops = gradientBrush.GradientStops;
        if (stops.Count < 2) return;

        // 获取原有的所有颜色
        var colors = new System.Collections.Generic.List<Color>();
        foreach (var stop in stops)
        {
            colors.Add(stop.Color);
        }
        
        // 为了实现无缝循环流动，把第一个颜色追加到最后
        colors.Add(colors[0]);

        // 重新分配 GradientStops
        stops.Clear();
        for (int i = 0; i < colors.Count; i++)
        {
            stops.Add(new GradientStop(colors[i], (double)i / (colors.Count - 1)));
        }

        // 设置铺展模式，超出范围时重复
        gradientBrush.SpreadMethod = GradientSpreadMethod.Repeat;

        double offset = 0;

        while (true)
        {
            offset -= 0.005; // 每次流动一小步，负数表示向右流动
            if (offset <= -1.0) offset = 0;

            // 让渐变保持水平方向，但位置不断偏移，形成河流般的流动效果
            gradientBrush.StartPoint = new RelativePoint(offset, 0, RelativeUnit.Relative);
            gradientBrush.EndPoint = new RelativePoint(offset + 1, 0, RelativeUnit.Relative);

            await Task.Delay(16); // 约60帧
        }
    }

    private async void OnStartButtonClick(object? sender, RoutedEventArgs e)
    {
        if (_isTransitioning) return;
        await NavigateToStep(2);
    }

    // 主题选择页面按钮
    private async void OnThemeBackClick(object? sender, RoutedEventArgs e)
    {
        if (_isTransitioning) return;
        await NavigateToStep(1);
    }

    private async void OnThemeNextClick(object? sender, RoutedEventArgs e)
    {
        if (_isTransitioning) return;
        await NavigateToStep(3);
    }

    // 数据位置选择页面按钮
    private async void OnDataLocationBackClick(object? sender, RoutedEventArgs e)
    {
        if (_isTransitioning) return;
        await NavigateToStep(2);
    }

    private async void OnDataLocationNextClick(object? sender, RoutedEventArgs e)
    {
        if (_isTransitioning) return;

        // 应用数据位置选择
        if (!_isDebugMode)
        {
            _resolver.ApplyLocationChoice(_selectedDataLocationMode, null, _migrateExistingData);
        }

        await NavigateToStep(4);
    }

    private async void OnEnterClick(object? sender, RoutedEventArgs e)
    {
        if (_isTransitioning) return;
        _isTransitioning = true;

        try
        {
            await PlayExitAnimationAsync();
            _completionSource.TrySetResult(true);
            Close();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[OobeWindow] Error: {ex.Message}");
            _completionSource.TrySetResult(true);
            Close();
        }
    }

    private void InitializeDataLocationStep()
    {
        if (this.FindControl<TextBlock>("SystemPathText") is { } systemPathText)
        {
            systemPathText.Text = _resolver.DefaultSystemDataPath;
        }

        if (this.FindControl<TextBlock>("PortablePathText") is { } portablePathText)
        {
            portablePathText.Text = _resolver.DefaultPortableDataPath;
        }

        var canWriteToAppRoot = _resolver.IsPortableModeAllowed();
        if (this.FindControl<RadioButton>("PortableRadio") is { } portableRadio)
        {
            portableRadio.IsEnabled = canWriteToAppRoot;
        }

        if (!canWriteToAppRoot)
        {
            if (this.FindControl<Border>("AdminWarningBanner") is { } warningBanner)
            {
                warningBanner.IsVisible = true;
            }
        }

        if (_resolver.HasExistingSystemData())
        {
            _migrateExistingData = true;
            if (this.FindControl<Border>("MigrationInfoBorder") is { } migrationInfo)
            {
                migrationInfo.IsVisible = true;
            }
            if (this.FindControl<TextBlock>("MigrationInfoText") is { } migrationText)
            {
                migrationText.Text = "检测到现有数据，选择便携模式时将自动迁移。";
            }
        }
    }

    private void SelectDataLocationMode(DataLocationMode mode)
    {
        _selectedDataLocationMode = mode;

        if (this.FindControl<RadioButton>("SystemRadio") is { } systemRadio)
        {
            systemRadio.IsChecked = mode == DataLocationMode.System;
        }

        if (this.FindControl<RadioButton>("PortableRadio") is { } portableRadio)
        {
            portableRadio.IsChecked = mode == DataLocationMode.Portable;
        }

        if (this.FindControl<Border>("SystemOptionBorder") is { } systemBorder)
        {
            systemBorder.BorderBrush = mode == DataLocationMode.System
                ? Application.Current?.Resources["AccentFillColorDefaultBrush"] as IBrush
                : Application.Current?.Resources["CardStrokeColorDefaultBrush"] as IBrush;
            systemBorder.BorderThickness = mode == DataLocationMode.System
                ? new Thickness(2)
                : new Thickness(1);
        }

        if (this.FindControl<Border>("PortableOptionBorder") is { } portableBorder)
        {
            portableBorder.BorderBrush = mode == DataLocationMode.Portable
                ? Application.Current?.Resources["AccentFillColorDefaultBrush"] as IBrush
                : Application.Current?.Resources["CardStrokeColorDefaultBrush"] as IBrush;
            portableBorder.BorderThickness = mode == DataLocationMode.Portable
                ? new Thickness(2)
                : new Thickness(1);
        }
    }

    // 主题选择方法
    private void SelectThemeMode(Services.ThemeMode mode)
    {
        _selectedThemeMode = mode;

        // 立即应用主题到启动器
        ThemeService.ApplyTheme(mode, _selectedAccentColor);

        if (this.FindControl<RadioButton>("LightModeRadio") is { } lightModeRadio)
        {
            lightModeRadio.IsChecked = mode == Services.ThemeMode.Light;
        }

        if (this.FindControl<RadioButton>("DarkModeRadio") is { } darkModeRadio)
        {
            darkModeRadio.IsChecked = mode == Services.ThemeMode.Dark;
        }

        if (this.FindControl<Border>("LightModeOption") is { } lightModeOption)
        {
            lightModeOption.BorderBrush = mode == Services.ThemeMode.Light
                ? Application.Current?.Resources["AccentFillColorDefaultBrush"] as IBrush
                : Application.Current?.Resources["CardStrokeColorDefaultBrush"] as IBrush;
            lightModeOption.BorderThickness = mode == Services.ThemeMode.Light
                ? new Thickness(2)
                : new Thickness(1);
        }

        if (this.FindControl<Border>("DarkModeOption") is { } darkModeOption)
        {
            darkModeOption.BorderBrush = mode == Services.ThemeMode.Dark
                ? Application.Current?.Resources["AccentFillColorDefaultBrush"] as IBrush
                : Application.Current?.Resources["CardStrokeColorDefaultBrush"] as IBrush;
            darkModeOption.BorderThickness = mode == Services.ThemeMode.Dark
                ? new Thickness(2)
                : new Thickness(1);
        }
    }

    private void SelectAccentColor(string colorName, string colorValue)
    {
        _selectedAccentColor = colorValue;

        // 更新所有颜色圆圈边框
        var colorBorders = new[] { "BlueColor", "PurpleColor", "GreenColor", "OrangeColor", "PinkColor", "TealColor" };
        foreach (var name in colorBorders)
        {
            if (this.FindControl<Border>(name) is { } border)
            {
                var isSelected = name == colorName;
                border.BorderBrush = isSelected
                    ? Application.Current?.Resources["TextFillColorPrimaryBrush"] as IBrush
                    : null;
                border.BorderThickness = isSelected ? new Thickness(3) : new Thickness(0);
            }
        }
    }

    private void SelectMonetSource(MonetSource source)
    {
        _selectedMonetSource = source;

        if (this.FindControl<RadioButton>("MonetFromWallpaperRadio") is { } wallpaperRadio)
        {
            wallpaperRadio.IsChecked = source == MonetSource.Wallpaper;
        }

        if (this.FindControl<RadioButton>("MonetFromCustomRadio") is { } customRadio)
        {
            customRadio.IsChecked = source == MonetSource.Custom;
        }

        if (this.FindControl<RadioButton>("MonetDisabledRadio") is { } disabledRadio)
        {
            disabledRadio.IsChecked = source == MonetSource.Disabled;
        }

        UpdateMonetOptionBorder("MonetFromWallpaperOption", source == MonetSource.Wallpaper);
        UpdateMonetOptionBorder("MonetFromCustomOption", source == MonetSource.Custom);
        UpdateMonetOptionBorder("MonetDisabledOption", source == MonetSource.Disabled);
    }

    private void UpdateMonetOptionBorder(string borderName, bool isSelected)
    {
        if (this.FindControl<Border>(borderName) is { } border)
        {
            border.BorderBrush = isSelected
                ? Application.Current?.Resources["AccentFillColorDefaultBrush"] as IBrush
                : Application.Current?.Resources["CardStrokeColorDefaultBrush"] as IBrush;
            border.BorderThickness = isSelected ? new Thickness(2) : new Thickness(1);
        }
    }

    private async Task NavigateToStep(int step)
    {
        if (_isTransitioning || step == _currentStep) return;
        _isTransitioning = true;

        // 获取当前步骤的控件
        Grid? currentStepControl = _currentStep switch
        {
            1 => this.FindControl<Grid>("TypingStep"),
            2 => this.FindControl<Grid>("ThemeStep"),
            3 => this.FindControl<Grid>("DataLocationStep"),
            4 => this.FindControl<Grid>("WelcomeStep"),
            _ => null
        };

        // 获取目标步骤的控件
        Grid? nextStepControl = step switch
        {
            1 => this.FindControl<Grid>("TypingStep"),
            2 => this.FindControl<Grid>("ThemeStep"),
            3 => this.FindControl<Grid>("DataLocationStep"),
            4 => this.FindControl<Grid>("WelcomeStep"),
            _ => null
        };

        if (currentStepControl == null || nextStepControl == null)
        {
            _isTransitioning = false;
            return;
        }

        await AnimateOpacityAsync(currentStepControl, 1, 0, AnimationDurationMs);
        currentStepControl.IsVisible = false;

        nextStepControl.IsVisible = true;
        nextStepControl.Opacity = 0;
        await AnimateOpacityAsync(nextStepControl, 0, 1, AnimationDurationMs);

        _currentStep = step;
        _isTransitioning = false;
    }

    private async Task PlayExitAnimationAsync()
    {
        var contentGrid = this.FindControl<Grid>("ContentGrid");
        if (contentGrid != null)
        {
            await AnimateOpacityAsync(contentGrid, 1, 0, AnimationDurationMs);
        }
    }

    private static async Task AnimateOpacityAsync(Control element, double from, double to, int durationMs)
    {
        var steps = 20;
        var delay = durationMs / steps;

        for (int i = 0; i <= steps; i++)
        {
            var progress = (double)i / steps;
            var eased = EaseOutCubic(progress);
            element.Opacity = from + (to - from) * eased;
            await Task.Delay(delay);
        }
    }

    private static double EaseOutCubic(double t) => 1 - Math.Pow(1 - t, 3);
    private static double EaseOutQuad(double t) => 1 - Math.Pow(1 - t, 2);
    private static double EaseOutBack(double t)
    {
        const double c1 = 1.70158;
        const double c3 = c1 + 1;
        var t1 = t - 1;
        return 1 + c3 * Math.Pow(t1, 3) + c1 * Math.Pow(t1, 2);
    }
}

// 枚举定义（使用 Services 命名空间中的 ThemeMode）
public enum MonetSource
{
    Wallpaper,
    Custom,
    Disabled
}
