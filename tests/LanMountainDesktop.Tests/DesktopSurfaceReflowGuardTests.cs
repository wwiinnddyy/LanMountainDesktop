using System;
using System.IO;
using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 桌面子系统曾经在每次窗口尺寸/设置变化时清空所有页并重建全部组件；这条守卫钉住几何重排的复用路径，
/// 防止“只算几何”的注释再次与实现相反。
/// </summary>
public sealed class DesktopSurfaceReflowGuardTests
{
    [Fact]
    public void GeometryRebuild_ReusesPageHostsInsteadOfRecreatingThem()
    {
        var root = RepositoryRoot();
        var paging = File.ReadAllText(Path.Combine(
            root, "desktop", "LanMountainDesktop", "Views", "MainWindow.DesktopPaging.cs"));
        var reflow = ExtractMethod(paging, "UpdateDesktopSurfaceLayout");

        Assert.Contains("RepositionDesktopPageComponents(index, refreshContext: false)", reflow);
        Assert.DoesNotContain("DesktopPagesContainer.Children.Clear()", reflow);
        Assert.DoesNotContain("_desktopPageComponentGrids.Clear()", reflow);
        Assert.DoesNotContain("RestoreDesktopPageComponents(index)", reflow);

        var components = File.ReadAllText(Path.Combine(
            root, "desktop", "LanMountainDesktop", "Views", "MainWindow.ComponentSystem.cs"));
        var reposition = ExtractMethod(components, "RepositionDesktopPageComponents");

        Assert.Contains("pageGrid.Children.Add(host)", reposition);
        Assert.Contains("ApplyCellSize(_currentDesktopCellSize)", reposition);
        Assert.Contains("Grid.SetColumn(host, clampedColumn)", reposition);
    }

    private static string ExtractMethod(string source, string methodName)
    {
        var searchIndex = 0;
        while (true)
        {
            var methodIndex = source.IndexOf(methodName + "(", searchIndex, StringComparison.Ordinal);
            if (methodIndex < 0)
            {
                throw new InvalidOperationException($"Could not locate method '{methodName}'.");
            }

            var lineStart = source.LastIndexOf('\n', methodIndex);
            var signature = source.Substring(lineStart < 0 ? 0 : lineStart + 1, methodIndex - (lineStart < 0 ? 0 : lineStart + 1));
            if (signature.Contains("private", StringComparison.Ordinal) ||
                signature.Contains("internal", StringComparison.Ordinal) ||
                signature.Contains("public", StringComparison.Ordinal))
            {
                var braceIndex = source.IndexOf('{', methodIndex);
                if (braceIndex < 0)
                {
                    break;
                }

                var depth = 0;
                for (var i = braceIndex; i < source.Length; i++)
                {
                    if (source[i] == '{') depth++;
                    else if (source[i] == '}')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            return source.Substring(methodIndex, i - methodIndex + 1);
                        }
                    }
                }
            }

            searchIndex = methodIndex + methodName.Length;
        }

        throw new InvalidOperationException($"Could not extract method '{methodName}'.");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LanMountainDesktop.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
