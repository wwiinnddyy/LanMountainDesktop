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
        : this(
            new PluginGeneratedSettingsPageViewModel(
                HostSettingsFacadeProvider.GetOrCreate().Settings,
                string.Empty,
                new PluginSettingsSectionRegistration("_preview", "preview", []),
                new PluginLocalizer(AppContext.BaseDirectory, "en-US")))
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
}
