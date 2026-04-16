# LanMountainDesktop Launcher 全面改进计划

## 概述

本计划旨在将 LanMountainDesktop 的 Launcher 改进为符合原子化架构的独立启动器，参考 ClassIsland 的极简设计，同时保留阑山桌面的特色功能。

## 目标

1. **P0 (必须完成)**: 重写 Launcher 为极简模式，移除与主程序的耦合
2. **P1 (应该完成)**: 将 OOBE、Splash、更新、插件管理迁移到主程序
3. **P2 (推荐完成)**: 实现 Launcher 自更新机制
4. **P3 (可选优化)**: 性能优化和代码清理
5. **P4 (长期规划)**: 增强功能和可扩展性

## 当前问题

1. Launcher 是 Avalonia 应用，启动慢、内存占用高
2. Launcher 引用了 PluginSdk，与主程序有耦合
3. 主程序引用了 Launcher，构建关系复杂
4. Launcher 职责过多（OOBE + Splash + 更新 + 插件 + 启动）
5. 缺少 Launcher 自更新机制
6. GitHub Actions 工作流需要适配新的目录结构

## 改进后架构

```
安装根目录/
├── LanMountainDesktop.exe              ← 启动器（唯一入口，极简，~100行代码）
├── app-1.0.0/                          ← 版本目录
│   ├── .current                        ← 当前版本标记
│   ├── LanMountainDesktop.exe          ← 主程序
│   └── ... (所有依赖)
└── .launcher/                          ← 启动器数据（可选）
    └── snapshots/                      ← 版本快照
```

## 详细实施步骤

### P0: 基础架构重构

#### 1. 重写 Launcher 为极简模式

**文件**: `LanMountainDesktop.Launcher/Program.cs`

**目标**: 
- 代码量控制在 100 行以内
- 零外部依赖（不使用 Avalonia）
- 只负责：版本选择、启动主程序、清理旧版本

**完整实现代码**:

