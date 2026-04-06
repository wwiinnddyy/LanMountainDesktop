using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;

namespace LanMountainDesktop.Views.Components;

public partial class NotificationBoxWidget : UserControl,
    IDesktopComponentWidget,
    IComponentSettingsContextAware,
    IComponentRuntimeContextAware
{
    private readonly List<NotificationItemControl> _notificationControls = [];
    private NotificationListenerService? _notificationService;
    private IComponentInstanceSettingsStore _componentSettings = null!;
    private ISettingsService _appSettingsService = null!;
    private AppSettingsSnapshot _appSettings = new();
    private ComponentSettingsSnapshot _componentSettingsSnapshot = new();
    private bool _isAttached;
    private bool _isPrivacyMode;
    private bool _isNightVisual;
    private double _currentCellSize = 48d;

    public NotificationBoxWidget()
    {
        InitializeComponent();

        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        ActualThemeVariantChanged += OnActualThemeVariantChanged;

        PointerPressed += (_, e) =>
        {
            if (e.Source == NotificationListPanel)
            {
                ClearSelection();
            }
        };
    }

    public void ApplyCellSize(double cellSize)
    {
        _currentCellSize = Math.Max(1, cellSize);
        UpdateAdaptiveLayout();
    }

    public void SetComponentSettingsContext(DesktopComponentSettingsContext context)
    {
        _componentSettings = context.ComponentSettingsStore;
        LoadSettings();
        RefreshUI();
    }

    public void SetComponentRuntimeContext(DesktopComponentRuntimeContext context)
    {
        _notificationService = NotificationListenerServiceProvider.GetOrCreate(_appSettingsService);
        if (_notificationService != null)
        {
            _notificationService.NotificationReceived += OnNotificationReceived;
            _notificationService.NotificationRemoved += OnNotificationRemoved;
        }
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = true;
        _isNightVisual = ResolveNightMode();
        LoadSettings();
        RefreshUI();
        UpdateAdaptiveLayout();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;

        if (_notificationService != null)
        {
            _notificationService.NotificationReceived -= OnNotificationReceived;
            _notificationService.NotificationRemoved -= OnNotificationRemoved;
        }
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        _isNightVisual = ResolveNightMode();
        UpdateAdaptiveLayout();
    }

    private bool ResolveNightMode()
    {
        if (ActualThemeVariant == ThemeVariant.Dark)
        {
            return true;
        }

        if (ActualThemeVariant == ThemeVariant.Light)
        {
            return false;
        }

        if (this.TryFindResource("AdaptiveSurfaceBaseBrush", out var value) &&
            value is ISolidColorBrush brush)
        {
            return CalculateRelativeLuminance(brush.Color) < 0.45;
        }

        return true;
    }

    private static double CalculateRelativeLuminance(Color color)
    {
        static double ToLinear(double channel)
        {
            return channel <= 0.03928
                ? channel / 12.92
                : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }

        var r = ToLinear(color.R / 255d);
        var g = ToLinear(color.G / 255d);
        var b = ToLinear(color.B / 255d);
        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    private void LoadSettings()
    {
        var appSettingsFacade = HostSettingsFacadeProvider.GetOrCreate();
        _appSettingsService = appSettingsFacade.Settings;
        _appSettings = _appSettingsService.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
        _isPrivacyMode = _appSettings.NotificationBoxPrivacyMode;

        _componentSettingsSnapshot = _componentSettings?.Load()
            ?? new ComponentSettingsSnapshot();
    }

    private void RefreshUI()
    {
        if (!_isAttached) return;

        var hasNotifications = _notificationService?.GetNotifications().Count > 0;
        PrivacyOverlay.IsVisible = _isPrivacyMode && hasNotifications && _notificationService?.GetUnreadCount() > 0;
        NotificationListPanel.IsVisible = !PrivacyOverlay.IsVisible;

        var unreadCount = _notificationService?.GetUnreadCount() ?? 0;
        UnreadBadge.IsVisible = unreadCount > 0;
        UnreadCountText.Text = unreadCount.ToString();

        ClearButton.IsVisible = _componentSettingsSnapshot.NotificationBoxShowClearButton
            && hasNotifications;

        UpdateStatusText();
        RenderNotifications();
    }

    private void RenderNotifications()
    {
        NotificationListPanel.Children.Clear();
        _notificationControls.Clear();

        if (_notificationService == null)
        {
            EmptyStateText.IsVisible = true;
            EmptyStateText.Text = "通知服务未启动";
            return;
        }

        var notifications = _notificationService.GetNotifications();

        if (notifications.Count == 0)
        {
            EmptyStateText.IsVisible = true;
            EmptyStateText.Text = "暂无通知";
            return;
        }

        EmptyStateText.IsVisible = false;

        notifications = ApplySorting(notifications);

        var maxCount = _componentSettingsSnapshot.NotificationBoxMaxDisplayCount;
        notifications = notifications.Take(maxCount).ToList();

        if (_componentSettingsSnapshot.NotificationBoxGroupByApp)
        {
            RenderGroupedNotifications(notifications);
        }
        else
        {
            RenderFlatNotifications(notifications);
        }
    }

    private IReadOnlyList<NotificationItem> ApplySorting(IReadOnlyList<NotificationItem> notifications)
    {
        return _componentSettingsSnapshot.NotificationBoxSortOrder switch
        {
            "TimeAsc" => notifications.OrderBy(n => n.ReceivedTime).ToList(),
            "AppGroup" => notifications.OrderBy(n => n.AppName).ThenByDescending(n => n.ReceivedTime).ToList(),
            _ => notifications.OrderByDescending(n => n.ReceivedTime).ToList()
        };
    }

    private void RenderFlatNotifications(IReadOnlyList<NotificationItem> notifications)
    {
        foreach (var notification in notifications)
        {
            var control = CreateNotificationControl(notification);
            NotificationListPanel.Children.Add(control);
            _notificationControls.Add(control);
        }
    }

    private void RenderGroupedNotifications(IReadOnlyList<NotificationItem> notifications)
    {
        var grouped = notifications.GroupBy(n => n.AppName).ToList();

        foreach (var group in grouped)
        {
            var groupHeader = new TextBlock
            {
                Text = group.Key,
                FontWeight = FontWeight.SemiBold,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.Parse("#8B95A5")),
                Margin = new Thickness(0, 6, 0, 3)
            };
            NotificationListPanel.Children.Add(groupHeader);

            foreach (var notification in group)
            {
                var control = CreateNotificationControl(notification);
                NotificationListPanel.Children.Add(control);
                _notificationControls.Add(control);
            }
        }
    }

    private NotificationItemControl CreateNotificationControl(NotificationItem notification)
    {
        var control = new NotificationItemControl(notification, _componentSettingsSnapshot, _isNightVisual);
        control.Clicked += OnNotificationClicked;
        control.MarkAsRead += OnMarkAsRead;
        return control;
    }

    private void OnNotificationReceived(object? sender, NotificationItem notification)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!_isAttached) return;
            RefreshUI();
        });
    }

    private void OnNotificationRemoved(object? sender, string notificationId)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!_isAttached) return;
            RefreshUI();
        });
    }

    private void OnNotificationClicked(object? sender, NotificationItem notification)
    {
    }

    private void OnMarkAsRead(object? sender, NotificationItem notification)
    {
        _notificationService?.MarkAsRead(notification.Id);
        RefreshUI();
    }

    private void OnClearButtonClick(object? sender, RoutedEventArgs e)
    {
        _notificationService?.ClearAll();
        RefreshUI();
        e.Handled = true;
    }

    private void UpdateStatusText()
    {
        var total = _notificationService?.GetNotifications().Count ?? 0;
        var max = _componentSettingsSnapshot.NotificationBoxMaxDisplayCount;
        StatusTextBlock.Text = $"共 {total} 条" + (total > max ? $"(显{max})" : "");
    }

    private void ClearSelection()
    {
        foreach (var control in _notificationControls)
        {
            control.IsSelected = false;
        }
    }

    private void UpdateAdaptiveLayout()
    {
        var scale = Math.Clamp(_currentCellSize / 48.0, 0.7, 1.8);
        var fontScale = Math.Clamp(scale, 0.8, 1.4);

        var cornerRadius = ResolveUnifiedMainRadiusValue();
        RootBorder.CornerRadius = new CornerRadius(cornerRadius);
        CardBorder.CornerRadius = new CornerRadius(cornerRadius);

        CardBorder.Background = new SolidColorBrush(_isNightVisual ? Color.Parse("#1B2129") : Color.Parse("#FCFCFD"));

        HeaderTextBlock.FontSize = 15 * fontScale;
        HeaderIcon.FontSize = 16 * fontScale;
        UnreadCountText.FontSize = 11 * fontScale;
        EmptyStateText.FontSize = 13 * fontScale;
        StatusTextBlock.FontSize = 11 * fontScale;

        var padding = Math.Clamp(12 * scale, 8, 20);
        var verticalPadding = Math.Clamp(10 * scale, 6, 16);
        CardBorder.Padding = new Thickness(padding, verticalPadding);

        foreach (var control in _notificationControls)
        {
            control.UpdateTheme(_isNightVisual, fontScale);
        }
    }

    private static double ResolveUnifiedMainRadiusValue() =>
        HostAppearanceThemeProvider.GetOrCreate().GetCurrent().CornerRadiusTokens.Lg.TopLeft;
}

