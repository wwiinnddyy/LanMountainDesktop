using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using LanMontainDesktop.Models;
using LanMontainDesktop.Services;

namespace LanMontainDesktop.Views.Components;

public partial class ClassScheduleSettingsWindow : UserControl
{
    private readonly AppSettingsService _appSettingsService = new();
    private readonly LocalizationService _localizationService = new();
    private readonly List<ImportedClassScheduleSnapshot> _importedSchedules = [];
    private string _activeScheduleId = string.Empty;
    private string _languageCode = "zh-CN";

    public event EventHandler? SettingsChanged;

    public ClassScheduleSettingsWindow()
    {
        InitializeComponent();
        LoadState();
        ApplyLocalization();
        RenderImportedSchedules();
    }

    private void LoadState()
    {
        var snapshot = _appSettingsService.Load();
        _languageCode = _localizationService.NormalizeLanguageCode(snapshot.LanguageCode);

        _importedSchedules.Clear();
        foreach (var item in snapshot.ImportedClassSchedules)
        {
            if (string.IsNullOrWhiteSpace(item.Id) ||
                string.IsNullOrWhiteSpace(item.FilePath))
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

    private void ApplyLocalization()
    {
        TitleTextBlock.Text = L("schedule.settings.title", "课表导入");
        DescriptionTextBlock.Text = L(
            "schedule.settings.desc",
            "导入 ClassIsland 的 CSES 课表文件并选择启用项。");
        AddScheduleButtonTextBlock.Text = L("schedule.settings.add", "添加课表");
        EmptyStateTextBlock.Text = L("schedule.settings.empty", "暂无导入课表");
    }

    private async void OnAddScheduleClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        var storageProvider = topLevel?.StorageProvider;
        if (storageProvider is null)
        {
            return;
        }

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L("schedule.settings.picker_title", "选择 ClassIsland 课表文件"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(L("schedule.settings.picker_file_type", "ClassIsland CSES 课表"))
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
            SaveState();
            RenderImportedSchedules();
            return;
        }

        var displayName = Path.GetFileNameWithoutExtension(importedPath)?.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = L("schedule.settings.unnamed", "未命名课表");
        }

        var imported = new ImportedClassScheduleSnapshot
        {
            Id = Guid.NewGuid().ToString("N"),
            DisplayName = displayName,
            FilePath = importedPath
        };

        _importedSchedules.Add(imported);
        _activeScheduleId = imported.Id;
        SaveState();
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
                "LanMontainDesktop",
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

        if (_importedSchedules.Count == 0)
        {
            EmptyStateTextBlock.IsVisible = true;
            return;
        }

        EmptyStateTextBlock.IsVisible = false;
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
                    ? L("schedule.settings.unnamed", "未命名课表")
                    : item.DisplayName,
                FontSize = 14,
                FontWeight = FontWeight.SemiBold,
                Foreground = ResolveThemeBrush("AdaptiveTextPrimaryBrush", "#FFEFF3FF"),
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            var path = new TextBlock
            {
                Text = item.FilePath,
                FontSize = 11,
                Foreground = ResolveThemeBrush("AdaptiveTextSecondaryBrush", "#FF99A2B5"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap
            };

            var textStack = new StackPanel
            {
                Spacing = 4,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { title, path }
            };

            var deleteButton = new Button
            {
                Content = L("schedule.settings.delete", "删除"),
                Tag = item.Id,
                Padding = new Thickness(10, 6),
                MinWidth = 64,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            deleteButton.Click += OnDeleteScheduleClick;

            var rowGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                ColumnSpacing = 10
            };
            rowGrid.Children.Add(selector);
            rowGrid.Children.Add(textStack);
            rowGrid.Children.Add(deleteButton);
            Grid.SetColumn(selector, 0);
            Grid.SetColumn(textStack, 1);
            Grid.SetColumn(deleteButton, 2);

            var rowBorder = new Border
            {
                Padding = new Thickness(10, 8),
                CornerRadius = new CornerRadius(12),
                Background = ResolveThemeBrush("AdaptiveSurfaceRaisedBrush", "#1AFFFFFF"),
                BorderBrush = ResolveThemeBrush("AdaptiveButtonBorderBrush", "#22000000"),
                BorderThickness = new Thickness(1),
                Child = rowGrid
            };

            ScheduleItemsPanel.Children.Add(rowBorder);
        }
    }

    private void OnScheduleSelectionChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton button ||
            button.IsChecked != true ||
            button.Tag is not string scheduleId)
        {
            return;
        }

        if (string.Equals(_activeScheduleId, scheduleId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _activeScheduleId = scheduleId;
        SaveState();
    }

    private void OnDeleteScheduleClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string scheduleId)
        {
            return;
        }

        var target = _importedSchedules.FirstOrDefault(item =>
            string.Equals(item.Id, scheduleId, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            return;
        }

        _importedSchedules.Remove(target);
        TryDeleteImportedFile(target.FilePath);
        if (string.Equals(_activeScheduleId, scheduleId, StringComparison.OrdinalIgnoreCase))
        {
            _activeScheduleId = _importedSchedules.Count > 0 ? _importedSchedules[0].Id : string.Empty;
        }

        SaveState();
        RenderImportedSchedules();
    }

    private void SaveState()
    {
        var snapshot = _appSettingsService.Load();
        snapshot.ImportedClassSchedules = _importedSchedules
            .Select(item => new ImportedClassScheduleSnapshot
            {
                Id = item.Id,
                DisplayName = item.DisplayName,
                FilePath = item.FilePath
            })
            .ToList();
        snapshot.ActiveImportedClassScheduleId = _activeScheduleId ?? string.Empty;
        _appSettingsService.Save(snapshot);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private string L(string key, string fallback)
    {
        return _localizationService.GetString(_languageCode, key, fallback);
    }

    private static void TryDeleteImportedFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return;
        }

        try
        {
            File.Delete(filePath);
        }
        catch
        {
            // Keep settings operation resilient even when file deletion fails.
        }
    }

    private IBrush ResolveThemeBrush(string key, string fallbackHex)
    {
        if (this.TryFindResource(key, out var value) && value is IBrush brush)
        {
            return brush;
        }

        return new SolidColorBrush(Color.Parse(fallbackHex));
    }
}
