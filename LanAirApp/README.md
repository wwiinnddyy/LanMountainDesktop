# LanAirApp

`LanAirApp` 是阑山桌面插件生态的对外发布工作区。

这里集中放置：
- 插件开发标准
- 插件打包与构建工具
- 插件开发与打包文档
- 示例插件

目录结构：
- `docs/`：插件开发文档、打包文档
- `releases/`：已经打包完成、可直接分享与安装的 `.laapp` 插件包
- `samples/`：示例插件，其中 `LanMountainDesktop.SamplePlugin` 是示例开发插件
- `standards/`：插件标准文件与模板
- `tools/`：插件打包与构建工具

面向用户的安装流程：
1. 将插件构建或打包为 `.laapp` 文件。
2. 打开 `设置 -> 插件`。
3. 点击 `打开 .laapp 插件包`。
4. 选择插件包完成安装。

宿主侧的插件加载、安装、发现、解析与设置页接入逻辑，保留在 `LanMountainDesktop/plugins/`。

`LanMountainDesktop.PluginSdk` 仅作为插件开发 SDK 使用，提供 `IPlugin`、`IPluginContext`、清单模型与扩展注册接口。
