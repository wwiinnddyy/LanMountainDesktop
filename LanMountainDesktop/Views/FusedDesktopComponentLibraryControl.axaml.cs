using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views;

/// <summary>
/// 融合桌面组件库控件 - 专门用于添加组件到系统桌面（负一屏）
/// </summary>
public partial class FusedDesktopComponentLibraryControl : UserControl
{
    /// <summary>
    /// 添加组件到融合桌面事件
    /// </summary>
    public event EventHandler<string>? AddComponentRequested;
    
    public FusedDesktopComponentLibraryControl()
    {
        InitializeComponent();
        LoadComponents();
    }
    
    /// <summary>
    /// 加载可用组件列表
    /// </summary>
    private void LoadComponents()
    {
        var registry = ComponentRegistry.CreateDefault();
        
        foreach (var definition in registry.GetAll())
        {
            if (!definition.AllowDesktopPlacement)
            {
                continue;
            }
            
            var button = new Button
            {
                Width = 100,
                Height = 100,
                Margin = new Thickness(4),
                Padding = new Thickness(8),
                CornerRadius = new CornerRadius(12),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Tag = definition.Id
            };
            
            var textBlock = new TextBlock
            {
                Text = definition.DisplayName,
                FontSize = 11,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 2,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            
            button.Content = textBlock;
            button.Click += OnAddComponentClick;
            
            ComponentPanel.Children.Add(button);
        }
    }
    
    /// <summary>
    /// 添加组件按钮点击
    /// </summary>
    private void OnAddComponentClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string componentId)
        {
            AddComponentRequested?.Invoke(this, componentId);
        }
    }
}
