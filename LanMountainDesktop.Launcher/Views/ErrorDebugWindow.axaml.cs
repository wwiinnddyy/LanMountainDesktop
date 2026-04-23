using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;

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
            Title = "Select LanMountainDesktop host executable",
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

    private void UpdatePathDisplay(string? path)
    {
        if (this.FindControl<TextBlock>("PathTextBlock") is { } pathTextBlock)
        {
            pathTextBlock.Text = string.IsNullOrEmpty(path) ? "Not selected" : path;
        }
    }
}
