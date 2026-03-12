using System;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using LanMountainDesktop.Views.Components;
using LanMountainDesktop.Views.SettingsPages;

namespace LanMountainDesktop.Views;

public partial class SettingsWindow
{
    private string CurrentControlAccessStage => _controlsBound ? "module init" : "control binding";

    private T? TryGetOptionalPageControl<T>(Control? pageRoot, string controlName)
        where T : Control
    {
        return pageRoot?.FindControl<T>(controlName);
    }

    private T RequirePageControl<T>(Control pageRoot, string controlName)
        where T : Control
    {
        var control = pageRoot.FindControl<T>(controlName);
        if (control is null)
        {
            throw new InvalidOperationException(
                $"Independent settings module control resolution failed. Page='{pageRoot.Name ?? pageRoot.GetType().Name}'; Control='{controlName}'; Stage='{CurrentControlAccessStage}'.");
        }

        return control;
    }

    private T RequireSettingsPage<T>(T? page, string pageName)
        where T : Control
    {
        return page ?? throw new InvalidOperationException(
            $"Independent settings module page resolution failed. Page='{pageName}'; Stage='{CurrentControlAccessStage}'.");
    }

    private WallpaperSettingsPage WallpaperSettingsPageRoot => RequireSettingsPage(WallpaperSettingsPanel, nameof(WallpaperSettingsPanel));
    private GridSettingsPage GridSettingsPageRoot => RequireSettingsPage(GridSettingsPanel, nameof(GridSettingsPanel));
    private ColorSettingsPage ColorSettingsPageRoot => RequireSettingsPage(ColorSettingsPanel, nameof(ColorSettingsPanel));
    private StatusBarSettingsPage StatusBarSettingsPageRoot => RequireSettingsPage(StatusBarSettingsPanel, nameof(StatusBarSettingsPanel));
    private RegionSettingsPage RegionSettingsPageRoot => RequireSettingsPage(RegionSettingsPanel, nameof(RegionSettingsPanel));
    private WeatherSettingsPage WeatherSettingsPageRoot => RequireSettingsPage(WeatherSettingsPanel, nameof(WeatherSettingsPanel));
    private UpdateSettingsPage UpdateSettingsPageRoot => RequireSettingsPage(UpdateSettingsPanel, nameof(UpdateSettingsPanel));
    private AboutSettingsPage AboutSettingsPageRoot => RequireSettingsPage(AboutSettingsPanel, nameof(AboutSettingsPanel));
    private LauncherSettingsPage LauncherSettingsPageRoot => RequireSettingsPage(LauncherSettingsPanel, nameof(LauncherSettingsPanel));

    private T WallpaperControl<T>(string name) where T : Control => RequirePageControl<T>(WallpaperSettingsPageRoot, name);
    private T GridControl<T>(string name) where T : Control => RequirePageControl<T>(GridSettingsPageRoot, name);
    private T ColorControl<T>(string name) where T : Control => RequirePageControl<T>(ColorSettingsPageRoot, name);
    private T StatusBarControl<T>(string name) where T : Control => RequirePageControl<T>(StatusBarSettingsPageRoot, name);
    private T RegionControl<T>(string name) where T : Control => RequirePageControl<T>(RegionSettingsPageRoot, name);
    private T WeatherControl<T>(string name) where T : Control => RequirePageControl<T>(WeatherSettingsPageRoot, name);
    private T UpdateControl<T>(string name) where T : Control => RequirePageControl<T>(UpdateSettingsPageRoot, name);
    private T AboutControl<T>(string name) where T : Control => RequirePageControl<T>(AboutSettingsPageRoot, name);
    private T LauncherControl<T>(string name) where T : Control => RequirePageControl<T>(LauncherSettingsPageRoot, name);

