namespace LanMountainDesktop.AirAppSdk;

public sealed class AirAppSettingOptionChoice
{
    public AirAppSettingOptionChoice(string value, string titleLocalizationKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        ArgumentException.ThrowIfNullOrWhiteSpace(titleLocalizationKey);

        Value = value.Trim();
        TitleLocalizationKey = titleLocalizationKey.Trim();
    }

    public string Value { get; }

    public string TitleLocalizationKey { get; }
}
