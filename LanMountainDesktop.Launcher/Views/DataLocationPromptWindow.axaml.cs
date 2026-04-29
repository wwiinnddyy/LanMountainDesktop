using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using LanMountainDesktop.Launcher.Models;
using LanMountainDesktop.Launcher.Services;

namespace LanMountainDesktop.Launcher.Views;

internal partial class DataLocationPromptWindow : Window
{
    private readonly TaskCompletionSource<DataLocationPromptResult?> _completionSource = new();
    private readonly DataLocationResolver _resolver;
    private bool _isTransitioning;

    public DataLocationPromptWindow()
    {
        AvaloniaXamlLoader.Load(this);
        Loaded += OnWindowLoaded;
        Opened += OnWindowOpened;
        _resolver = new DataLocationResolver(AppContext.BaseDirectory);
    }

    internal DataLocationPromptWindow(DataLocationResolver resolver)
    {
        AvaloniaXamlLoader.Load(this);
        Loaded += OnWindowLoaded;
        Opened += OnWindowOpened;
        _resolver = resolver;
    }

    private void OnWindowLoaded(object? sender, RoutedEventArgs e)
    {
        BindControls();
        UpdateUiState();
    }

    private void BindControls()
    {
        var systemRadio = this.FindControl<RadioButton>("SystemRadio");
        var portableRadio = this.FindControl<RadioButton>("PortableRadio");
        var confirmButton = this.FindControl<Button>("ConfirmButton");
        var cancelButton = this.FindControl<Button>("CancelButton");

        if (systemRadio is not null)
        {
            systemRadio.IsCheckedChanged += OnSelectionChanged;
        }

        if (portableRadio is not null)
        {
            portableRadio.IsCheckedChanged += OnSelectionChanged;
        }

        if (confirmButton is not null)
        {
            confirmButton.Click += OnConfirmClick;
        }

        if (cancelButton is not null)
        {
            cancelButton.Click += OnCancelClick;
        }
    }

    private void UpdateUiState()
    {
        var systemPathText = this.FindControl<TextBlock>("SystemPathText");
        var portablePathText = this.FindControl<TextBlock>("PortablePathText");
        var adminWarningBanner = this.FindControl<Border>("AdminWarningBanner");
        var portableRadio = this.FindControl<RadioButton>("PortableRadio");
        var migrationInfoBorder = this.FindControl<Border>("MigrationInfoBorder");
        var migrationInfoText = this.FindControl<TextBlock>("MigrationInfoText");

        if (systemPathText is not null)
        {
            systemPathText.Text = _resolver.DefaultSystemDataPath;
        }

        if (portablePathText is not null)
        {
            portablePathText.Text = _resolver.DefaultPortableDataPath;
        }

        var portableAllowed = _resolver.IsPortableModeAllowed();

        if (adminWarningBanner is not null)
        {
            adminWarningBanner.IsVisible = !portableAllowed;
        }

        if (portableRadio is not null)
        {
            portableRadio.IsEnabled = portableAllowed;
        }

        var hasExistingData = _resolver.HasExistingSystemData();
        if (migrationInfoBorder is not null)
        {
            migrationInfoBorder.IsVisible = hasExistingData;
        }

        if (migrationInfoText is not null && hasExistingData)
        {
            migrationInfoText.Text = "Existing system data was detected. Choosing portable mode will migrate the current data automatically.";
        }
    }

    private void OnSelectionChanged(object? sender, RoutedEventArgs e)
    {
        var systemRadio = this.FindControl<RadioButton>("SystemRadio");
        var portableRadio = this.FindControl<RadioButton>("PortableRadio");
        var systemBorder = this.FindControl<Border>("SystemOptionBorder");
        var portableBorder = this.FindControl<Border>("PortableOptionBorder");

        var isSystem = systemRadio?.IsChecked == true;
        var isPortable = portableRadio?.IsChecked == true;

        if (systemBorder is not null)
        {
            systemBorder.BorderBrush = isSystem
                ? Application.Current?.FindResource("AccentFillColorDefaultBrush") as IBrush
                : Application.Current?.FindResource("CardStrokeColorDefaultBrush") as IBrush;
            systemBorder.BorderThickness = isSystem ? new Thickness(2) : new Thickness(1);
        }

        if (portableBorder is not null)
        {
            portableBorder.BorderBrush = isPortable
                ? Application.Current?.FindResource("AccentFillColorDefaultBrush") as IBrush
                : Application.Current?.FindResource("CardStrokeColorDefaultBrush") as IBrush;
            portableBorder.BorderThickness = isPortable ? new Thickness(2) : new Thickness(1);
        }
    }

