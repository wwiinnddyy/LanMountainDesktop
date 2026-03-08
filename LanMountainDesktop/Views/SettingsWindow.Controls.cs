using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using LibVLCSharp.Avalonia;
namespace LanMountainDesktop.Views;

public partial class SettingsWindow
{
    // --- WallpaperSettingsPage ---
    internal TextBlock WallpaperPanelTitleTextBlock => WallpaperSettingsPanel.FindControl<TextBlock>("WallpaperPanelTitleTextBlock")!;
    internal TextBlock WallpaperPathTextBlock => WallpaperSettingsPanel.FindControl<TextBlock>("WallpaperPathTextBlock")!;
    internal TextBlock WallpaperStatusTextBlock => WallpaperSettingsPanel.FindControl<TextBlock>("WallpaperStatusTextBlock")!;
    internal ComboBox WallpaperPlacementComboBox => WallpaperSettingsPanel.FindControl<ComboBox>("WallpaperPlacementComboBox")!;
    internal Border WallpaperPreviewHost => WallpaperSettingsPanel.FindControl<Border>("WallpaperPreviewHost")!;
    internal Border WallpaperPreviewFrame => WallpaperSettingsPanel.FindControl<Border>("WallpaperPreviewFrame")!;
    internal Border WallpaperPreviewViewport => WallpaperSettingsPanel.FindControl<Border>("WallpaperPreviewViewport")!;
    internal LibVLCSharp.Avalonia.VideoView? WallpaperPreviewVideoView => WallpaperSettingsPanel.FindControl<LibVLCSharp.Avalonia.VideoView>("WallpaperPreviewVideoView");
    internal Grid WallpaperPreviewGrid => WallpaperSettingsPanel.FindControl<Grid>("WallpaperPreviewGrid")!;
    internal Border WallpaperPreviewTopStatusBarHost => WallpaperSettingsPanel.FindControl<Border>("WallpaperPreviewTopStatusBarHost")!;
    internal StackPanel WallpaperPreviewTopStatusComponentsPanel => WallpaperSettingsPanel.FindControl<StackPanel>("WallpaperPreviewTopStatusComponentsPanel")!;
    internal LanMountainDesktop.Views.Components.ClockWidget WallpaperPreviewClockWidget => WallpaperSettingsPanel.FindControl<LanMountainDesktop.Views.Components.ClockWidget>("WallpaperPreviewClockWidget")!;
    internal Border WallpaperPreviewBottomTaskbarContainer => WallpaperSettingsPanel.FindControl<Border>("WallpaperPreviewBottomTaskbarContainer")!;
    internal Border WallpaperPreviewTaskbarFixedActionsHost => WallpaperSettingsPanel.FindControl<Border>("WallpaperPreviewTaskbarFixedActionsHost")!;
    internal StackPanel WallpaperPreviewBackButtonVisual => WallpaperSettingsPanel.FindControl<StackPanel>("WallpaperPreviewBackButtonVisual")!;
    internal TextBlock WallpaperPreviewBackButtonTextBlock => WallpaperSettingsPanel.FindControl<TextBlock>("WallpaperPreviewBackButtonTextBlock")!;
    internal StackPanel WallpaperPreviewTaskbarDynamicActionsHost => WallpaperSettingsPanel.FindControl<StackPanel>("WallpaperPreviewTaskbarDynamicActionsHost")!;
    internal Border WallpaperPreviewTaskbarSettingsActionHost => WallpaperSettingsPanel.FindControl<Border>("WallpaperPreviewTaskbarSettingsActionHost")!;
    internal StackPanel WallpaperPreviewComponentLibraryVisual => WallpaperSettingsPanel.FindControl<StackPanel>("WallpaperPreviewComponentLibraryVisual")!;
    internal TextBlock WallpaperPreviewComponentLibraryTextBlock => WallpaperSettingsPanel.FindControl<TextBlock>("WallpaperPreviewComponentLibraryTextBlock")!;
    internal FluentIcons.Avalonia.SymbolIcon WallpaperPreviewSettingsButtonIcon => WallpaperSettingsPanel.FindControl<FluentIcons.Avalonia.SymbolIcon>("WallpaperPreviewSettingsButtonIcon")!;
    internal Button PickWallpaperButton => WallpaperSettingsPanel.FindControl<Button>("PickWallpaperButton")!;
    internal Button ClearWallpaperButton => WallpaperSettingsPanel.FindControl<Button>("ClearWallpaperButton")!;
    internal FluentAvalonia.UI.Controls.SettingsExpander WallpaperPlacementSettingsExpander => WallpaperSettingsPanel.FindControl<FluentAvalonia.UI.Controls.SettingsExpander>("WallpaperPlacementSettingsExpander")!;

