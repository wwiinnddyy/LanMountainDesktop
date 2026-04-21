# LanMountainDesktop Launcher 改进计划 V2

## 核心设计理念

**Launcher 是核心协调器，不是极简启动器**

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           Launcher 职责定位                                   │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  Launcher 负责（启动前 & 退出后）：                                            │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  • OOBE 首次引导                                                      │   │
│  │  • 启动动画 (Splash)                                                  │   │
│  │  • 插件安装                                                           │   │
│  │  • 插件更新                                                           │   │
│  │  • 应用增量更新安装（不是下载！）                                        │   │
│  │  • 应用静默更新安装                                                   │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
│  主程序负责（运行时）：                                                        │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  • 多线程下载（有完整 Downloader）                                     │   │
│  │  • 更新渠道切换                                                        │   │
│  │  • 下载管理                                                           │   │
│  │  • 与 Launcher 通讯（启动进度）                                         │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
│  关键优势：                                                                  │
│  • Launcher 在应用启动前运行 → 可以安装更新而不担心文件占用                    │
│  • Launcher 在应用退出后运行 → 可以完成待处理的安装任务                        │
│  • 主程序专注下载 → 利用完整的多线程下载器提高效率                            │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

## 为什么保留 Avalonia？

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         保留 Avalonia 的理由                                  │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  1. 启动画面 (Splash)                                                        │
│     ┌─────────────────────────────────────────────────────────────────┐    │
│     │  • 需要显示启动进度                                                  │    │
│     │  • 需要显示品牌 Logo                                                 │    │
│     │  • 需要流畅的动画效果                                                 │    │
│     │  • 纯 Win32 实现复杂且不易维护                                         │    │
│     └─────────────────────────────────────────────────────────────────┘    │
│                                                                             │
│  2. OOBE 首次引导                                                            │
│     ┌─────────────────────────────────────────────────────────────────┐    │
│     │  • 需要多步骤向导界面                                                │    │
│     │  • 需要丰富的交互控件                                                │    │
│     │  • 需要与主程序一致的视觉风格                                          │    │
│     │  • Avalonia 提供完整的 UI 框架                                        │    │
│     └─────────────────────────────────────────────────────────────────┘    │
│                                                                             │
│  3. 与主程序的技术栈一致                                                      │
│     ┌─────────────────────────────────────────────────────────────────┐    │
│     │  • 共享主题和资源                                                    │    │
│     │  • 共享控件和样式                                                    │    │
│     │  • 便于维护和迭代                                                    │    │
│     └─────────────────────────────────────────────────────────────────┘    │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

## 改进后的架构设计

### 目录结构（保持不变）

```
安装根目录/
├── LanMountainDesktop.exe              ← Launcher（Avalonia 应用）
├── app-1.0.0/                          ← 版本目录
│   ├── .current                        ← 当前版本标记
│   ├── LanMountainDesktop.exe          ← 主程序
│   └── ... (所有依赖)
└── .launcher/                          ← Launcher 数据目录
    ├── update/                         ← 更新缓存
    │   └── incoming/                   ← 下载的更新包（主程序下载到这里）
    └── snapshots/                      ← 版本快照
```

### 核心流程设计

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                          启动流程（含通讯机制）                                │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  1. 用户启动 LanMountainDesktop.exe (Launcher)                               │
│     ↓                                                                       │
│  2. Launcher 检查是否有待处理的更新安装                                        │
│     ↓                                                                       │
│  3. 有更新？──Yes──▶ 显示 Splash "正在安装更新..."                             │
│     ↓                    ↓                                                  │
│     No              安装更新（增量/静默）                                       │
│     ↓                    ↓                                                  │
│  4. 检查是否首次运行 ──Yes──▶ 显示 OOBE 窗口                                   │
│     ↓ No                    ↓                                               │
│  5. 显示 Splash "正在启动..."      完成 OOBE                                  │
│     ↓                                                                       │
│  6. 启动主程序进程（带通讯参数）                                                │
│     ↓                                                                       │
│  7. Launcher 保持运行，监听主程序进度 ─────── IPC 通讯 ───────▶ 主程序           │
│     ↓                                                                       │
│  8. 主程序报告启动进度 ─────── IPC 通讯 ───────▶ Launcher 更新 Splash           │
│     ↓                                                                       │
│  9. 主程序完全启动 ──Yes──▶ Launcher 关闭 Splash，进入后台/退出                  │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 退出流程

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                          退出流程（处理待安装任务）                             │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  1. 主程序准备退出                                                            │
│     ↓                                                                       │
│  2. 检查是否有待安装的更新/插件 ──Yes──▶ 重启 Launcher 并传递参数               │
│     ↓ No                    ↓                                               │
│  3. 正常退出              Launcher 在应用退出后运行                             │
│                              ↓                                              │
│                           安装待处理的任务                                     │
│                              ↓                                              │
│                           完成后再次启动主程序                                  │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