    private async void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        if (_isTransitioning)
        {
            return;
        }

        _isTransitioning = true;

        var portableRadio = this.FindControl<RadioButton>("PortableRadio");
        var selectedMode = portableRadio?.IsChecked == true
            ? DataLocationMode.Portable
            : DataLocationMode.System;

        var migrateExistingData = selectedMode == DataLocationMode.Portable && _resolver.HasExistingSystemData();

        try
        {
            await PlayExitAnimationAsync();
            _completionSource.TrySetResult(new DataLocationPromptResult
            {
                SelectedMode = selectedMode,
                MigrateExistingData = migrateExistingData
            });
        }
        catch (Exception ex)
        {
            Logger.Warn($"Error during data location prompt exit animation: {ex.Message}");
            _completionSource.TrySetResult(new DataLocationPromptResult
            {
                SelectedMode = selectedMode,
                MigrateExistingData = migrateExistingData
            });
        }
    }

    private async void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        if (_isTransitioning)
        {
            return;
        }

        _isTransitioning = true;

        try
        {
            await PlayExitAnimationAsync();
            _completionSource.TrySetResult(null);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Error during data location prompt cancel: {ex.Message}");
            _completionSource.TrySetResult(null);
        }
    }

    private async void OnWindowOpened(object? sender, EventArgs e)
    {
        await PlayEntranceAnimationAsync();
    }

    private async Task PlayEntranceAnimationAsync()
    {
        try
        {
            var contentGrid = this.FindControl<Grid>("ContentGrid");
            if (contentGrid is null)
            {
                return;
            }

            var translateTransform = contentGrid.RenderTransform as TranslateTransform ?? new TranslateTransform();
            contentGrid.RenderTransform = translateTransform;

            contentGrid.Opacity = 0;
            translateTransform.Y = 24;

            var fadeInAnimation = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(500),
                Easing = new CubicEaseOut(),
                FillMode = FillMode.Forward,
                Children =
                {
                    new KeyFrame
                    {
                        Setters = { new Setter(OpacityProperty, 0.0) },
                        KeyTime = TimeSpan.FromMilliseconds(0)
                    },
                    new KeyFrame
                    {
                        Setters = { new Setter(OpacityProperty, 1.0) },
                        KeyTime = TimeSpan.FromMilliseconds(500)
                    }
                }
            };

            var slideUpAnimation = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(500),
                Easing = new CubicEaseOut(),
                FillMode = FillMode.Forward,
                Children =
                {
                    new KeyFrame
                    {
                        Setters = { new Setter(TranslateTransform.YProperty, 24.0) },
                        KeyTime = TimeSpan.FromMilliseconds(0)
                    },
                    new KeyFrame
                    {
                        Setters = { new Setter(TranslateTransform.YProperty, 0.0) },
                        KeyTime = TimeSpan.FromMilliseconds(500)
                    }
                }
            };

            await Task.WhenAll(
                fadeInAnimation.RunAsync(contentGrid),
                slideUpAnimation.RunAsync(translateTransform));
        }
        catch (Exception ex)
        {
            Logger.Warn($"Error playing data location prompt entrance animation: {ex.Message}");
        }
    }

    private async Task PlayExitAnimationAsync()
    {
        try
        {
            var contentGrid = this.FindControl<Grid>("ContentGrid");
            if (contentGrid is null)
            {
                await Task.Delay(150);
                return;
            }

            var fadeOutAnimation = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(200),
                Easing = new CubicEaseIn(),
                FillMode = FillMode.Forward,
                Children =
                {
                    new KeyFrame
                    {
                        Setters = { new Setter(OpacityProperty, 1.0) },
                        KeyTime = TimeSpan.FromMilliseconds(0)
                    },
                    new KeyFrame
                    {
                        Setters = { new Setter(OpacityProperty, 0.0) },
                        KeyTime = TimeSpan.FromMilliseconds(200)
                    }
                }
            };

            await fadeOutAnimation.RunAsync(contentGrid);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Error playing data location prompt exit animation: {ex.Message}");
        }
    }

    internal Task<DataLocationPromptResult?> WaitForChoiceAsync() => _completionSource.Task;
}
