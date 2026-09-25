# LanMountainDesktop.AirAppTemplate

阑山桌面轻应用（AirApp）官方 `dotnet new` 模板包。

AirApp 是阑山桌面**唯一**的扩展形态，桌面组件与窗口轻应用都由
`LanMountainDesktop.AirAppSdk` 一个 SDK 提供。旧的 `LanMountainDesktop.PluginSdk`
与 `plugin.json` 已停止支持，宿主不再识别。

## 基线

| 项 | 值 |
|---|---|
| 目标框架 | `net10.0` |
| SDK | `LanMountainDesktop.AirAppSdk` `1.0.0` |
| 清单 | `airapp.json` |
| 包格式 | `.laapp` |
| API 版本 | `1.0.0`（宿主按主版本号匹配） |

## 配置包源

SDK 发布在 GitHub Packages，**读取也需要认证**。在你的项目里放一份
`NuGet.config`，凭据用环境变量占位符引用（这份文件就可以提交，源地址与凭据槽只说一次）：

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="lanmountain" value="https://nuget.pkg.github.com/wwiinnddyy/index.json" />
  </packageSources>
  <packageSourceCredentials>
    <lanmountain>
      <add key="Username" value="%LANMOUNTAIN_PACKAGES_USER%" />
      <add key="ClearTextPassword" value="%LANMOUNTAIN_PACKAGES_TOKEN%" />
    </lanmountain>
  </packageSourceCredentials>
</configuration>
```

本地把 `LANMOUNTAIN_PACKAGES_USER` / `LANMOUNTAIN_PACKAGES_TOKEN` 设进环境
（PAT 需 `read:packages`）；CI 里在 workflow 的 `env:` 中把仓库 secret 注入这两个变量。

**别再写 `dotnet nuget add source --name lanmountain`**：上面这份 `NuGet.config` 已经登记了同名源，
再 add 会直接失败（实测 `The name specified has already been added to the list of available package sources.`，
而且是在 Restore 之前就红）；换成 `update source` 又在 Linux runner 上报
`Password encryption is not supported on .NET Core for this platform.`。
变量没设时 `%VAR%` 替换成空串，还原报 `Value cannot be null or empty string. (Parameter 'password')`，
所以建议在 Restore 前加一步判空、把这句话翻译成"仓库没配 LANMOUNTAIN_PACKAGES_TOKEN"。
完整片段见宿主文档《AirApp 开发 → 快速开始 → 环境准备》。

## 安装与创建

```powershell
dotnet new install LanMountainDesktop.AirAppTemplate
dotnet new lmd-airapp -n YourAirAppName
```

生成的工程引用 `LanMountainDesktop.AirAppSdk`，构建时由 SDK 的 MSBuild targets
自动产出 `.laapp` 包，无需自己写打包脚本。

## 包内容约定

`.laapp` 必须包含：

- `airapp.json`
- `entranceAssembly` 指向的入口程序集
- 入口程序集旁边的 `.deps.json`（宿主强制校验，缺失会拒绝加载）

可选内容：

- `Localization/*.json`
- 资源文件与其他托管依赖

## 发布到轻应用市场

仓库根目录放 `airappmarket-entry.template.json`，由市场仓库
[LanAirApp](https://github.com/wwiinnddyy/LanAirApp) 的索引收录。
条目中的 `apiVersion` 必须与本 SDK 的 API 版本主版本号一致，否则宿主会拒绝安装。
