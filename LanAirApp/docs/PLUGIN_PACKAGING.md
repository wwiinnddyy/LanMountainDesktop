# 插件打包文档

LanMountainDesktop 插件的安装包格式固定为 `.laapp`。

`LanAirApp/` 负责提供打包标准与打包工具；`.laapp` 的安装、发现和运行时加载由 `LanMountainDesktop/plugins/` 负责。

## `.laapp` 格式说明
- 本质上是一个标准 zip 压缩包
- 包根目录必须包含 `plugin.json`
- 包根目录还必须包含入口程序集及其依赖

## 建议打包内容
- `plugin.json`
- `YourPlugin.dll`
- 依赖程序集
- `Localization/zh-CN.json`
- `Localization/en-US.json`
- 插件运行所需的其他资源文件

## 使用打包工具
```powershell
dotnet run --project .\LanAirApp\tools\LanMountainDesktop.PluginPackager -- --input .\path\to\plugin-output --output .\artifacts\YourPlugin.laapp --overwrite
```

## 应用内安装流程
1. 打开 `设置 -> 插件`
2. 点击 `打开 .laapp 插件包`
3. 选择要安装的插件包
4. 如果插件注册了设置页或组件，安装后重启应用

## 注意事项
- `plugin.json` 中的 `entranceAssembly` 必须能在包内找到。
- 包内应尽量避免无关开发产物。
- `.laapp` 是标准安装格式，建议不要对外分发散装目录。