    // --- GridSettingsPage ---
    internal TextBlock GridPanelTitleTextBlock => GridSettingsPanel.FindControl<TextBlock>("GridPanelTitleTextBlock")!;
    internal Border GridPreviewHost => GridSettingsPanel.FindControl<Border>("GridPreviewHost")!;
    internal Border GridPreviewFrame => GridSettingsPanel.FindControl<Border>("GridPreviewFrame")!;
    internal Border GridPreviewViewport => GridSettingsPanel.FindControl<Border>("GridPreviewViewport")!;
    internal Canvas GridPreviewLinesCanvas => GridSettingsPanel.FindControl<Canvas>("GridPreviewLinesCanvas")!;
    internal Grid GridPreviewGrid => GridSettingsPanel.FindControl<Grid>("GridPreviewGrid")!;
    internal Border GridPreviewTopStatusBarHost => GridSettingsPanel.FindControl<Border>("GridPreviewTopStatusBarHost")!;
    internal StackPanel GridPreviewTopStatusComponentsPanel => GridSettingsPanel.FindControl<StackPanel>("GridPreviewTopStatusComponentsPanel")!;
    internal Border GridPreviewBottomTaskbarContainer => GridSettingsPanel.FindControl<Border>("GridPreviewBottomTaskbarContainer")!;
    internal Border GridPreviewTaskbarFixedActionsHost => GridSettingsPanel.FindControl<Border>("GridPreviewTaskbarFixedActionsHost")!;
    internal StackPanel GridPreviewBackButtonVisual => GridSettingsPanel.FindControl<StackPanel>("GridPreviewBackButtonVisual")!;
    internal TextBlock GridPreviewBackButtonTextBlock => GridSettingsPanel.FindControl<TextBlock>("GridPreviewBackButtonTextBlock")!;
    internal StackPanel GridPreviewTaskbarDynamicActionsHost => GridSettingsPanel.FindControl<StackPanel>("GridPreviewTaskbarDynamicActionsHost")!;
    internal Border GridPreviewTaskbarSettingsActionHost => GridSettingsPanel.FindControl<Border>("GridPreviewTaskbarSettingsActionHost")!;
    internal StackPanel GridPreviewComponentLibraryVisual => GridSettingsPanel.FindControl<StackPanel>("GridPreviewComponentLibraryVisual")!;
    internal FluentIcons.Avalonia.FluentIcon GridPreviewComponentLibraryIcon => GridSettingsPanel.FindControl<FluentIcons.Avalonia.FluentIcon>("GridPreviewComponentLibraryIcon")!;
    internal TextBlock GridPreviewComponentLibraryTextBlock => GridSettingsPanel.FindControl<TextBlock>("GridPreviewComponentLibraryTextBlock")!;
    internal FluentIcons.Avalonia.SymbolIcon GridPreviewSettingsButtonIcon => GridSettingsPanel.FindControl<FluentIcons.Avalonia.SymbolIcon>("GridPreviewSettingsButtonIcon")!;
    internal FluentAvalonia.UI.Controls.SettingsExpander GridRowsSettingsExpander => GridSettingsPanel.FindControl<FluentAvalonia.UI.Controls.SettingsExpander>("GridRowsSettingsExpander")!;
    internal FluentAvalonia.UI.Controls.SettingsExpander GridSpacingSettingsExpander => GridSettingsPanel.FindControl<FluentAvalonia.UI.Controls.SettingsExpander>("GridSpacingSettingsExpander")!;
    internal FluentAvalonia.UI.Controls.SettingsExpander GridEdgeInsetSettingsExpander => GridSettingsPanel.FindControl<FluentAvalonia.UI.Controls.SettingsExpander>("GridEdgeInsetSettingsExpander")!;
    internal Slider GridSizeSlider => GridSettingsPanel.FindControl<Slider>("GridSizeSlider")!;
    internal FluentAvalonia.UI.Controls.NumberBox GridSizeNumberBox => GridSettingsPanel.FindControl<FluentAvalonia.UI.Controls.NumberBox>("GridSizeNumberBox")!;
    internal ComboBox GridSpacingPresetComboBox => GridSettingsPanel.FindControl<ComboBox>("GridSpacingPresetComboBox")!;
    internal ComboBoxItem GridSpacingRelaxedComboBoxItem => GridSettingsPanel.FindControl<ComboBoxItem>("GridSpacingRelaxedComboBoxItem")!;
    internal ComboBoxItem GridSpacingCompactComboBoxItem => GridSettingsPanel.FindControl<ComboBoxItem>("GridSpacingCompactComboBoxItem")!;
    internal Slider GridEdgeInsetSlider => GridSettingsPanel.FindControl<Slider>("GridEdgeInsetSlider")!;
    internal FluentAvalonia.UI.Controls.NumberBox GridEdgeInsetNumberBox => GridSettingsPanel.FindControl<FluentAvalonia.UI.Controls.NumberBox>("GridEdgeInsetNumberBox")!;
    internal TextBlock GridEdgeInsetComputedPxTextBlock => GridSettingsPanel.FindControl<TextBlock>("GridEdgeInsetComputedPxTextBlock")!;
    internal Button ApplyGridButton => GridSettingsPanel.FindControl<Button>("ApplyGridButton")!;
    internal TextBlock GridInfoTextBlock => GridSettingsPanel.FindControl<TextBlock>("GridInfoTextBlock")!;

