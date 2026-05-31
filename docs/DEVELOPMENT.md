# 开发文档 / Development

## 中文

### 环境准备

- 安装 `.NET SDK 10`
- 桌面端建议优先在 Windows 上开发和验证
- 仓库主入口解决方案文件为 `LanMountainDesktop.slnx`
- SDK 版本由仓库根目录 `global.json` 锁定

### 常用命令

#### 还原与构建

```bash
dotnet restore
dotnet build LanMountainDesktop.slnx -c Debug
```

#### 运行桌面宿主

**开发模式 (直接运行主程序,跳过 Launcher):**
```bash
dotnet run --project LanMountainDesktop/LanMountainDesktop.csproj
```

**生产模式 (通过 Launcher 启动):**
```bash
# 先构建 Launcher
dotnet build LanMountainDesktop.Launcher/LanMountainDesktop.Launcher.csproj -c Debug

# 通过 Launcher 启动主程序
dotnet run --project LanMountainDesktop.Launcher/LanMountainDesktop.Launcher.csproj -- launch
```

Air APP 开发调试时需要同时构建 `LanMountainDesktop.AirAppRuntime`。正常 Launcher 启动会预启动该 Runtime；直接运行 Host 时，Host 会在第一次打开 Air APP 时兜底启动 Runtime。

**Launcher 其他命令:**
```bash
# 安装插件
dotnet run --project LanMountainDesktop.Launcher/LanMountainDesktop.Launcher.csproj -- plugin install <path-to-plugin.laapp>

# Launcher 不提供更新/回滚 CLI；调试更新请运行主程序并使用 Host 更新服务。
```

#### 运行测试

```bash
dotnet test LanMountainDesktop.slnx -c Debug
```

### 常见工作区域

- 宿主应用：`LanMountainDesktop/`
- **Launcher (启动器)：`LanMountainDesktop.Launcher/`**
- **AirApp Runtime (轻应用生命周期容器)：`LanMountainDesktop.AirAppRuntime/`**
- Plugin SDK：`LanMountainDesktop.PluginSdk/`
- 共享契约：`LanMountainDesktop.Shared.Contracts/`
- 测试：`LanMountainDesktop.Tests/`
- 插件打包脚本：`scripts/Pack-PluginPackages.ps1`
- **增量更新脚本：`scripts/Generate-DeltaPackage.ps1`, `scripts/Sign-FileMap.ps1`**

### 调试建议

- **Launcher 启动问题优先看 `LanMountainDesktop.Launcher/Program.cs`、`Shell/LauncherOrchestrator.cs` 和 `Startup/LaunchPipeline.cs`**
- **版本管理问题优先看 `LanMountainDesktop.Launcher/Deployment/DeploymentLocator.cs`**
- **更新检查、下载、应用和回滚问题优先看 `LanMountainDesktop/Services/Update/UpdateOrchestrator.cs`、`UpdateInstallGateway.cs` 和 `UpdateRollbackGateway.cs`**
- 启动问题优先看 `LanMountainDesktop/Program.cs` 和 `LanMountainDesktop/App.axaml.cs`
- 设置窗口和设置页问题优先看 `LanMountainDesktop/Views/`、`ViewModels/` 与相关 `Services/`
- 插件加载与安装问题优先看 `LanMountainDesktop/plugins/`
- 组件元数据或可放置规则问题优先看 `LanMountainDesktop/ComponentSystem/`

### 常见问题

- 如果提示 SDK 版本不匹配，先检查 `dotnet --info`
- 如果视频或 WebView 能力异常，优先在 Windows 环境验证
- 如果需要重置本地配置，可删除 `%LOCALAPPDATA%\\LanMountainDesktop\\settings.json` 后重启
- 如果需要验证插件打包或本地 feed，使用 `scripts/Pack-PluginPackages.ps1`

### Linux 录音依赖

如果在 Linux 上使用录音机或自习监测相关能力，需要安装音频库：

- Debian/Ubuntu：`sudo apt install libportaudio2 libasound2`
- Fedora/RHEL：`sudo dnf install portaudio-libs alsa-lib`
- Arch Linux：`sudo pacman -S portaudio alsa-lib`
- Alpine Linux：`sudo apk add portaudio alsa-lib`

