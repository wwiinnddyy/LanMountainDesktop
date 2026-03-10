# AirAppMarket

`airappmarket/` 是阑山桌面的官方插件市场源目录。

当前阶段职责：
- 提供官方插件市场索引 `index.json`
- 提供索引 schema
- 提供静态图标资产
- 提供本地与 CI 使用的索引校验工具

Bootstrap 方式：
1. 用户先通过阑山桌面内置的 `设置 -> 插件 -> 打开 .laapp 插件包` 手动安装 `LanMountainDesktop.PluginMarketplace`。
2. 市场插件启动后，会从这里的官方索引拉取插件列表。
3. 后续插件安装与更新都通过市场插件完成。

官方索引地址：

`https://raw.githubusercontent.com/wwiinnddyy/LanMountainDesktop/main/airappmarket/index.json`

约束：
- 这里只维护官方市场源，不做多源聚合。
- 第一阶段不提供独立 GitHub Pages 页面。
- 索引中的下载链接默认指向本仓库已提交的 `.laapp` 发布包。