    // --- ColorSettingsPage ---
    internal TextBlock ColorPanelTitleTextBlock => ColorSettingsPanel.FindControl<TextBlock>("ColorPanelTitleTextBlock")!;
    internal FluentAvalonia.UI.Controls.SettingsExpander ThemeModeSettingsExpander => ColorSettingsPanel.FindControl<FluentAvalonia.UI.Controls.SettingsExpander>("ThemeModeSettingsExpander")!;
    internal FluentAvalonia.UI.Controls.SettingsExpander ThemeColorSettingsExpander => ColorSettingsPanel.FindControl<FluentAvalonia.UI.Controls.SettingsExpander>("ThemeColorSettingsExpander")!;
    internal ToggleSwitch NightModeToggleSwitch => ColorSettingsPanel.FindControl<ToggleSwitch>("NightModeToggleSwitch")!;
    internal TextBlock ThemeColorStatusTextBlock => ColorSettingsPanel.FindControl<TextBlock>("ThemeColorStatusTextBlock")!;
    internal TextBlock RecommendedColorsLabelTextBlock => ColorSettingsPanel.FindControl<TextBlock>("RecommendedColorsLabelTextBlock")!;
    internal TextBlock SystemMonetColorsLabelTextBlock => ColorSettingsPanel.FindControl<TextBlock>("SystemMonetColorsLabelTextBlock")!;
    internal Button RecommendedColorButton1 => ColorSettingsPanel.FindControl<Button>("RecommendedColorButton1")!;
    internal Button RecommendedColorButton2 => ColorSettingsPanel.FindControl<Button>("RecommendedColorButton2")!;
    internal Button RecommendedColorButton3 => ColorSettingsPanel.FindControl<Button>("RecommendedColorButton3")!;
    internal Button RecommendedColorButton4 => ColorSettingsPanel.FindControl<Button>("RecommendedColorButton4")!;
    internal Button RecommendedColorButton5 => ColorSettingsPanel.FindControl<Button>("RecommendedColorButton5")!;
    internal Button RecommendedColorButton6 => ColorSettingsPanel.FindControl<Button>("RecommendedColorButton6")!;
    internal Border RecommendedColorSwatch1 => ColorSettingsPanel.FindControl<Border>("RecommendedColorSwatch1")!;
    internal Border RecommendedColorSwatch2 => ColorSettingsPanel.FindControl<Border>("RecommendedColorSwatch2")!;
    internal Border RecommendedColorSwatch3 => ColorSettingsPanel.FindControl<Border>("RecommendedColorSwatch3")!;
    internal Border RecommendedColorSwatch4 => ColorSettingsPanel.FindControl<Border>("RecommendedColorSwatch4")!;
    internal Border RecommendedColorSwatch5 => ColorSettingsPanel.FindControl<Border>("RecommendedColorSwatch5")!;
    internal Border RecommendedColorSwatch6 => ColorSettingsPanel.FindControl<Border>("RecommendedColorSwatch6")!;
    internal Button RefreshMonetColorsButton => ColorSettingsPanel.FindControl<Button>("RefreshMonetColorsButton")!;
    internal Button MonetColorButton1 => ColorSettingsPanel.FindControl<Button>("MonetColorButton1")!;
    internal Button MonetColorButton2 => ColorSettingsPanel.FindControl<Button>("MonetColorButton2")!;
    internal Button MonetColorButton3 => ColorSettingsPanel.FindControl<Button>("MonetColorButton3")!;
    internal Button MonetColorButton4 => ColorSettingsPanel.FindControl<Button>("MonetColorButton4")!;
    internal Button MonetColorButton5 => ColorSettingsPanel.FindControl<Button>("MonetColorButton5")!;
    internal Button MonetColorButton6 => ColorSettingsPanel.FindControl<Button>("MonetColorButton6")!;
    internal Border MonetColorSwatch1 => ColorSettingsPanel.FindControl<Border>("MonetColorSwatch1")!;
    internal Border MonetColorSwatch2 => ColorSettingsPanel.FindControl<Border>("MonetColorSwatch2")!;
    internal Border MonetColorSwatch3 => ColorSettingsPanel.FindControl<Border>("MonetColorSwatch3")!;
    internal Border MonetColorSwatch4 => ColorSettingsPanel.FindControl<Border>("MonetColorSwatch4")!;
    internal Border MonetColorSwatch5 => ColorSettingsPanel.FindControl<Border>("MonetColorSwatch5")!;
    internal Border MonetColorSwatch6 => ColorSettingsPanel.FindControl<Border>("MonetColorSwatch6")!;