```csharp
// LanMountainDesktop.Launcher/Program.cs
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LanMountainDesktop.Launcher;

internal static class Program
{
    private const string HostExecutableName = "LanMountainDesktop.exe";
    private const string HostExecutableNameLinux = "LanMountainDesktop";
    
    [STAThread]
    private static int Main(string[] args)
    {
        var rootDir = GetRootDirectory();
        
        // 1. 查找最佳版本
        var installation = FindBestVersion(rootDir);
        if (installation == null)
        {
            ShowError("找不到有效的 LanMountainDesktop 版本，请重新安装。");
            return 1;
        }
        
        // 2. 清理旧版本（异步，不阻塞）
        _ = Task.Run(() => CleanupOldVersions(rootDir));
        
        // 3. 启动主程序
        return LaunchHost(installation, args);
    }
    
    private static string GetRootDirectory()
    {
        return Path.GetFullPath(
            Path.GetDirectoryName(Environment.ProcessPath) ?? "");
    }
    
    private static string? FindBestVersion(string rootDir)
    {
        var exeName = OperatingSystem.IsWindows() 
            ? HostExecutableName 
            : HostExecutableNameLinux;
            
        return Directory.GetDirectories(rootDir)
            .Where(x => IsValidVersionDirectory(x, exeName))
            .OrderBy(x => File.Exists(Path.Combine(x, ".current")) ? 0 : 1)
            .ThenByDescending(x => ParseVersion(Path.GetFileName(x)))
            .FirstOrDefault();
    }
    
    private static bool IsValidVersionDirectory(string path, string exeName)
    {
        var dirName = Path.GetFileName(path);
        return dirName.StartsWith("app-") &&
               !File.Exists(Path.Combine(path, ".destroy")) &&
               !File.Exists(Path.Combine(path, ".partial")) &&
               File.Exists(Path.Combine(path, exeName));
    }
    
    private static Version ParseVersion(string dirName)
    {
        // app-1.0.0 or app-1.0.0-123
        var parts = dirName.Split('-');
        if (parts.Length >= 2 && Version.TryParse(parts[1], out var v))
            return v;
        return new Version(0, 0);
    }
    
    private static void CleanupOldVersions(string rootDir)
    {
        try
        {
            var oldVersions = Directory.GetDirectories(rootDir)
                .Where(x => File.Exists(Path.Combine(x, ".destroy")));
            
            foreach (var dir in oldVersions)
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }
        catch { /* 忽略清理失败 */ }
    }
    
    private static int LaunchHost(string installation, string[] args)
    {
        var exeName = OperatingSystem.IsWindows() 
            ? HostExecutableName 
            : HostExecutableNameLinux;
        var exePath = Path.Combine(installation, exeName);
        
        // Linux/macOS: 确保可执行权限
        if (!OperatingSystem.IsWindows())
        {
            EnsureExecutable(exePath);
        }
        
        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = Path.GetDirectoryName(installation),
            UseShellExecute = true
        };
        
        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);
        
        // 传递环境变量
        startInfo.EnvironmentVariables["LMD_PACKAGE_ROOT"] = 
            Path.GetDirectoryName(installation);
        startInfo.EnvironmentVariables["LMD_VERSION"] = 
            Path.GetFileName(installation).Replace("app-", "");
        
        try
        {
            Process.Start(startInfo);
            return 0;
        }
        catch (Exception ex)
        {
            ShowError($"启动失败: {ex.Message}");
            return 1;
        }
    }
    
    private static void EnsureExecutable(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "chmod",
                Arguments = $"+x \"{path}\"",
                CreateNoWindow = true
            })?.WaitForExit();
        }
        catch { }
    }
    
    private static void ShowError(string message)
    {
        if (OperatingSystem.IsWindows())
        {
            // Win32 MessageBox
            try
            {
                MessageBox(IntPtr.Zero, message, "LanMountainDesktop", 0x10);
            }
            catch 
            { 
                Console.Error.WriteLine(message);
            }
        }
        else
        {
            Console.Error.WriteLine(message);
        }
    }
    
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
```

#### 2. 修改 Launcher 项目文件

**文件**: `LanMountainDesktop.Launcher/LanMountainDesktop.Launcher.csproj`

**完整内容**:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <Version>1.0.0</Version>
    <ApplicationIcon>Assets\logo_nightly.ico</ApplicationIcon>
  </PropertyGroup>
  
  <!-- 图标资源 -->
  <ItemGroup>
    <Content Include="..\LanMountainDesktop\Assets\logo_nightly.ico" Link="Assets\logo_nightly.ico">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
  </ItemGroup>
</Project>
```

#### 3. 移除主程序对 Launcher 的引用

**文件**: `LanMountainDesktop/LanMountainDesktop.csproj`

**修改**: 删除以下行
```xml
<!-- 删除这一行 -->
<ProjectReference Include="..\LanMountainDesktop.Launcher\LanMountainDesktop.Launcher.csproj" ReferenceOutputAssembly="false" />
```

#### 4. 修改主程序支持新架构

**文件**: `LanMountainDesktop/Program.cs`

**修改**: 添加环境变量读取

```csharp
// 在 Program.cs 中添加
internal static class LaunchContext
{
    public static string? PackageRoot => 
        Environment.GetEnvironmentVariable("LMD_PACKAGE_ROOT");
    public static string? Version => 
        Environment.GetEnvironmentVariable("LMD_VERSION");
    public static bool IsLaunchedByLauncher => 
        !string.IsNullOrEmpty(PackageRoot);
}
```

---

### P1: 功能迁移

#### 5. 将 OOBE 迁移到主程序

**新建文件**: `LanMountainDesktop/Services/Oobe/OobeService.cs`

```csharp
using LanMountainDesktop.Models;
using LanMountainDesktop.Services.Settings;

namespace LanMountainDesktop.Services.Oobe;

public class OobeService
{
    private readonly string _oobeStatePath;
    