## Launcher 与主程序的通讯机制

### IPC 方案选择

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           IPC 通讯方案                                        │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  方案 1: 命令行参数 + 退出码（推荐用于启动阶段）                                │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  Launcher 启动主程序:                                                │   │
│  │    LanMountainDesktop.exe --launcher-pid 12345 --ipc-port 50000      │   │
│  │                                                                      │   │
│  │  主程序通过命名管道/HTTP 与 Launcher 通讯                               │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
│  方案 2: 命名管道（推荐用于进度报告）                                           │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  Launcher 创建命名管道: \\.\pipe\LanMountainDesktop_Launcher          │   │
│  │  主程序连接并发送进度消息                                              │   │
│  │                                                                      │   │
│  │  消息格式: JSON                                                       │   │
│  │    {"stage": "initializing", "progress": 30, "message": "加载设置..."} │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
│  方案 3: 共享内存/文件（简单状态同步）                                          │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  Launcher 和主程序读写同一个状态文件                                     │   │
│  │  .launcher/state/startup_status.json                                  │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 通讯协议设计

```csharp
// 共享契约（LanMountainDesktop.Shared.Contracts）
namespace LanMountainDesktop.Shared.Contracts.Launcher;

public enum StartupStage
{
    Initializing,
    LoadingSettings,
    LoadingPlugins,
    InitializingUI,
    Ready
}

public record StartupProgressMessage
{
    public StartupStage Stage { get; init; }
    public int ProgressPercent { get; init; }  // 0-100
    public string? Message { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public static class LauncherIpc
{
    public const string PipeName = "LanMountainDesktop_Launcher";
    public const string EnvironmentVariablePrefix = "LMD_";
}
```

## 详细实施步骤

### P0: 架构调整（核心）

#### 1. 调整 Launcher 项目引用

**文件**: `LanMountainDesktop.Launcher/LanMountainDesktop.Launcher.csproj`

**修改**:
- 保留 Avalonia 依赖
- 移除 PluginSdk 引用（Launcher 不需要）
- 添加 Shared.Contracts 引用（用于 IPC）

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <ApplicationIcon>Assetsogo_nightly.ico</ApplicationIcon>
  </PropertyGroup>

  <ItemGroup>
    <!-- 保留 Avalonia -->
    <PackageReference Include="Avalonia" Version="11.3.12" />
    <PackageReference Include="Avalonia.Desktop" Version="11.3.12" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="11.3.12" />
    
    <!-- 只引用 Shared.Contracts（IPC 协议） -->
    <ProjectReference Include="..\LanMountainDesktop.Shared.Contracts\LanMountainDesktop.Shared.Contracts.csproj" />
  </ItemGroup>
  
  <!-- 图标资源 -->
  <ItemGroup>
    <Content Include="..\LanMountainDesktop\Assets\logo_nightly.ico" Link="Assets\logo_nightly.ico">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
  </ItemGroup>
</Project>
```

#### 2. 移除主程序对 Launcher 的引用

**文件**: `LanMountainDesktop/LanMountainDesktop.csproj`

**修改**: 删除 Launcher 引用
```xml
<!-- 删除 -->
<!-- <ProjectReference Include="..\LanMountainDesktop.Launcher\LanMountainDesktop.Launcher.csproj" ReferenceOutputAssembly="false" /> -->
```

#### 3. 创建 IPC 通讯契约

**新建文件**: `LanMountainDesktop.Shared.Contracts/Launcher/LauncherIpc.cs`

```csharp
namespace LanMountainDesktop.Shared.Contracts.Launcher;