    internal TextBlock WallpaperPanelTitleTextBlock => WallpaperControl<TextBlock>("WallpaperPanelTitleTextBlock");
    internal TextBlock WallpaperPathTextBlock => WallpaperControl<TextBlock>("WallpaperPathTextBlock");
    internal TextBlock WallpaperStatusTextBlock => WallpaperControl<TextBlock>("WallpaperStatusTextBlock");
    internal ComboBox WallpaperPlacementComboBox => WallpaperControl<ComboBox>("WallpaperPlacementComboBox");
    internal Border WallpaperPreviewHost => WallpaperControl<Border>("WallpaperPreviewHost");
    internal Border WallpaperPreviewFrame => WallpaperControl<Border>("WallpaperPreviewFrame");
    internal Border WallpaperPreviewViewport => WallpaperControl<Border>("WallpaperPreviewViewport");
    internal Image WallpaperPreviewVideoImage => WallpaperControl<Image>("WallpaperPreviewVideoImage");
    internal Grid WallpaperPreviewGrid => WallpaperControl<Grid>("WallpaperPreviewGrid");
    internal Border WallpaperPreviewTopStatusBarHost => WallpaperControl<Border>("WallpaperPreviewTopStatusBarHost");
    internal StackPanel WallpaperPreviewTopStatusComponentsPanel => WallpaperControl<StackPanel>("WallpaperPreviewTopStatusComponentsPanel");
    internal ClockWidget WallpaperPreviewClockWidget => WallpaperControl<ClockWidget>("WallpaperPreviewClockWidget");
    internal Border WallpaperPreviewBottomTaskbarContainer => WallpaperControl<Border>("WallpaperPreviewBottomTaskbarContainer");
    internal Border WallpaperPreviewTaskbarFixedActionsHost => WallpaperControl<Border>("WallpaperPreviewTaskbarFixedActionsHost");
    internal StackPanel WallpaperPreviewBackButtonVisual => WallpaperControl<StackPanel>("WallpaperPreviewBackButtonVisual");
    internal TextBlock WallpaperPreviewBackButtonTextBlock => WallpaperControl<TextBlock>("WallpaperPreviewBackButtonTextBlock");
    internal StackPanel WallpaperPreviewTaskbarDynamicActionsHost => WallpaperControl<StackPanel>("WallpaperPreviewTaskbarDynamicActionsHost");
    internal Border WallpaperPreviewTaskbarSettingsActionHost => WallpaperControl<Border>("WallpaperPreviewTaskbarSettingsActionHost");
    internal StackPanel WallpaperPreviewComponentLibraryVisual => WallpaperControl<StackPanel>("WallpaperPreviewComponentLibraryVisual");
    internal TextBlock WallpaperPreviewComponentLibraryTextBlock => WallpaperControl<TextBlock>("WallpaperPreviewComponentLibraryTextBlock");
    internal FluentIcons.Avalonia.SymbolIcon WallpaperPreviewSettingsButtonIcon => WallpaperControl<FluentIcons.Avalonia.SymbolIcon>("WallpaperPreviewSettingsButtonIcon");
    internal Button PickWallpaperButton => WallpaperControl<Button>("PickWallpaperButton");
    internal Button ClearWallpaperButton => WallpaperControl<Button>("ClearWallpaperButton");
    internal FluentAvalonia.UI.Controls.SettingsExpander WallpaperPlacementSettingsExpander => WallpaperControl<FluentAvalonia.UI.Controls.SettingsExpander>("WallpaperPlacementSettingsExpander");
    private Image? OptionalWallpaperPreviewVideoImage => TryGetOptionalPageControl<Image>(WallpaperSettingsPanel, "WallpaperPreviewVideoImage");
    private Border? OptionalWallpaperPreviewViewport => TryGetOptionalPageControl<Border>(WallpaperSettingsPanel, "WallpaperPreviewViewport");
    private bool IsWallpaperSettingsPageVisible => string.Equals(NormalizeSettingsPageTag(_selectedSettingsTabTag), "Appearance", StringComparison.OrdinalIgnoreCase);

