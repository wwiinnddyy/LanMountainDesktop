using System.Collections.Generic;
using System;
using TKCode.Arc.Icons.Lucide;

namespace LanMountainDesktop.ComponentSystem;

public static class ComponentCategoryIconResolver
{
    public static LucideIconKind ResolveCategoryIcon(
        string categoryId,
        IEnumerable<DesktopComponentDefinition> categoryComponents)
    {
        if (string.Equals(categoryId, "all", StringComparison.OrdinalIgnoreCase))
        {
            return LucideIconKind.LayoutGrid;
        }

        var icon = categoryId.ToLowerInvariant() switch
        {
            "clock" => LucideIconKind.Clock,
            "date" => LucideIconKind.CalendarDays,
            "weather" => LucideIconKind.Sun,
            "board" => LucideIconKind.PenLine,
            "media" => LucideIconKind.Play,
            "info" => LucideIconKind.Newspaper,
            "calculator" => LucideIconKind.Calculator,
            "study" => LucideIconKind.BookOpen,
            "file" => LucideIconKind.Folder,
            _ => (LucideIconKind?)null
        };

        if (icon.HasValue)
        {
            return icon.Value;
        }

        var firstComponent = categoryComponents.FirstOrDefault();
        if (firstComponent is null || string.IsNullOrWhiteSpace(firstComponent.IconKey))
        {
            return LucideIconKind.LayoutGrid;
        }

        if (Enum.TryParse<LucideIconKind>(firstComponent.IconKey, ignoreCase: true, out var resolvedIcon))
        {
            return resolvedIcon;
        }

        // D2 兼容：组件定义里仍写 FluentIcons 时代的名字时，按语义换到 Lucide 同名档。
        return firstComponent.IconKey.Trim().ToLowerInvariant() switch
        {
            "apps" or "grid" => LucideIconKind.LayoutGrid,
            "weathersunny" => LucideIconKind.Sun,
            "news" => LucideIconKind.Newspaper,
            "edit" => LucideIconKind.PenLine,
            "book" => LucideIconKind.BookOpen,
            "calendar" or "calendarmonth" => LucideIconKind.CalendarDays,
            "folder" or "folderlink" => LucideIconKind.Folder,
            "alert" => LucideIconKind.TriangleAlert,
            _ => LucideIconKind.LayoutGrid
        };
    }
}