public enum StartupStage
{
    Initializing,
    LoadingSettings,
    LoadingPlugins,
    InitializingUI,
    Ready
}

public record StartupProgressMessage
{
    public StartupStage Stage { get; init; }
    public int ProgressPercent { get; init; }
    public string? Message { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public static class LauncherIpcConstants
{
    public const string PipeName = "LanMountainDesktop_Launcher";
    public const string LauncherPidEnvVar = "LMD_LAUNCHER_PID";
    public const string PackageRootEnvVar = "LMD_PACKAGE_ROOT";
    public const string VersionEnvVar = "LMD_VERSION";
}
```

### P1: Launcher 端实现

#### 4. 实现 IPC 服务端

**新建文件**: `LanMountainDesktop.Launcher/Services/Ipc/LauncherIpcServer.cs`

```csharp
using System.IO.Pipes;
using System.Text.Json;
using LanMountainDesktop.Shared.Contracts.Launcher;

namespace LanMountainDesktop.Launcher.Services.Ipc;

public class LauncherIpcServer : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private NamedPipeServerStream? _pipeServer;
    private readonly Action<StartupProgressMessage> _onProgress;
    
    public LauncherIpcServer(Action<StartupProgressMessage> onProgress)
    {
        _onProgress = onProgress;
    }
    
    public async Task StartAsync()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                _pipeServer = new NamedPipeServerStream(
                    LauncherIpcConstants.PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Message);
                
                await _pipeServer.WaitForConnectionAsync(_cts.Token);
                
                using var reader = new StreamReader(_pipeServer);
                var json = await reader.ReadToEndAsync(_cts.Token);
                
                if (!string.IsNullOrEmpty(json))
                {
                    var message = JsonSerializer.Deserialize<StartupProgressMessage>(json);
                    if (message != null)
                    {
                        _onProgress(message);
                    }
                }
                
                _pipeServer.Disconnect();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"IPC error: {ex.Message}");
            }
        }
    }
    
    public void Dispose()
    {
        _cts.Cancel();
        _pipeServer?.Dispose();
        _cts.Dispose();
    }
}
```

#### 5. 修改 Launcher 启动流程

**修改文件**: `LanMountainDesktop.Launcher/Services/LauncherFlowCoordinator.cs`

```csharp
public async Task<LauncherResult> RunAsync()
{
    // 1. 清理旧版本
    _deploymentLocator.CleanupDestroyedDeployments();
    
    // 2. 检查并安装待处理的更新（主程序下载的）
    var pendingUpdate = _updateEngine.CheckPendingUpdate();
    if (pendingUpdate.HasUpdate)
    {
        _splashWindow?.UpdateStatus("正在安装更新...");
        var updateResult = await _updateEngine.ApplyPendingUpdateAsync();
        if (!updateResult.Success)
        {
            return updateResult;
        }
    }
    
    // 3. 检查并安装待处理的插件更新
    var pendingPlugins = _pluginUpgradeQueueService.CheckPendingUpgrades();
    if (pendingPlugins.HasUpgrades)
    {
        _splashWindow?.UpdateStatus("正在更新插件...");
        var pluginResult = _pluginUpgradeQueueService.ApplyPendingUpgrades();
        if (!pluginResult.Success)
        {
            return pluginResult;
        }
    }
    
    // 4. OOBE
    if (_oobeStateService.IsFirstRun())
    {
        _splashWindow?.Hide();
        foreach (var step in _oobeSteps)
        {
            await step.RunAsync(CancellationToken.None);
        }
        _splashWindow?.Show();
    }
    
    // 5. 启动 IPC 服务端监听主程序进度
    using var ipcServer = new LauncherIpcServer(msg =>
    {
        _splashWindow?.UpdateProgress(msg.ProgressPercent, msg.Message);
    });
    _ = ipcServer.StartAsync();
    
    // 6. 启动主程序
    _splashWindow?.UpdateStatus("正在启动...");
    var hostResult = LaunchHostWithIpc();
    if (!hostResult.Success)
    {
        return hostResult;
    }
    
    // 7. 等待主程序报告就绪或超时
    await WaitForHostReadyOrTimeoutAsync(TimeSpan.FromSeconds(30));
    
    return new LauncherResult { Success = true };
}
```

### P2: 主程序端实现

#### 6. 实现 IPC 客户端

**新建文件**: `LanMountainDesktop/Services/Launcher/LauncherIpcClient.cs`

```csharp
using System.IO.Pipes;
using System.Text.Json;
using LanMountainDesktop.Shared.Contracts.Launcher;