    internal TextBlock GridPanelTitleTextBlock => GridControl<TextBlock>("GridPanelTitleTextBlock");
    internal Border GridPreviewHost => GridControl<Border>("GridPreviewHost");
    internal Border GridPreviewFrame => GridControl<Border>("GridPreviewFrame");
    internal Border GridPreviewViewport => GridControl<Border>("GridPreviewViewport");
    internal Canvas GridPreviewLinesCanvas => GridControl<Canvas>("GridPreviewLinesCanvas");
    internal Grid GridPreviewGrid => GridControl<Grid>("GridPreviewGrid");
    internal Border GridPreviewTopStatusBarHost => GridControl<Border>("GridPreviewTopStatusBarHost");
    internal StackPanel GridPreviewTopStatusComponentsPanel => GridControl<StackPanel>("GridPreviewTopStatusComponentsPanel");
    internal Border GridPreviewBottomTaskbarContainer => GridControl<Border>("GridPreviewBottomTaskbarContainer");
    internal Border GridPreviewTaskbarFixedActionsHost => GridControl<Border>("GridPreviewTaskbarFixedActionsHost");
    internal StackPanel GridPreviewBackButtonVisual => GridControl<StackPanel>("GridPreviewBackButtonVisual");
    internal TextBlock GridPreviewBackButtonTextBlock => GridControl<TextBlock>("GridPreviewBackButtonTextBlock");
    internal StackPanel GridPreviewTaskbarDynamicActionsHost => GridControl<StackPanel>("GridPreviewTaskbarDynamicActionsHost");
    internal Border GridPreviewTaskbarSettingsActionHost => GridControl<Border>("GridPreviewTaskbarSettingsActionHost");
    internal StackPanel GridPreviewComponentLibraryVisual => GridControl<StackPanel>("GridPreviewComponentLibraryVisual");
    internal FluentIcons.Avalonia.FluentIcon GridPreviewComponentLibraryIcon => GridControl<FluentIcons.Avalonia.FluentIcon>("GridPreviewComponentLibraryIcon");
    internal TextBlock GridPreviewComponentLibraryTextBlock => GridControl<TextBlock>("GridPreviewComponentLibraryTextBlock");
    internal FluentIcons.Avalonia.SymbolIcon GridPreviewSettingsButtonIcon => GridControl<FluentIcons.Avalonia.SymbolIcon>("GridPreviewSettingsButtonIcon");
    internal FluentAvalonia.UI.Controls.SettingsExpander GridRowsSettingsExpander => GridControl<FluentAvalonia.UI.Controls.SettingsExpander>("GridRowsSettingsExpander");
    internal FluentAvalonia.UI.Controls.SettingsExpander GridSpacingSettingsExpander => GridControl<FluentAvalonia.UI.Controls.SettingsExpander>("GridSpacingSettingsExpander");
    internal FluentAvalonia.UI.Controls.SettingsExpander GridEdgeInsetSettingsExpander => GridControl<FluentAvalonia.UI.Controls.SettingsExpander>("GridEdgeInsetSettingsExpander");
    internal Slider GridSizeSlider => GridControl<Slider>("GridSizeSlider");
    internal FluentAvalonia.UI.Controls.NumberBox GridSizeNumberBox => GridControl<FluentAvalonia.UI.Controls.NumberBox>("GridSizeNumberBox");
    internal ComboBox GridSpacingPresetComboBox => GridControl<ComboBox>("GridSpacingPresetComboBox");
    internal ComboBoxItem GridSpacingRelaxedComboBoxItem => GridControl<ComboBoxItem>("GridSpacingRelaxedComboBoxItem");
    internal ComboBoxItem GridSpacingCompactComboBoxItem => GridControl<ComboBoxItem>("GridSpacingCompactComboBoxItem");
    internal Slider GridEdgeInsetSlider => GridControl<Slider>("GridEdgeInsetSlider");
    internal FluentAvalonia.UI.Controls.NumberBox GridEdgeInsetNumberBox => GridControl<FluentAvalonia.UI.Controls.NumberBox>("GridEdgeInsetNumberBox");
    internal TextBlock GridEdgeInsetComputedPxTextBlock => GridControl<TextBlock>("GridEdgeInsetComputedPxTextBlock");
    internal Button ApplyGridButton => GridControl<Button>("ApplyGridButton");
    internal TextBlock GridInfoTextBlock => GridControl<TextBlock>("GridInfoTextBlock");

