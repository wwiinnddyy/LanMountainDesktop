using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using LanMountainDesktop.Controls;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.ViewModels;

namespace LanMountainDesktop.Views.SettingsPages;

public partial class GeneratedPluginSettingsPage : SettingsPageBase
{
    public GeneratedPluginSettingsPage()
        : this(Design.IsDesignMode ? CreateDesignTimeViewModel() : CreateDefaultViewModel())
    {
    }

    public GeneratedPluginSettingsPage(PluginGeneratedSettingsPageViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        InitializeComponent();

        if (DescriptionTextBlock is not null)
        {
            DescriptionTextBlock.IsVisible = !string.IsNullOrWhiteSpace(ViewModel.Description);
        }

        BuildDynamicOptions();
    }

    public PluginGeneratedSettingsPageViewModel ViewModel { get; }
    private void BuildDynamicOptions()
    {
        if (DynamicOptionsHost is null)
        {
            return;
        }

        DynamicOptionsHost.Children.Clear();
        foreach (var option in ViewModel.Section.Options)
        {
            DynamicOptionsHost.Children.Add(CreateOptionControl(option));
        }
    }

    private Control CreateOptionControl(SettingsOptionDefinition option)
    {
        var title = ViewModel.Localizer.GetString(option.TitleLocalizationKey, option.TitleLocalizationKey);
        var description = string.IsNullOrWhiteSpace(option.DescriptionLocalizationKey)
            ? null
            : ViewModel.Localizer.GetString(option.DescriptionLocalizationKey, option.DescriptionLocalizationKey);
        var card = new SettingsOptionCard
        {
            IconKey = "Settings",
            Title = title,
            Description = description
        };

        switch (option.OptionType)
        {
            case SettingsOptionType.Toggle:
                card.ActionContent = CreateToggle(option);
                break;
            case SettingsOptionType.Number:
                card.DetailsContent = CreateNumber(option);
                break;
            case SettingsOptionType.Select:
                card.DetailsContent = CreateSelect(option);
                break;
            case SettingsOptionType.Path:
                card.DetailsContent = CreateText(option, "Path");
                break;
            case SettingsOptionType.List:
                card.DetailsContent = CreateText(option, "Comma-separated values");
                break;
            default:
                card.DetailsContent = CreateText(option, null);
                break;
        }

        return card;
    }

    private Control CreateToggle(SettingsOptionDefinition option)
    {
        var toggleSwitch = new ToggleSwitch
        {
            IsChecked = ViewModel.SettingsService.GetValue<bool?>(
                SettingsScope.Plugin,
                option.Key,
                ViewModel.PluginId,
                sectionId: ViewModel.Section.Id) ?? (option.DefaultValue as bool? ?? false)
        };

        toggleSwitch.IsCheckedChanged += (_, _) =>
        {
            ViewModel.SettingsService.SetValue(
                SettingsScope.Plugin,
                option.Key,
                toggleSwitch.IsChecked == true,
                ViewModel.PluginId,
                sectionId: ViewModel.Section.Id,
                changedKeys: [option.Key]);
        };

        return toggleSwitch;
    }

    private Control CreateNumber(SettingsOptionDefinition option)
    {
        var currentValue = ViewModel.SettingsService.GetValue<double?>(
            SettingsScope.Plugin,
            option.Key,
            ViewModel.PluginId,
            sectionId: ViewModel.Section.Id);

        var numeric = new NumericUpDown
        {
            Minimum = (decimal)(option.Minimum ?? 0d),
            Maximum = (decimal)(option.Maximum ?? 9999d),
            Value = (decimal)(currentValue ?? Convert.ToDouble(option.DefaultValue ?? 0d))
        };

        numeric.ValueChanged += (_, _) =>
        {
            ViewModel.SettingsService.SetValue(
                SettingsScope.Plugin,
                option.Key,
                (double)(numeric.Value ?? 0m),
                ViewModel.PluginId,
                sectionId: ViewModel.Section.Id,
                changedKeys: [option.Key]);
        };

        return numeric;
    }

    private Control CreateSelect(SettingsOptionDefinition option)
    {
        var choices = option.Choices
            .Select(choice => new SelectionOption(
                choice.Value,
                ViewModel.Localizer.GetString(choice.TitleLocalizationKey, choice.TitleLocalizationKey)))
            .ToArray();

        var comboBox = new ComboBox
        {
            ItemsSource = choices
        };

        var currentValue = ViewModel.SettingsService.GetValue<string>(
            SettingsScope.Plugin,
            option.Key,
            ViewModel.PluginId,
            sectionId: ViewModel.Section.Id);
        comboBox.SelectedItem = choices.FirstOrDefault(choice =>
            string.Equals(choice.Value, currentValue ?? option.DefaultValue?.ToString(), StringComparison.OrdinalIgnoreCase));

        comboBox.SelectionChanged += (_, _) =>
        {
            if (comboBox.SelectedItem is not SelectionOption selected)
            {
                return;
            }

            ViewModel.SettingsService.SetValue(
                SettingsScope.Plugin,
                option.Key,
                selected.Value,
                ViewModel.PluginId,
                sectionId: ViewModel.Section.Id,
                changedKeys: [option.Key]);
        };

        return comboBox;
    }

