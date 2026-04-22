# 版本同步说明

## 目标

发布版的用户可见版本必须统一指向“应用版本”，不能再出现：

- Launcher UI 显示 `1.0.0`
- 应用设置页显示 `0.8.x`
- `version.json`、安装包、Release 资产名称各写各的

## 默认仓库状态

仓库内的静态版本现在故意保留为开发占位值：

- `Directory.Build.props`
- `LanMountainDesktop/LanMountainDesktop.csproj`
- `LanMountainDesktop.Launcher/LanMountainDesktop.Launcher.csproj`
- `LanMountainDesktop.Shared.Contracts/LanMountainDesktop.Shared.Contracts.csproj`
- `LanMountainDesktop/app.manifest`
- `LanMountainDesktop.Launcher/app.manifest`

这些值只是提醒“当前不是正式注入构建”，不能代表发布版本。

## Release 工作流怎么做

Release 工作流会先从 tag 提取版本：

- `v0.8.5.2` -> `0.8.5.2`
- 程序集四段版本 -> `0.8.5.2`

随后显式执行：

- `scripts/Set-ReleaseVersion.ps1`

这个步骤会同步更新：

- 主程序 `.csproj` 的 `Version`
- Launcher `.csproj` 的 `Version`
- Shared.Contracts `.csproj` 的 `Version`
- `Directory.Build.props`
- 主程序 `app.manifest`
- Launcher `app.manifest`

之后构建和发布阶段继续通过 MSBuild 属性注入：

- `Version`
- `AssemblyVersion`
- `FileVersion`
- `InformationalVersion`

因此最终会统一落到：

- Launcher UI 读取到的应用版本
- 应用设置页显示的版本
- `version.json`
- 程序集文件版本
- Windows manifest
- 安装包版本
- GitHub Release 资产名称

## 维护规则

- 日常开发不要手动把仓库默认版本改成正式版本号。
- 正式发版只需要打 tag，版本同步交给工作流。
- 如果新增新的版本承载点，必须同时补到 `Set-ReleaseVersion.ps1` 和 Release 工作流里。