    internal TextBlock ColorPanelTitleTextBlock => ColorControl<TextBlock>("ColorPanelTitleTextBlock");
    internal FluentAvalonia.UI.Controls.SettingsExpander ThemeModeSettingsExpander => ColorControl<FluentAvalonia.UI.Controls.SettingsExpander>("ThemeModeSettingsExpander");
    internal FluentAvalonia.UI.Controls.SettingsExpander ThemeColorSettingsExpander => ColorControl<FluentAvalonia.UI.Controls.SettingsExpander>("ThemeColorSettingsExpander");
    internal ToggleSwitch NightModeToggleSwitch => ColorControl<ToggleSwitch>("NightModeToggleSwitch");
    internal TextBlock ThemeColorStatusTextBlock => ColorControl<TextBlock>("ThemeColorStatusTextBlock");
    internal TextBlock RecommendedColorsLabelTextBlock => ColorControl<TextBlock>("RecommendedColorsLabelTextBlock");
    internal TextBlock SystemMonetColorsLabelTextBlock => ColorControl<TextBlock>("SystemMonetColorsLabelTextBlock");
    internal Button RecommendedColorButton1 => ColorControl<Button>("RecommendedColorButton1");
    internal Button RecommendedColorButton2 => ColorControl<Button>("RecommendedColorButton2");
    internal Button RecommendedColorButton3 => ColorControl<Button>("RecommendedColorButton3");
    internal Button RecommendedColorButton4 => ColorControl<Button>("RecommendedColorButton4");
    internal Button RecommendedColorButton5 => ColorControl<Button>("RecommendedColorButton5");
    internal Button RecommendedColorButton6 => ColorControl<Button>("RecommendedColorButton6");
    internal Border RecommendedColorSwatch1 => ColorControl<Border>("RecommendedColorSwatch1");
    internal Border RecommendedColorSwatch2 => ColorControl<Border>("RecommendedColorSwatch2");
    internal Border RecommendedColorSwatch3 => ColorControl<Border>("RecommendedColorSwatch3");
    internal Border RecommendedColorSwatch4 => ColorControl<Border>("RecommendedColorSwatch4");
    internal Border RecommendedColorSwatch5 => ColorControl<Border>("RecommendedColorSwatch5");
    internal Border RecommendedColorSwatch6 => ColorControl<Border>("RecommendedColorSwatch6");
    internal Button RefreshMonetColorsButton => ColorControl<Button>("RefreshMonetColorsButton");
    internal Button MonetColorButton1 => ColorControl<Button>("MonetColorButton1");
    internal Button MonetColorButton2 => ColorControl<Button>("MonetColorButton2");
    internal Button MonetColorButton3 => ColorControl<Button>("MonetColorButton3");
    internal Button MonetColorButton4 => ColorControl<Button>("MonetColorButton4");
    internal Button MonetColorButton5 => ColorControl<Button>("MonetColorButton5");
    internal Button MonetColorButton6 => ColorControl<Button>("MonetColorButton6");
    internal Border MonetColorSwatch1 => ColorControl<Border>("MonetColorSwatch1");
    internal Border MonetColorSwatch2 => ColorControl<Border>("MonetColorSwatch2");
    internal Border MonetColorSwatch3 => ColorControl<Border>("MonetColorSwatch3");
    internal Border MonetColorSwatch4 => ColorControl<Border>("MonetColorSwatch4");
    internal Border MonetColorSwatch5 => ColorControl<Border>("MonetColorSwatch5");
    internal Border MonetColorSwatch6 => ColorControl<Border>("MonetColorSwatch6");

