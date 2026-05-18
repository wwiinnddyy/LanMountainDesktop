# 数据设置页实现计划

> **Goal:** 在设置窗口中新增「数据」设置页，可视化展示阑山桌面各类本地数据的存储占用，支持数据清理。

> **Architecture:** 采用 MVVM 模式，新增 DataStorageService 负责异步扫描各类数据大小，DataSettingsPage 使用 Fluent Design 横向堆叠条形图展示存储分布。

> **Tech Stack:** Avalonia UI, FluentAvaloniaUI, CommunityToolkit.Mvvm, C# 13

---

## 文件结构

| 文件 | 职责 |
|------|------|
| `LanMountainDesktop/Services/DataStorageService.cs` | 扫描各类数据目录大小，计算磁盘总容量 |
| `LanMountainDesktop/ViewModels/DataSettingsPageViewModel.cs` | 数据设置页视图模型，绑定存储数据和清理命令 |
| `LanMountainDesktop/Views/SettingsPages/DataSettingsPage.axaml` | 数据设置页 XAML 视图（堆叠条形图 + 列表） |
| `LanMountainDesktop/Views/SettingsPages/DataSettingsPage.axaml.cs` | 页面代码隐藏，注册设置页属性 |
| `LanMountainDesktop/Views/SettingsWindow.axaml.cs` | 修改图标映射，添加 Database 图标 |

---

## Task 1: 创建 DataStorageService

**Files:**
- Create: `LanMountainDesktop/Services/DataStorageService.cs`

**职责：** 扫描阑山桌面各类数据的存储占用，计算磁盘总容量。

- [ ] **Step 1: 创建 DataStorageService**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace LanMountainDesktop.Services;

public sealed record StorageCategoryInfo(
    string Id,
    string Name,
    string Description,
    string DirectoryPath,
    bool IsCleanable,
    string ColorHex);

public sealed record StorageScanResult(
    StorageCategoryInfo Category,
    long SizeBytes,
    double PercentageOfTotal);

public sealed class DataStorageService
{
    private static readonly IReadOnlyList<StorageCategoryInfo> Categories = new List<StorageCategoryInfo>
    {
        new("logs", "日志文件", "应用运行日志", "", true, "#9E9E9E"),
        new("whiteboards", "白板笔记", "桌面白板笔记数据", "", true, "#FF9800"),
        new("plugins", "插件数据", "已安装插件文件", "", true, "#2196F3"),
        new("market", "插件市场缓存", "插件市场元数据缓存", "", true, "#9C27B0"),
        new("wallpapers", "壁纸文件", "下载的壁纸资源", "", true, "#E91E63"),
        new("settings", "设置文件", "应用配置数据", "", false, "#4CAF50")
    };

    public IReadOnlyList<StorageCategoryInfo> GetCategories() => Categories;

    public async Task<IReadOnlyList<StorageScanResult>> ScanAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<StorageScanResult>();
        var dataRoot = AppDataPathProvider.GetDataRoot();
        var logDirectory = AppLogger.LogDirectory;

        long totalSize = 0;
        var categorySizes = new Dictionary<string, long>();

        foreach (var category in Categories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string path = category.Id switch
            {
                "logs" => logDirectory,
                "settings" => dataRoot,
                _ => Path.Combine(dataRoot, category.DirectoryPath)
            };

            long size = 0;
            if (category.Id == "settings")
            {
                size = await GetSettingsSizeAsync(dataRoot, cancellationToken);
            }
            else if (Directory.Exists(path))
            {
                size = await GetDirectorySizeAsync(path, cancellationToken);
            }

            categorySizes[category.Id] = size;
            totalSize += size;
        }

        foreach (var category in Categories)
        {
            var size = categorySizes.GetValueOrDefault(category.Id, 0);
            var percentage = totalSize > 0 ? (double)size / totalSize * 100 : 0;
            results.Add(new StorageScanResult(category, size, percentage));
        }