    // --- StatusBarSettingsPage ---
    internal TextBlock StatusBarPanelTitleTextBlock => StatusBarSettingsPanel.FindControl<TextBlock>("StatusBarPanelTitleTextBlock")!;
    internal FluentAvalonia.UI.Controls.SettingsExpander StatusBarClockSettingsExpander => StatusBarSettingsPanel.FindControl<FluentAvalonia.UI.Controls.SettingsExpander>("StatusBarClockSettingsExpander")!;
    internal FluentAvalonia.UI.Controls.SettingsExpander StatusBarSpacingSettingsExpander => StatusBarSettingsPanel.FindControl<FluentAvalonia.UI.Controls.SettingsExpander>("StatusBarSpacingSettingsExpander")!;
    internal ToggleSwitch StatusBarClockToggleSwitch => StatusBarSettingsPanel.FindControl<ToggleSwitch>("StatusBarClockToggleSwitch")!;
    internal RadioButton ClockFormatHMSSRadio => StatusBarSettingsPanel.FindControl<RadioButton>("ClockFormatHMSSRadio")!;
    internal RadioButton ClockFormatHMRadio => StatusBarSettingsPanel.FindControl<RadioButton>("ClockFormatHMRadio")!;
    internal ComboBox StatusBarSpacingModeComboBox => StatusBarSettingsPanel.FindControl<ComboBox>("StatusBarSpacingModeComboBox")!;
    internal ComboBoxItem StatusBarSpacingModeCompactItem => StatusBarSettingsPanel.FindControl<ComboBoxItem>("StatusBarSpacingModeCompactItem")!;
    internal ComboBoxItem StatusBarSpacingModeRelaxedItem => StatusBarSettingsPanel.FindControl<ComboBoxItem>("StatusBarSpacingModeRelaxedItem")!;
    internal ComboBoxItem StatusBarSpacingModeCustomItem => StatusBarSettingsPanel.FindControl<ComboBoxItem>("StatusBarSpacingModeCustomItem")!;
    internal FluentAvalonia.UI.Controls.SettingsExpanderItem StatusBarSpacingCustomPanel => StatusBarSettingsPanel.FindControl<FluentAvalonia.UI.Controls.SettingsExpanderItem>("StatusBarSpacingCustomPanel")!;
    internal Slider StatusBarSpacingSlider => StatusBarSettingsPanel.FindControl<Slider>("StatusBarSpacingSlider")!;
    internal FluentAvalonia.UI.Controls.NumberBox StatusBarSpacingNumberBox => StatusBarSettingsPanel.FindControl<FluentAvalonia.UI.Controls.NumberBox>("StatusBarSpacingNumberBox")!;
    internal TextBlock StatusBarSpacingComputedPxTextBlock => StatusBarSettingsPanel.FindControl<TextBlock>("StatusBarSpacingComputedPxTextBlock")!;