    internal TextBlock StatusBarPanelTitleTextBlock => StatusBarControl<TextBlock>("StatusBarPanelTitleTextBlock");
    internal FluentAvalonia.UI.Controls.SettingsExpander StatusBarClockSettingsExpander => StatusBarControl<FluentAvalonia.UI.Controls.SettingsExpander>("StatusBarClockSettingsExpander");
    internal FluentAvalonia.UI.Controls.SettingsExpander StatusBarSpacingSettingsExpander => StatusBarControl<FluentAvalonia.UI.Controls.SettingsExpander>("StatusBarSpacingSettingsExpander");
    internal ToggleSwitch StatusBarClockToggleSwitch => StatusBarControl<ToggleSwitch>("StatusBarClockToggleSwitch");
    internal RadioButton ClockFormatHMSSRadio => StatusBarControl<RadioButton>("ClockFormatHMSSRadio");
    internal RadioButton ClockFormatHMRadio => StatusBarControl<RadioButton>("ClockFormatHMRadio");
    internal ComboBox StatusBarSpacingModeComboBox => StatusBarControl<ComboBox>("StatusBarSpacingModeComboBox");
    internal ComboBoxItem StatusBarSpacingModeCompactItem => StatusBarControl<ComboBoxItem>("StatusBarSpacingModeCompactItem");
    internal ComboBoxItem StatusBarSpacingModeRelaxedItem => StatusBarControl<ComboBoxItem>("StatusBarSpacingModeRelaxedItem");
    internal ComboBoxItem StatusBarSpacingModeCustomItem => StatusBarControl<ComboBoxItem>("StatusBarSpacingModeCustomItem");
    internal FluentAvalonia.UI.Controls.SettingsExpanderItem StatusBarSpacingCustomPanel => StatusBarControl<FluentAvalonia.UI.Controls.SettingsExpanderItem>("StatusBarSpacingCustomPanel");
    internal Slider StatusBarSpacingSlider => StatusBarControl<Slider>("StatusBarSpacingSlider");
    internal FluentAvalonia.UI.Controls.NumberBox StatusBarSpacingNumberBox => StatusBarControl<FluentAvalonia.UI.Controls.NumberBox>("StatusBarSpacingNumberBox");
    internal TextBlock StatusBarSpacingComputedPxTextBlock => StatusBarControl<TextBlock>("StatusBarSpacingComputedPxTextBlock");

    internal TextBlock RegionPanelTitleTextBlock => RegionControl<TextBlock>("RegionPanelTitleTextBlock");
    internal FluentAvalonia.UI.Controls.SettingsExpander LanguageSettingsExpander => RegionControl<FluentAvalonia.UI.Controls.SettingsExpander>("LanguageSettingsExpander");
    internal FluentAvalonia.UI.Controls.SettingsExpander TimeZoneSettingsExpander => RegionControl<FluentAvalonia.UI.Controls.SettingsExpander>("TimeZoneSettingsExpander");
    internal ComboBox LanguageComboBox => RegionControl<ComboBox>("LanguageComboBox");
    internal ComboBoxItem LanguageChineseItem => RegionControl<ComboBoxItem>("LanguageChineseItem");
    internal ComboBoxItem LanguageEnglishItem => RegionControl<ComboBoxItem>("LanguageEnglishItem");
    internal ComboBox TimeZoneComboBox => RegionControl<ComboBox>("TimeZoneComboBox");

