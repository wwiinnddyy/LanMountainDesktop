using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using LanMountainDesktop.Controls;
using LanMountainDesktop.AirAppSdk;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.ViewModels;

namespace LanMountainDesktop.Views.SettingsPages;

public partial class GeneratedAirAppSettingsPage : AirAppSettingsPageBase
{
    public GeneratedAirAppSettingsPage()
        : this(Design.IsDesignMode ? CreateDesignTimeViewModel() : CreateDefaultViewModel())
    {
    }

    public GeneratedAirAppSettingsPage(AirAppGeneratedSettingsPageViewModel viewModel)
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

    public AirAppGeneratedSettingsPageViewModel ViewModel { get; }
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

    private Control CreateOptionControl(AirAppSettingOptionDefinition option)
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
            case AirAppSettingOptionType.Toggle:
                card.ActionContent = CreateToggle(option);
                break;
            case AirAppSettingOptionType.Number:
                card.DetailsContent = CreateNumber(option);
                break;
            case AirAppSettingOptionType.Select:
                card.DetailsContent = CreateSelect(option);
                break;
            case AirAppSettingOptionType.Path:
                card.DetailsContent = CreateText(option, "Path");
                break;
            case AirAppSettingOptionType.List:
                card.DetailsContent = CreateText(option, "Comma-separated values");
                break;
            default:
                card.DetailsContent = CreateText(option, null);
                break;
        }

        return card;
    }

    private Control CreateToggle(AirAppSettingOptionDefinition option)
    {
        var toggleSwitch = new ToggleSwitch
        {
            IsChecked = ViewModel.SettingsService.GetValue<bool?>(
                AirAppSettingsScope.AirApp,
                option.Key,
                ViewModel.AirAppId,
                sectionId: ViewModel.Section.Id) ?? (option.DefaultValue as bool? ?? false)
        };

        toggleSwitch.IsCheckedChanged += (_, _) =>
        {
            ViewModel.SettingsService.SetValue(
                AirAppSettingsScope.AirApp,
                option.Key,
                toggleSwitch.IsChecked == true,
                ViewModel.AirAppId,
                sectionId: ViewModel.Section.Id,
                changedKeys: [option.Key]);
        };

        return toggleSwitch;
    }

    private Control CreateNumber(AirAppSettingOptionDefinition option)
    {
        var currentValue = ViewModel.SettingsService.GetValue<double?>(
            AirAppSettingsScope.AirApp,
            option.Key,
            ViewModel.AirAppId,
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
                AirAppSettingsScope.AirApp,
                option.Key,
                (double)(numeric.Value ?? 0m),
                ViewModel.AirAppId,
                sectionId: ViewModel.Section.Id,
                changedKeys: [option.Key]);
        };

        return numeric;
    }

    private Control CreateSelect(AirAppSettingOptionDefinition option)
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
            AirAppSettingsScope.AirApp,
            option.Key,
            ViewModel.AirAppId,
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
                AirAppSettingsScope.AirApp,
                option.Key,
                selected.Value,
                ViewModel.AirAppId,
                sectionId: ViewModel.Section.Id,
                changedKeys: [option.Key]);
        };

        return comboBox;
    }

    private Control CreateText(AirAppSettingOptionDefinition option, string? watermark)
    {
        var currentValue = option.OptionType == AirAppSettingOptionType.List
            ? string.Join(
                ", ",
                ViewModel.SettingsService.GetValue<IReadOnlyList<string>>(
                    AirAppSettingsScope.AirApp,
                    option.Key,
                    ViewModel.AirAppId,
                    sectionId: ViewModel.Section.Id) ?? (option.DefaultValue as IReadOnlyList<string> ?? []))
            : ViewModel.SettingsService.GetValue<string>(
                AirAppSettingsScope.AirApp,
                option.Key,
                ViewModel.AirAppId,
                sectionId: ViewModel.Section.Id) ?? option.DefaultValue?.ToString() ?? string.Empty;

        var textBox = new TextBox
        {
            Watermark = watermark,
            Text = currentValue
        };

        textBox.LostFocus += (_, _) =>
        {
            object value = option.OptionType == AirAppSettingOptionType.List
                ? textBox.Text?
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToArray() ?? []
                : textBox.Text ?? string.Empty;

            ViewModel.SettingsService.SetValue(
                AirAppSettingsScope.AirApp,
                option.Key,
                value,
                ViewModel.AirAppId,
                sectionId: ViewModel.Section.Id,
                changedKeys: [option.Key]);
        };

        return textBox;
    }

    private static AirAppGeneratedSettingsPageViewModel CreateDefaultViewModel()
    {
        return new AirAppGeneratedSettingsPageViewModel(
            HostSettingsFacadeProvider.GetOrCreate().Settings,
            string.Empty,
            new AirAppSettingsSectionRegistration("_preview", "preview", []),
            new AirAppLocalizer(AppContext.BaseDirectory, "en-US"));
    }

    private static AirAppGeneratedSettingsPageViewModel CreateDesignTimeViewModel()
    {
        const string pluginId = "preview.plugin";
        var settingsService = new DesignTimeSettingsService();
        var section = new AirAppSettingsSectionRegistration(
            "desktop_preview",
            "Preview Widget Settings",
            [
                new AirAppSettingOptionDefinition(
                    "enable_glow",
                    AirAppSettingOptionType.Toggle,
                    "Enable glow",
                    "Adds a soft highlight around the preview widget.",
                    true),
                new AirAppSettingOptionDefinition(
                    "refresh_minutes",
                    AirAppSettingOptionType.Number,
                    "Refresh interval",
                    "How often the plugin refreshes its cached content.",
                    30d,
                    minimum: 5d,
                    maximum: 120d),
                new AirAppSettingOptionDefinition(
                    "layout_density",
                    AirAppSettingOptionType.Select,
                    "Layout density",
                    "Choose how compact the widget layout should feel.",
                    "balanced",
                    [
                        new AirAppSettingOptionChoice("compact", "Compact"),
                        new AirAppSettingOptionChoice("balanced", "Balanced"),
                        new AirAppSettingOptionChoice("comfortable", "Comfortable")
                    ]),
                new AirAppSettingOptionDefinition(
                    "content_path",
                    AirAppSettingOptionType.Path,
                    "Content folder",
                    "Local folder used by the plugin for mock assets.",
                    @"C:\Preview\AirAppAssets"),
                new AirAppSettingOptionDefinition(
                    "keywords",
                    AirAppSettingOptionType.List,
                    "Pinned keywords",
                    "Comma-separated topics that will be emphasized in the widget.",
                    new[] { "avalonia", "preview", "design-time" })
            ],
            "Mock plugin settings shown only in Avalonia design mode.");

        settingsService.SetValue(
            AirAppSettingsScope.AirApp,
            "enable_glow",
            true,
            pluginId,
            sectionId: section.Id);
        settingsService.SetValue(
            AirAppSettingsScope.AirApp,
            "refresh_minutes",
            30d,
            pluginId,
            sectionId: section.Id);
        settingsService.SetValue(
            AirAppSettingsScope.AirApp,
            "layout_density",
            "balanced",
            pluginId,
            sectionId: section.Id);
        settingsService.SetValue(
            AirAppSettingsScope.AirApp,
            "content_path",
            @"C:\Preview\AirAppAssets",
            pluginId,
            sectionId: section.Id);
        settingsService.SetValue(
            AirAppSettingsScope.AirApp,
            "keywords",
            new[] { "avalonia", "preview", "design-time" },
            pluginId,
            sectionId: section.Id);

        return new AirAppGeneratedSettingsPageViewModel(
            settingsService,
            pluginId,
            section,
            new AirAppLocalizer(AppContext.BaseDirectory, "en-US"));
    }

    private sealed class DesignTimeSettingsService : ISettingsService
    {
        private readonly Dictionary<string, object?> _values = new(StringComparer.OrdinalIgnoreCase);

        public event EventHandler<SettingsChangedEvent>? Changed;

        public T LoadSnapshot<T>(AirAppSettingsScope scope, string? subjectId = null, string? placementId = null) where T : new()
            => new();

        public void SaveSnapshot<T>(
            AirAppSettingsScope scope,
            T snapshot,
            string? subjectId = null,
            string? placementId = null,
            string? sectionId = null,
            IReadOnlyCollection<string>? changedKeys = null)
        {
            RaiseChanged(scope, subjectId, placementId, sectionId, changedKeys);
        }

        public T LoadSection<T>(
            AirAppSettingsScope scope,
            string subjectId,
            string sectionId,
            string? placementId = null) where T : new()
            => new();

        public void SaveSection<T>(
            AirAppSettingsScope scope,
            string subjectId,
            string sectionId,
            T section,
            string? placementId = null,
            IReadOnlyCollection<string>? changedKeys = null)
        {
            RaiseChanged(scope, subjectId, placementId, sectionId, changedKeys);
        }

        public void DeleteSection(
            AirAppSettingsScope scope,
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
            AirAppSettingsScope scope,
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
            AirAppSettingsScope scope,
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
            AirAppSettingsScope scope,
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
            AirAppSettingsScope scope,
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
            => _settingsService.LoadSnapshot<T>(AirAppSettingsScope.ComponentInstance, ComponentId, PlacementId);

        public void SaveSnapshot<T>(T snapshot, IReadOnlyCollection<string>? changedKeys = null)
            => _settingsService.SaveSnapshot(AirAppSettingsScope.ComponentInstance, snapshot, ComponentId, PlacementId, changedKeys: changedKeys);

        public T LoadSection<T>(string sectionId) where T : new()
            => _settingsService.LoadSection<T>(AirAppSettingsScope.ComponentInstance, ComponentId, sectionId, PlacementId);

        public void SaveSection<T>(string sectionId, T section, IReadOnlyCollection<string>? changedKeys = null)
            => _settingsService.SaveSection(AirAppSettingsScope.ComponentInstance, ComponentId, sectionId, section, PlacementId, changedKeys);

        public void DeleteSection(string sectionId)
            => _settingsService.DeleteSection(AirAppSettingsScope.ComponentInstance, ComponentId, sectionId, PlacementId);

        public T? GetValue<T>(string key)
            => _settingsService.GetValue<T>(AirAppSettingsScope.ComponentInstance, key, ComponentId, PlacementId);

        public void SetValue<T>(string key, T value, IReadOnlyCollection<string>? changedKeys = null)
            => _settingsService.SetValue(AirAppSettingsScope.ComponentInstance, key, value, ComponentId, PlacementId, changedKeys: changedKeys);
    }
}
