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
`NuGet.config`，并把 `USERNAME` 换成你的 GitHub 用户名、
`TOKEN` 换成一个带 `read:packages` 权限的 PAT：

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
      <add key="Username" value="USERNAME" />
      <add key="ClearTextPassword" value="TOKEN" />
    </lanmountain>
  </packageSourceCredentials>
</configuration>
```

不要把带 token 的 `NuGet.config` 提交进仓库。CI 里用
`dotnet nuget add source ... --username x --password ${{ secrets.YOUR_PAT }}` 注入。

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