    internal TextBlock WeatherPanelTitleTextBlock => WeatherControl<TextBlock>("WeatherPanelTitleTextBlock");
    internal TextBlock WeatherPreviewSectionTextBlock => WeatherControl<TextBlock>("WeatherPreviewSectionTextBlock");
    internal TextBlock WeatherSettingsSectionTextBlock => WeatherControl<TextBlock>("WeatherSettingsSectionTextBlock");
    internal FluentAvalonia.UI.Controls.SettingsExpander WeatherPreviewSettingsExpander => WeatherControl<FluentAvalonia.UI.Controls.SettingsExpander>("WeatherPreviewSettingsExpander");
    internal FluentAvalonia.UI.Controls.SettingsExpander WeatherLocationSettingsExpander => WeatherControl<FluentAvalonia.UI.Controls.SettingsExpander>("WeatherLocationSettingsExpander");
    internal FluentAvalonia.UI.Controls.SettingsExpander WeatherCitySearchSettingsExpander => WeatherControl<FluentAvalonia.UI.Controls.SettingsExpander>("WeatherCitySearchSettingsExpander");
    internal FluentAvalonia.UI.Controls.SettingsExpander WeatherCoordinateSettingsExpander => WeatherControl<FluentAvalonia.UI.Controls.SettingsExpander>("WeatherCoordinateSettingsExpander");
    internal FluentAvalonia.UI.Controls.SettingsExpander WeatherAlertFilterSettingsExpander => WeatherControl<FluentAvalonia.UI.Controls.SettingsExpander>("WeatherAlertFilterSettingsExpander");
    internal FluentAvalonia.UI.Controls.SettingsExpander WeatherIconPackSettingsExpander => WeatherControl<FluentAvalonia.UI.Controls.SettingsExpander>("WeatherIconPackSettingsExpander");
    internal FluentAvalonia.UI.Controls.SettingsExpander WeatherNoTlsSettingsExpander => WeatherControl<FluentAvalonia.UI.Controls.SettingsExpander>("WeatherNoTlsSettingsExpander");
    internal Button WeatherPreviewButton => WeatherControl<Button>("WeatherPreviewButton");
    internal ComboBox WeatherLocationModeComboBox => WeatherControl<ComboBox>("WeatherLocationModeComboBox");
    internal ComboBoxItem WeatherLocationModeCityItem => WeatherControl<ComboBoxItem>("WeatherLocationModeCityItem");
    internal ComboBoxItem WeatherLocationModeCoordinatesItem => WeatherControl<ComboBoxItem>("WeatherLocationModeCoordinatesItem");
    internal ListBoxItem WeatherLocationModeCityChipItem => WeatherControl<ListBoxItem>("WeatherLocationModeCityChipItem");
    internal ListBoxItem WeatherLocationModeCoordinatesChipItem => WeatherControl<ListBoxItem>("WeatherLocationModeCoordinatesChipItem");
    internal ListBox WeatherLocationModeChipListBox => WeatherControl<ListBox>("WeatherLocationModeChipListBox");
    internal ToggleSwitch WeatherAutoRefreshToggleSwitch => WeatherControl<ToggleSwitch>("WeatherAutoRefreshToggleSwitch");
    internal Button WeatherSearchButton => WeatherControl<Button>("WeatherSearchButton");
    internal Button WeatherApplyCityButton => WeatherControl<Button>("WeatherApplyCityButton");
    internal Button WeatherApplyCoordinatesButton => WeatherControl<Button>("WeatherApplyCoordinatesButton");
    internal TextBox WeatherExcludedAlertsTextBox => WeatherControl<TextBox>("WeatherExcludedAlertsTextBox");
    internal ComboBox WeatherIconPackComboBox => WeatherControl<ComboBox>("WeatherIconPackComboBox");
    internal ToggleSwitch WeatherNoTlsToggleSwitch => WeatherControl<ToggleSwitch>("WeatherNoTlsToggleSwitch");
    internal TextBox WeatherCitySearchTextBox => WeatherControl<TextBox>("WeatherCitySearchTextBox");
    internal ComboBox WeatherCityResultsComboBox => WeatherControl<ComboBox>("WeatherCityResultsComboBox");
    internal TextBlock WeatherSearchStatusTextBlock => WeatherControl<TextBlock>("WeatherSearchStatusTextBlock");
    internal TextBox WeatherLocationKeyTextBox => WeatherControl<TextBox>("WeatherLocationKeyTextBox");
    internal TextBox WeatherLocationNameTextBox => WeatherControl<TextBox>("WeatherLocationNameTextBox");
    internal FluentAvalonia.UI.Controls.NumberBox WeatherLatitudeNumberBox => WeatherControl<FluentAvalonia.UI.Controls.NumberBox>("WeatherLatitudeNumberBox");
    internal FluentAvalonia.UI.Controls.NumberBox WeatherLongitudeNumberBox => WeatherControl<FluentAvalonia.UI.Controls.NumberBox>("WeatherLongitudeNumberBox");
    internal TextBlock WeatherCoordinateStatusTextBlock => WeatherControl<TextBlock>("WeatherCoordinateStatusTextBlock");
    internal TextBlock WeatherPreviewResultTextBlock => WeatherControl<TextBlock>("WeatherPreviewResultTextBlock");
    internal Image WeatherPreviewIconImage => WeatherControl<Image>("WeatherPreviewIconImage");
    internal FluentIcons.Avalonia.Fluent.SymbolIcon WeatherPreviewIconSymbol => WeatherControl<FluentIcons.Avalonia.Fluent.SymbolIcon>("WeatherPreviewIconSymbol");
    internal TextBlock WeatherPreviewTemperatureTextBlock => WeatherControl<TextBlock>("WeatherPreviewTemperatureTextBlock");
    internal TextBlock WeatherPreviewUpdatedTextBlock => WeatherControl<TextBlock>("WeatherPreviewUpdatedTextBlock");
    internal FluentAvalonia.UI.Controls.ProgressRing WeatherSearchProgressRing => WeatherControl<FluentAvalonia.UI.Controls.ProgressRing>("WeatherSearchProgressRing");
    internal FluentAvalonia.UI.Controls.ProgressRing WeatherPreviewProgressRing => WeatherControl<FluentAvalonia.UI.Controls.ProgressRing>("WeatherPreviewProgressRing");
    internal ComboBoxItem WeatherIconPackFluentRegularItem => WeatherControl<ComboBoxItem>("WeatherIconPackFluentRegularItem");
    internal ComboBoxItem WeatherIconPackFluentFilledItem => WeatherControl<ComboBoxItem>("WeatherIconPackFluentFilledItem");
    internal TextBlock WeatherLocationSelectionTitleTextBlock => WeatherControl<TextBlock>("WeatherLocationSelectionTitleTextBlock");
    internal TextBlock WeatherLocationSelectionDescriptionTextBlock => WeatherControl<TextBlock>("WeatherLocationSelectionDescriptionTextBlock");
    internal TextBlock WeatherLocationValueTextBlock => WeatherControl<TextBlock>("WeatherLocationValueTextBlock");
    internal TextBlock WeatherLocationStatusTextBlock => WeatherControl<TextBlock>("WeatherLocationStatusTextBlock");
    internal TextBlock WeatherAlertListTitleTextBlock => WeatherControl<TextBlock>("WeatherAlertListTitleTextBlock");
    internal TextBlock WeatherAlertListDescriptionTextBlock => WeatherControl<TextBlock>("WeatherAlertListDescriptionTextBlock");
    internal TextBlock WeatherFooterHintTextBlock => WeatherControl<TextBlock>("WeatherFooterHintTextBlock");

