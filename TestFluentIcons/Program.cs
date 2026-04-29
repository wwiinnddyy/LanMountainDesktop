using System;
using System.Linq;
using FluentAvalonia.UI.Controls;

class Test
{
    static void Main()
    {
        var faSymbols = new System.Collections.Generic.HashSet<string>(Enum.GetNames(typeof(FASymbol)));

        // 从错误信息中提取的图标名称
        var usedIcons = new[]
        {
            "Info", "Color", "Apps", "Code", "Home", "Settings",
            "WeatherMoon", "Search", "Location", "City", "Warning",
            "ShieldDismiss", "Shield", "Announcements", "Package",
            "StatusCircle", "Book", "BranchFork", "ArrowSync",
            "GlobeArrowForward", "Options", "Store", "Layer",
            "FolderOpen", "Clock", "Maximize"
        };

        Console.WriteLine("Checking icon availability in FASymbol:");
        foreach (var icon in usedIcons.Distinct().OrderBy(i => i))
        {
            bool exists = faSymbols.Contains(icon);
            Console.WriteLine($"  {icon}: {(exists ? "OK" : "MISSING")}");
        }
    }
}