    private Control CreateText(SettingsOptionDefinition option, string? watermark)
    {
        var currentValue = option.OptionType == SettingsOptionType.List
            ? string.Join(
                ", ",
                ViewModel.SettingsService.GetValue<IReadOnlyList<string>>(
                    SettingsScope.Plugin,
                    option.Key,
                    ViewModel.PluginId,
                    sectionId: ViewModel.Section.Id) ?? (option.DefaultValue as IReadOnlyList<string> ?? []))
            : ViewModel.SettingsService.GetValue<string>(
                SettingsScope.Plugin,
                option.Key,
                ViewModel.PluginId,
                sectionId: ViewModel.Section.Id) ?? option.DefaultValue?.ToString() ?? string.Empty;

        var textBox = new TextBox
        {
            Watermark = watermark,
            Text = currentValue
        };

        textBox.LostFocus += (_, _) =>
        {
            object value = option.OptionType == SettingsOptionType.List
                ? textBox.Text?
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToArray() ?? []
                : textBox.Text ?? string.Empty;

            ViewModel.SettingsService.SetValue(
                SettingsScope.Plugin,
                option.Key,
                value,
                ViewModel.PluginId,
                sectionId: ViewModel.Section.Id,
                changedKeys: [option.Key]);
        };

        return textBox;
    }

    private static PluginGeneratedSettingsPageViewModel CreateDefaultViewModel()
    {
        return new PluginGeneratedSettingsPageViewModel(
            HostSettingsFacadeProvider.GetOrCreate().Settings,
            string.Empty,
            new PluginSettingsSectionRegistration("_preview", "preview", []),
            new PluginLocalizer(AppContext.BaseDirectory, "en-US"));
    }

    private static PluginGeneratedSettingsPageViewModel CreateDesignTimeViewModel()
    {
        const string pluginId = "preview.plugin";
        var settingsService = new DesignTimeSettingsService();
        var section = new PluginSettingsSectionRegistration(
            "desktop_preview",
            "Preview Widget Settings",
            [
                new SettingsOptionDefinition(
                    "enable_glow",
                    SettingsOptionType.Toggle,
                    "Enable glow",
                    "Adds a soft highlight around the preview widget.",
                    true),
                new SettingsOptionDefinition(
                    "refresh_minutes",
                    SettingsOptionType.Number,
                    "Refresh interval",
                    "How often the plugin refreshes its cached content.",
                    30d,
                    minimum: 5d,
                    maximum: 120d),
                new SettingsOptionDefinition(
                    "layout_density",
                    SettingsOptionType.Select,
                    "Layout density",
                    "Choose how compact the widget layout should feel.",
                    "balanced",
                    [
                        new SettingsOptionChoice("compact", "Compact"),
                        new SettingsOptionChoice("balanced", "Balanced"),
                        new SettingsOptionChoice("comfortable", "Comfortable")
                    ]),
                new SettingsOptionDefinition(
                    "content_path",
                    SettingsOptionType.Path,
                    "Content folder",
                    "Local folder used by the plugin for mock assets.",
                    @"C:\Preview\PluginAssets"),
                new SettingsOptionDefinition(
                    "keywords",
                    SettingsOptionType.List,
                    "Pinned keywords",
                    "Comma-separated topics that will be emphasized in the widget.",
                    new[] { "avalonia", "preview", "design-time" })
            ],
            "Mock plugin settings shown only in Avalonia design mode.");

        settingsService.SetValue(
            SettingsScope.Plugin,
            "enable_glow",
            true,
            pluginId,
            sectionId: section.Id);
        settingsService.SetValue(
            SettingsScope.Plugin,
            "refresh_minutes",
            30d,
            pluginId,
            sectionId: section.Id);
        settingsService.SetValue(
            SettingsScope.Plugin,
            "layout_density",
            "balanced",
            pluginId,
            sectionId: section.Id);
        settingsService.SetValue(
            SettingsScope.Plugin,
            "content_path",
            @"C:\Preview\PluginAssets",
            pluginId,
            sectionId: section.Id);
        settingsService.SetValue(
            SettingsScope.Plugin,
            "keywords",
            new[] { "avalonia", "preview", "design-time" },
            pluginId,
            sectionId: section.Id);

        return new PluginGeneratedSettingsPageViewModel(
            settingsService,
            pluginId,
            section,
            new PluginLocalizer(AppContext.BaseDirectory, "en-US"));
    }

    private sealed class DesignTimeSettingsService : ISettingsService
    {
        private readonly Dictionary<string, object?> _values = new(StringComparer.OrdinalIgnoreCase);

        public event EventHandler<SettingsChangedEvent>? Changed;

        public T LoadSnapshot<T>(SettingsScope scope, string? subjectId = null, string? placementId = null) where T : new()
            => new();