    internal TextBlock UpdatePanelTitleTextBlock => UpdateControl<TextBlock>("UpdatePanelTitleTextBlock");
    internal FluentAvalonia.UI.Controls.SettingsExpander UpdateOptionsSettingsExpander => UpdateControl<FluentAvalonia.UI.Controls.SettingsExpander>("UpdateOptionsSettingsExpander");
    internal FluentAvalonia.UI.Controls.SettingsExpander UpdateActionsSettingsExpander => UpdateControl<FluentAvalonia.UI.Controls.SettingsExpander>("UpdateActionsSettingsExpander");
    internal TextBlock UpdateCurrentVersionLabelTextBlock => UpdateControl<TextBlock>("UpdateCurrentVersionLabelTextBlock");
    internal TextBlock UpdateCurrentVersionValueTextBlock => UpdateControl<TextBlock>("UpdateCurrentVersionValueTextBlock");
    internal TextBlock UpdateLatestVersionLabelTextBlock => UpdateControl<TextBlock>("UpdateLatestVersionLabelTextBlock");
    internal TextBlock UpdateLatestVersionValueTextBlock => UpdateControl<TextBlock>("UpdateLatestVersionValueTextBlock");
    internal TextBlock UpdatePublishedAtLabelTextBlock => UpdateControl<TextBlock>("UpdatePublishedAtLabelTextBlock");
    internal TextBlock UpdatePublishedAtValueTextBlock => UpdateControl<TextBlock>("UpdatePublishedAtValueTextBlock");
    internal TextBlock UpdateChannelLabelTextBlock => UpdateControl<TextBlock>("UpdateChannelLabelTextBlock");
    internal ListBoxItem UpdateChannelStableChipItem => UpdateControl<ListBoxItem>("UpdateChannelStableChipItem");
    internal ListBoxItem UpdateChannelPreviewChipItem => UpdateControl<ListBoxItem>("UpdateChannelPreviewChipItem");
    internal ToggleSwitch AutoCheckUpdatesToggleSwitch => UpdateControl<ToggleSwitch>("AutoCheckUpdatesToggleSwitch");
    internal ListBox UpdateChannelChipListBox => UpdateControl<ListBox>("UpdateChannelChipListBox");
    internal Button CheckForUpdatesButton => UpdateControl<Button>("CheckForUpdatesButton");
    internal Button DownloadAndInstallUpdateButton => UpdateControl<Button>("DownloadAndInstallUpdateButton");
    internal ProgressBar UpdateDownloadProgressBar => UpdateControl<ProgressBar>("UpdateDownloadProgressBar");
    internal TextBlock UpdateDownloadProgressTextBlock => UpdateControl<TextBlock>("UpdateDownloadProgressTextBlock");
    internal TextBlock UpdateStatusTextBlock => UpdateControl<TextBlock>("UpdateStatusTextBlock");

