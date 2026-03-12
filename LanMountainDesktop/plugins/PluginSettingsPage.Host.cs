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
using FluentIcons.Avalonia.Fluent;
using FluentIcons.Common;
using FluentAvalonia.UI.Controls;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.SettingsPages;

public partial class PluginSettingsPage : UserControl
{
    private static readonly IBrush SuccessBrush = new SolidColorBrush(Color.Parse("#FF0F766E"));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#FFC42B1C"));
    private static readonly IBrush DestructiveBrush = new SolidColorBrush(Color.Parse("#FFF87171"));

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
            InstalledPluginsSettingsExpander.Items.Clear();
            PluginRestartHintTextBlock.IsVisible = false;
            PluginCatalogEmptyTextBlock.IsVisible = false;
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
            "Detected {0} plugin(s); enabled {1}; loaded {2}; settings sections {3}; widgets {4}; failures {5}.",
            runtime.Catalog.Count,
            enabledCount,
            runtime.Catalog.Count(entry => entry.IsLoaded),
            runtime.SettingsSections.Count,
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
        InstalledPluginsSettingsExpander.Items.Clear();

        var plugins = runtime.Catalog
            .OrderBy(entry => entry.Manifest.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        PluginCatalogEmptyTextBlock.IsVisible = plugins.Count == 0;
        PluginRestartHintTextBlock.IsVisible = plugins.Count > 0;

        foreach (var plugin in plugins)
        {
            InstalledPluginsSettingsExpander.Items.Add(CreatePluginCatalogItem(runtime, plugin));
        }
    }

    private SettingsExpanderItem CreatePluginCatalogItem(PluginRuntimeService runtime, PluginCatalogEntry entry)
    {
        return new SettingsExpanderItem
        {
            Content = entry.Manifest.Name,
            Description = BuildPluginSubtitle(entry),
            IconSource = CreatePluginCatalogIconSource(),
            IsClickEnabled = false,
            Footer = CreatePluginCatalogActions(runtime, entry)
        };
    }

    private void OnInstallPluginPackageClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        UiExceptionGuard.FireAndForgetGuarded(
            OnInstallPluginPackageAsync,
            "PluginSettings.InstallPackage",
            context: "Page=PluginSettings",
            onHandledException: ex =>
            {
                SetPackageImportStatus(
                    F(
                        "settings.plugins.install_failed_format",
                        "Failed to install plugin package: {0}",
                        ex.Message),
                    isError: true);
                return Task.CompletedTask;
            });
    }

    private async Task OnInstallPluginPackageAsync()
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
            RefreshPluginNavigation(TopLevel.GetTopLevel(this));
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

    private void OnDeletePluginClick(PluginRuntimeService runtime, PluginCatalogEntry entry)
    {
        try
        {
            if (!runtime.DeleteInstalledPlugin(entry.Manifest.Id))
            {
                SetPackageImportStatus(
                    F(
                        "settings.plugins.delete_failed_format",
                        "Failed to delete plugin: {0}",
                        entry.Manifest.Name),
                    isError: true);
                return;
            }

            RefreshFromRuntime();
            RefreshPluginNavigation(TopLevel.GetTopLevel(this));
            PluginSystemStatusTextBlock.Text = F(
                "settings.plugins.delete_success_format",
                "Plugin '{0}' was staged for deletion. Restart the app to finish removing it.",
                entry.Manifest.Name);
            SetPackageImportStatus(
                F(
                    "settings.plugins.delete_success_format",
                    "Plugin '{0}' was staged for deletion. Restart the app to finish removing it.",
                    entry.Manifest.Name),
                isError: false);
        }
        catch (Exception ex)
        {
            SetPackageImportStatus(
                F(
                    "settings.plugins.delete_failed_detail_format",
                    "Failed to delete plugin '{0}': {1}",
                    entry.Manifest.Name,
                    ex.Message),
                isError: true);
        }
    }

    private void OnPluginEnabledChanged(PluginRuntimeService runtime, PluginCatalogEntry entry, bool isEnabled)
    {
        try
        {
            if (!runtime.SetPluginEnabled(entry.Manifest.Id, isEnabled))
            {
                return;
            }

            RefreshFromRuntime();
            var toggleState = isEnabled
                ? L("settings.plugins.toggle_state_enabled", "enabled")
                : L("settings.plugins.toggle_state_disabled", "disabled");
            SetPackageImportStatus(
                F(
                    "settings.plugins.toggle_result_format",
                    "Plugin '{0}' was {1} for the next launch. Restart the app to apply page and widget changes.",
                    entry.Manifest.Name,
                    toggleState),
                isError: false);
        }
        catch (Exception ex)
        {
            SetPackageImportStatus(
                F(
                    "settings.plugins.toggle_failed_detail_format",
                    "Failed to update plugin '{0}': {1}",
                    entry.Manifest.Name,
                    ex.Message),
                isError: true);
        }
    }

    private void RefreshPluginNavigation(TopLevel? topLevel)
    {
        switch (topLevel)
        {
            case MainWindow mainWindow:
                mainWindow.RefreshPluginSettingsNavigation();
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
        var publisher = string.IsNullOrWhiteSpace(entry.Manifest.Author)
            ? L("settings.plugins.publisher_unknown", "Unknown publisher")
            : entry.Manifest.Author;
        return F(
            "settings.plugins.publisher_format",
            "Publisher: {0}",
            publisher);
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

    private FluentIcons.Avalonia.Fluent.SymbolIconSource CreatePluginCatalogIconSource()
    {
        return new FluentIcons.Avalonia.Fluent.SymbolIconSource
        {
            Symbol = FluentIcons.Common.Symbol.PuzzlePiece,
            IconVariant = FluentIcons.Common.IconVariant.Regular
        };
    }

    private Control CreatePluginCatalogActions(PluginRuntimeService runtime, PluginCatalogEntry entry)
    {
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                CreateEnablePluginToggle(runtime, entry),
                CreateDeletePluginButton(runtime, entry)
            }
        };
    }

    private ToggleSwitch CreateEnablePluginToggle(PluginRuntimeService runtime, PluginCatalogEntry entry)
    {
        var toggle = new ToggleSwitch
        {
            IsChecked = entry.IsEnabled,
            VerticalAlignment = VerticalAlignment.Center
        };

        ToolTip.SetTip(
            toggle,
            entry.IsEnabled
                ? L("settings.plugins.toggle_off", "Disable")
                : L("settings.plugins.toggle_on", "Enable"));
        toggle.IsCheckedChanged += (_, _) => OnPluginEnabledChanged(runtime, entry, toggle.IsChecked == true);
        return toggle;
    }

    private Button CreateDeletePluginButton(PluginRuntimeService runtime, PluginCatalogEntry entry)
    {
        var button = new Button
        {
            Width = 36,
            Height = 36,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Content = new FluentIcons.Avalonia.Fluent.SymbolIcon
            {
                Symbol = FluentIcons.Common.Symbol.Delete,
                IconVariant = FluentIcons.Common.IconVariant.Regular,
                FontSize = 18,
                Foreground = DestructiveBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        ToolTip.SetTip(button, L("settings.plugins.delete_button", "Delete plugin"));
        button.Click += (_, _) => OnDeletePluginClick(runtime, entry);
        return button;
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
