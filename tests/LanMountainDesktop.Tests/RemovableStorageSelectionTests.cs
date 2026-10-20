using LanMountainDesktop.Services;
using LanMountainDesktop.Views.Components;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class RemovableStorageSelectionTests
{
    [Fact]
    public void EmptyDriveList_ReturnsEmptySelection()
    {
        var selected = RemovableStorageWidget.ResolveSelectedDriveRoot([], "E:\\");

        Assert.Equal(string.Empty, selected);
    }

    [Fact]
    public void ExistingSelection_IsKept()
    {
        var drives = new[]
        {
            Drive("E:\\", "E:"),
            Drive("F:\\", "F:")
        };

        Assert.Equal("F:\\", RemovableStorageWidget.ResolveSelectedDriveRoot(drives, "F:\\"));
    }

    [Fact]
    public void MissingSelection_FallsBackToFirstConnectedDrive()
    {
        var drives = new[]
        {
            Drive("E:\\", "E:"),
            Drive("F:\\", "F:")
        };

        Assert.Equal("E:\\", RemovableStorageWidget.ResolveSelectedDriveRoot(drives, "G:\\"));
    }

    private static RemovableStorageDrive Drive(string rootPath, string driveLetter)
    {
        return new RemovableStorageDrive(rootPath, driveLetter, null);
    }
}