### 打包入口

- 桌面宿主打包说明：`LanMountainDesktop/PACKAGING.md`
- 插件相关本地包生成：`scripts/Pack-PluginPackages.ps1`
- CI 和工作流说明：`.github/README.md` 与相关 workflow 文档

### 文档协作约定

- 产品信息更新到 `docs/PRODUCT.md`
- 架构边界更新到 `docs/ARCHITECTURE.md`
- 需求与实施拆解更新到 `.trae/specs/`
- AI 协作入口和代码地图更新到 `AGENTS.md` 与 `docs/ai/`

### Launcher 架构说明

LanMountainDesktop 使用 Launcher 作为唯一入口,负责版本目录选择、AirApp Runtime 预启动和主程序启动。更新检查、下载、应用和回滚全部由 Host 负责。

#### 目录结构

安装后的目录结构:
```
C:\Program Files\LanMountainDesktop\
├── LanMountainDesktop.Launcher.exe  ← 唯一入口
├── app-1.0.0/                        ← 版本目录
│   ├── .current                      ← 当前版本标记
│   ├── LanMountainDesktop.exe
│   └── ... (所有依赖)
├── app-1.0.1/                        ← 新版本
│   ├── .partial                      ← 下载中标记
│   └── ...
└── .Launcher/                        ← Launcher 数据
    ├── state/                        ← OOBE 状态
    ├── update/incoming/              ← 更新缓存
    └── snapshots/                    ← 更新快照
```

#### 版本标记文件

- `.current` - 标记当前使用的版本
- `.partial` - 标记下载未完成的版本
- `.destroy` - 标记待删除的旧版本

#### 启动流程

1. 用户启动 `LanMountainDesktop.Launcher.exe`
2. Launcher 扫描 `app-*` 目录,选择最佳版本
3. 如果是首次启动,显示 OOBE 引导
4. 显示 Splash 启动动画
5. 预启动 AirApp Runtime
6. 启动主程序 `app-{version}/LanMountainDesktop.exe`
7. 主程序启动成功后附加 Host PID 给 AirApp Runtime，并清理标记为 `.destroy` 的旧版本

#### 更新流程

1. Host 调用更新源检查更新并按频道过滤版本
2. Host 下载 PLONDS file map、签名和对象文件到 `.Launcher/update/incoming/`
3. Host 写入 `deployment.lock` 并调用 `UpdateInstallGateway`
4. Host 验证签名和文件 hash，创建新 `app-*` 目录，切换 `.current`
5. Host 写入快照并清理 incoming；旧版本按启动清理策略处理

#### 版本回退

```bash
运行主程序，打开设置页中的更新区域触发回滚。
```

回退会切换到上一个有效版本,并保留快照记录。

## English

Use `LanMountainDesktop.slnx` as the workspace entry point. The standard loop is `dotnet restore`, `dotnet build LanMountainDesktop.slnx -c Debug`, `dotnet run --project LanMountainDesktop/LanMountainDesktop.csproj`, and `dotnet test LanMountainDesktop.slnx -c Debug`.

For packaging, see `LanMountainDesktop/PACKAGING.md`. For plugin package generation or local feed workflows, use `scripts/Pack-PluginPackages.ps1`.

In-app marketplace plugin installs use a per-user pending plugin queue. The package is downloaded and verified immediately, then applied on the next Host startup before plugin discovery. `LanMountainDesktop.Launcher.exe plugin install` remains only as a maintenance compatibility command.

**Launcher Architecture**: LanMountainDesktop uses a Launcher as the single entry point, responsible for version management, updates, and launching the main application. See the Chinese section above for detailed architecture documentation.

## VeloPack Release Assets

- Windows incremental release packaging now uses VeloPack native outputs (eleases.win.json, *.nupkg).
- Host owns update check/download/apply/rollback orchestration. Launcher only selects and starts the current version; VeloPack is used for package generation.
- Legacy delta script flow is retained behind a disabled fallback switch in CI.
