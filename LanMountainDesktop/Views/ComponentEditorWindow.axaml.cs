using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using FluentIcons.Common;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Services;
using LanMountainDesktop.Theme;
using Material.Styles.Themes;
using Material.Styles.Themes.Base;

using Material.Icons;
using Material.Icons.Avalonia;

namespace LanMountainDesktop.Views;

public partial class ComponentEditorWindow : Window
{
    private readonly Dictionary<string, Size> _sizeCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly CustomMaterialTheme _materialTheme;
    private DesktopComponentEditorDescriptor? _descriptor;
    private string? _currentComponentId;
    private bool _suppressAspectRatioCorrection;
    private Size _lastStableSize;

    public ComponentEditorWindow()
    {
        InitializeComponent();
        _materialTheme = Styles.OfType<CustomMaterialTheme>().FirstOrDefault()
            ?? throw new InvalidOperationException("Component editor Material theme is missing.");
        _lastStableSize = new Size(Width, Height);
        ApplyChromeMode(useSystemChrome: false);
    }

    public void ApplyDescriptor(
        DesktopComponentEditorDescriptor descriptor,
        DesktopComponentEditorContext context)
    {
        _descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        _currentComponentId = context.ComponentId;

        var editor = descriptor.CreateEditor(context);
        EditorContentHost.Content = editor;
        TitleTextBlock.Text = descriptor.Definition.DisplayName;
        HeaderIcon.Kind = ResolveSymbol(descriptor.Definition.IconKey);
        Title = descriptor.Definition.DisplayName;

        ApplyPreferredSize(descriptor);
    }

    public void ApplyChromeMode(bool useSystemChrome)
    {
        var preferSystemChrome = useSystemChrome || OperatingSystem.IsMacOS();
        if (preferSystemChrome)
        {
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.PreferSystemChrome;
            ExtendClientAreaTitleBarHeightHint = -1;
            SystemDecorations = SystemDecorations.Full;
            CustomTitleBarHost.IsVisible = false;
            return;
        }

        SystemDecorations = SystemDecorations.BorderOnly;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome;
        ExtendClientAreaTitleBarHeightHint = 52;
        CustomTitleBarHost.IsVisible = true;
    }

    internal void ApplyTheme(ComponentEditorThemePalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);

        RequestedThemeVariant = palette.IsNightMode ? ThemeVariant.Dark : ThemeVariant.Light;
        _materialTheme.BaseTheme = palette.IsNightMode ? BaseThemeMode.Dark : BaseThemeMode.Light;
        _materialTheme.PrimaryColor = palette.PrimaryColor;
        _materialTheme.SecondaryColor = palette.SecondaryColor;