    public OobeService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _oobeStatePath = Path.Combine(appData, "LanMountainDesktop", ".oobe_completed");
    }
    
    public bool IsFirstRun()
    {
        return !File.Exists(_oobeStatePath);
    }
    
    public void MarkCompleted()
    {
        var dir = Path.GetDirectoryName(_oobeStatePath);
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(_oobeStatePath, DateTime.UtcNow.ToString("O"));
    }
}
```

**新建文件**: `LanMountainDesktop/Views/Oobe/OobeWindow.axaml`

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="LanMountainDesktop.Views.Oobe.OobeWindow"
        Title="欢迎使用阑山桌面"
        Width="800"
        Height="600"
        WindowStartupLocation="CenterScreen">
  <Grid>
    <!-- OOBE 界面内容 -->
    <TextBlock Text="欢迎使用阑山桌面" FontSize="24" HorizontalAlignment="Center" Margin="0,50,0,0"/>
    <Button Content="开始使用" HorizontalAlignment="Center" VerticalAlignment="Bottom" Margin="0,0,0,50" Click="OnStartClick"/>
  </Grid>
</Window>
```

**修改文件**: `LanMountainDesktop/App.axaml.cs`

```csharp
// 在 OnFrameworkInitializationCompleted 中添加
private async Task InitializeOobeAsync()
{
    var oobeService = new OobeService();
    if (oobeService.IsFirstRun())
    {
        var oobeWindow = new Views.Oobe.OobeWindow();
        await oobeWindow.ShowDialog();
        oobeService.MarkCompleted();
    }
}
```

#### 6. 将 Splash 迁移到主程序

**新建文件**: `LanMountainDesktop/Views/Splash/SplashWindow.axaml`

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="LanMountainDesktop.Views.Splash.SplashWindow"
        Title="阑山桌面"
        Width="400"
        Height="300"
        WindowStartupLocation="CenterScreen"
        ShowInTaskbar="False"
        SystemDecorations="None">
  <Grid Background="{DynamicResource SystemAccentColor}">
    <StackPanel VerticalAlignment="Center" HorizontalAlignment="Center">
      <Image Source="/Assets/logo_nightly.png" Width="100" Height="100"/>
      <TextBlock Text="阑山桌面" FontSize="20" Margin="0,20,0,0" HorizontalAlignment="Center"/>
      <TextBlock x:Name="StatusText" Text="正在启动..." Margin="0,10,0,0" HorizontalAlignment="Center"/>
    </StackPanel>
  </Grid>
</Window>
```

**修改文件**: `LanMountainDesktop/App.axaml.cs`

```csharp
// 在初始化时显示 Splash
private SplashWindow? _splashWindow;

private void ShowSplash()
{
    _splashWindow = new SplashWindow();
    _splashWindow.Show();
}

private void CloseSplash()
{
    _splashWindow?.Close();
    _splashWindow = null;
}
```

#### 7. 将更新逻辑迁移到主程序

**新建目录**: `LanMountainDesktop/Services/Update/`

**新建文件**: `LanMountainDesktop/Services/Update/UpdateService.cs`

```csharp
using System.Net.Http.Json;
using LanMountainDesktop.Models;

namespace LanMountainDesktop.Services.Update;

public class UpdateService
{
    private readonly HttpClient _httpClient;
    private readonly string _currentVersion;
    
    public UpdateService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "LanMountainDesktop");
        _currentVersion = GetType().Assembly.GetName().Version?.ToString() ?? "1.0.0";
    }
    
    public async Task<UpdateCheckResult> CheckForUpdateAsync(UpdateChannel channel)
    {
        // 调用 GitHub Release API
        var releases = await _httpClient.GetFromJsonAsync<List<GitHubRelease>>(
            "https://api.github.com/repos/ClassIsland/LanMountainDesktop/releases");
        
        var latest = channel == UpdateChannel.Stable
            ? releases?.FirstOrDefault(r => !r.Prerelease)
            : releases?.FirstOrDefault();
            
        if (latest == null)
            return new UpdateCheckResult { HasUpdate = false };
            
        var latestVersion = latest.TagName.TrimStart('v');
        var hasUpdate = new Version(latestVersion) > new Version(_currentVersion);
        
        return new UpdateCheckResult
        {
            HasUpdate = hasUpdate,
            Version = latestVersion,
            DownloadUrl = latest.Assets.FirstOrDefault()?.BrowserDownloadUrl
        };
    }
}

