namespace LanMountainDesktop.PluginSdk;

public sealed class SettingsOptionChoice
{
    public SettingsOptionChoice(string value, string titleLocalizationKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        ArgumentException.ThrowIfNullOrWhiteSpace(titleLocalizationKey);

        Value = value.Trim();
        TitleLocalizationKey = titleLocalizationKey.Trim();
    }

    public string Value { get; }

    public string TitleLocalizationKey { get; }
}