namespace LanMountainDesktop.Services.Launcher;

public class LauncherIpcClient : IDisposable
{
    private NamedPipeClientStream? _pipeClient;
    
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _pipeClient = new NamedPipeClientStream(
            ".",
            LauncherIpcConstants.PipeName,
            PipeDirection.Out);
        
        await _pipeClient.ConnectAsync(5000, cancellationToken);
    }
    
    public async Task ReportProgressAsync(StartupProgressMessage message)
    {
        if (_pipeClient?.IsConnected != true)
            return;
        
        var json = JsonSerializer.Serialize(message);
        using var writer = new StreamWriter(_pipeClient, leaveOpen: true);
        await writer.WriteAsync(json);
        await writer.FlushAsync();
    }
    
    public void Dispose()
    {
        _pipeClient?.Dispose();
    }
}
```

#### 7. 主程序启动时报告进度

**修改文件**: `LanMountainDesktop/App.axaml.cs`

```csharp
public override async void OnFrameworkInitializationCompleted()
{
    // 检查是否从 Launcher 启动
    var launcherPid = Environment.GetEnvironmentVariable(LauncherIpcConstants.LauncherPidEnvVar);
    if (!string.IsNullOrEmpty(launcherPid))
    {
        // 连接到 Launcher 的 IPC 服务端
        _launcherIpc = new LauncherIpcClient();
        await _launcherIpc.ConnectAsync();
        
        // 报告启动进度
        await _launcherIpc.ReportProgressAsync(new StartupProgressMessage
        {
            Stage = StartupStage.Initializing,
            ProgressPercent = 10,
            Message = "正在初始化..."
        });
    }
    
    // 初始化设置
    await _launcherIpc?.ReportProgressAsync(new StartupProgressMessage
    {
        Stage = StartupStage.LoadingSettings,
        ProgressPercent = 30,
        Message = "正在加载设置..."
    });
    InitializeSettings();
    
    // 加载插件
    await _launcherIpc?.ReportProgressAsync(new StartupProgressMessage
    {
        Stage = StartupStage.LoadingPlugins,
        ProgressPercent = 50,
        Message = "正在加载插件..."
    });
    await InitializePluginsAsync();
    
    // 初始化 UI
    await _launcherIpc?.ReportProgressAsync(new StartupProgressMessage
    {
        Stage = StartupStage.InitializingUI,
        ProgressPercent = 80,
        Message = "正在初始化界面..."
    });
    InitializeUI();
    
    // 就绪
    await _launcherIpc?.ReportProgressAsync(new StartupProgressMessage
    {
        Stage = StartupStage.Ready,
        ProgressPercent = 100,
        Message = "就绪"
    });
    
    base.OnFrameworkInitializationCompleted();
}
```

### P3: 更新流程整合

#### 8. 主程序下载更新

**主程序职责**:
```csharp
// 主程序中的更新服务
public class AppUpdateService
{
    public async Task DownloadUpdateAsync(string version, string downloadUrl)
    {
        // 使用多线程下载器下载更新包
        var downloader = new MultiThreadedDownloader();
        var targetPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LanMountainDesktop",
            ".launcher",
            "update",
            "incoming",
            $"update-{version}.zip");
        
        await downloader.DownloadAsync(downloadUrl, targetPath);
        