public class UpdateCheckResult
{
    public bool HasUpdate { get; set; }
    public string? Version { get; set; }
    public string? DownloadUrl { get; set; }
}

public enum UpdateChannel { Stable, Preview }

public class GitHubRelease
{
    public string TagName { get; set; } = "";
    public bool Prerelease { get; set; }
    public List<GitHubAsset> Assets { get; set; } = new();
}

public class GitHubAsset
{
    public string BrowserDownloadUrl { get; set; } = "";
}
```

#### 8. 将插件管理迁移到主程序

**新建目录**: `LanMountainDesktop/Services/Plugins/`

**新建文件**: `LanMountainDesktop/Services/Plugins/PluginUpdateService.cs`

```csharp
namespace LanMountainDesktop.Services.Plugins;

public class PluginUpdateService
{
    private readonly string _pluginsDirectory;
    
    public PluginUpdateService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _pluginsDirectory = Path.Combine(appData, "LanMountainDesktop", "plugins");
    }
    
    public async Task CheckAndUpdatePluginsAsync()
    {
        // 检查插件更新
        // 下载并安装更新
    }
}
```

---

### P2: 自更新机制

#### 9. 实现 Launcher 自更新

**修改文件**: `LanMountainDesktop.Launcher/Program.cs`

```csharp
// 在 Main 方法开头添加自更新检查
private static void CheckForLauncherUpdate()
{
    var rootDir = GetRootDirectory();
    var updatePath = Path.Combine(rootDir, "LanMountainDesktop.Launcher.Update.exe");
    
    if (File.Exists(updatePath))
    {
        // 有新版本 Launcher，替换自身
        try
        {
            var currentPath = Environment.ProcessPath;
            var backupPath = currentPath + ".old";
            
            // 重命名当前版本
            if (File.Exists(backupPath))
                File.Delete(backupPath);
            File.Move(currentPath!, backupPath);
            
            // 移动新版本
            File.Move(updatePath, currentPath!);
            
            // 删除备份
            File.Delete(backupPath);
            
            // 重启自己
            Process.Start(new ProcessStartInfo
            {
                FileName = currentPath,
                UseShellExecute = true
            });
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            // 回滚
            Console.Error.WriteLine($"Launcher 更新失败: {ex.Message}");
        }
    }
}
```

#### 10. 主程序支持更新 Launcher

**新建文件**: `LanMountainDesktop/Services/Update/LauncherUpdateService.cs`

```csharp
namespace LanMountainDesktop.Services.Update;

public class LauncherUpdateService
{
    private readonly HttpClient _httpClient;
    
    public LauncherUpdateService()
    {
        _httpClient = new HttpClient();
    }
    
    public async Task<bool> UpdateLauncherAsync(string downloadUrl)
    {
        var rootDir = LaunchContext.PackageRoot 
            ?? Path.GetDirectoryName(Environment.ProcessPath)!;
        var updatePath = Path.Combine(rootDir, "LanMountainDesktop.Launcher.Update.exe");
        
        // 下载新版本
        var response = await _httpClient.GetAsync(downloadUrl);
        await using var fs = File.Create(updatePath);
        await response.Content.CopyToAsync(fs);
        
        return true;
    }
    
