using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using FluentIcons.Common;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Host.Abstractions;
using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services.Settings;

namespace LanMountainDesktop.Views.Components;

public partial class StickyNoteWidget : UserControl,
    IDesktopComponentWidget,
    IComponentPlacementContextAware,
    IComponentSettingsContextAware,
    IDesktopPageVisibilityAwareComponentWidget,
    IDisposable
{
    private static readonly Color LightNoteYellow = Color.FromRgb(0xFF, 0xF9, 0xC4);
    private static readonly Color LightNoteBorder = Color.FromRgb(0xE0, 0xC8, 0x78);
    private static readonly Color LightNoteForeground = Color.FromRgb(0x5D, 0x4E, 0x37);
    private static readonly Color LightNoteHint = Color.FromRgb(0x8B, 0x7D, 0x5A);

    private static readonly Color DarkNoteYellow = Color.FromRgb(0x5D, 0x52, 0x29);
    private static readonly Color DarkNoteBorder = Color.FromRgb(0x7A, 0x6D, 0x3A);
    private static readonly Color DarkNoteForeground = Color.FromRgb(0xE8, 0xE0, 0xC8);
    private static readonly Color DarkNoteHint = Color.FromRgb(0xA0, 0x96, 0x70);

    private string _componentId = BuiltInComponentIds.DesktopStickyNote;
    private string _placementId = string.Empty;
    private IComponentSettingsAccessor? _settingsAccessor;
    private string _markdownContent = string.Empty;
    private bool _isEditing;
    private bool _isDirty;
    private bool _isOnActivePage = true;
    private bool _isEditMode;
    private bool _disposed;
    private bool _isApplyingPersistedContent;

    private readonly DispatcherTimer _autoSaveTimer = new()
    {
        Interval = TimeSpan.FromSeconds(30)
    };

    private CancellationTokenSource? _renderDebounceCts;

    public StickyNoteWidget()
    {
        InitializeComponent();

        _autoSaveTimer.Tick += OnAutoSaveTimerTick;
        NoteTextBox.TextChanged += OnNoteTextBoxTextChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;

        ApplyNoteColors();
        UpdateDisplay();
    }

    public void ApplyCellSize(double cellSize)
    {
        var scale = Math.Clamp(cellSize / 48d, 0.82, 2.2);

        RootBorder.CornerRadius = new CornerRadius(
            ComponentChromeCornerRadiusHelper.ResolveMainRectangleRadiusValue(
                new ComponentChromeContext(
                    _componentId,
                    _placementId,
                    Math.Max(1, cellSize),
                    Appearance.AppearanceCornerRadiusTokenFactory.Create(
                        Settings.Core.GlobalAppearanceSettings.DefaultCornerRadiusStyle))));

        RootBorder.Padding = new Thickness(
            Math.Clamp(2 * scale, 1, 4),
            Math.Clamp(2 * scale, 1, 4));

        var contentMargin = Math.Clamp(12 * scale, 6, 20);
        MarkdownViewer.Margin = new Thickness(contentMargin, contentMargin, contentMargin, contentMargin - 2);
        NoteTextBox.Margin = new Thickness(contentMargin, contentMargin, contentMargin, contentMargin - 2);
        NoteTextBox.FontSize = Math.Clamp(13 * scale, 10, 22);

        var buttonSize = Math.Clamp(28 * scale, 22, 40);
        ToggleButton.Width = buttonSize;
        ToggleButton.Height = buttonSize;
        ToggleButton.CornerRadius = new CornerRadius(buttonSize / 2d);
        ToggleButton.Margin = new Thickness(Math.Clamp(4 * scale, 2, 8), Math.Clamp(4 * scale, 2, 8), Math.Clamp(4 * scale, 2, 8), 0);
        ToggleIcon.FontSize = Math.Clamp(13 * scale, 10, 18);
    }

    public void SetComponentPlacementContext(string componentId, string? placementId)
    {
        if (_isDirty && !string.IsNullOrWhiteSpace(_placementId))
        {
            PersistNoteImmediately();
        }

        _componentId = string.IsNullOrWhiteSpace(componentId)
            ? BuiltInComponentIds.DesktopStickyNote
            : componentId.Trim();
        _placementId = placementId?.Trim() ?? string.Empty;

        if (_isEditing)
        {
            ExitEditMode();
        }

        LoadPersistedContent();
    }

    public void SetComponentSettingsContext(DesktopComponentSettingsContext context)
    {
        _settingsAccessor = context.ComponentSettingsAccessor;
        LoadPersistedContent();
    }

    public void SetDesktopPageContext(bool isOnActivePage, bool isEditMode)
    {
        _isOnActivePage = isOnActivePage;
        _isEditMode = isEditMode;

        ToggleButton.IsHitTestVisible = !isEditMode;
        NoteTextBox.IsReadOnly = isEditMode;

        if (isEditMode && _isEditing)
        {
            ExitEditMode();
        }
    }

    private void OnToggleButtonClick(object? sender, RoutedEventArgs e)
    {
        if (_isEditing)
        {
            ExitEditMode();
        }
        else
        {
            EnterEditMode();
        }
    }

    private void EnterEditMode()
    {
        _isEditing = true;

        NoteTextBox.Text = _markdownContent;
        MarkdownViewer.IsVisible = false;
        NoteTextBox.IsVisible = true;
        ToggleIcon.Symbol = Symbol.Checkmark;

        Dispatcher.UIThread.Post(() => NoteTextBox.Focus(), DispatcherPriority.Input);
    }

    private void ExitEditMode()
    {
        _isEditing = false;

        var editedContent = NoteTextBox.Text ?? string.Empty;
        if (editedContent != _markdownContent)
        {
            _markdownContent = editedContent;
            _isDirty = true;
        }

        NoteTextBox.IsVisible = false;
        MarkdownViewer.IsVisible = true;
        ToggleIcon.Symbol = Symbol.Edit;

        UpdateDisplay();

        if (_isDirty)
        {
            PersistNoteImmediately();
        }
    }

    private void OnNoteTextBoxTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isApplyingPersistedContent || !_isEditing)
        {
            return;
        }

        _isDirty = true;

        if (!_autoSaveTimer.IsEnabled)
        {
            _autoSaveTimer.Start();
        }
    }

    private void OnAutoSaveTimerTick(object? sender, EventArgs e)
    {
        _autoSaveTimer.Stop();

        if (_isDirty && _isEditing)
        {
            _markdownContent = NoteTextBox.Text ?? string.Empty;
            PersistNoteImmediately();
        }
    }

    private void UpdateDisplay()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_markdownContent))
            {
                MarkdownViewer.Markdown = "*Click ✏️ to write a note...*";
                return;
            }

            _renderDebounceCts?.Cancel();
            _renderDebounceCts?.Dispose();
            _renderDebounceCts = new CancellationTokenSource();
            var token = _renderDebounceCts.Token;

            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    await Task.Delay(150, token);
                    if (!token.IsCancellationRequested)
                    {
                        MarkdownViewer.Markdown = _markdownContent;
                    }
                }
                catch (OperationCanceledException) { }
            });
        }
        catch (Exception ex)
        {
            MarkdownViewer.Markdown = $"*Error: {ex.Message}*";
        }
    }

    private void LoadPersistedContent()
    {
        if (_settingsAccessor is null)
        {
            return;
        }

        try
        {
            var snapshot = _settingsAccessor.LoadSnapshot<ComponentSettingsSnapshot>();
            _isApplyingPersistedContent = true;
            _markdownContent = snapshot.StickyNoteContent ?? string.Empty;
            _isDirty = false;
            UpdateDisplay();
        }
        catch
        {
            _markdownContent = string.Empty;
            UpdateDisplay();
        }
        finally
        {
            _isApplyingPersistedContent = false;
        }
    }

    private void PersistNoteImmediately()
    {
        if (_settingsAccessor is null || _disposed)
        {
            return;
        }

        try
        {
            var snapshot = _settingsAccessor.LoadSnapshot<ComponentSettingsSnapshot>();
            snapshot.StickyNoteContent = _markdownContent;
            _settingsAccessor.SaveSnapshot(snapshot,
                [nameof(ComponentSettingsSnapshot.StickyNoteContent)]);
            _isDirty = false;
        }
        catch
        {
        }
    }

    private void ApplyNoteColors()
    {
        var isDark = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;

        if (isDark)
        {
            RootBorder.Background = new SolidColorBrush(DarkNoteYellow);
            RootBorder.BorderBrush = new SolidColorBrush(DarkNoteBorder);
            NoteTextBox.Foreground = new SolidColorBrush(DarkNoteForeground);
            ToggleIcon.Foreground = new SolidColorBrush(DarkNoteHint);
        }
        else
        {
            RootBorder.Background = new SolidColorBrush(LightNoteYellow);
            RootBorder.BorderBrush = new SolidColorBrush(LightNoteBorder);
            NoteTextBox.Foreground = new SolidColorBrush(LightNoteForeground);
            ToggleIcon.Foreground = new SolidColorBrush(LightNoteHint);
        }
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        Application.Current!.ActualThemeVariantChanged += OnThemeVariantChanged;
        ApplyNoteColors();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        Application.Current!.ActualThemeVariantChanged -= OnThemeVariantChanged;

        if (_isDirty)
        {
            if (_isEditing)
            {
                _markdownContent = NoteTextBox.Text ?? string.Empty;
            }
            PersistNoteImmediately();
        }

        _autoSaveTimer.Stop();
    }

    private void OnThemeVariantChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(ApplyNoteColors);
    }

    public void ForceSave()
    {
        if (_isEditing)
        {
            _markdownContent = NoteTextBox.Text ?? string.Empty;
        }

        if (_isDirty || _isEditing)
        {
            PersistNoteImmediately();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _autoSaveTimer.Stop();
        _renderDebounceCts?.Cancel();
        _renderDebounceCts?.Dispose();

        if (_isDirty)
        {
            if (_isEditing)
            {
                _markdownContent = NoteTextBox.Text ?? string.Empty;
            }
            PersistNoteImmediately();
        }
    }
}