    internal TextBlock AboutPanelTitleTextBlock => AboutControl<TextBlock>("AboutPanelTitleTextBlock");
    internal FluentAvalonia.UI.Controls.SettingsExpander AboutStartupSettingsExpander => AboutControl<FluentAvalonia.UI.Controls.SettingsExpander>("AboutStartupSettingsExpander");
    internal FluentAvalonia.UI.Controls.SettingsExpander AboutRenderModeSettingsExpander => AboutControl<FluentAvalonia.UI.Controls.SettingsExpander>("AboutRenderModeSettingsExpander");
    internal ToggleSwitch AutoStartWithWindowsToggleSwitch => AboutControl<ToggleSwitch>("AutoStartWithWindowsToggleSwitch");
    internal ComboBox AppRenderModeComboBox => AboutControl<ComboBox>("AppRenderModeComboBox");
    internal TextBlock CurrentRenderBackendLabelTextBlock => AboutControl<TextBlock>("CurrentRenderBackendLabelTextBlock");
    internal TextBlock CurrentRenderBackendValueTextBlock => AboutControl<TextBlock>("CurrentRenderBackendValueTextBlock");
    internal TextBlock CurrentRenderBackendImplementationTextBlock => AboutControl<TextBlock>("CurrentRenderBackendImplementationTextBlock");
    internal TextBlock VersionTextBlock => AboutControl<TextBlock>("VersionTextBlock");
    internal TextBlock CodeNameTextBlock => AboutControl<TextBlock>("CodeNameTextBlock");
    internal TextBlock FontInfoTextBlock => AboutControl<TextBlock>("FontInfoTextBlock");

    internal TextBlock LauncherSettingsPanelTitleTextBlock => LauncherControl<TextBlock>("LauncherSettingsPanelTitleTextBlock");
    internal FluentAvalonia.UI.Controls.SettingsExpander LauncherHiddenItemsSettingsExpander => LauncherControl<FluentAvalonia.UI.Controls.SettingsExpander>("LauncherHiddenItemsSettingsExpander");
    internal TextBlock LauncherHiddenItemsEmptyTextBlock => LauncherControl<TextBlock>("LauncherHiddenItemsEmptyTextBlock");
    internal TextBlock LauncherHiddenItemsDescriptionTextBlock => LauncherControl<TextBlock>("LauncherHiddenItemsDescriptionTextBlock");
}
