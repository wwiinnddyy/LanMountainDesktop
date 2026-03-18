using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.ComponentEditors;

public partial class ClassScheduleComponentEditor : ComponentEditorViewBase
{
    private readonly List<ImportedClassScheduleSnapshot> _importedSchedules = [];
    private string? _activeScheduleId;
    private bool _suppressEvents;

    public ClassScheduleComponentEditor()
        : this(null)
    {
    }

    public ClassScheduleComponentEditor(DesktopComponentEditorContext? context)
        : base(context)
    {
        InitializeComponent();
        LoadState();
        ApplyState();
        RenderImportedSchedules();
    }

    private void LoadState()
    {
        var snapshot = LoadSnapshot();
        _importedSchedules.Clear();
        foreach (var item in snapshot.ImportedClassSchedules)
        {
            if (string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.FilePath))
            {
                continue;
            }

            _importedSchedules.Add(new ImportedClassScheduleSnapshot
            {
                Id = item.Id.Trim(),
                DisplayName = item.DisplayName?.Trim() ?? string.Empty,
                FilePath = item.FilePath.Trim()
            });
        }

        _activeScheduleId = snapshot.ActiveImportedClassScheduleId?.Trim() ?? string.Empty;
        if (_importedSchedules.Count > 0 &&
            !_importedSchedules.Any(item => string.Equals(item.Id, _activeScheduleId, StringComparison.OrdinalIgnoreCase)))
        {
            _activeScheduleId = _importedSchedules[0].Id;
        }
    }

    private void ApplyState()
    {
        var snapshot = LoadSnapshot();
        var colorSchemeSource = snapshot.ColorSchemeSource;

        HeadlineTextBlock.Text = Context?.Definition.DisplayName ?? "Class Schedule";
        DescriptionTextBlock.Text = L(
            "schedule.settings.desc",
            "Import a ClassIsland CSES schedule file and choose which one to use.");

        ColorSchemeHeaderTextBlock.Text = L("component.settings.color_scheme", "Color Scheme");
        FollowSystemColorSchemeItem.Content = L("component.color_scheme.follow_system", "Follow system color scheme");
        UseNativeColorSchemeItem.Content = L("component.color_scheme.native", "Use component custom color scheme");

        AddScheduleButton.Content = L("schedule.settings.add", "Add Schedule");
        EmptyStateTextBlock.Text = L("schedule.settings.empty", "No imported schedules yet.");

        _suppressEvents = true;
        ColorSchemeComboBox.SelectedItem =
            string.IsNullOrEmpty(colorSchemeSource) ||
            string.Equals(colorSchemeSource, ThemeAppearanceValues.ColorSchemeFollowSystem, StringComparison.OrdinalIgnoreCase)
                ? FollowSystemColorSchemeItem
                : UseNativeColorSchemeItem;
        _suppressEvents = false;
    }

    private void OnColorSchemeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (_suppressEvents)
        {
            return;
        }

        var colorSchemeSource = ColorSchemeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag
            ? tag
            : ThemeAppearanceValues.ColorSchemeFollowSystem;

        var snapshot = LoadSnapshot();
        snapshot.ColorSchemeSource = colorSchemeSource;
        SaveSnapshot(snapshot, nameof(ComponentSettingsSnapshot.ColorSchemeSource));
    }

    private async void OnAddScheduleClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is not { } storageProvider)
        {
            return;
        }

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L("schedule.settings.picker_title", "Choose ClassIsland schedule file"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(L("schedule.settings.picker_file_type", "ClassIsland CSES Schedule"))
                {
                    Patterns = ["*.cses", "*.yaml", "*.yml"]
                }
            ]
        });

        if (files.Count == 0)
        {
            return;
        }

        var importedPath = await ImportScheduleFileAsync(files[0]);
        if (string.IsNullOrWhiteSpace(importedPath))
        {
            return;
        }

        var existing = _importedSchedules.FirstOrDefault(item =>
            string.Equals(item.FilePath, importedPath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            _activeScheduleId = existing.Id;
        }
        else
        {
            _importedSchedules.Add(new ImportedClassScheduleSnapshot
            {
                Id = Guid.NewGuid().ToString("N"),
                DisplayName = Path.GetFileNameWithoutExtension(importedPath)?.Trim()
                    ?? L("schedule.settings.unnamed", "Untitled Schedule"),
                FilePath = importedPath
            });
            _activeScheduleId = _importedSchedules[^1].Id;
        }

        PersistState();
        RenderImportedSchedules();
    }

    private async Task<string?> ImportScheduleFileAsync(IStorageFile file)
    {
        try
        {
            var extension = Path.GetExtension(file.Name);
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = ".cses";
            }

            var importedDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LanMountainDesktop",
                "Schedules");
            Directory.CreateDirectory(importedDirectory);

            var destinationPath = Path.Combine(
                importedDirectory,
                $"{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}{extension}");

            await using var sourceStream = await file.OpenReadAsync();
            await using var destinationStream = File.Create(destinationPath);
            await sourceStream.CopyToAsync(destinationStream);
            return destinationPath;
        }
        catch
        {
            return null;
        }
    }

    private void RenderImportedSchedules()
    {
        ScheduleItemsPanel.Children.Clear();
        EmptyStateTextBlock.IsVisible = _importedSchedules.Count == 0;
        if (_importedSchedules.Count == 0)
        {
            return;
        }

        foreach (var item in _importedSchedules)
        {
            var selector = new RadioButton
            {
                GroupName = "class_schedule_imports",
                IsChecked = string.Equals(item.Id, _activeScheduleId, StringComparison.OrdinalIgnoreCase),
                VerticalAlignment = VerticalAlignment.Center,
                Tag = item.Id
            };
            selector.IsCheckedChanged += OnScheduleSelectionChanged;

            var title = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(item.DisplayName)
                    ? L("schedule.settings.unnamed", "Untitled Schedule")
                    : item.DisplayName,
                FontWeight = FontWeight.SemiBold
            };

            var path = new TextBlock
            {
                Text = item.FilePath,
                FontSize = 11,
                Opacity = 0.7,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            var deleteButton = new Button
            {
                Content = L("schedule.settings.delete", "Delete"),
                Tag = item.Id,
                Padding = new Thickness(12, 8),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            deleteButton.Click += OnDeleteScheduleClick;

            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                ColumnSpacing = 12
            };
            var details = new StackPanel
            {
                Spacing = 4,
                Children = { title, path }
            };
            row.Children.Add(selector);
            row.Children.Add(details);
            row.Children.Add(deleteButton);
            Grid.SetColumn(details, 1);
            Grid.SetColumn(deleteButton, 2);

            ScheduleItemsPanel.Children.Add(new Border
            {
                Padding = new Thickness(12, 10),
                CornerRadius = new CornerRadius(16),
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                Child = row
            });
        }
    }

    private void OnScheduleSelectionChanged(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is not RadioButton radioButton || radioButton.IsChecked != true || radioButton.Tag is not string scheduleId)
        {
            return;
        }

        _activeScheduleId = scheduleId;
        PersistState();
    }

    private void OnDeleteScheduleClick(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is not Button button || button.Tag is not string scheduleId)
        {
            return;
        }

        var removed = _importedSchedules.RemoveAll(item =>
            string.Equals(item.Id, scheduleId, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
        {
            return;
        }

        if (string.Equals(_activeScheduleId, scheduleId, StringComparison.OrdinalIgnoreCase))
        {
            _activeScheduleId = _importedSchedules.FirstOrDefault()?.Id ?? string.Empty;
        }

        PersistState();
        RenderImportedSchedules();
    }

    private void PersistState()
    {
        var snapshot = LoadSnapshot();
        snapshot.ImportedClassSchedules = _importedSchedules
            .Select(item => new ImportedClassScheduleSnapshot
            {
                Id = item.Id,
                DisplayName = item.DisplayName,
                FilePath = item.FilePath
            })
            .ToList();
        snapshot.ActiveImportedClassScheduleId = _activeScheduleId;
        SaveSnapshot(
            snapshot,
            nameof(ComponentSettingsSnapshot.ImportedClassSchedules),
            nameof(ComponentSettingsSnapshot.ActiveImportedClassScheduleId));
    }
}