        return results;
    }

    public async Task<long> GetTotalDiskSpaceAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var dataRoot = AppDataPathProvider.GetDataRoot();
            var driveInfo = new DriveInfo(Path.GetPathRoot(dataRoot) ?? dataRoot);
            return driveInfo.TotalSize;
        }, cancellationToken);
    }

    public async Task<long> GetAvailableDiskSpaceAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var dataRoot = AppDataPathProvider.GetDataRoot();
            var driveInfo = new DriveInfo(Path.GetPathRoot(dataRoot) ?? dataRoot);
            return driveInfo.AvailableFreeSpace;
        }, cancellationToken);
    }

    public async Task<bool> CleanCategoryAsync(string categoryId, CancellationToken cancellationToken = default)
    {
        var category = Categories.FirstOrDefault(c =>
            string.Equals(c.Id, categoryId, StringComparison.OrdinalIgnoreCase));

        if (category is null || !category.IsCleanable)
        {
            return false;
        }

        var dataRoot = AppDataPathProvider.GetDataRoot();
        string path = categoryId switch
        {
            "logs" => AppLogger.LogDirectory,
            _ => Path.Combine(dataRoot, category.DirectoryPath)
        };

        if (!Directory.Exists(path))
        {
            return false;
        }

        return await Task.Run(() =>
        {
            try
            {
                if (categoryId == "logs")
                {
                    foreach (var file in Directory.GetFiles(path, "*.log"))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        TryDeleteFile(file);
                    }
                }
                else
                {
                    foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        TryDeleteFile(file);
                    }

                    foreach (var dir in Directory.GetDirectories(path, "*", SearchOption.AllDirectories)
                        .OrderByDescending(d => d.Length))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        TryDeleteDirectory(dir);
                    }
                }

                AppLogger.Info("DataStorage", $"Cleaned category '{categoryId}' at '{path}'.");
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Warn("DataStorage", $"Failed to clean category '{categoryId}'.", ex);
                return false;
            }
        }, cancellationToken);
    }

    private static async Task<long> GetDirectorySizeAsync(string path, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            long size = 0;
            try
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var info = new FileInfo(file);
                        if (info.Exists)
                        {
                            size += info.Length;
                        }
                    }
                    catch
                    {
                        // Ignore files we can't access
                    }
                }
            }
            catch
            {
                // Ignore directories we can't access
            }

            return size;
        }, cancellationToken);
    }

    private static async Task<long> GetSettingsSizeAsync(string dataRoot, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            long size = 0;
            var settingFiles = new[] { "settings.json", "plugin-settings.json", "launcher-settings.json" };
            foreach (var file in settingFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = Path.Combine(dataRoot, file);
                if (File.Exists(path))
                {
                    try
                    {
                        size += new FileInfo(path).Length;
                    }
                    catch
                    {
                        // Ignore
                    }
                }
            }

            return size;
        }, cancellationToken);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
        catch
        {
            // Ignore deletion failures
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, false);
        }
        catch
        {
            // Ignore deletion failures
        }
    }

    public static string FormatBytes(long bytes)
    {
        const long KB = 1024;
        const long MB = KB * 1024;
        const long GB = MB * 1024;
        const long TB = GB * 1024;

        return bytes switch
        {
            >= TB => $"{bytes / (double)TB:F2} TB",
            >= GB => $"{bytes / (double)GB:F2} GB",
            >= MB => $"{bytes / (double)MB:F2} MB",
            >= KB => $"{bytes / (double)KB:F2} KB",
            _ => $"{bytes} B"
        };
    }
}
```

---

## Task 2: 创建 DataSettingsPageViewModel

**Files:**
- Create: `LanMountainDesktop/ViewModels/DataSettingsPageViewModel.cs`

- [ ] **Step 1: 创建 ViewModel**

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.ViewModels;

public sealed partial class DataStorageItemViewModel : ObservableObject
{
    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public string ColorHex { get; }
    public bool IsCleanable { get; }

    [ObservableProperty]
    private string _sizeText = "--";

    [ObservableProperty]
    private double _percentage;

    [ObservableProperty]
    private bool _isCleaning;

    public DataStorageItemViewModel(StorageCategoryInfo category)
    {
        Id = category.Id;
        Name = category.Name;
        Description = category.Description;
        ColorHex = category.ColorHex;
        IsCleanable = category.IsCleanable;
    }

    public void UpdateSize(long sizeBytes, double percentage)
    {
        SizeText = DataStorageService.FormatBytes(sizeBytes);
        Percentage = percentage;
    }
}

public sealed partial class DataSettingsPageViewModel : ViewModelBase
{
    private readonly DataStorageService _storageService = new();
    private CancellationTokenSource? _scanCts;

    [ObservableProperty]
    private string _pageTitle = "数据与存储";

    [ObservableProperty]
    private string _totalSizeText = "--";

    [ObservableProperty]
    private string _diskUsageText = "--";

    [ObservableProperty]
    private double _diskUsagePercentage;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _hasData;

    public ObservableCollection<DataStorageItemViewModel> Items { get; } = new();

    public DataSettingsPageViewModel()
    {
        var categories = _storageService.GetCategories();
        foreach (var category in categories)
        {
            Items.Add(new DataStorageItemViewModel(category));
        }

        _ = ScanAsync();
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();
        var token = _scanCts.Token;

        IsScanning = true;
        try
        {
            var results = await _storageService.ScanAsync(token);
            var totalSize = results.Sum(r => r.SizeBytes);
            var totalDisk = await _storageService.GetTotalDiskSpaceAsync(token);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                TotalSizeText = DataStorageService.FormatBytes(totalSize);
                DiskUsagePercentage = totalDisk > 0 ? (double)totalSize / totalDisk * 100 : 0;
                DiskUsageText = $"占总磁盘 {DiskUsagePercentage:F1}%";
                HasData = totalSize > 0;

                foreach (var result in results)
                {
                    var item = Items.FirstOrDefault(i =>
                        string.Equals(i.Id, result.Category.Id, StringComparison.OrdinalIgnoreCase));
                    item?.UpdateSize(result.SizeBytes, result.PercentageOfTotal);
                }
            });
        }
        catch (OperationCanceledException)
        {
            // Ignore cancellation
        }
        catch (Exception ex)
        {
            AppLogger.Warn("DataSettings", "Failed to scan storage.", ex);
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private async Task CleanAsync(string categoryId)
    {
        var item = Items.FirstOrDefault(i =>
            string.Equals(i.Id, categoryId, StringComparison.OrdinalIgnoreCase));
        if (item is null || !item.IsCleanable)
        {
            return;
        }

        item.IsCleaning = true;
        try
        {
            await _storageService.CleanCategoryAsync(categoryId);
            await ScanAsync();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("DataSettings", $"Failed to clean category '{categoryId}'.", ex);
        }
        finally
        {
            item.IsCleaning = false;
        }
    }

    [RelayCommand]
    private async Task CleanAllAsync()
    {
        foreach (var item in Items.Where(i => i.IsCleanable))
        {
            item.IsCleaning = true;
        }

        try
        {
            foreach (var item in Items.Where(i => i.IsCleanable))
            {
                await _storageService.CleanCategoryAsync(item.Id);
            }

            await ScanAsync();
        }
        catch (Exception ex)
        {
            AppLogger.Warn("DataSettings", "Failed to clean all categories.", ex);
        }
        finally
        {
            foreach (var item in Items)
            {
                item.IsCleaning = false;
            }
        }
    }
}
```