public class NotificationItemControl : Border
{
    private readonly NotificationItem _item;
    private readonly ComponentSettingsSnapshot _settings;
    private bool _isPointerPressed;
    private Point _pointerPressedPosition;
    private bool _isNightVisual;

    public NotificationItemControl(NotificationItem item, ComponentSettingsSnapshot settings, bool isNightVisual)
    {
        _item = item;
        _settings = settings;
        _isNightVisual = isNightVisual;

        Background = _item.IsRead
            ? new SolidColorBrush(isNightVisual ? Color.Parse("#2D3440") : Color.Parse("#F5F5F5"))
            : new SolidColorBrush(isNightVisual ? Color.Parse("#3D4250") : Color.Parse("#FFFFFF"));
        CornerRadius = new CornerRadius(6);
        Padding = new Thickness(10, 6);
        Cursor = new Cursor(StandardCursorType.Hand);
        BorderBrush = _item.IsRead
            ? new SolidColorBrush(Colors.Transparent)
            : new SolidColorBrush(Color.Parse("#E24B2D"));
        BorderThickness = _item.IsRead ? new Thickness(0) : new Thickness(2, 0, 0, 0);

        BuildUI();

        PointerPressed += OnPointerPressed;
        PointerReleased += OnPointerReleased;
    }

