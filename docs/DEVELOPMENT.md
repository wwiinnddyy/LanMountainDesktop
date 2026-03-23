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

```bash
dotnet run --project LanMountainDesktop/LanMountainDesktop.csproj
```

#### 运行测试

```bash
dotnet test LanMountainDesktop.slnx -c Debug
```

### 常见工作区域

- 宿主应用：`LanMountainDesktop/`
- Plugin SDK：`LanMountainDesktop.PluginSdk/`
- 共享契约：`LanMountainDesktop.Shared.Contracts/`
- 测试：`LanMountainDesktop.Tests/`
- 插件打包脚本：`scripts/Pack-PluginPackages.ps1`

### 调试建议

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

## English

Use `LanMountainDesktop.slnx` as the workspace entry point. The standard loop is `dotnet restore`, `dotnet build LanMountainDesktop.slnx -c Debug`, `dotnet run --project LanMountainDesktop/LanMountainDesktop.csproj`, and `dotnet test LanMountainDesktop.slnx -c Debug`.

For packaging, see `LanMountainDesktop/PACKAGING.md`. For plugin package generation or local feed workflows, use `scripts/Pack-PluginPackages.ps1`.
