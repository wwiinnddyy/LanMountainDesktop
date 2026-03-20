using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using FluentIcons.Avalonia;
using FluentIcons.Common;
using LanMountainDesktop.DesktopComponents.Runtime;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;
using LanMountainDesktop.Theme;

namespace LanMountainDesktop.Views.Components;

public partial class StudySessionHistoryWidget : UserControl, IDesktopComponentWidget, IDesktopPageVisibilityAwareComponentWidget, IDisposable
{
    private const double MinTextContrast = 4.5;
    private enum HistoryDialogMode
    {
        None = 0,
        Rename = 1,
        Delete = 2
    }

    private static readonly Color[] PrimaryColorCandidates =
    {
        Color.Parse("#FFF8FAFC"),
        Color.Parse("#FFEAF3FF"),
        Color.Parse("#FF101C2A"),
        Color.Parse("#FF1B2E45"),
        Color.Parse("#FFFFFFFF")
    };

    private static readonly Color[] SecondaryColorCandidates =
    {
        Color.Parse("#FFDDE7F3"),
        Color.Parse("#FFCBD9EA"),
        Color.Parse("#FF24384F"),
        Color.Parse("#FF2F4763"),
        Color.Parse("#FF0F1D2D")
    };

    private static readonly Color DarkSubstrate = Color.Parse("#FF0B1220");
    private static readonly Color LightSubstrate = Color.Parse("#FFF1F5FA");

    private readonly IStudyAnalyticsService _studyAnalyticsService = StudyAnalyticsServiceFactory.CreateDefault();
    private LanMountainDesktop.PluginSdk.ISettingsService _settingsService = LanMountainDesktop.Services.Settings.HostSettingsFacadeProvider.GetOrCreate().Settings;
    private readonly LocalizationService _localizationService = new();

    private double _currentCellSize = 48;
    private string _languageCode = "zh-CN";
    private bool _isAttached;
    private bool _isOnActivePage = true;
    private bool _isSubscribed;
    private bool _isDisposed;
    private bool _isCompactMode;
    private bool _isUltraCompactMode;
    private string? _loadingSessionId;
    private HistoryDialogMode _dialogMode;
    private string? _dialogSessionId;
    private string _dialogSessionLabel = string.Empty;
    private StudyAnalyticsSnapshot? _currentSnapshot;
    private string? _transientStatus;
    private DateTimeOffset _transientStatusExpireAt;

    public StudySessionHistoryWidget()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        SizeChanged += OnSizeChanged;
        ActualThemeVariantChanged += OnActualThemeVariantChanged;
        DialogCancelButton.Click += (_, _) => CloseDialog();
        DialogConfirmButton.Click += (_, _) => ConfirmDialog();
        DialogRenameTextBox.KeyDown += OnDialogRenameTextBoxKeyDown;