    public void RestartWithNewLauncher()
    {
        var launcherPath = Path.Combine(
            LaunchContext.PackageRoot ?? "",
            "LanMountainDesktop.exe");
            
        Process.Start(new ProcessStartInfo
        {
            FileName = launcherPath,
            UseShellExecute = true
        });
        
        // 退出主程序，让 Launcher 接管
        Environment.Exit(0);
    }
}
```

---

### P3: 清理旧代码

#### 11. 删除文件清单

**删除以下文件/目录**:

```
LanMountainDesktop.Launcher/
├── App.axaml                          ← 删除
├── App.axaml.cs                       ← 删除
├── Views/                             ← 删除整个目录
│   ├── OobeWindow.axaml
│   ├── OobeWindow.axaml.cs
│   ├── SplashWindow.axaml
│   └── SplashWindow.axaml.cs
├── Services/                          ← 删除大部分
│   ├── LauncherFlowCoordinator.cs     ← 删除
│   ├── OobeStateService.cs            ← 删除
│   ├── UpdateCheckService.cs          ← 删除
│   ├── UpdateEngineService.cs         ← 删除
│   ├── PluginInstallerService.cs      ← 删除
│   └── PluginUpgradeQueueService.cs   ← 删除
└── Models/                            ← 删除（如不再需要）
```

---

### P4: GitHub Actions 工作流修改

#### 12. 修改 release.yml

**关键修改点**:

1. **Launcher 单独编译**:
```yaml
- name: Publish Launcher
  run: |
    dotnet publish LanMountainDesktop.Launcher/LanMountainDesktop.Launcher.csproj `
      -c Release `
      -o ./publish/launcher-win-x64 `
      --self-contained `
      -r win-x64 `
      -p:PublishSingleFile=false `
      -p:PublishTrimmed=false `
      -p:DebugType=none
```

2. **目录结构调整**:
```yaml
- name: Restructure for Launcher
  run: |
    $version = "${{ needs.prepare.outputs.version }}"
    $publishDir = "publish/windows-x64"
    $launcherDir = "publish/launcher-win-x64"
    $appDir = "app-$version"
    
    # 创建新结构
    $newStructure = "publish-launcher/windows-x64"
    New-Item -ItemType Directory -Path $newStructure -Force
    
    # 移动主程序到 app-{version}/
    $appPath = Join-Path $newStructure $appDir
    Move-Item -Path $publishDir -Destination $appPath -Force
    
    # 复制 Launcher 到根目录
    Copy-Item -Path "$launcherDir\*" -Destination $newStructure -Recurse -Force
    
    # 创建 .current 标记
    New-Item -ItemType File -Path (Join-Path $appPath ".current") -Force
```

3. **Linux/macOS 同样调整**:
- Linux: 修改 DEB 打包流程
- macOS: 修改 DMG 打包流程

#### 13. 修改 build.yml

**修改**: 移除 Launcher 相关构建步骤，因为 Launcher 现在完全独立

---

### P5: 图标资源处理

#### 14. Launcher 图标配置

**方案**: 使用链接方式引用主程序图标

**文件**: `LanMountainDesktop.Launcher/LanMountainDesktop.Launcher.csproj`

```xml
<ItemGroup>
  <!-- 链接主程序的图标 -->
  <Content Include="..\LanMountainDesktop\Assets\logo_nightly.ico" Link="Assets\logo_nightly.ico">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
</ItemGroup>
```

#### 15. 安装程序配置

**文件**: `LanMountainDesktop/installer/LanMountainDesktop.iss` (Inno Setup)

**关键配置**:

```ini
[Setup]
AppName=阑山桌面
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\LanMountainDesktop
OutputBaseFilename=LanMountainDesktop-Setup-{#MyAppVersion}-x64
SetupIconFile=..\Assets\logo_nightly.ico
UninstallDisplayIcon={app}\LanMountainDesktop.exe

[Files]
; Launcher
Source: "..\..\publish\windows-x64\LanMountainDesktop.exe"; DestDir: "{app}"; Flags: ignoreversion
; 主程序版本目录
Source: "..\..\publish\windows-x64\app-{#MyAppVersion}\*"; DestDir: "{app}\app-{#MyAppVersion}"; Flags: ignoreversion recursesubdirs

[Icons]
; 桌面快捷方式
Name: "{autodesktop}\阑山桌面"; Filename: "{app}\LanMountainDesktop.exe"; IconFilename: "{app}\LanMountainDesktop.exe"
; 开始菜单
Name: "{group}\阑山桌面"; Filename: "{app}\LanMountainDesktop.exe"; IconFilename: "{app}\LanMountainDesktop.exe"
```