    private void BuildUI()
    {
        var grid = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("Auto,*,Auto") };

        if (_settings.NotificationBoxShowAppIcon)
        {
            var iconBorder = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(_isNightVisual ? Color.Parse("#4D5560") : Color.Parse("#E8EAED")),
                Margin = new Thickness(0, 0, 8, 0)
            };
            var iconText = new TextBlock
            {
                Text = _item.AppName.Length > 0 ? _item.AppName[0].ToString() : "?",
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(_isNightVisual ? Color.Parse("#E8EAED") : Color.Parse("#202327"))
            };
            iconBorder.Child = iconText;
            grid.Children.Add(iconBorder);
        }

        var contentPanel = new StackPanel { Spacing = 1 };
        Grid.SetColumn(contentPanel, 1);

        var titleBlock = new TextBlock
        {
            Text = _item.Title,
            FontWeight = FontWeight.SemiBold,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
            Foreground = new SolidColorBrush(_isNightVisual ? Color.Parse("#E8EAED") : Color.Parse("#202327"))
        };

        var contentBlock = new TextBlock
        {
            Text = _item.Content,
            FontSize = 11,
            Foreground = new SolidColorBrush(_isNightVisual ? Color.Parse("#A8B1C2") : Color.Parse("#5E6671")),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 2,
            TextWrapping = TextWrapping.Wrap
        };

        contentPanel.Children.Add(titleBlock);
        if (!string.IsNullOrWhiteSpace(_item.Content))
        {
            contentPanel.Children.Add(contentBlock);
        }
        grid.Children.Add(contentPanel);

        if (_settings.NotificationBoxShowTimestamp)
        {
            var timeText = _settings.NotificationBoxTimeFormat == "Relative"
                ? GetRelativeTime(_item.ReceivedTime)
                : _item.ReceivedTime.ToString("HH:mm");

            var timeBlock = new TextBlock
            {
                Text = timeText,
                FontSize = 10,
                Foreground = new SolidColorBrush(_isNightVisual ? Color.Parse("#8B95A5") : Color.Parse("#8B95A5")),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                Margin = new Thickness(6, 0, 0, 0)
            };
            Grid.SetColumn(timeBlock, 2);
            grid.Children.Add(timeBlock);
        }

        Child = grid;
    }

    public void UpdateTheme(bool isNightVisual, double fontScale)
    {
        _isNightVisual = isNightVisual;
        Background = _item.IsRead
            ? new SolidColorBrush(isNightVisual ? Color.Parse("#2D3440") : Color.Parse("#F5F5F5"))
            : new SolidColorBrush(isNightVisual ? Color.Parse("#3D4250") : Color.Parse("#FFFFFF"));

        if (Child is Grid grid)
        {
            foreach (var child in grid.Children)
            {
                if (child is StackPanel panel)
                {
                    foreach (var textBlock in panel.Children.OfType<TextBlock>())
                    {
                        textBlock.FontSize *= fontScale;
                    }
                }
            }
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _isPointerPressed = true;
            _pointerPressedPosition = e.GetPosition(this);
            IsSelected = true;
            e.Handled = true;
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isPointerPressed) return;

        _isPointerPressed = false;
        var releasePosition = e.GetPosition(this);
        var distance = Math.Sqrt(
            Math.Pow(releasePosition.X - _pointerPressedPosition.X, 2) +
            Math.Pow(releasePosition.Y - _pointerPressedPosition.Y, 2));

        if (distance < 5)
        {
            Clicked?.Invoke(this, _item);
            MarkAsRead?.Invoke(this, _item);
        }

        e.Handled = true;
    }

    private static string GetRelativeTime(DateTime time)
    {
        var diff = DateTime.Now - time;

        if (diff.TotalMinutes < 1) return "刚刚";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}分前";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}小时前";
        return $"{(int)diff.TotalDays}天前";
    }

    public bool IsSelected { get; set; }

    public event EventHandler<NotificationItem>? Clicked;
    public event EventHandler<NotificationItem>? MarkAsRead;
}

public static class NotificationListenerServiceProvider
{
    private static NotificationListenerService? _instance;

    public static NotificationListenerService GetOrCreate(ISettingsService settingsService)
    {
        if (_instance == null)
        {
            _instance = new NotificationListenerService(settingsService);
            _instance.InitializeAsync().ConfigureAwait(false);
        }
        return _instance;
    }
}
