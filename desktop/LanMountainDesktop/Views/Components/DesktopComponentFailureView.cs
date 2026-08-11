using System;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

internal sealed class DesktopComponentFailureView : UserControl, IDesktopComponentWidget
{
    private readonly Border _rootBorder;
    private readonly TextBlock _titleBlock;
    private readonly TextBlock _summaryBlock;
    private readonly TextBlock _statusBlock;
    private readonly Button _toggleDetailsButton;
    private readonly Button _copyReportButton;
    private readonly Border _detailsBorder;
    private readonly TextBox _reportTextBox;
    private readonly string _componentId;
    private readonly string? _placementId;
    private readonly string _reportText;
    private bool _detailsVisible;

    public DesktopComponentFailureView(
        string componentName,
        string componentId,
        string? placementId,
        int? pageIndex,
        string action,
        Exception exception)
    {
        _componentId = componentId;
        _placementId = placementId;
        _reportText = BuildReport(componentName, componentId, placementId, pageIndex, action, exception);

        _titleBlock = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(componentName) ? "组件暂时不可用" : componentName,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };

        _summaryBlock = new TextBlock
        {
            Text = "该组件已临时停用，并由信息占位保留原位置。你可以展开详情或复制错误报告。",
            Foreground = CreateBrush("#FFD6DEE9"),
            TextWrapping = TextWrapping.Wrap
        };

        _statusBlock = new TextBlock
        {
            IsVisible = false,
            Foreground = CreateBrush("#FF93C5FD"),
            TextWrapping = TextWrapping.Wrap
        };

        _toggleDetailsButton = CreateButton("查看错误信息", OnToggleDetailsClick);
        _copyReportButton = CreateButton("复制错误报告", OnCopyReportClick);

        _reportTextBox = new TextBox
        {
            Text = _reportText,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 96,
            MaxHeight = 220,
            Background = CreateBrush("#CC0F172A"),
            Foreground = CreateBrush("#FFE2E8F0"),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8)
        };

        _detailsBorder = new Border
        {
            IsVisible = false,
            Background = CreateBrush("#660F172A"),
            BorderBrush = CreateBrush("#33475569"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Child = _reportTextBox
        };

        _rootBorder = new Border
        {
            Background = CreateBrush("#D91E293B"),
            BorderBrush = CreateBrush("#336B7280"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(14),
            ClipToBounds = true,
            Child = new StackPanel
            {
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    _titleBlock,
                    _summaryBlock,
                    new WrapPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        ItemSpacing = 8,
                        LineSpacing = 8,
                        Children =
                        {
                            _toggleDetailsButton,
                            _copyReportButton
                        }
                    },
                    _statusBlock,
                    _detailsBorder
                }
            }
        };

        Content = _rootBorder;
        ApplyCellSize(48);
    }

    public void ApplyCellSize(double cellSize)
    {
        var normalized = Math.Max(1, cellSize);
        _rootBorder.CornerRadius = new CornerRadius(Math.Clamp(normalized * 0.24, 12, 24));
        _rootBorder.Padding = new Thickness(Math.Clamp(normalized * 0.24, 10, 18));
        _titleBlock.FontSize = Math.Clamp(normalized * 0.36, 14, 22);
        _summaryBlock.FontSize = Math.Clamp(normalized * 0.24, 11, 15);
        _statusBlock.FontSize = Math.Clamp(normalized * 0.22, 10, 13);
        _toggleDetailsButton.FontSize = Math.Clamp(normalized * 0.22, 10, 14);
        _copyReportButton.FontSize = Math.Clamp(normalized * 0.22, 10, 14);
        _toggleDetailsButton.Padding = new Thickness(Math.Clamp(normalized * 0.18, 8, 12), 6);
        _copyReportButton.Padding = new Thickness(Math.Clamp(normalized * 0.18, 8, 12), 6);
        _reportTextBox.FontSize = Math.Clamp(normalized * 0.2, 10, 13);
        _reportTextBox.MaxHeight = Math.Clamp(normalized * 5.2, 120, 260);
    }

    private static Button CreateButton(string text, EventHandler<RoutedEventArgs> clickHandler)
    {
        var button = new Button
        {
            Content = text,
            Background = CreateBrush("#80334155"),
            Foreground = Brushes.White,
            BorderBrush = CreateBrush("#335B6575"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(999),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        button.Click += clickHandler;
        return button;
    }

    private void OnToggleDetailsClick(object? sender, RoutedEventArgs e)
    {
        _detailsVisible = !_detailsVisible;
        _detailsBorder.IsVisible = _detailsVisible;
        _toggleDetailsButton.Content = _detailsVisible ? "隐藏错误信息" : "查看错误信息";
        UpdateStatus(null);
    }

    private void OnCopyReportClick(object? sender, RoutedEventArgs e)
    {
        UiExceptionGuard.FireAndForgetGuarded(
            CopyReportAsync,
            "DesktopComponentFailureView.CopyReport",
            UiExceptionGuard.BuildContext(
                ("ComponentId", _componentId),
                ("PlacementId", _placementId)));
    }

    private async Task CopyReportAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        var clipboard = topLevel?.Clipboard;
        if (clipboard is null)
        {
            UpdateStatus("当前环境不支持复制错误报告。");
            return;
        }

        await clipboard.SetTextAsync(_reportText);
        UpdateStatus("错误报告已复制到剪贴板。");
    }

    private void UpdateStatus(string? message)
    {
        _statusBlock.Text = message ?? string.Empty;
        _statusBlock.IsVisible = !string.IsNullOrWhiteSpace(message);
    }

    private static string BuildReport(
        string componentName,
        string componentId,
        string? placementId,
        int? pageIndex,
        string action,
        Exception exception)
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
        var builder = new StringBuilder();
        builder.AppendLine("LanMountainDesktop Component Failure Report");
        builder.AppendLine($"GeneratedAt: {DateTimeOffset.Now:O}");
        builder.AppendLine($"AppVersion: {version}");
        builder.AppendLine($"Action: {action}");
        builder.AppendLine($"ComponentName: {componentName}");
        builder.AppendLine($"ComponentId: {componentId}");
        builder.AppendLine($"PlacementId: {placementId ?? string.Empty}");
        builder.AppendLine($"PageIndex: {pageIndex?.ToString() ?? string.Empty}");
        builder.AppendLine($"ExceptionType: {exception.GetType().FullName}");
        builder.AppendLine($"ExceptionMessage: {exception.Message}");
        builder.AppendLine();
        builder.AppendLine(exception.ToString());
        return builder.ToString();
    }

    private static IBrush CreateBrush(string colorHex)
    {
        return new SolidColorBrush(Color.Parse(colorHex));
    }
}