---

## Task 3: 创建 DataSettingsPage.axaml

**Files:**
- Create: `LanMountainDesktop/Views/SettingsPages/DataSettingsPage.axaml`

- [ ] **Step 1: 创建 XAML 视图**

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:LanMountainDesktop.ViewModels"
             xmlns:ui="using:FluentAvalonia.UI.Controls"
             xmlns:fi="using:FluentIcons.Avalonia"
             x:Class="LanMountainDesktop.Views.SettingsPages.DataSettingsPage"
             x:DataType="vm:DataSettingsPageViewModel">
    <ScrollViewer VerticalScrollBarVisibility="Auto">
        <StackPanel Classes="settings-page-container settings-page-animated"
                    Spacing="16">

            <!-- 存储概览卡片 -->
            <Border Background="{DynamicResource CardBackgroundFillColorDefaultBrush}"
                    CornerRadius="{DynamicResource DesignCornerRadiusMd}"
                    Padding="20">
                <StackPanel Spacing="12">
                    <TextBlock Text="存储概览"
                               FontSize="16"
                               FontWeight="SemiBold" />

                    <!-- 堆叠条形图 -->
                    <Grid Height="28"
                          IsVisible="{Binding HasData}">
                        <Border Background="{DynamicResource ControlFillColorTertiaryBrush}"
                                CornerRadius="{DynamicResource DesignCornerRadiusSm}"
                                ClipToBounds="True">
                            <StackPanel Orientation="Horizontal"
                                        x:Name="StorageBarPanel">
                                <!-- 动态生成分段 -->
                            </StackPanel>
                        </Border>
                    </Grid>

                    <!-- 总大小和磁盘占比 -->
                    <Grid ColumnDefinitions="*,Auto">
                        <StackPanel Grid.Column="0"
                                    Orientation="Horizontal"
                                    Spacing="8">
                            <TextBlock Text="{Binding TotalSizeText}"
                                       FontSize="24"
                                       FontWeight="SemiBold" />
                            <TextBlock Text="{Binding DiskUsageText}"
                                       VerticalAlignment="Bottom"
                                       Margin="0,0,0,4"
                                       Opacity="0.7" />
                        </StackPanel>
                        <Button Grid.Column="1"
                                Command="{Binding ScanCommand}"
                                IsEnabled="{Binding !IsScanning}"
                                VerticalAlignment="Center">
                            <StackPanel Orientation="Horizontal"
                                        Spacing="6">
                                <fi:FluentIcon Icon="ArrowSync"
                                               IconVariant="Regular"
                                               FontSize="14" />
                                <TextBlock Text="刷新" />
                            </StackPanel>
                        </Button>
                    </Grid>

                    <!-- 图例 -->
                    <ItemsControl ItemsSource="{Binding Items}"
                                  IsVisible="{Binding HasData}">
                        <ItemsControl.ItemsPanel>
                            <ItemsPanelTemplate>
                                <WrapPanel Orientation="Horizontal"
                                           ItemWidth="140"
                                           ItemHeight="28" />
                            </ItemsPanelTemplate>
                        </ItemsControl.ItemsPanel>
                        <ItemsControl.ItemTemplate>
                            <DataTemplate x:DataType="vm:DataStorageItemViewModel">
                                <StackPanel Orientation="Horizontal"
                                            Spacing="6"
                                            VerticalAlignment="Center">
                                    <Border Width="12"
                                            Height="12"
                                            CornerRadius="2"
                                            Background="{Binding ColorHex, Converter={StaticResource HexToBrushConverter}}" />
                                    <TextBlock Text="{Binding Name}"
                                               FontSize="12"
                                               Opacity="0.8" />
                                </StackPanel>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </StackPanel>
            </Border>

            <!-- 数据类型详情列表 -->
            <TextBlock Text="数据详情"
                       FontSize="16"
                       FontWeight="SemiBold"
                       Margin="0,8,0,0" />

            <ItemsControl ItemsSource="{Binding Items}">
                <ItemsControl.ItemTemplate>
                    <DataTemplate x:DataType="vm:DataStorageItemViewModel">
                        <Border Background="{DynamicResource CardBackgroundFillColorDefaultBrush}"
                                CornerRadius="{DynamicResource DesignCornerRadiusMd}"
                                Padding="16"
                                Margin="0,4">
                            <Grid ColumnDefinitions="Auto,*,Auto,Auto"
                                  ColumnSpacing="12">
                                <Border Grid.Column="0"
                                        Width="12"
                                        Height="12"
                                        CornerRadius="2"
                                        Background="{Binding ColorHex, Converter={StaticResource HexToBrushConverter}}"
                                        VerticalAlignment="Center" />

                                <StackPanel Grid.Column="1"
                                            VerticalAlignment="Center">
                                    <TextBlock Text="{Binding Name}"
                                               FontWeight="SemiBold" />
                                    <TextBlock Text="{Binding Description}"
                                               FontSize="12"
                                               Opacity="0.6" />
                                </StackPanel>

                                <TextBlock Grid.Column="2"
                                           Text="{Binding SizeText}"
                                           VerticalAlignment="Center"
                                           FontWeight="SemiBold"
                                           Opacity="0.8" />

                                <Button Grid.Column="3"
                                        Command="{Binding $parent[ItemsControl].((vm:DataSettingsPageViewModel)DataContext).CleanCommand}"
                                        CommandParameter="{Binding Id}"
                                        IsVisible="{Binding IsCleanable}"
                                        IsEnabled="{Binding !IsCleaning}"
                                        VerticalAlignment="Center">
                                    <StackPanel Orientation="Horizontal"
                                                Spacing="4">
                                        <fi:FluentIcon Icon="Delete"
                                                       IconVariant="Regular"
                                                       FontSize="14" />
                                        <TextBlock Text="清理" />
                                    </StackPanel>
                                </Button>
                            </Grid>
                        </Border>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>

            <!-- 一键清理 -->
            <Button Command="{Binding CleanAllCommand}"
                    HorizontalAlignment="Stretch"
                    HorizontalContentAlignment="Center"
                    Margin="0,8">
                <StackPanel Orientation="Horizontal"
                            Spacing="6">
                    <fi:FluentIcon Icon="Broom"
                                   IconVariant="Regular"
                                   FontSize="16" />
                    <TextBlock Text="一键清理所有可清理数据" />
                </StackPanel>
            </Button>
        </StackPanel>
    </ScrollViewer>