    // --- RegionSettingsPage ---
    internal TextBlock RegionPanelTitleTextBlock => RegionSettingsPanel.FindControl<TextBlock>("RegionPanelTitleTextBlock")!;
    internal FluentAvalonia.UI.Controls.SettingsExpander LanguageSettingsExpander => RegionSettingsPanel.FindControl<FluentAvalonia.UI.Controls.SettingsExpander>("LanguageSettingsExpander")!;
    internal FluentAvalonia.UI.Controls.SettingsExpander TimeZoneSettingsExpander => RegionSettingsPanel.FindControl<FluentAvalonia.UI.Controls.SettingsExpander>("TimeZoneSettingsExpander")!;
    internal ComboBox LanguageComboBox => RegionSettingsPanel.FindControl<ComboBox>("LanguageComboBox")!;
    internal ComboBoxItem LanguageChineseItem => RegionSettingsPanel.FindControl<ComboBoxItem>("LanguageChineseItem")!;
    internal ComboBoxItem LanguageEnglishItem => RegionSettingsPanel.FindControl<ComboBoxItem>("LanguageEnglishItem")!;
    internal ComboBox TimeZoneComboBox => RegionSettingsPanel.FindControl<ComboBox>("TimeZoneComboBox")!;

    // --- WeatherSettingsPage ---
    internal TextBlock WeatherPanelTitleTextBlock => WeatherSettingsPanel.FindControl<TextBlock>("WeatherPanelTitleTextBlock")!;
    internal FluentAvalonia.UI.Controls.SettingsExpander WeatherPreviewSettingsExpander => WeatherSettingsPanel.FindControl<FluentAvalonia.UI.Controls.SettingsExpander>("WeatherPreviewSettingsExpander")!;
    internal FluentAvalonia.UI.Controls.SettingsExpander WeatherLocationSettingsExpander => WeatherSettingsPanel.FindControl<FluentAvalonia.UI.Controls.SettingsExpander>("WeatherLocationSettingsExpander")!;
    internal FluentAvalonia.UI.Controls.SettingsExpander WeatherCitySearchSettingsExpander => WeatherSettingsPanel.FindControl<FluentAvalonia.UI.Controls.SettingsExpander>("WeatherCitySearchSettingsExpander")!;
    internal FluentAvalonia.UI.Controls.SettingsExpander WeatherCoordinateSettingsExpander => WeatherSettingsPanel.FindControl<FluentAvalonia.UI.Controls.SettingsExpander>("WeatherCoordinateSettingsExpander")!;
    internal FluentAvalonia.UI.Controls.SettingsExpander WeatherAlertFilterSettingsExpander => WeatherSettingsPanel.FindControl<FluentAvalonia.UI.Controls.SettingsExpander>("WeatherAlertFilterSettingsExpander")!;
    internal FluentAvalonia.UI.Controls.SettingsExpander WeatherIconPackSettingsExpander => WeatherSettingsPanel.FindControl<FluentAvalonia.UI.Controls.SettingsExpander>("WeatherIconPackSettingsExpander")!;
    internal FluentAvalonia.UI.Controls.SettingsExpander WeatherNoTlsSettingsExpander => WeatherSettingsPanel.FindControl<FluentAvalonia.UI.Controls.SettingsExpander>("WeatherNoTlsSettingsExpander")!;
    internal Button WeatherPreviewButton => WeatherSettingsPanel.FindControl<Button>("WeatherPreviewButton")!;
    internal ComboBox WeatherLocationModeComboBox => WeatherSettingsPanel.FindControl<ComboBox>("WeatherLocationModeComboBox")!;
    internal ComboBoxItem WeatherLocationModeCityItem => WeatherSettingsPanel.FindControl<ComboBoxItem>("WeatherLocationModeCityItem")!;
    internal ComboBoxItem WeatherLocationModeCoordinatesItem => WeatherSettingsPanel.FindControl<ComboBoxItem>("WeatherLocationModeCoordinatesItem")!;
    internal ListBoxItem WeatherLocationModeCityChipItem => WeatherSettingsPanel.FindControl<ListBoxItem>("WeatherLocationModeCityChipItem")!;
    internal ListBoxItem WeatherLocationModeCoordinatesChipItem => WeatherSettingsPanel.FindControl<ListBoxItem>("WeatherLocationModeCoordinatesChipItem")!;
    internal ListBox WeatherLocationModeChipListBox => WeatherSettingsPanel.FindControl<ListBox>("WeatherLocationModeChipListBox")!;
    internal ToggleSwitch WeatherAutoRefreshToggleSwitch => WeatherSettingsPanel.FindControl<ToggleSwitch>("WeatherAutoRefreshToggleSwitch")!;
    internal Button WeatherSearchButton => WeatherSettingsPanel.FindControl<Button>("WeatherSearchButton")!;
    internal Button WeatherApplyCityButton => WeatherSettingsPanel.FindControl<Button>("WeatherApplyCityButton")!;
    internal Button WeatherApplyCoordinatesButton => WeatherSettingsPanel.FindControl<Button>("WeatherApplyCoordinatesButton")!;
    internal TextBox WeatherExcludedAlertsTextBox => WeatherSettingsPanel.FindControl<TextBox>("WeatherExcludedAlertsTextBox")!;
    internal ComboBox WeatherIconPackComboBox => WeatherSettingsPanel.FindControl<ComboBox>("WeatherIconPackComboBox")!;
    internal ToggleSwitch WeatherNoTlsToggleSwitch => WeatherSettingsPanel.FindControl<ToggleSwitch>("WeatherNoTlsToggleSwitch")!;
    internal TextBox WeatherCitySearchTextBox => WeatherSettingsPanel.FindControl<TextBox>("WeatherCitySearchTextBox")!;
    internal ComboBox WeatherCityResultsComboBox => WeatherSettingsPanel.FindControl<ComboBox>("WeatherCityResultsComboBox")!;
    internal TextBlock WeatherSearchStatusTextBlock => WeatherSettingsPanel.FindControl<TextBlock>("WeatherSearchStatusTextBlock")!;
    internal TextBox WeatherLocationKeyTextBox => WeatherSettingsPanel.FindControl<TextBox>("WeatherLocationKeyTextBox")!;
    internal TextBox WeatherLocationNameTextBox => WeatherSettingsPanel.FindControl<TextBox>("WeatherLocationNameTextBox")!;
    internal FluentAvalonia.UI.Controls.NumberBox WeatherLatitudeNumberBox => WeatherSettingsPanel.FindControl<FluentAvalonia.UI.Controls.NumberBox>("WeatherLatitudeNumberBox")!;
    internal FluentAvalonia.UI.Controls.NumberBox WeatherLongitudeNumberBox => WeatherSettingsPanel.FindControl<FluentAvalonia.UI.Controls.NumberBox>("WeatherLongitudeNumberBox")!;
    internal TextBlock WeatherCoordinateStatusTextBlock => WeatherSettingsPanel.FindControl<TextBlock>("WeatherCoordinateStatusTextBlock")!;
    internal TextBlock WeatherPreviewResultTextBlock => WeatherSettingsPanel.FindControl<TextBlock>("WeatherPreviewResultTextBlock")!;
    internal FluentIcons.Avalonia.Fluent.SymbolIcon WeatherPreviewIconSymbol => WeatherSettingsPanel.FindControl<FluentIcons.Avalonia.Fluent.SymbolIcon>("WeatherPreviewIconSymbol")!;
    internal TextBlock WeatherPreviewTemperatureTextBlock => WeatherSettingsPanel.FindControl<TextBlock>("WeatherPreviewTemperatureTextBlock")!;
    internal TextBlock WeatherPreviewUpdatedTextBlock => WeatherSettingsPanel.FindControl<TextBlock>("WeatherPreviewUpdatedTextBlock")!;
    internal FluentAvalonia.UI.Controls.ProgressRing WeatherSearchProgressRing => WeatherSettingsPanel.FindControl<FluentAvalonia.UI.Controls.ProgressRing>("WeatherSearchProgressRing")!;
    internal FluentAvalonia.UI.Controls.ProgressRing WeatherPreviewProgressRing => WeatherSettingsPanel.FindControl<FluentAvalonia.UI.Controls.ProgressRing>("WeatherPreviewProgressRing")!;
    internal ComboBoxItem WeatherIconPackFluentRegularItem => WeatherSettingsPanel.FindControl<ComboBoxItem>("WeatherIconPackFluentRegularItem")!;
    internal ComboBoxItem WeatherIconPackFluentFilledItem => WeatherSettingsPanel.FindControl<ComboBoxItem>("WeatherIconPackFluentFilledItem")!;
    internal TextBlock WeatherLocationStatusTextBlock => WeatherSettingsPanel.FindControl<TextBlock>("WeatherLocationStatusTextBlock")!;

