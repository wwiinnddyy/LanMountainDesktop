using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using FluentAvalonia.UI.Controls;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.SettingsPages;

public partial class PluginSettingsPage : UserControl
{
    private static readonly IBrush SuccessBrush = new SolidColorBrush(Color.Parse("#FF0F766E"));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#FFC42B1C"));

    private readonly AppSettingsService _appSettingsService = new();
    private readonly LocalizationService _localizationService = new();
    private string? _packageImportStatusMessage;
    private bool _packageImportStatusIsError;

    public PluginSettingsPage()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => RefreshFromRuntime();
    }

    public void RefreshFromRuntime()
    {
        var runtime = (Application.Current as App)?.PluginRuntimeService;
        UpdateInstallerUi(runtime);
        if (runtime is null)
        {
            PluginSystemStatusTextBlock.Text = L("settings.plugins.runtime_unavailable", "Plugin runtime is not available.");
            PluginRuntimeSummaryPanel.Children.Clear();
            PluginCatalogItemsHost.Children.Clear();
            PluginRestartHintTextBlock.IsVisible = false;
            return;
        }

        BuildRuntimeSummary(runtime);
        BuildPluginCatalog(runtime);
    }

    private void UpdateInstallerUi(PluginRuntimeService? runtime)
    {
        InstallPluginPackageButton.Content = L("settings.plugins.install_button", "Open .laapp package");
        InstallPluginPackageButton.IsEnabled = runtime is not null;
        PluginPackageImportHintTextBlock.Text = runtime is null
            ? L(
                "settings.plugins.install_unavailable",
                "Plugin runtime is unavailable, so .laapp packages cannot be installed right now.")
            : F(
                "settings.plugins.install_hint_format",
                "Open a .laapp package to install it into: {0}",
                runtime.PluginsDirectory);

        PluginPackageImportStatusTextBlock.IsVisible = !string.IsNullOrWhiteSpace(_packageImportStatusMessage);
        PluginPackageImportStatusTextBlock.Text = _packageImportStatusMessage ?? string.Empty;
        PluginPackageImportStatusTextBlock.Foreground = _packageImportStatusIsError ? ErrorBrush : SuccessBrush;
    }

    private void BuildRuntimeSummary(PluginRuntimeService runtime)
    {
        var failures = runtime.LoadResults.Where(result => !result.IsSuccess).ToArray();
        var enabledCount = runtime.Catalog.Count(entry => entry.IsEnabled);
        PluginSystemStatusTextBlock.Text = F(
            "settings.plugins.summary_format",
            "Detected {0} plugin(s); enabled {1}; loaded {2}; settings pages {3}; widgets {4}; failures {5}.",
            runtime.Catalog.Count,
            enabledCount,
            runtime.LoadedPlugins.Count,
            runtime.SettingsPages.Count,
            runtime.DesktopComponents.Count,
            failures.Length);

        PluginRuntimeSummaryPanel.Children.Clear();
        foreach (var plugin in runtime.Catalog.OrderBy(entry => entry.Manifest.Name, StringComparer.OrdinalIgnoreCase))
        {
            var status = plugin.IsEnabled
                ? plugin.IsLoaded
                    ? L("settings.plugins.state.enabled", "Enabled")
                    : L("settings.plugins.state.enabled_failed", "Enabled / failed to load")
                : L("settings.plugins.state.disabled", "Disabled");
            PluginRuntimeSummaryPanel.Children.Add(CreateSummaryLine(
                F(
                    "settings.plugins.summary_item_format",
                    "{0}  v{1}  |  {2}",
                    plugin.Manifest.Name,
                    plugin.Manifest.Version ?? "dev",
                    status)));
        }
    }

    private void BuildPluginCatalog(PluginRuntimeService runtime)
    {
        PluginCatalogItemsHost.Children.Clear();

        var plugins = runtime.Catalog
            .OrderBy(entry => entry.Manifest.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        PluginCatalogEmptyTextBlock.IsVisible = plugins.Count == 0;
        PluginRestartHintTextBlock.IsVisible = plugins.Count > 0;

        foreach (var plugin in plugins)
        {
            PluginCatalogItemsHost.Children.Add(CreatePluginCatalogItem(runtime, plugin));
        }
    }

    private Control CreatePluginCatalogItem(PluginRuntimeService runtime, PluginCatalogEntry entry)
    {
        var title = new TextBlock
        {
            Text = entry.Manifest.Name,
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };

        var subtitle = new TextBlock
        {
            Text = BuildPluginSubtitle(entry),
            Foreground = PluginSystemDescriptionTextBlock.Foreground,
            TextWrapping = TextWrapping.Wrap
        };

        var enabledToggle = new ToggleSwitch
        {
            IsChecked = entry.IsEnabled,
            OnContent = L("settings.plugins.toggle_on", "Enabled"),
            OffContent = L("settings.plugins.toggle_off", "Disabled"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        enabledToggle.IsCheckedChanged += (_, _) => OnPluginEnableChanged(runtime, entry, enabledToggle.IsChecked == true);

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 12,
            Children =
            {
                new StackPanel
                {
                    Spacing = 4,
                    Children = { title, subtitle }
                },
                enabledToggle
            }
        };
        Grid.SetColumn(enabledToggle, 1);

        var details = new TextBlock
        {
            Text = BuildPluginDetails(entry),
            Foreground = PluginSystemDescriptionTextBlock.Foreground,
            TextWrapping = TextWrapping.Wrap
        };

        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#14000000")),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(14),
            Child = new StackPanel
            {
                Spacing = 10,
                Children = { header, details }
            }
        };
    }

    private void OnPluginEnableChanged(PluginRuntimeService runtime, PluginCatalogEntry entry, bool isEnabled)
    {
        runtime.SetPluginEnabled(entry.Manifest.Id, isEnabled);
        BuildRuntimeSummary(runtime);
        BuildPluginCatalog(runtime);
        PluginSystemStatusTextBlock.Text = F(
            "settings.plugins.toggle_result_format",
            "Plugin '{0}' was {1} for the next launch. Restart the app to apply page and widget changes.",
            entry.Manifest.Name,
            isEnabled
                ? L("settings.plugins.toggle_state_enabled", "enabled")
                : L("settings.plugins.toggle_state_disabled", "disabled"));
    }

    private async void OnInstallPluginPackageClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var runtime = (Application.Current as App)?.PluginRuntimeService;
        if (runtime is null)
        {
            SetPackageImportStatus(
                L(
                    "settings.plugins.install_unavailable",
                    "Plugin runtime is unavailable, so .laapp packages cannot be installed right now."),
                isError: true);
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        var storageProvider = topLevel?.StorageProvider;
        if (storageProvider is null)
        {
            SetPackageImportStatus(
                L("settings.plugins.install_picker_unavailable", "Storage provider is unavailable."),
                isError: true);
            return;
        }

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L("settings.plugins.install_picker_title", "Select plugin package"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(L("settings.plugins.install_file_type", ".laapp plugin package"))
                {
                    Patterns = [$"*{PluginSdkInfo.PackageFileExtension}"]
                }
            ]
        });

        if (files.Count == 0)
        {
            return;
        }

        string? temporaryPackagePath = null;
        try
        {
            temporaryPackagePath = await CopyPackageToTemporaryFileAsync(files[0]);
            if (string.IsNullOrWhiteSpace(temporaryPackagePath))
            {
                SetPackageImportStatus(
                    L("settings.plugins.install_copy_failed", "Failed to copy the selected .laapp package."),
                    isError: true);
                return;
            }

            var manifest = runtime.InstallPluginPackage(temporaryPackagePath);
            RefreshFromRuntime();
            SetPackageImportStatus(
                F(
                    "settings.plugins.install_success_format",
                    "Installed plugin '{0}'. Restart the app to apply newly added settings pages and widgets.",
                    manifest.Name),
                isError: false);
        }
        catch (Exception ex)
        {
            SetPackageImportStatus(
                F(
                    "settings.plugins.install_failed_format",
                    "Failed to install plugin package: {0}",
                    ex.Message),
                isError: true);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(temporaryPackagePath))
            {
                try
                {
                    File.Delete(temporaryPackagePath);
                }
                catch
                {
                    // Ignore temporary file cleanup errors.
                }
            }
        }
    }

    private void RefreshPluginNavigation(TopLevel? topLevel)
    {
        switch (topLevel)
        {
            case MainWindow mainWindow:
                mainWindow.RefreshPluginSettingsNavigation();
                break;
            case SettingsWindow settingsWindow:
                settingsWindow.RefreshPluginSettingsNavigation();
                break;
        }
    }

    private void SetPackageImportStatus(string message, bool isError)
    {
        _packageImportStatusMessage = string.IsNullOrWhiteSpace(message) ? null : message;
        _packageImportStatusIsError = isError;
        UpdateInstallerUi((Application.Current as App)?.PluginRuntimeService);
    }

    private string BuildPluginSubtitle(PluginCatalogEntry entry)
    {
        var source = entry.IsPackage
            ? L("settings.plugins.source_package", ".laapp package")
            : L("settings.plugins.source_manifest", "Loose manifest");
        var state = entry.IsEnabled
            ? entry.IsLoaded
                ? L("settings.plugins.state.loaded", "Loaded")
                : L("settings.plugins.state.load_failed", "Load failed")
            : L("settings.plugins.state.disabled", "Disabled");
        return F(
            "settings.plugins.subtitle_format",
            "{0} | {1} | {2}",
            state,
            source,
            entry.Manifest.Id);
    }

    private string BuildPluginDetails(PluginCatalogEntry entry)
    {
        var detail = F(
            "settings.plugins.detail_format",
            "Settings pages: {0} | Widgets: {1}",
            entry.SettingsPageCount,
            entry.WidgetCount);
        return string.IsNullOrWhiteSpace(entry.ErrorMessage)
            ? detail
            : detail + Environment.NewLine + entry.ErrorMessage;
    }

    private TextBlock CreateSummaryLine(string text)
    {
        return new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = PluginSystemDescriptionTextBlock.Foreground
        };
    }

    private string L(string key, string fallback)
    {
        var snapshot = _appSettingsService.Load();
        return _localizationService.GetString(snapshot.LanguageCode, key, fallback);
    }

    private string F(string key, string fallback, params object[] args)
    {
        return string.Format(CultureInfo.CurrentCulture, L(key, fallback), args);
    }

    private static async Task<string?> CopyPackageToTemporaryFileAsync(IStorageFile file)
    {
        try
        {
            var extension = Path.GetExtension(file.Name);
            if (string.IsNullOrWhiteSpace(extension))
            {
                extension = PluginSdkInfo.PackageFileExtension;
            }

            var temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                "LanMountainDesktop",
                "PluginImports");
            Directory.CreateDirectory(temporaryDirectory);

            var temporaryPackagePath = Path.Combine(
                temporaryDirectory,
                $"{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}{extension}");

            await using var sourceStream = await file.OpenReadAsync();
            await using var destinationStream = File.Create(temporaryPackagePath);
            await sourceStream.CopyToAsync(destinationStream);
            return temporaryPackagePath;
        }
        catch
        {
            return null;
        }
    }
}
