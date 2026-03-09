# 宿主侧插件运行时

这个目录用于归档阑山桌面宿主侧的插件相关实现。

职责范围：
- 已安装插件的发现
- `.laapp` 安装包安装与替换
- 插件运行时加载
- 插件贡献的设置页与桌面组件接入
- 宿主侧插件设置页的安装、显示与刷新

当前宿主侧核心文件：
- `PluginLoader.cs`
- `PluginLoadContext.cs`
- `PluginLoaderOptions.cs`
- `PluginLoadResult.cs`
- `LoadedPlugin.cs`
- `PluginRuntimeService.cs`
- `PluginContributions.cs`
- `PluginCatalogEntry.cs`
- `PluginSettingsPage.axaml`
- `PluginSettingsPage.Host.cs`
- `MainWindow.PluginSettingsHost.cs`
- `SettingsWindow.PluginSettingsHost.cs`
- `MainWindow.PluginSettingsLocalization.cs`
- `SettingsWindow.PluginSettingsLocalization.cs`
- `MainWindow.PluginSettingsControls.cs`
- `SettingsWindow.PluginSettingsControls.cs`

说明：
- 插件开发标准、插件打包工具、示例插件与开发文档统一放在仓库根目录下的 `LanAirApp/`
- 宿主本体的插件加载、解析、安装与插件设置页接入逻辑统一放在 `LanMountainDesktop/plugins/`
- `LanMountainDesktop.PluginSdk` 只保留插件作者需要引用的契约、清单模型和扩展注册接口
