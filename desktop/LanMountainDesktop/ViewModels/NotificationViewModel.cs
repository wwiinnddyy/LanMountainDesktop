using System;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.ViewModels;

public partial class NotificationViewModel : ViewModelBase
{
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string? _message;
    [ObservableProperty] private Bitmap? _icon;
    [ObservableProperty] private NotificationSeverity _severity;
    [ObservableProperty] private NotificationPosition _position;
    [ObservableProperty] private bool _isClosing;
    
    public TimeSpan Duration { get; set; } = TimeSpan.FromSeconds(4);
    public Action? OnClick { get; set; }
    public Guid Id { get; } = Guid.NewGuid();

    public string SeverityIcon =>
        Severity switch
        {
            NotificationSeverity.Success => "CheckmarkCircle",
            NotificationSeverity.Warning => "Warning",
            NotificationSeverity.Error => "DismissCircle",
            _ => "Info"
        };

    public string SeverityColorResource =>
        Severity switch
        {
            NotificationSeverity.Success => "SystemFillColorSuccessBrush",
            NotificationSeverity.Warning => "SystemFillColorCautionBrush",
            NotificationSeverity.Error => "SystemFillColorCriticalBrush",
            _ => "SystemFillColorAttentionBrush"
        };
}