</UserControl>
```

---

## Task 4: 创建 DataSettingsPage.axaml.cs

**Files:**
- Create: `LanMountainDesktop/Views/SettingsPages/DataSettingsPage.axaml.cs`

- [ ] **Step 1: 创建代码隐藏**

```csharp
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.ViewModels;

namespace LanMountainDesktop.Views.SettingsPages;

[SettingsPageInfo(
    "data",
    "Data",
    SettingsPageCategory.General,
    IconKey = "Database",
    SortOrder = 5,
    TitleLocalizationKey = "settings.data.title",
    DescriptionLocalizationKey = "settings.data.description")]
public partial class DataSettingsPage : SettingsPageBase
{
    public DataSettingsPage()
        : this(new DataSettingsPageViewModel())
    {
    }

    public DataSettingsPage(DataSettingsPageViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        InitializeComponent();
    }

    public DataSettingsPageViewModel ViewModel { get; }
}
```

---

## Task 5: 修改 SettingsWindow.axaml.cs 添加图标映射

**Files:**
- Modify: `LanMountainDesktop/Views/SettingsWindow.axaml.cs`

- [ ] **Step 1: 在 MapIcon 方法中添加 Database 图标映射**

在 `MapIcon` 方法的 switch 表达式中添加：

```csharp
"Database" => Symbol.Database,
```

---

## Task 6: 添加颜色转换器（如需要）

**Files:**
- Modify: `LanMountainDesktop/Theme/` 或 `LanMountainDesktop/Controls/` 中的资源字典

如果项目中没有 HexToBrushConverter，需要创建一个简单的值转换器：

```csharp
using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace LanMountainDesktop.Converters;

public class HexToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                return new SolidColorBrush(Color.Parse(hex));
            }
            catch
            {
                // Ignore parse errors
            }
        }

        return new SolidColorBrush(Colors.Gray);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
```

---

## 测试验证

1. 构建项目：`dotnet build LanMountainDesktop.slnx -c Debug`
2. 运行应用：`dotnet run --project LanMountainDesktop/LanMountainDesktop.csproj`
3. 打开设置窗口，确认「数据」选项卡出现在左侧导航中
4. 点击「数据」选项卡，确认：
   - 堆叠条形图显示各类数据占比
   - 总大小和磁盘占比显示正确
   - 数据详情列表显示每类数据大小
   - 刷新按钮可以重新扫描
   - 清理按钮可以清理对应数据
