# 插件开发文档

LanMountainDesktop 插件基于 `LanMountainDesktop.PluginSdk` 开发。

`LanAirApp/` 负责对外发布插件开发标准、示例插件和打包工具；宿主应用内部的插件加载与解析逻辑位于 `LanMountainDesktop/plugins/`。
`LanMountainDesktop.PluginSdk` 只提供插件作者需要依赖的开发契约，不再承载宿主侧运行时加载实现。

## 必需文件
- `plugin.json`
- `plugin.json` 中声明的入口程序集
- 使用插件入口特性标记的入口类型

## 推荐开发流程
1. 以 `LanAirApp/samples/LanMountainDesktop.SamplePlugin` 为起点。
2. 修改 `plugin.json`，填写你自己的插件 `id`、名称、作者、版本和入口程序集。
3. 实现 `IPlugin` 或继承 `PluginBase`。
4. 通过 `IPluginContext` 注册服务、设置页和桌面组件。
5. 将输出内容打包为 `.laapp` 文件。

## 运行时能力
- 插件可以注册自己的设置页。
- 插件可以注册自己的桌面组件。
- 插件可以注册自己的服务，并通过插件消息总线进行通信。
- 宿主优先加载 `.laapp` 包，其次才是散装清单。

## 多语言建议
- 插件应当内置 `Localization/zh-CN.json` 与 `Localization/en-US.json`。
- 插件界面文案、组件文案、状态文案建议统一通过插件本地化层读取。
- 建议优先读取宿主传入的语言代码，再回退到插件默认语言。

## 目录建议
一个标准插件项目建议至少包含：
- `plugin.json`
- `Localization/zh-CN.json`
- `Localization/en-US.json`
- 插件程序集与依赖文件

## 示例项目与工具
- 示例插件：`LanAirApp/samples/LanMountainDesktop.SamplePlugin`
- 打包工具：`LanAirApp/tools/LanMountainDesktop.PluginPackager`
- 标准模板：`LanAirApp/standards/plugin.template.json`