        SetBrushResource("EditorPrimaryBrush", palette.PrimaryColor);
        SetBrushResource("EditorOnPrimaryBrush", palette.IsNightMode ? Colors.Black : Colors.White);
        SetBrushResource("EditorSecondaryBrush", palette.SecondaryColor);
        SetBrushResource("EditorTertiaryBrush", palette.TertiaryColor);
        SetBrushResource("EditorWindowBackgroundBrush", palette.WindowBackgroundColor);
        SetBrushResource("EditorSurfaceBrush", palette.SurfaceColor);
        SetBrushResource("EditorSurfaceContainerBrush", palette.SurfaceContainerColor);
        SetBrushResource("EditorSurfaceContainerHighBrush", palette.SurfaceContainerHighColor);
        SetBrushResource("EditorSelectFieldBackgroundBrush", palette.SurfaceContainerHighColor);
        SetBrushResource(
            "EditorSelectFieldHoverBrush",
            ColorMath.Blend(palette.SurfaceContainerHighColor, palette.PrimaryColor, palette.IsNightMode ? 0.18 : 0.08));
        SetBrushResource(
            "EditorSelectFieldFocusBrush",
            ColorMath.Blend(palette.SurfaceContainerHighColor, palette.PrimaryColor, palette.IsNightMode ? 0.24 : 0.12));
        SetBrushResource("EditorSelectOutlineBrush", palette.OutlineColor);
        SetBrushResource(
            "EditorSelectOutlineStrongBrush",
            ColorMath.EnsureContrast(palette.PrimaryColor, palette.SurfaceContainerHighColor, 3.0));
        SetBrushResource(
            "EditorSelectMenuItemHoverBrush",
            ColorMath.Blend(palette.SurfaceContainerColor, palette.PrimaryColor, palette.IsNightMode ? 0.20 : 0.10));
        SetBrushResource(
            "EditorSelectMenuItemSelectedBrush",
            ColorMath.Blend(palette.SurfaceContainerColor, palette.PrimaryColor, palette.IsNightMode ? 0.30 : 0.16));
        SetBrushResource("EditorTopAppBarBackgroundBrush", palette.TopAppBarColor);
        SetBrushResource("EditorHeaderIconBackgroundBrush", palette.HeaderIconBackgroundColor);
        SetBrushResource("EditorTitleBarButtonHoverBrush", palette.TitleBarButtonHoverColor);
        SetBrushResource("ComponentEditorHeroBackgroundBrush", palette.SurfaceContainerHighColor);
        SetBrushResource("ComponentEditorCardBackgroundBrush", palette.SurfaceContainerColor);
        SetBrushResource("ComponentEditorCardBorderBrush", palette.OutlineColor);
        SetBrushResource("ComponentEditorPrimaryTextBrush", palette.OnSurfaceColor);
        SetBrushResource("ComponentEditorSecondaryTextBrush", palette.OnSurfaceVariantColor);
        SetBrushResource("EditorDividerBrush", palette.DividerColor);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);

        if (_descriptor is null || _suppressAspectRatioCorrection)
        {
            _lastStableSize = e.NewSize;
            return;
        }

        var correctedSize = CoerceSize(e.NewSize, e.PreviousSize, _descriptor);
        if (Math.Abs(correctedSize.Width - e.NewSize.Width) < 0.5 &&
            Math.Abs(correctedSize.Height - e.NewSize.Height) < 0.5)
        {
            _lastStableSize = correctedSize;
            CacheCurrentSize();
            return;
        }

        _suppressAspectRatioCorrection = true;
        Width = correctedSize.Width;
        Height = correctedSize.Height;
        _suppressAspectRatioCorrection = false;
        _lastStableSize = correctedSize;
        CacheCurrentSize();
    }

    private void SetBrushResource(string key, Color color)
    {
        Resources[key] = new SolidColorBrush(color);
    }

    private void ApplyPreferredSize(DesktopComponentEditorDescriptor descriptor)
    {
        var width = descriptor.PreferredWidth;
        var height = descriptor.PreferredHeight;

        if (!string.IsNullOrWhiteSpace(_currentComponentId) &&
            _sizeCache.TryGetValue(_currentComponentId, out var cached))
        {
            width = cached.Width;
            height = cached.Height;
        }

        _suppressAspectRatioCorrection = true;
        MinWidth = descriptor.PreferredWidth * descriptor.MinScale;
        MinHeight = descriptor.PreferredHeight * descriptor.MinScale;
        MaxWidth = descriptor.PreferredWidth * descriptor.MaxScale;
        MaxHeight = descriptor.PreferredHeight * descriptor.MaxScale;
        Width = width;
        Height = height;
        _lastStableSize = new Size(width, height);
        _suppressAspectRatioCorrection = false;
    }

    private void CacheCurrentSize()
    {
        if (_descriptor is null || string.IsNullOrWhiteSpace(_currentComponentId))
        {
            return;
        }

        _sizeCache[_currentComponentId] = _lastStableSize;
    }

    private static Size CoerceSize(Size currentSize, Size previousSize, DesktopComponentEditorDescriptor descriptor)
    {
        var preferredWidth = descriptor.PreferredWidth;
        var preferredHeight = descriptor.PreferredHeight;
        var aspectRatio = descriptor.AspectRatio;
        var minWidth = preferredWidth * descriptor.MinScale;
        var maxWidth = preferredWidth * descriptor.MaxScale;
        var minHeight = preferredHeight * descriptor.MinScale;
        var maxHeight = preferredHeight * descriptor.MaxScale;

        var deltaWidth = Math.Abs(currentSize.Width - previousSize.Width);
        var deltaHeight = Math.Abs(currentSize.Height - previousSize.Height);

        double width;
        double height;
        if (deltaWidth >= deltaHeight)
        {
            width = Math.Clamp(currentSize.Width, minWidth, maxWidth);
            height = Math.Clamp(width / aspectRatio, minHeight, maxHeight);
            width = Math.Clamp(height * aspectRatio, minWidth, maxWidth);
        }
        else
        {
            height = Math.Clamp(currentSize.Height, minHeight, maxHeight);
            width = Math.Clamp(height * aspectRatio, minWidth, maxWidth);
            height = Math.Clamp(width / aspectRatio, minHeight, maxHeight);
        }

        return new Size(width, height);
    }

    private void OnWindowTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _ = sender;
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private static MaterialIconKind ResolveSymbol(string iconKey)
    {
        return iconKey switch
        {
            "Clock" => MaterialIconKind.Clock,
            "Timer" => MaterialIconKind.Timer,
            "WeatherSunny" => MaterialIconKind.WeatherSunny,
            "CalendarDate" => MaterialIconKind.CalendarRange,
            "CalendarMonth" => MaterialIconKind.CalendarMonth,
            "MicOn" => MaterialIconKind.Microphone,
            "News" => MaterialIconKind.Newspaper,
            "Image" => MaterialIconKind.Image,
            "Book" => MaterialIconKind.BookOpenVariant,
            "History" => MaterialIconKind.History,
            "DataLine" => MaterialIconKind.ChartLine,
            "Edit" => MaterialIconKind.Pencil,
            "Calculator" => MaterialIconKind.Calculator,
            "Storage" => MaterialIconKind.UsbFlashDrive,
            "Globe" => MaterialIconKind.Web,
            "Play" => MaterialIconKind.Play,
            _ => MaterialIconKind.Settings
        };
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        Close();
    }
}
