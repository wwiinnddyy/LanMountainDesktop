using System.Collections.Generic;
using FluentIcons.Common;

namespace LanMountainDesktop.ComponentSystem;

public static class ComponentCategoryIconResolver
{
    public static Icon ResolveCategoryIcon(
        string categoryId,
        IEnumerable<DesktopComponentDefinition> categoryComponents)
    {
        if (string.Equals(categoryId, "all", StringComparison.OrdinalIgnoreCase))
        {
            return Icon.Apps;
        }

        var icon = categoryId.ToLowerInvariant() switch
        {
            "clock" => Icon.Clock,
            "date" => Icon.Calendar,
            "weather" => Icon.WeatherSunny,
            "board" => Icon.Edit,
            "media" => Icon.Play,
            "info" => Icon.News,
            "calculator" => Icon.Calculator,
            "study" => Icon.Book,
            "file" => Icon.Folder,
            _ => (Icon?)null
        };

        if (icon.HasValue)
        {
            return icon.Value;
        }

        var firstComponent = categoryComponents.FirstOrDefault();
        if (firstComponent is null || string.IsNullOrWhiteSpace(firstComponent.IconKey))
        {
            return Icon.Apps;
        }

        if (Enum.TryParse<Icon>(firstComponent.IconKey, ignoreCase: true, out var resolvedIcon))
        {
            return resolvedIcon;
        }

        return Icon.Apps;
    }
}