    // --- UpdateSettingsPage ---
    internal TextBlock UpdatePanelTitleTextBlock => UpdateSettingsPanel.FindControl<TextBlock>("UpdatePanelTitleTextBlock")!;
    internal FluentAvalonia.UI.Controls.SettingsExpander UpdateOptionsSettingsExpander => UpdateSettingsPanel.FindControl<FluentAvalonia.UI.Controls.SettingsExpander>("UpdateOptionsSettingsExpander")!;
    internal FluentAvalonia.UI.Controls.SettingsExpander UpdateActionsSettingsExpander => UpdateSettingsPanel.FindControl<FluentAvalonia.UI.Controls.SettingsExpander>("UpdateActionsSettingsExpander")!;
    internal TextBlock UpdateCurrentVersionLabelTextBlock => UpdateSettingsPanel.FindControl<TextBlock>("UpdateCurrentVersionLabelTextBlock")!;
    internal TextBlock UpdateCurrentVersionValueTextBlock => UpdateSettingsPanel.FindControl<TextBlock>("UpdateCurrentVersionValueTextBlock")!;
    internal TextBlock UpdateLatestVersionLabelTextBlock => UpdateSettingsPanel.FindControl<TextBlock>("UpdateLatestVersionLabelTextBlock")!;
    internal TextBlock UpdateLatestVersionValueTextBlock => UpdateSettingsPanel.FindControl<TextBlock>("UpdateLatestVersionValueTextBlock")!;
    internal TextBlock UpdatePublishedAtLabelTextBlock => UpdateSettingsPanel.FindControl<TextBlock>("UpdatePublishedAtLabelTextBlock")!;
    internal TextBlock UpdatePublishedAtValueTextBlock => UpdateSettingsPanel.FindControl<TextBlock>("UpdatePublishedAtValueTextBlock")!;
    internal TextBlock UpdateChannelLabelTextBlock => UpdateSettingsPanel.FindControl<TextBlock>("UpdateChannelLabelTextBlock")!;
    internal ListBoxItem UpdateChannelStableChipItem => UpdateSettingsPanel.FindControl<ListBoxItem>("UpdateChannelStableChipItem")!;
    internal ListBoxItem UpdateChannelPreviewChipItem => UpdateSettingsPanel.FindControl<ListBoxItem>("UpdateChannelPreviewChipItem")!;
    internal ToggleSwitch AutoCheckUpdatesToggleSwitch => UpdateSettingsPanel.FindControl<ToggleSwitch>("AutoCheckUpdatesToggleSwitch")! ;
    internal ListBox UpdateChannelChipListBox => UpdateSettingsPanel.FindControl<ListBox>("UpdateChannelChipListBox")!;
    internal Button CheckForUpdatesButton => UpdateSettingsPanel.FindControl<Button>("CheckForUpdatesButton")!;
    internal Button DownloadAndInstallUpdateButton => UpdateSettingsPanel.FindControl<Button>("DownloadAndInstallUpdateButton")!;
    internal ProgressBar UpdateDownloadProgressBar => UpdateSettingsPanel.FindControl<ProgressBar>("UpdateDownloadProgressBar")!;
    internal TextBlock UpdateDownloadProgressTextBlock => UpdateSettingsPanel.FindControl<TextBlock>("UpdateDownloadProgressTextBlock")!;
    internal TextBlock UpdateStatusTextBlock => UpdateSettingsPanel.FindControl<TextBlock>("UpdateStatusTextBlock")!;

