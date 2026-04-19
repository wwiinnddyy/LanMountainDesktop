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

**Launcher 其他命令:**
```bash
# 检查更新
dotnet run --project LanMountainDesktop.Launcher/LanMountainDesktop.Launcher.csproj -- update check

# 安装插件
dotnet run --project LanMountainDesktop.Launcher/LanMountainDesktop.Launcher.csproj -- plugin install <path-to-plugin.laapp>

# 版本回退
dotnet run --project LanMountainDesktop.Launcher/LanMountainDesktop.Launcher.csproj -- update rollback
```

#### 运行测试

```bash
dotnet test LanMountainDesktop.slnx -c Debug
```

### 常见工作区域

- 宿主应用：`LanMountainDesktop/`
- **Launcher (启动器)：`LanMountainDesktop.Launcher/`**
- Plugin SDK：`LanMountainDesktop.PluginSdk/`
- 共享契约：`LanMountainDesktop.Shared.Contracts/`
- 测试：`LanMountainDesktop.Tests/`
- 插件打包脚本：`scripts/Pack-PluginPackages.ps1`
- **增量更新脚本：`scripts/Generate-DeltaPackage.ps1`, `scripts/Sign-FileMap.ps1`**

### 调试建议

- **Launcher 启动问题优先看 `LanMountainDesktop.Launcher/Program.cs` 和 `Services/LauncherFlowCoordinator.cs`**
- **版本管理问题优先看 `LanMountainDesktop.Launcher/Services/DeploymentLocator.cs`**
- **更新系统问题优先看 `LanMountainDesktop.Launcher/Services/UpdateEngineService.cs` 和 `UpdateCheckService.cs`**
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

LanMountainDesktop 使用 Launcher 作为唯一入口,负责版本管理、更新和启动主程序。

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
└── .launcher/                        ← Launcher 数据
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
5. 检查并应用待处理的更新
6. 处理插件升级队列
7. 启动主程序 `app-{version}/LanMountainDesktop.exe`
8. 清理标记为 `.destroy` 的旧版本

#### 更新流程

1. Launcher 调用 GitHub Release API 检查更新
2. 根据更新频道(Stable/Preview)过滤版本
3. 下载增量包到 `app-{new_version}/` 并标记 `.partial`
4. 验证文件完整性(SHA256)
5. 删除 `.partial`,添加 `.current` 到新版本
6. 标记旧版本 `.destroy`
7. 下次启动时自动清理

#### 版本回退

```bash
dotnet run --project LanMountainDesktop.Launcher/LanMountainDesktop.Launcher.csproj -- update rollback
```

回退会切换到上一个有效版本,并保留快照记录。

## English

Use `LanMountainDesktop.slnx` as the workspace entry point. The standard loop is `dotnet restore`, `dotnet build LanMountainDesktop.slnx -c Debug`, `dotnet run --project LanMountainDesktop/LanMountainDesktop.csproj`, and `dotnet test LanMountainDesktop.slnx -c Debug`.

For packaging, see `LanMountainDesktop/PACKAGING.md`. For plugin package generation or local feed workflows, use `scripts/Pack-PluginPackages.ps1`.

**Launcher Architecture**: LanMountainDesktop uses a Launcher as the single entry point, responsible for version management, updates, and launching the main application. See the Chinese section above for detailed architecture documentation.

## VeloPack Release Assets

- Windows incremental release packaging now uses VeloPack native outputs (eleases.win.json, *.nupkg).
- Launcher still performs update apply/rollback; VeloPack is used for package generation.
- Legacy delta script flow is retained behind a disabled fallback switch in CI.