        // 标记为待安装
        File.WriteAllText(
            Path.Combine(Path.GetDirectoryName(targetPath)!, ".pending"),
            version);
    }
}
```

#### 9. Launcher 安装更新

**Launcher 职责**:
```csharp
// Launcher 中的更新安装服务
public class UpdateInstallationService
{
    public async Task<InstallResult> InstallPendingUpdateAsync()
    {
        var pendingPath = Path.Combine(
            _appRoot,
            ".launcher",
            "update",
            "incoming",
            ".pending");
        
        if (!File.Exists(pendingPath))
            return InstallResult.NoUpdate;
        
        var version = File.ReadAllText(pendingPath);
        var updatePackagePath = Path.Combine(
            Path.GetDirectoryName(pendingPath)!,
            $"update-{version}.zip");
        
        // 创建新版本目录
        var newVersionDir = Path.Combine(_appRoot, $"app-{version}");
        Directory.CreateDirectory(newVersionDir);
        File.WriteAllText(Path.Combine(newVersionDir, ".partial"), "");
        
        // 解压更新包
        ZipFile.ExtractToDirectory(updatePackagePath, newVersionDir);
        
        // 验证文件完整性
        // ...
        
        // 切换版本标记
        var currentDir = _deploymentLocator.FindCurrentDeploymentDirectory();
        if (currentDir != null)
        {
            File.Delete(Path.Combine(currentDir, ".current"));
            File.WriteAllText(Path.Combine(currentDir, ".destroy"), "");
        }
        
        File.WriteAllText(Path.Combine(newVersionDir, ".current"), "");
        File.Delete(Path.Combine(newVersionDir, ".partial"));
        
        // 清理待安装标记
        File.Delete(pendingPath);
        File.Delete(updatePackagePath);
        
        return InstallResult.Success;
    }
}
```

### P4: GitHub Actions 工作流

#### 10. 修改 release.yml

**关键修改点**:

```yaml
# 1. Launcher 单独编译（保留 Avalonia）
- name: Publish Launcher
  run: |
    dotnet publish LanMountainDesktop.Launcher/LanMountainDesktop.Launcher.csproj `
      -c Release `
      -o ./publish/launcher-win-x64 `
      --self-contained `
      -r win-x64 `
      -p:PublishSingleFile=false `
      -p:DebugType=none

# 2. 目录结构调整
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

## 文件变更清单

### 修改文件

1. `LanMountainDesktop.Launcher/LanMountainDesktop.Launcher.csproj` - 调整引用
2. `LanMountainDesktop/LanMountainDesktop.csproj` - 移除 Launcher 引用
3. `LanMountainDesktop.Launcher/Services/LauncherFlowCoordinator.cs` - 添加 IPC 和更新安装
4. `LanMountainDesktop/App.axaml.cs` - 添加 IPC 客户端和进度报告
5. `.github/workflows/release.yml` - 调整打包流程

### 新增文件

1. `LanMountainDesktop.Shared.Contracts/Launcher/LauncherIpc.cs` - IPC 契约
2. `LanMountainDesktop.Launcher/Services/Ipc/LauncherIpcServer.cs` - IPC 服务端
3. `LanMountainDesktop/Services/Launcher/LauncherIpcClient.cs` - IPC 客户端
4. `LanMountainDesktop.Launcher/Services/Update/UpdateInstallationService.cs` - 更新安装

### 删除文件

1. 主程序对 Launcher 的项目引用（已存在）

## 实施顺序

### 第一阶段：基础架构
1. 创建 IPC 契约（Shared.Contracts）
2. 调整 Launcher 项目引用
3. 移除主程序对 Launcher 的引用
4. 测试基本启动

### 第二阶段：IPC 实现
1. 实现 Launcher IPC 服务端
2. 实现主程序 IPC 客户端
3. 测试进度报告

### 第三阶段：更新流程
1. 主程序实现下载功能
2. Launcher 实现安装功能
3. 测试完整更新流程

### 第四阶段：CI/CD
1. 修改 GitHub Actions
2. 测试打包流程
3. 验证安装程序

## 验证清单

- [ ] Launcher 能正常启动主程序
- [ ] Launcher 显示 Splash 并接收进度更新
- [ ] 主程序能向 Launcher 报告启动进度
- [ ] 主程序能下载更新
- [ ] Launcher 能安装待处理的更新
- [ ] OOBE 流程正常
- [ ] 插件更新流程正常
- [ ] GitHub Actions 打包成功
- [ ] 安装程序图标正常
- [ ] 快捷方式图标正常