        ReloadLanguageCode();
        ApplyCellSize(_currentCellSize);
    }

    public void ApplyCellSize(double cellSize)
    {
        _currentCellSize = Math.Max(1, cellSize);
        UpdateAdaptiveLayout();
        if (_currentSnapshot is not null)
        {
            RenderSnapshot(_currentSnapshot);
        }
    }

    public void SetDesktopPageContext(bool isOnActivePage, bool isEditMode)
    {
        _ = isEditMode;
        _isOnActivePage = isOnActivePage;
        if (_isAttached && _isOnActivePage)
        {
            RefreshFromService();
        }
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = true;
        ReloadLanguageCode();

        if (!_isSubscribed)
        {
            _studyAnalyticsService.SnapshotUpdated += OnStudySnapshotUpdated;
            _isSubscribed = true;
        }

        RefreshFromService();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        if (_isSubscribed)
        {
            _studyAnalyticsService.SnapshotUpdated -= OnStudySnapshotUpdated;
            _isSubscribed = false;
        }
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateAdaptiveLayout();
        if (_currentSnapshot is not null)
        {
            RenderSnapshot(_currentSnapshot);
        }
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        if (_currentSnapshot is not null)
        {
            RenderSnapshot(_currentSnapshot);
        }
    }

    private void OnStudySnapshotUpdated(object? sender, StudyAnalyticsSnapshotChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_isAttached)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(_loadingSessionId) &&
                string.Equals(e.Snapshot.SelectedSessionReportId, _loadingSessionId, StringComparison.OrdinalIgnoreCase))
            {
                _loadingSessionId = null;
                SetTransientStatus(L("study.session_history.loaded", "Data loaded"), 1.5);
            }

            _currentSnapshot = e.Snapshot;
            if (_isOnActivePage)
            {
                RenderSnapshot(e.Snapshot);
            }
        }, DispatcherPriority.Background);
    }

    private void RefreshFromService()
    {
        _currentSnapshot = _studyAnalyticsService.GetSnapshot();
        RenderSnapshot(_currentSnapshot);
    }

    private void RenderSnapshot(StudyAnalyticsSnapshot snapshot)
    {
        var panelColor = ResolvePanelBackgroundColor();
        var panelSamples = BuildPanelBackgroundSamples(panelColor);
        TitleTextBlock.Text = L("study.session_history.title", "Session History");
        TitleTextBlock.Foreground = CreateAdaptiveBrush(panelSamples, PrimaryColorCandidates, MinTextContrast);

        if (_transientStatus is not null && DateTimeOffset.UtcNow > _transientStatusExpireAt)
        {
            _transientStatus = null;
        }

        SessionListPanel.Children.Clear();
        var history = snapshot.SessionHistory;
        if (history.Count == 0)
        {
            if (_dialogMode != HistoryDialogMode.None)
            {
                CloseDialog();
            }

            StatusTextBlock.Text = _transientStatus ?? L("study.session_history.empty", "No session history");
            StatusTextBlock.Foreground = CreateAdaptiveBrush(panelSamples, SecondaryColorCandidates, MinTextContrast);
            ApplyHistoryTypographyLayout();
            UpdateDialogVisual(snapshot, panelColor);
            return;
        }

        if (!string.IsNullOrWhiteSpace(_dialogSessionId))
        {
            var dialogEntry = FindHistoryEntry(history, _dialogSessionId);
            if (dialogEntry is null)
            {
                CloseDialog();
            }
            else
            {
                _dialogSessionLabel = dialogEntry.Label;
            }
        }

        foreach (var entry in history)
        {
            SessionListPanel.Children.Add(CreateSessionRow(entry, snapshot.SelectedSessionReportId, panelColor));
        }

        StatusTextBlock.Text = _transientStatus ?? string.Empty;
        StatusTextBlock.Foreground = CreateAdaptiveBrush(panelSamples, SecondaryColorCandidates, MinTextContrast);
        ApplyHistoryTypographyLayout();
        UpdateDialogVisual(snapshot, panelColor);
    }

    private Control CreateSessionRow(StudySessionHistoryEntry entry, string? selectedSessionId, Color panelColor)
    {
        var isSelected = string.Equals(selectedSessionId, entry.SessionId, StringComparison.OrdinalIgnoreCase);
        var isLoading = string.Equals(_loadingSessionId, entry.SessionId, StringComparison.OrdinalIgnoreCase);
        var isDialogOpen = _dialogMode != HistoryDialogMode.None;

        var rowBackground = isSelected
            ? Color.Parse("#4A5FA9FF")
            : Color.Parse("#2CFFFFFF");
        var rowBorderColor = isSelected
            ? Color.Parse("#99C7E0FF")
            : Color.Parse("#33FFFFFF");

        var rowBorder = new Border
        {
            CornerRadius = ComponentChromeCornerRadiusHelper.Scale(_currentCellSize * 0.20, 8, 14),
            Background = new SolidColorBrush(rowBackground),
            BorderBrush = new SolidColorBrush(rowBorderColor),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(
                ComponentChromeCornerRadiusHelper.SafeValue(8, 6, 12),
                ComponentChromeCornerRadiusHelper.SafeValue(6, 4, 10))
        };

        var panelComposite = ToOpaqueAgainst(panelColor, DarkSubstrate);
        var rowComposite = ToOpaqueAgainst(rowBackground, panelComposite);
        var rowPrimaryBrush = CreateAdaptiveBrush(new[] { rowComposite }, PrimaryColorCandidates, MinTextContrast);
        var rowSecondaryBrush = CreateAdaptiveBrush(new[] { rowComposite }, SecondaryColorCandidates, MinTextContrast);

        var rowGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"),
            ColumnSpacing = _isUltraCompactMode ? 4 : 6
        };

        var textStack = new StackPanel
        {
            Spacing = _isUltraCompactMode ? 0 : 2
        };
        var rowTitleTextBlock = new TextBlock
        {
            Text = entry.Label,
            Foreground = rowPrimaryBrush
        };
        ApplyTextLayout(
            rowTitleTextBlock,
            ComponentTypographyLayoutService.FitAdaptiveTextLayout(
                entry.Label,
                ResolveHistoryContentWidth(),
                _isUltraCompactMode ? 18 : 22,
                1,
                1,
                10,
                17,
                new[] { FontWeight.SemiBold, FontWeight.Medium },
                1.08d),
            TextWrapping.NoWrap);
        textStack.Children.Add(rowTitleTextBlock);

        if (!_isUltraCompactMode)
        {
            var metaText = isLoading
                ? L("study.session_history.loading", "Loading data...")
                : string.Format(
                    CultureInfo.InvariantCulture,
                    L("study.session_history.meta_format", "{0} · Avg {1:F1}"),
                    FormatDuration(entry.Duration),
                    entry.AverageScore);

            var metaTextBlock = new TextBlock
            {
                Text = metaText,
                Foreground = rowSecondaryBrush
            };
            ApplyTextLayout(
                metaTextBlock,
                ComponentTypographyLayoutService.FitAdaptiveTextLayout(
                    metaText,
                    ResolveHistoryContentWidth(),
                    _isUltraCompactMode ? 16 : 20,
                    1,
                    1,
                    9,
                    14,
                    new[] { FontWeight.Normal, FontWeight.Medium },
                    1.08d),
                TextWrapping.NoWrap);
            textStack.Children.Add(metaTextBlock);
        }
        rowGrid.Children.Add(textStack);

        var playButton = CreateActionIconButton(
            L("study.session_history.action.view", "View"),
            Symbol.Play,
            isLoading || isDialogOpen,
            isSelected ? Color.Parse("#5A7FDEFF") : Color.Parse("#3D649FFF"),
            rowComposite,
            () => SelectReport(entry.SessionId),
            IconVariant.Filled);
        Grid.SetColumn(playButton, 1);
        rowGrid.Children.Add(playButton);

        var renameButton = CreateActionIconButton(
            L("study.session_history.action.rename", "Rename"),
            Symbol.Edit,
            isLoading || isDialogOpen,
            Color.Parse("#2BFFFFFF"),
            rowComposite,
            () => ShowRenameDialog(entry.SessionId, entry.Label));
        Grid.SetColumn(renameButton, 2);
        rowGrid.Children.Add(renameButton);

        var deleteButton = CreateActionIconButton(
            L("study.session_history.action.delete", "Delete"),
            Symbol.Delete,
            isLoading || isDialogOpen,
            Color.Parse("#5AC74E58"),
            rowComposite,
            () => ShowDeleteDialog(entry.SessionId, entry.Label));
        Grid.SetColumn(deleteButton, 3);
        rowGrid.Children.Add(deleteButton);

        rowBorder.Child = rowGrid;
        return rowBorder;
    }

    private Button CreateActionIconButton(
        string tooltip,
        Symbol symbol,
        bool isDisabled,
        Color buttonBackground,
        Color rowComposite,
        Action onClick,
        IconVariant iconVariant = IconVariant.Regular)
    {
        var buttonComposite = ToOpaqueAgainst(buttonBackground, rowComposite);
        var iconBrush = CreateAdaptiveBrush(new[] { buttonComposite }, PrimaryColorCandidates, MinTextContrast);

        var iconSize = Math.Clamp(13 * (_isCompactMode ? 0.92 : 1.0), 11, 17);
        var icon = new SymbolIcon
        {
            Symbol = symbol,
            IconVariant = iconVariant,
            FontSize = iconSize,
            Width = iconSize,
            Height = iconSize,
            Foreground = iconBrush,
            IsHitTestVisible = false
        };

        var button = new Button
        {
            MinWidth = _isUltraCompactMode ? 26 : 34,
            Width = _isUltraCompactMode ? 26 : 34,
            Height = Math.Clamp(26 * (_isCompactMode ? 0.90 : 1.0), 24, 30),
            Padding = new Thickness(0),
            CornerRadius = ComponentChromeCornerRadiusHelper.Scale(10, 8, 12),
            Background = new SolidColorBrush(buttonBackground),
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Content = icon,
            IsEnabled = !isDisabled
        };
        button.Classes.Add("study-history-action-button");
        ToolTip.SetTip(button, tooltip);
        button.Click += (_, _) => onClick();
        return button;
    }

    private void SelectReport(string sessionId)
    {
        CloseDialog();

        _loadingSessionId = sessionId;
        SetTransientStatus(L("study.session_history.loading", "Loading data..."), 4);
        if (_currentSnapshot is not null)
        {
            RenderSnapshot(_currentSnapshot);
        }

        if (_studyAnalyticsService.SelectSessionReport(sessionId))
        {
            return;
        }

        _loadingSessionId = null;
        SetTransientStatus(L("study.session_history.select_failed", "Unable to switch session"));
        if (_currentSnapshot is not null)
        {
            RenderSnapshot(_currentSnapshot);
        }
    }

    private void ShowRenameDialog(string sessionId, string label)
    {
        _dialogMode = HistoryDialogMode.Rename;
        _dialogSessionId = sessionId;
        _dialogSessionLabel = label;
        DialogRenameTextBox.Text = label;
        if (_currentSnapshot is not null)
        {
            RenderSnapshot(_currentSnapshot);
        }
    }

    private void ShowDeleteDialog(string sessionId, string label)
    {
        _dialogMode = HistoryDialogMode.Delete;
        _dialogSessionId = sessionId;
        _dialogSessionLabel = label;
        if (_currentSnapshot is not null)
        {
            RenderSnapshot(_currentSnapshot);
        }
    }

    private void ConfirmDialog()
    {
        if (string.IsNullOrWhiteSpace(_dialogSessionId))
        {
            CloseDialog();
            return;
        }

        if (_dialogMode == HistoryDialogMode.Rename)
        {
            ConfirmRename(_dialogSessionId);
            return;
        }

        if (_dialogMode == HistoryDialogMode.Delete)
        {
            ConfirmDelete(_dialogSessionId);
            return;
        }

        CloseDialog();
    }

    private void ConfirmRename(string sessionId)
    {
        var nextLabel = (DialogRenameTextBox.Text ?? string.Empty).Trim();
        if (!_studyAnalyticsService.RenameSessionReport(sessionId, nextLabel))
        {
            SetTransientStatus(L("study.session_history.rename_failed", "Unable to rename session"));
        }
        else
        {
            SetTransientStatus(L("study.session_history.loaded", "Data loaded"), 1.2);
        }

        CloseDialog();
    }

    private void ConfirmDelete(string sessionId)
    {
        if (!_studyAnalyticsService.DeleteSessionReport(sessionId))
        {
            SetTransientStatus(L("study.session_history.delete_failed", "Unable to delete session"));
        }
        else
        {
            SetTransientStatus(L("study.session_history.loaded", "Data loaded"), 1.2);
        }

        CloseDialog();
    }

    private void CloseDialog()
    {
        _dialogMode = HistoryDialogMode.None;
        _dialogSessionId = null;
        _dialogSessionLabel = string.Empty;
        DialogRenameTextBox.Text = string.Empty;
    }

    private void OnDialogRenameTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (_dialogMode != HistoryDialogMode.Rename)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            ConfirmDialog();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseDialog();
            e.Handled = true;
        }
    }

    private void UpdateDialogVisual(StudyAnalyticsSnapshot snapshot, Color panelColor)
    {
        var isVisible = _dialogMode != HistoryDialogMode.None && !string.IsNullOrWhiteSpace(_dialogSessionId);
        DialogOverlayBorder.IsVisible = isVisible;
        if (!isVisible)
        {
            return;
        }

        var dialogBackground = Color.Parse("#D92A3E5D");
        var dialogComposite = ToOpaqueAgainst(dialogBackground, ToOpaqueAgainst(panelColor, DarkSubstrate));
        DialogCardBorder.Background = new SolidColorBrush(dialogBackground);
        DialogCardBorder.BorderBrush = new SolidColorBrush(Color.Parse("#66FFFFFF"));
        DialogTitleTextBlock.Foreground = CreateAdaptiveBrush(new[] { dialogComposite }, PrimaryColorCandidates, MinTextContrast);
        DialogMessageTextBlock.Foreground = CreateAdaptiveBrush(new[] { dialogComposite }, SecondaryColorCandidates, MinTextContrast);
        DialogRenameTextBox.Foreground = CreateAdaptiveBrush(new[] { dialogComposite }, PrimaryColorCandidates, MinTextContrast);
        DialogRenameTextBox.Background = new SolidColorBrush(Color.Parse("#24FFFFFF"));
        DialogRenameTextBox.BorderBrush = new SolidColorBrush(Color.Parse("#52FFFFFF"));

        var cancelBackground = Color.Parse("#33FFFFFF");
        var confirmBackground = _dialogMode == HistoryDialogMode.Delete
            ? Color.Parse("#B7504D")
            : Color.Parse("#4A73CC");

        DialogCancelButton.Background = new SolidColorBrush(cancelBackground);
        DialogCancelButton.BorderBrush = Brushes.Transparent;
        DialogCancelButton.BorderThickness = new Thickness(0);
        DialogCancelButton.Foreground = CreateAdaptiveBrush(
            new[] { ToOpaqueAgainst(cancelBackground, dialogComposite) },
            PrimaryColorCandidates,
            MinTextContrast);

        DialogConfirmButton.Background = new SolidColorBrush(confirmBackground);
        DialogConfirmButton.BorderBrush = Brushes.Transparent;
        DialogConfirmButton.BorderThickness = new Thickness(0);
        DialogConfirmButton.Foreground = CreateAdaptiveBrush(
            new[] { ToOpaqueAgainst(confirmBackground, dialogComposite) },
            PrimaryColorCandidates,
            MinTextContrast);

        var entry = FindHistoryEntry(snapshot.SessionHistory, _dialogSessionId);
        var label = entry?.Label ?? _dialogSessionLabel;
        if (_dialogMode == HistoryDialogMode.Rename)
        {
            DialogTitleTextBlock.Text = L("study.session_history.dialog.rename_title", "Rename Session");
            DialogMessageTextBlock.Text = string.Format(
                CultureInfo.InvariantCulture,
                L("study.session_history.dialog.rename_message", "Set a new name for \"{0}\"."),
                label);
            DialogRenameTextBox.Watermark = L("study.session_history.rename_placeholder", "Enter session name");
            if (string.IsNullOrWhiteSpace(DialogRenameTextBox.Text))
            {
                DialogRenameTextBox.Text = label;
            }

            DialogRenameTextBox.IsVisible = true;
            DialogConfirmButton.Content = L("study.session_history.rename_confirm", "Confirm rename");
            DialogCancelButton.Content = L("study.session_history.rename_cancel", "Cancel rename");
        }
        else
        {
            DialogTitleTextBlock.Text = L("study.session_history.dialog.delete_title", "Delete Session");
            DialogMessageTextBlock.Text = string.Format(
                CultureInfo.InvariantCulture,
                L("study.session_history.dialog.delete_message", "Delete \"{0}\"? This cannot be undone."),
                label);
            DialogRenameTextBox.IsVisible = false;
            DialogConfirmButton.Content = L("study.session_history.dialog.delete_confirm", "Delete");
            DialogCancelButton.Content = L("study.session_history.rename_cancel", "Cancel rename");
        }

        ApplyHistoryTypographyLayout();
    }

    private void SetTransientStatus(string status, double seconds = 2.2)
    {
        _transientStatus = status;
        _transientStatusExpireAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(0.6, seconds));
    }

    private void ReloadLanguageCode()
    {
        var snapshot = _settingsService.Load();
        _languageCode = _localizationService.NormalizeLanguageCode(snapshot.LanguageCode);
    }

    private void UpdateAdaptiveLayout()
    {
        var cellScale = Math.Clamp(_currentCellSize / 48d, 0.76, 2.2);
        var widthScale = Bounds.Width > 1 ? Bounds.Width / 360d : cellScale;
        var heightScale = Bounds.Height > 1 ? Bounds.Height / 180d : cellScale;
        var scale = Math.Clamp(Math.Min(cellScale, Math.Min(widthScale, heightScale) * 1.05), 0.68, 2.2);

        _isCompactMode = scale < 0.92 || (Bounds.Width > 1 && Bounds.Width < 320) || (Bounds.Height > 1 && Bounds.Height < 145);
        _isUltraCompactMode = scale < 0.78 || (Bounds.Width > 1 && Bounds.Width < 280) || (Bounds.Height > 1 && Bounds.Height < 120);

        RootBorder.CornerRadius = ComponentChromeCornerRadiusHelper.Scale(_currentCellSize * 0.44, 12, 36);
        RootBorder.Padding = new Thickness(
            ComponentChromeCornerRadiusHelper.SafeValue(12 * scale, 7, 22),
            ComponentChromeCornerRadiusHelper.SafeValue(9 * scale, 5, 16));

        ContentRootGrid.RowSpacing = _isUltraCompactMode
            ? Math.Clamp(4 * scale, 2, 6)
            : Math.Clamp(7 * scale, 4, 10);

        SessionListPanel.Spacing = _isUltraCompactMode
            ? Math.Clamp(4 * scale, 2, 5)
            : Math.Clamp(6 * scale, 3, 8);

        DialogOverlayBorder.Padding = new Thickness(
            ComponentChromeCornerRadiusHelper.SafeValue(12 * scale, 8, 20),
            ComponentChromeCornerRadiusHelper.SafeValue(10 * scale, 8, 18));
        DialogCardBorder.CornerRadius = ComponentChromeCornerRadiusHelper.Scale(12 * scale, 10, 18);
        DialogCardBorder.Padding = new Thickness(
            ComponentChromeCornerRadiusHelper.SafeValue(12 * scale, 9, 20),
            ComponentChromeCornerRadiusHelper.SafeValue(11 * scale, 8, 18));
        DialogCancelButton.FontSize = Math.Clamp(11 * scale, 10, 16);
        DialogConfirmButton.FontSize = Math.Clamp(11 * scale, 10, 16);
        DialogCancelButton.Height = Math.Clamp(30 * scale, 26, 38);
        DialogConfirmButton.Height = Math.Clamp(30 * scale, 26, 38);

        ApplyHistoryTypographyLayout();
    }

    private void ApplyHistoryTypographyLayout()
    {
        var contentWidth = ResolveHistoryContentWidth();

        var titleLayout = ComponentTypographyLayoutService.FitAdaptiveTextLayout(
            TitleTextBlock.Text,
            contentWidth,
            _isUltraCompactMode ? 18 : 24,
            1,
            1,
            10,
            22,
            new[] { FontWeight.SemiBold, FontWeight.Medium },
            1.08d);
        ApplyTextLayout(TitleTextBlock, titleLayout, TextWrapping.NoWrap);

        var statusLayout = ComponentTypographyLayoutService.FitAdaptiveTextLayout(
            StatusTextBlock.Text,
            contentWidth,
            _isUltraCompactMode ? 18 : 28,
            1,
            _isUltraCompactMode ? 1 : 2,
            9,
            18,
            new[] { FontWeight.Normal, FontWeight.Medium },
            1.10d);
        ApplyTextLayout(StatusTextBlock, statusLayout, statusLayout.MaxLines > 1 ? TextWrapping.Wrap : TextWrapping.NoWrap);

        if (!DialogOverlayBorder.IsVisible)
        {
            return;
        }

        var dialogWidth = DialogCardBorder.Bounds.Width > 1
            ? Math.Max(1, DialogCardBorder.Bounds.Width - DialogCardBorder.Padding.Left - DialogCardBorder.Padding.Right)
            : Math.Max(180, contentWidth);

        var dialogTitleLayout = ComponentTypographyLayoutService.FitAdaptiveTextLayout(
            DialogTitleTextBlock.Text,
            dialogWidth,
            _isUltraCompactMode ? 18 : 24,
            1,
            1,
            11,
            20,
            new[] { FontWeight.SemiBold, FontWeight.Medium },
            1.08d);
        ApplyTextLayout(DialogTitleTextBlock, dialogTitleLayout, TextWrapping.NoWrap);

        var dialogMessageLayout = ComponentTypographyLayoutService.FitAdaptiveTextLayout(
            DialogMessageTextBlock.Text,
            dialogWidth,
            _isUltraCompactMode ? 42 : 56,
            1,
            3,
            10,
            17,
            new[] { FontWeight.Normal, FontWeight.Medium },
            1.12d);
        ApplyTextLayout(DialogMessageTextBlock, dialogMessageLayout, dialogMessageLayout.MaxLines > 1 ? TextWrapping.Wrap : TextWrapping.NoWrap);

        var renameLayout = ComponentTypographyLayoutService.FitAdaptiveTextLayout(
            DialogRenameTextBox.Text ?? DialogRenameTextBox.Watermark,
            dialogWidth,
            _isUltraCompactMode ? 30 : 36,
            1,
            1,
            10,
            16,
            new[] { FontWeight.Normal },
            1.08d);
        DialogRenameTextBox.FontSize = renameLayout.FontSize;
        DialogRenameTextBox.FontWeight = renameLayout.Weight;
    }

    private double ResolveHistoryContentWidth()
    {
        if (Bounds.Width <= 1)
        {
            return Math.Max(90, _currentCellSize * 4.5);
        }

        var reservedWidth = _isUltraCompactMode ? 126 : 150;
        return Math.Max(90, Bounds.Width - reservedWidth);
    }

    private static void ApplyTextLayout(TextBlock textBlock, ComponentAdaptiveTextLayout layout, TextWrapping wrapping)
    {
        textBlock.FontSize = layout.FontSize;
        textBlock.FontWeight = layout.Weight;
        textBlock.LineHeight = layout.LineHeight;
        textBlock.MaxLines = layout.MaxLines;
        textBlock.TextWrapping = wrapping;
        textBlock.TextTrimming = TextTrimming.CharacterEllipsis;
    }

    private static StudySessionHistoryEntry? FindHistoryEntry(IReadOnlyList<StudySessionHistoryEntry> history, string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        for (var i = 0; i < history.Count; i++)
        {
            var entry = history[i];
            if (string.Equals(entry.SessionId, sessionId, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        return null;
    }

    private string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        if (duration.TotalHours >= 1)
        {
            var totalHours = (int)Math.Floor(duration.TotalHours);
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}", totalHours, duration.Minutes, duration.Seconds);
        }

        return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", duration.Minutes, duration.Seconds);
    }

    private string L(string key, string fallback)
    {
        return _localizationService.GetString(_languageCode, key, fallback);
    }

    private Color ResolvePanelBackgroundColor()
    {
        if (RootBorder.Background is ISolidColorBrush solidBackground)
        {
            return solidBackground.Color;
        }

        if (this.TryFindResource("AdaptiveGlassStrongBackgroundBrush", out var resource) &&
            resource is ISolidColorBrush solidBrush)
        {
            return solidBrush.Color;
        }

        return Color.Parse("#FF1E293B");
    }

    private static IReadOnlyList<Color> BuildPanelBackgroundSamples(Color panelColor)
    {
        var opaqueOnDark = ToOpaqueAgainst(panelColor, DarkSubstrate);
        var opaqueOnLight = ToOpaqueAgainst(panelColor, LightSubstrate);

        return
        [
            opaqueOnDark,
            opaqueOnLight,
            ColorMath.Blend(opaqueOnDark, DarkSubstrate, 0.22),
            ColorMath.Blend(opaqueOnDark, Color.Parse("#FFFFFFFF"), 0.14),
            ColorMath.Blend(opaqueOnLight, Color.Parse("#FFFFFFFF"), 0.08)
        ];
    }

    private static Color ToOpaqueAgainst(Color foreground, Color background)
    {
        if (foreground.A >= 0xFF)
        {
            return Color.FromArgb(0xFF, foreground.R, foreground.G, foreground.B);
        }

        var alpha = foreground.A / 255d;
        var red = (byte)Math.Round((foreground.R * alpha) + (background.R * (1 - alpha)));
        var green = (byte)Math.Round((foreground.G * alpha) + (background.G * (1 - alpha)));
        var blue = (byte)Math.Round((foreground.B * alpha) + (background.B * (1 - alpha)));
        return Color.FromArgb(0xFF, red, green, blue);
    }

    private static IBrush CreateAdaptiveBrush(IReadOnlyList<Color> backgroundSamples, IReadOnlyList<Color> candidates, double minContrast)
    {
        var selected = candidates[0];
        var bestRatio = double.MinValue;

        foreach (var candidate in candidates)
        {
            var ratio = MinContrastRatio(candidate, backgroundSamples);
            if (ratio >= minContrast)
            {
                selected = candidate;
                bestRatio = ratio;
                break;
            }

            if (ratio > bestRatio)
            {
                bestRatio = ratio;
                selected = candidate;
            }
        }

        return new SolidColorBrush(Color.FromArgb(0xFF, selected.R, selected.G, selected.B));
    }

    private static double MinContrastRatio(Color foreground, IReadOnlyList<Color> backgrounds)
    {
        var min = double.MaxValue;
        for (var i = 0; i < backgrounds.Count; i++)
        {
            var ratio = ColorMath.ContrastRatio(foreground, backgrounds[i]);
            if (ratio < min)
            {
                min = ratio;
            }
        }

        return min;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        AttachedToVisualTree -= OnAttachedToVisualTree;
        DetachedFromVisualTree -= OnDetachedFromVisualTree;
        SizeChanged -= OnSizeChanged;
        ActualThemeVariantChanged -= OnActualThemeVariantChanged;
        DialogCancelButton.Click -= (_, _) => CloseDialog();
        DialogConfirmButton.Click -= (_, _) => ConfirmDialog();
        DialogRenameTextBox.KeyDown -= OnDialogRenameTextBoxKeyDown;

        if (_isSubscribed)
        {
            _studyAnalyticsService.SnapshotUpdated -= OnStudySnapshotUpdated;
            _isSubscribed = false;
        }
    }
}
