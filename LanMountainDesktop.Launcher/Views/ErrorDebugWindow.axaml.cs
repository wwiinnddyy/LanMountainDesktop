using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using LanMountainDesktop.Launcher.Resources;
using LanMountainDesktop.Launcher.Shell;

namespace LanMountainDesktop.Launcher.Views;

public partial class ErrorDebugWindow : Window
{
    private string? _selectedHostPath;
    private bool _isInitialized;

    public bool IsDevModeEnabled { get; private set; }

    public bool WasAccepted { get; private set; }

    public string? SelectedHostPath => _selectedHostPath;

    public ErrorDebugWindow()
    {
        AvaloniaXamlLoader.Load(this);
        Loaded += OnWindowLoaded;
    }

    public ErrorDebugWindow(bool devModeEnabled, string? initialPath)
        : this()
    {
        IsDevModeEnabled = devModeEnabled;
        _selectedHostPath = initialPath;
    }

    private void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        InitializeComponents();

        if (this.FindControl<ToggleSwitch>("DevModeToggle") is { } devModeToggle)
        {
            devModeToggle.IsChecked = IsDevModeEnabled;
        }

        UpdatePathDisplay(_selectedHostPath);
        RefreshBackgroundImageDisplay();
    }

    private void InitializeComponents()
    {
        if (this.FindControl<ToggleSwitch>("DevModeToggle") is { } devModeToggle)
        {
            devModeToggle.IsCheckedChanged += (_, _) =>
            {
                IsDevModeEnabled = devModeToggle.IsChecked ?? false;
            };
        }

        if (this.FindControl<Button>("BrowseButton") is { } browseButton)
        {
            browseButton.Click += OnBrowseClick;
        }

        if (this.FindControl<Button>("BrowseImageButton") is { } browseImageButton)
        {
            browseImageButton.Click += OnBrowseImageClick;
        }

        if (this.FindControl<Button>("ClearImageButton") is { } clearImageButton)
        {
            clearImageButton.Click += OnClearImageClick;
        }

        if (this.FindControl<Button>("OkButton") is { } okButton)
        {
            okButton.Click += (_, _) =>
            {
                WasAccepted = true;
                Close();
            };
        }

        if (this.FindControl<Button>("CancelButton") is { } cancelButton)
        {
            cancelButton.Click += (_, _) => Close();
        }
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        var storageProvider = StorageProvider;
        if (storageProvider is null)
        {
            return;
        }

        var options = new FilePickerOpenOptions
        {
            Title = Strings.DebugDebug_SelectExeDialog,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Executable")
                {
                    Patterns = OperatingSystem.IsWindows()
                        ? ["*.exe"]
                        : ["*"]
                }
            ]
        };

        var result = await storageProvider.OpenFilePickerAsync(options);
        if (result.Count <= 0)
        {
            return;
        }

        _selectedHostPath = result[0].Path.LocalPath;
        UpdatePathDisplay(_selectedHostPath);
    }

    private async void OnBrowseImageClick(object? sender, RoutedEventArgs e)
    {
        var storageProvider = StorageProvider;
        if (storageProvider is null)
        {
            return;
        }

        var patterns = LauncherBackgroundService
            .GetSupportedExtensions()
            .Select(extension => "*" + extension)
            .ToArray();

        var options = new FilePickerOpenOptions
        {
            Title = Strings.DebugDebug_SelectImageDialog,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(Strings.DebugDebug_ImageFiles)
                {
                    Patterns = patterns
                }
            ]
        };

        var result = await storageProvider.OpenFilePickerAsync(options);
        if (result.Count <= 0)
        {
            return;
        }

        var saveResult = LauncherBackgroundService.SaveBackgroundImage(result[0].Path.LocalPath);
        var status = saveResult.IsSuccess
            ? Strings.DebugDebug_BackgroundImageSaved
            : string.Format(Strings.DebugDebug_BackgroundImageSaveFailedFormat, saveResult.ErrorMessage ?? string.Empty);

        RefreshBackgroundImageDisplay(status);
    }

    private void OnClearImageClick(object? sender, RoutedEventArgs e)
    {
        var clearResult = LauncherBackgroundService.ClearBackgroundImage();
        var status = clearResult.IsSuccess
            ? Strings.DebugDebug_BackgroundImageCleared
            : string.Format(Strings.DebugDebug_BackgroundImageSaveFailedFormat, clearResult.ErrorMessage ?? string.Empty);

        RefreshBackgroundImageDisplay(status);
    }

    private void UpdatePathDisplay(string? path)
    {
        if (this.FindControl<TextBlock>("PathTextBlock") is { } pathTextBlock)
        {
            pathTextBlock.Text = string.IsNullOrEmpty(path) ? Strings.DebugDebug_NotSelected : path;
        }
    }

    private void RefreshBackgroundImageDisplay(string? statusOverride = null)
    {
        var imageInfo = LauncherBackgroundService.LoadBackgroundImage();

        if (this.FindControl<TextBlock>("BackgroundImagePathTextBlock") is { } pathTextBlock)
        {
            pathTextBlock.Text = imageInfo.Exists && !string.IsNullOrWhiteSpace(imageInfo.FilePath)
                ? imageInfo.FilePath
                : Strings.DebugDebug_BackgroundImageNotSet;
        }

        if (this.FindControl<TextBlock>("BackgroundImageStatusTextBlock") is { } statusTextBlock)
        {
            statusTextBlock.Text = statusOverride ?? ResolveBackgroundImageStatus(imageInfo);
        }

        if (this.FindControl<Button>("ClearImageButton") is { } clearButton)
        {
            clearButton.IsEnabled = imageInfo.Exists;
        }
    }

    private static string ResolveBackgroundImageStatus(LauncherBackgroundService.BackgroundImageInfo imageInfo)
    {
        if (imageInfo.IsValid)
        {
            return string.Format(
                Strings.DebugDebug_BackgroundImageReadyFormat,
                imageInfo.Width,
                imageInfo.Height);
        }

        if (imageInfo.Exists)
        {
            return string.Format(
                Strings.DebugDebug_BackgroundImageInvalidFormat,
                imageInfo.ErrorMessage ?? string.Empty);
        }

        return Strings.DebugDebug_BackgroundImageNotSet;
    }
}
