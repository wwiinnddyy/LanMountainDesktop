# Launcher 架构文档

> LanMountainDesktop.Launcher - 应用启动器和版本管理系统

## 目录

- [概述](#概述)
- [职责范围](#职责范围)
- [架构设计](#架构设计)
- [核心服务](#核心服务)
- [版本管理](#版本管理)
- [启动流程](#启动流程)
- [命令行接口](#命令行接口)
- [开发指南](#开发指南)

## 概述

Launcher 是 LanMountainDesktop 的唯一入口点,负责:
- 首次体验引导 (OOBE)
- 启动动画 (Splash Screen)
- 多版本管理和选择
- 应用更新 (增量更新、原子化更新)
- 插件安装和升级
- 版本回退

**设计理念**: 参考 ClassIsland 项目,实现原子化的多版本管理和随时版本回退能力。

## 职责范围

### 1. OOBE (Out-of-Box Experience)
- 首次启动引导
- 欢迎页面
- 初始设置向导

### 2. Splash Screen
- 启动动画
- 加载进度显示
- 品牌展示

### 3. 版本管理
- 多版本并存 (`app-{version}/` 目录)
- 版本选择算法
- 版本标记系统 (`.current`, `.partial`, `.destroy`)
- 旧版本自动清理

### 4. 应用更新
- GitHub Release API 集成
- 更新频道管理 (Stable/Preview)
- 增量更新下载
- 原子化更新应用
- 签名验证
- 版本回退

### 5. 插件管理
- 插件安装 (`.laapp` 包)
- 插件更新检查
- 插件升级队列处理

## 架构设计

### 目录结构

**安装后的目录结构:**
```
C:\Program Files\LanMountainDesktop\
├── LanMountainDesktop.Launcher.exe  ← 唯一入口
├── app-1.0.0/                        ← 版本目录
│   ├── .current                      ← 当前版本标记
│   ├── LanMountainDesktop.exe
│   ├── LanMountainDesktop.dll
│   └── ... (所有依赖)
├── app-1.0.1/                        ← 新版本
│   ├── .partial                      ← 下载中标记
│   └── ...
├── app-0.9.9/                        ← 旧版本
│   ├── .destroy                      ← 待删除标记
│   └── ...
└── .launcher/                        ← Launcher 数据目录
    ├── state/
    │   └── first_run_completed       ← OOBE 完成标记
    ├── update/
    │   ├── incoming/                 ← 更新缓存
    │   │   ├── files.json
    │   │   ├── files.json.sig
    │   │   └── update.zip
    │   └── public-key.pem            ← RSA 公钥
    └── snapshots/                    ← 更新快照
        └── {snapshot-id}.json
```

### 版本标记文件

| 文件名 | 作用 | 创建时机 | 删除时机 |
|--------|------|----------|----------|
| `.current` | 标记当前使用的版本 | 更新完成后 | 新版本激活时 |
| `.partial` | 标记下载未完成的版本 | 开始下载时 | 下载完成验证通过后 |
| `.destroy` | 标记待删除的旧版本 | 新版本激活时 | 目录删除后 |

## 核心服务

### DeploymentLocator
**职责**: 扫描和定位版本目录,选择最佳版本

**关键方法**:
```csharp
// 查找当前部署目录
string? FindCurrentDeploymentDirectory()

// 解析主程序可执行文件路径
string? ResolveHostExecutablePath()

// 获取当前版本号
string GetCurrentVersion()

// 构建下一个部署目录路径
string BuildNextDeploymentDirectory(string targetVersion)

// 清理标记为 .destroy 的目录
void CleanupDestroyedDeployments()
```

**版本选择算法**:
1. 扫描所有 `app-*` 目录
2. 过滤掉带 `.destroy` 或 `.partial` 标记的目录
3. 优先选择带 `.current` 标记的版本
4. 如果没有 `.current`,选择版本号最高的

### UpdateCheckService
**职责**: 检查 GitHub Release 更新

**关键方法**:
```csharp
// 检查更新
Task<UpdateCheckResult> CheckForUpdateAsync(
    string currentVersion,
    UpdateChannel channel,
    CancellationToken cancellationToken = default)
```

**更新频道**:
- `Stable` - 只检查 `prerelease=false` 的版本
- `Preview` - 检查所有版本 (包括 `prerelease=true`)

### UpdateEngineService
**职责**: 下载、验证、应用更新

**关键方法**:
```csharp
// 检查待处理的更新
LauncherResult CheckPendingUpdate()

// 下载更新
Task<LauncherResult> DownloadAsync(
    string manifestUrl,
    string signatureUrl,
    string archiveUrl,
    CancellationToken cancellationToken)

// 应用待处理的更新
LauncherResult ApplyPendingUpdate()

// 回退到上一个版本
LauncherResult RollbackLatest()

// 清理待删除的部署
void CleanupDestroyedDeployments()
```

### LauncherFlowCoordinator
**职责**: 协调完整的启动流程

**启动流程**:
1. 清理待删除的旧版本
2. 检查是否首次运行,显示 OOBE
3. 显示 Splash 窗口
4. 应用待处理的更新
5. 处理插件升级队列
6. 启动主程序
7. 关闭 Splash 窗口

### OobeStateService
**职责**: 管理首次运行状态

**关键方法**:
```csharp
// 检查是否首次运行
bool IsFirstRun()

// 标记 OOBE 已完成
void MarkCompleted()
```

### PluginInstallerService
**职责**: 处理插件安装

**关键方法**:
```csharp
// 安装插件包
Task<PluginInstallResult> InstallAsync(
    string packagePath,
    string targetDirectory,
    CancellationToken cancellationToken = default)
```

### PluginUpgradeQueueService
**职责**: 批量处理插件升级队列

**关键方法**:
```csharp
// 应用待处理的插件升级
LauncherResult ApplyPendingUpgrades(string pluginsDirectory)
```

## 版本管理

### 版本选择算法详解

```csharp
public string? FindCurrentDeploymentDirectory()
{
    var candidates = Directory.GetDirectories(rootDir, "app-*");
    
    // 1. 过滤无效版本
    var validCandidates = candidates
        .Where(path => 
            !File.Exists(Path.Combine(path, ".destroy")) &&
            !File.Exists(Path.Combine(path, ".partial")))
        .ToList();
    
    // 2. 优先选择带 .current 标记的
    var withMarkers = validCandidates
        .Where(path => File.Exists(Path.Combine(path, ".current")))
        .OrderByDescending(path => ParseVersion(path))
        .FirstOrDefault();
    
    if (withMarkers != null)
        return withMarkers;
    
    // 3. 选择版本号最高的
    return validCandidates
        .OrderByDescending(path => ParseVersion(path))
        .FirstOrDefault();
}
```

### 版本激活流程

```csharp
private void ActivateDeployment(string fromDeployment, string toDeployment)
{
    // 1. 在新版本添加 .current 标记
    File.WriteAllText(Path.Combine(toDeployment, ".current"), string.Empty);
    
    // 2. 移除旧版本的 .current 标记
    var fromCurrent = Path.Combine(fromDeployment, ".current");
    if (File.Exists(fromCurrent))
        File.Delete(fromCurrent);
    
    // 3. 标记旧版本为待删除
    File.WriteAllText(Path.Combine(fromDeployment, ".destroy"), string.Empty);
    
    // 4. 移除新版本的 .partial 标记 (如果有)
    var toPartial = Path.Combine(toDeployment, ".partial");
    if (File.Exists(toPartial))
        File.Delete(toPartial);
}
```

### 版本清理流程

```csharp
public void CleanupDestroyedDeployments()
{
    var destroyedDirs = Directory.GetDirectories(rootDir)
        .Where(x => File.Exists(Path.Combine(x, ".destroy")));
    
    foreach (var dir in destroyedDirs)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // 忽略删除失败 (可能文件被占用)
            // 下次启动时再试
        }
    }
}
```

## 启动流程

### 完整启动流程图

```
用户启动 Launcher.exe
    ↓
清理旧版本 (.destroy 目录)
    ↓
首次运行? ──Yes→ 显示 OOBE 窗口
    ↓ No
显示 Splash 窗口
    ↓
检查待处理的更新
    ↓
有更新? ──Yes→ 应用更新 (原子化)
    ↓ No
处理插件升级队列
    ↓
选择最佳版本 (DeploymentLocator)
    ↓
启动主程序 (Process.Start)
    ↓
关闭 Splash 窗口
    ↓
Launcher 退出
```

### 代码流程

**Program.cs**:
```csharp
static async Task<int> Main(string[] args)
{
    var commandContext = CommandContext.FromArgs(args);
    
    // 处理 CLI 命令
    if (commandContext.Command != "launch")
        return await Commands.RunCliCommandAsync(commandContext);
    
    // 启动 Avalonia 应用
    LauncherRuntimeContext.Current = commandContext;
    BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    return Environment.ExitCode;
}
```

**App.axaml.cs**:
```csharp
public override void OnFrameworkInitializationCompleted()
{
    var appRoot = Commands.ResolveAppRoot(context);
    var deploymentLocator = new DeploymentLocator(appRoot);
    var updateCheckService = new UpdateCheckService("owner", "repo");
    
    var coordinator = new LauncherFlowCoordinator(
        context,
        deploymentLocator,
        new OobeStateService(appRoot),
        new UpdateEngineService(deploymentLocator),
        updateCheckService,
        new PluginInstallerService());
    
    _ = RunCoordinatorAsync(desktop, coordinator);
}
```

**LauncherFlowCoordinator.RunAsync()**:
```csharp
public async Task<LauncherResult> RunAsync()
{
    // 1. 清理旧版本
    _deploymentLocator.CleanupDestroyedDeployments();
    
    // 2. OOBE
    if (_oobeStateService.IsFirstRun())
    {
        foreach (var step in _oobeSteps)
            await step.RunAsync(CancellationToken.None);
    }
    
    // 3. Splash
    var splashWindow = await Dispatcher.UIThread.InvokeAsync(() =>
    {
        var window = new SplashWindow();
        window.Show();
        return window;
    });
    
    try
    {
        // 4. 应用更新
        var updateResult = _updateEngine.ApplyPendingUpdate();
        if (!updateResult.Success)
            return updateResult;
        
        // 5. 插件升级
        var pluginsDir = Path.Combine(_deploymentLocator.GetAppRoot(), "plugins");
        var queueResult = new PluginUpgradeQueueService(_pluginInstallerService)
            .ApplyPendingUpgrades(pluginsDir);
        if (!queueResult.Success)
            return queueResult;
        
        // 6. 启动主程序
        var hostResult = LaunchHost();
        if (!hostResult.Success)
            return hostResult;
        
        return new LauncherResult { Success = true };
    }
    finally
    {
        await Dispatcher.UIThread.InvokeAsync(() => splashWindow.Close());
    }
}
```

## 命令行接口

### launch - 启动应用

```bash
LanMountainDesktop.Launcher.exe launch
```

启动完整流程: OOBE → Splash → 更新 → 插件 → 主程序

### update check - 检查更新

```bash
LanMountainDesktop.Launcher.exe update check
```

检查 GitHub Release 是否有新版本。

### update download - 下载更新

```bash
LanMountainDesktop.Launcher.exe update download --version 1.0.1
```

下载指定版本的更新包。

### update apply - 应用更新

```bash
LanMountainDesktop.Launcher.exe update apply
```

应用已下载的更新 (原子化操作)。

### update rollback - 版本回退

```bash
LanMountainDesktop.Launcher.exe update rollback
```

回退到上一个有效版本。

### plugin install - 安装插件

```bash
LanMountainDesktop.Launcher.exe plugin install <path-to-plugin.laapp>
```

安装 `.laapp` 插件包。

## 开发指南

### 本地调试

**直接运行 Launcher:**
```bash
dotnet run --project LanMountainDesktop.Launcher/LanMountainDesktop.Launcher.csproj -- launch
```

**调试特定命令:**
```bash
# 检查更新
dotnet run --project LanMountainDesktop.Launcher/LanMountainDesktop.Launcher.csproj -- update check

# 版本回退
dotnet run --project LanMountainDesktop.Launcher/LanMountainDesktop.Launcher.csproj -- update rollback
```

### 模拟多版本环境

```bash
# 1. 发布主程序
dotnet publish LanMountainDesktop/LanMountainDesktop.csproj -c Debug -o ./test-deploy/app-1.0.0

# 2. 创建 .current 标记
New-Item -ItemType File -Path ./test-deploy/app-1.0.0/.current

# 3. 复制 Launcher 到根目录
Copy-Item LanMountainDesktop.Launcher/bin/Debug/net10.0/* ./test-deploy/

# 4. 运行 Launcher
./test-deploy/LanMountainDesktop.Launcher.exe launch
```

### 测试更新流程

```bash
# 1. 创建两个版本
dotnet publish LanMountainDesktop/LanMountainDesktop.csproj -o ./test-deploy/app-1.0.0
dotnet publish LanMountainDesktop/LanMountainDesktop.csproj -o ./test-deploy/app-1.0.1

# 2. 生成增量包
pwsh ./scripts/Generate-DeltaPackage.ps1 `
  -PreviousVersion "1.0.0" `
  -CurrentVersion "1.0.1" `
  -PreviousDir "./test-deploy/app-1.0.0" `
  -CurrentDir "./test-deploy/app-1.0.1" `
  -OutputDir "./test-deploy/.launcher/update/incoming"

# 3. 测试应用更新
./test-deploy/LanMountainDesktop.Launcher.exe update apply
```

### 添加新的 OOBE 步骤

1. 实现 `IOobeStep` 接口:
```csharp
public class MyOobeStep : IOobeStep
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        // 显示 OOBE 窗口
        // 等待用户完成
    }
}
```

2. 在 `LauncherFlowCoordinator` 中注册:
```csharp
_oobeSteps = [
    new WelcomeOobeStep(_oobeStateService),
    new MyOobeStep()  // 添加新步骤
];
```

当前内置 OOBE 向导窗口（`OobeWindow`）内步骤顺序包含：开场 → 主题 → **数据保存位置** → **启动与展示** → 隐私与遥测 → 完成。「启动与展示」写入 Host 的 `settings.json`（PascalCase）并在 Windows 下同步 Run 项，实现代码在 `HostAppSettingsOobeMerger.cs` 与 `LauncherWindowsStartupService.cs`，界面与逻辑挂在 `Views/OobeWindow.axaml(.cs)`。

### 自定义更新源

修改 `App.axaml.cs` 中的 GitHub 仓库信息:
```csharp
var updateCheckService = new UpdateCheckService(
    "YourOrg",      // GitHub 组织/用户名
    "YourRepo"      // 仓库名
);
```

## 相关文档

- [更新系统详细文档](UPDATE_SYSTEM.md)
- [构建和部署指南](BUILD_AND_DEPLOY.md)
- [架构文档](ARCHITECTURE.md)
- [开发文档](DEVELOPMENT.md)

## Current OOBE and Elevation Contract

- OOBE state is a per-user truth source stored at `%LOCALAPPDATA%\LanMountainDesktop\.launcher\state\oobe-state.json`.
- Same-user reinstall or upgrade must not re-enter OOBE.
- `first_run_completed` is legacy compatibility data only and should not remain the long-term primary format.
- Launch source values are `normal`, `postinstall`, `apply-update`, `plugin-install`, and `debug-preview`.
- Auto-OOBE is allowed only for normal user-mode startup.
- `postinstall` may open OOBE only when the launcher is not elevated and the user state path is available.
- `apply-update`, `plugin-install`, and `debug-preview` must not auto-enter OOBE.
- Allowed elevation paths are limited to the installer itself, full installer update application, and user-confirmed legacy uninstall.
- Default plugin installation targets the current user's LocalAppData scope and must not request elevation by default.

## Public IPC Baseline

Launcher now consumes Host startup telemetry from the unified public IPC stack:

- Host publishes `StartupProgressMessage` via `lanmountain.launcher.startup-progress`
- Host publishes `LoadingStateMessage` via `lanmountain.launcher.loading-state`
- Launcher connects through `LanMountainDesktopIpcClient`

The previous custom length-prefixed named-pipe transport is no longer the primary startup communication path.

## Coordinator Guard

Launcher also owns a small per-user local coordinator used only between Launcher processes. It reserves `startup-attempt.json` before host launch, publishes a heartbeat, and exposes a local coordinator pipe for secondary Launchers. A secondary Launcher must attach to that coordinator or activate the existing Host through Public IPC instead of starting another Host process. See [Launcher Coordinator](LAUNCHER_COORDINATOR.md).