        public void SaveSnapshot<T>(
            SettingsScope scope,
            T snapshot,
            string? subjectId = null,
            string? placementId = null,
            string? sectionId = null,
            IReadOnlyCollection<string>? changedKeys = null)
        {
            RaiseChanged(scope, subjectId, placementId, sectionId, changedKeys);
        }

        public T LoadSection<T>(
            SettingsScope scope,
            string subjectId,
            string sectionId,
            string? placementId = null) where T : new()
            => new();

        public void SaveSection<T>(
            SettingsScope scope,
            string subjectId,
            string sectionId,
            T section,
            string? placementId = null,
            IReadOnlyCollection<string>? changedKeys = null)
        {
            RaiseChanged(scope, subjectId, placementId, sectionId, changedKeys);
        }

        public void DeleteSection(
            SettingsScope scope,
            string subjectId,
            string sectionId,
            string? placementId = null)
        {
            var prefix = BuildStorageKey(scope, subjectId, placementId, sectionId, key: null);
            foreach (var existingKey in _values.Keys.Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray())
            {
                _values.Remove(existingKey);
            }

            RaiseChanged(scope, subjectId, placementId, sectionId, changedKeys: null);
        }

        public T? GetValue<T>(
            SettingsScope scope,
            string key,
            string? subjectId = null,
            string? placementId = null,
            string? sectionId = null)
        {
            return _values.TryGetValue(BuildStorageKey(scope, subjectId, placementId, sectionId, key), out var value)
                ? ConvertValue<T>(value)
                : default;
        }

        public void SetValue<T>(
            SettingsScope scope,
            string key,
            T value,
            string? subjectId = null,
            string? placementId = null,
            string? sectionId = null,
            IReadOnlyCollection<string>? changedKeys = null)
        {
            _values[BuildStorageKey(scope, subjectId, placementId, sectionId, key)] = value;
            RaiseChanged(scope, subjectId, placementId, sectionId, changedKeys ?? [key]);
        }

        public IComponentSettingsAccessor GetComponentAccessor(string componentId, string? placementId)
        {
            return new DesignTimeComponentSettingsAccessor(this, componentId, placementId);
        }

        private static T? ConvertValue<T>(object? value)
        {
            if (value is null)
            {
                return default;
            }

            if (value is T typedValue)
            {
                return typedValue;
            }

            var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            try
            {
                return (T?)Convert.ChangeType(value, targetType);
            }
            catch
            {
                return default;
            }
        }

        private static string BuildStorageKey(
            SettingsScope scope,
            string? subjectId,
            string? placementId,
            string? sectionId,
            string? key)
        {
            return string.Join(
                "|",
                scope,
                subjectId ?? string.Empty,
                placementId ?? string.Empty,
                sectionId ?? string.Empty,
                key ?? string.Empty);
        }

        private void RaiseChanged(
            SettingsScope scope,
            string? subjectId,
            string? placementId,
            string? sectionId,
            IReadOnlyCollection<string>? changedKeys)
        {
            Changed?.Invoke(this, new SettingsChangedEvent(scope, subjectId, placementId, sectionId, changedKeys));
        }
    }

    private sealed class DesignTimeComponentSettingsAccessor : IComponentSettingsAccessor
    {
        private readonly DesignTimeSettingsService _settingsService;

        public DesignTimeComponentSettingsAccessor(
            DesignTimeSettingsService settingsService,
            string componentId,
            string? placementId)
        {
            _settingsService = settingsService;
            ComponentId = componentId;
            PlacementId = placementId;
        }

        public string ComponentId { get; }

        public string? PlacementId { get; }

        public T LoadSnapshot<T>() where T : new()
            => _settingsService.LoadSnapshot<T>(SettingsScope.ComponentInstance, ComponentId, PlacementId);

        public void SaveSnapshot<T>(T snapshot, IReadOnlyCollection<string>? changedKeys = null)
            => _settingsService.SaveSnapshot(SettingsScope.ComponentInstance, snapshot, ComponentId, PlacementId, changedKeys: changedKeys);

        public T LoadSection<T>(string sectionId) where T : new()
            => _settingsService.LoadSection<T>(SettingsScope.ComponentInstance, ComponentId, sectionId, PlacementId);

        public void SaveSection<T>(string sectionId, T section, IReadOnlyCollection<string>? changedKeys = null)
            => _settingsService.SaveSection(SettingsScope.ComponentInstance, ComponentId, sectionId, section, PlacementId, changedKeys);

        public void DeleteSection(string sectionId)
            => _settingsService.DeleteSection(SettingsScope.ComponentInstance, ComponentId, sectionId, PlacementId);

        public T? GetValue<T>(string key)
            => _settingsService.GetValue<T>(SettingsScope.ComponentInstance, key, ComponentId, PlacementId);

        public void SetValue<T>(string key, T value, IReadOnlyCollection<string>? changedKeys = null)
            => _settingsService.SetValue(SettingsScope.ComponentInstance, key, value, ComponentId, PlacementId, changedKeys: changedKeys);
    }
}