    // --- AboutSettingsPage ---
    internal TextBlock AboutPanelTitleTextBlock => AboutSettingsPanel.FindControl<TextBlock>("AboutPanelTitleTextBlock")!;
    internal FluentAvalonia.UI.Controls.SettingsExpander AboutStartupSettingsExpander => AboutSettingsPanel.FindControl<FluentAvalonia.UI.Controls.SettingsExpander>("AboutStartupSettingsExpander")!;
    internal ToggleSwitch AutoStartWithWindowsToggleSwitch => AboutSettingsPanel.FindControl<ToggleSwitch>("AutoStartWithWindowsToggleSwitch")!;
    internal TextBlock VersionTextBlock => AboutSettingsPanel.FindControl<TextBlock>("VersionTextBlock")!;
    internal TextBlock CodeNameTextBlock => AboutSettingsPanel.FindControl<TextBlock>("CodeNameTextBlock")!;
    internal TextBlock FontInfoTextBlock => AboutSettingsPanel.FindControl<TextBlock>("FontInfoTextBlock")!;

    // --- LauncherSettingsPage ---
    internal TextBlock LauncherSettingsPanelTitleTextBlock => LauncherSettingsPanel.FindControl<TextBlock>("LauncherSettingsPanelTitleTextBlock")!;
    internal FluentAvalonia.UI.Controls.SettingsExpander LauncherHiddenItemsSettingsExpander => LauncherSettingsPanel.FindControl<FluentAvalonia.UI.Controls.SettingsExpander>("LauncherHiddenItemsSettingsExpander")!;
    internal StackPanel LauncherHiddenItemsListPanel => LauncherSettingsPanel.FindControl<StackPanel>("LauncherHiddenItemsListPanel")!;
    internal TextBlock LauncherHiddenItemsEmptyTextBlock => LauncherSettingsPanel.FindControl<TextBlock>("LauncherHiddenItemsEmptyTextBlock")!;
    internal TextBlock LauncherHiddenItemsDescriptionTextBlock => LauncherSettingsPanel.FindControl<TextBlock>("LauncherHiddenItemsDescriptionTextBlock")!;

    // --- PluginSettingsPage (Added for completeness) ---
    internal TextBlock PluginSettingsPanelTitleTextBlock => PluginSettingsPanel.FindControl<TextBlock>("PluginSettingsPanelTitleTextBlock")!;
    internal FluentAvalonia.UI.Controls.SettingsExpander PluginSystemSettingsExpander => PluginSettingsPanel.FindControl<FluentAvalonia.UI.Controls.SettingsExpander>("PluginSystemSettingsExpander")!;
    internal TextBlock PluginSystemDescriptionTextBlock => PluginSettingsPanel.FindControl<TextBlock>("PluginSystemDescriptionTextBlock")!;
    internal TextBlock PluginSystemStatusTextBlock => PluginSettingsPanel.FindControl<TextBlock>("PluginSystemStatusTextBlock")!;
}