---

## 文件变更清单

### 修改文件

1. `LanMountainDesktop.Launcher/Program.cs` - 完全重写
2. `LanMountainDesktop.Launcher/LanMountainDesktop.Launcher.csproj` - 简化依赖
3. `LanMountainDesktop/LanMountainDesktop.csproj` - 移除 Launcher 引用
4. `LanMountainDesktop/Program.cs` - 添加 LaunchContext
5. `LanMountainDesktop/App.axaml.cs` - 添加 OOBE/Splash/更新入口
6. `.github/workflows/release.yml` - 调整打包流程
7. `.github/workflows/build.yml` - 适配新构建流程

### 新增文件

1. `LanMountainDesktop/Services/Oobe/OobeService.cs`
2. `LanMountainDesktop/Views/Oobe/OobeWindow.axaml`
3. `LanMountainDesktop/Views/Oobe/OobeWindow.axaml.cs`
4. `LanMountainDesktop/Views/Splash/SplashWindow.axaml`
5. `LanMountainDesktop/Views/Splash/SplashWindow.axaml.cs`
6. `LanMountainDesktop/Services/Update/UpdateService.cs`
7. `LanMountainDesktop/Services/Update/LauncherUpdateService.cs`
8. `LanMountainDesktop/Services/Plugins/PluginUpdateService.cs`

### 删除文件

1. `LanMountainDesktop.Launcher/App.axaml`
2. `LanMountainDesktop.Launcher/App.axaml.cs`
3. `LanMountainDesktop.Launcher/Views/` 目录
4. `LanMountainDesktop.Launcher/Services/LauncherFlowCoordinator.cs`
5. `LanMountainDesktop.Launcher/Services/OobeStateService.cs`
6. `LanMountainDesktop.Launcher/Services/UpdateCheckService.cs`
7. `LanMountainDesktop.Launcher/Services/UpdateEngineService.cs`
8. `LanMountainDesktop.Launcher/Services/PluginInstallerService.cs`
9. `LanMountainDesktop.Launcher/Services/PluginUpgradeQueueService.cs`

---

## 风险与回滚方案

### 风险

1. **启动失败**: 新 Launcher 可能有 bug 导致无法启动
2. **更新中断**: 更新逻辑迁移可能导致更新失败
3. **图标丢失**: 图标配置错误导致快捷方式无图标

### 回滚方案

1. 保留原 Launcher 代码分支
2. 准备紧急修复版本
3. 用户可手动下载完整安装包恢复

---

## 验证清单

- [ ] Launcher 能正常启动主程序
- [ ] 版本选择逻辑正确
- [ ] 旧版本清理正常
- [ ] OOBE 流程正常
- [ ] Splash 显示正常
- [ ] 更新检查正常
- [ ] 插件安装正常
- [ ] GitHub Actions 打包成功
- [ ] 安装程序图标正常
- [ ] 快捷方式图标正常

---

## 实施顺序建议

### 第一阶段（立即实施）
1. 重写 Launcher Program.cs
2. 修改 Launcher.csproj
3. 移除主程序对 Launcher 的引用
4. 测试基本启动功能

### 第二阶段（功能迁移）
1. 迁移 OOBE 到主程序
2. 迁移 Splash 到主程序
3. 迁移更新逻辑到主程序
4. 迁移插件管理到主程序

### 第三阶段（CI/CD）
1. 修改 release.yml
2. 修改 build.yml
3. 测试打包流程
4. 验证安装程序

### 第四阶段（优化）
1. 实现 Launcher 自更新
2. 性能优化
3. 清理旧代码
