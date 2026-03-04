using System.Collections.Generic;
using System.Linq;

namespace LanMountainDesktop.Models;

public sealed class StartMenuFolderNode
{
    public StartMenuFolderNode(string name, string relativePath)
    {
        Name = name;
        RelativePath = relativePath;
    }

    public string Name { get; }

    public string RelativePath { get; }

    public List<StartMenuFolderNode> Folders { get; } = [];

    public List<StartMenuAppEntry> Apps { get; } = [];

    public int TotalAppCount => Apps.Count + Folders.Sum(folder => folder.TotalAppCount);
}

