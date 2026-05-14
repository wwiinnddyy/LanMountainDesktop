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

        var firstComponent = categoryComponents.FirstOrDefault();
        if (firstComponent is null || string.IsNullOrWhiteSpace(firstComponent.IconKey))
        {
            return Icon.Apps;
        }

        if (Enum.TryParse<Icon>(firstComponent.IconKey, ignoreCase: true, out var icon))
        {
            return icon;
        }

        return Icon.Apps;
    }
}
