using System.Collections.Generic;

namespace LanMountainDesktop.AirAppSdk;

public sealed class AirAppSettingOptionDefinition
{
    public AirAppSettingOptionDefinition(
        string key,
        AirAppSettingOptionType optionType,
        string titleLocalizationKey,
        string? descriptionLocalizationKey = null,
        object? defaultValue = null,
        IReadOnlyList<AirAppSettingOptionChoice>? choices = null,
        double? minimum = null,
        double? maximum = null,
        string? validationPattern = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(titleLocalizationKey);

        Key = key.Trim();
        OptionType = optionType;
        TitleLocalizationKey = titleLocalizationKey.Trim();
        DescriptionLocalizationKey = string.IsNullOrWhiteSpace(descriptionLocalizationKey)
            ? null
            : descriptionLocalizationKey.Trim();
        DefaultValue = defaultValue;
        Choices = choices ?? [];
        Minimum = minimum;
        Maximum = maximum;
        ValidationPattern = string.IsNullOrWhiteSpace(validationPattern)
            ? null
            : validationPattern.Trim();
    }

    public string Key { get; }

    public AirAppSettingOptionType OptionType { get; }

    public string TitleLocalizationKey { get; }

    public string? DescriptionLocalizationKey { get; }

    public object? DefaultValue { get; }

    public IReadOnlyList<AirAppSettingOptionChoice> Choices { get; }

    public double? Minimum { get; }

    public double? Maximum { get; }

    public string? ValidationPattern { get; }
}
