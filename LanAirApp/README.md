# LanAirApp (Mirror)

## 中文

这里的 `LanAirApp/` 是放在宿主仓库里的镜像副本，只用于本地联调和工作区构建，不是插件市场或插件开发资料的最终权威来源。

### 这份镜像的角色

- 提供本地工作区里的 `airappmarket` 索引副本
- 提供插件文档、工具和样例镜像，便于和宿主一起联调
- 不承担宿主运行时职责

### 权威来源

- 插件市场与开发文档：独立 `LanAirApp` 仓库
- 权威示例插件：独立 `LanMountainDesktop.SamplePlugin`
- 本目录中的 `samples/LanMountainDesktop.SamplePlugin` 只是镜像模板副本

## English

This `LanAirApp/` directory is a mirror that lives inside the host repository. It exists for local workspace integration and build convenience only. It is not the final authority for the plugin market or developer-facing plugin materials.

### Role of this mirror

- keep a local copy of the `airappmarket` index for workspace integration
- keep mirrored docs, tools, and sample templates for local development
- avoid duplicating host runtime responsibilities

### Sources of truth

- Plugin market and developer docs: standalone `LanAirApp`
- Authoritative sample plugin: standalone `LanMountainDesktop.SamplePlugin`
- `samples/LanMountainDesktop.SamplePlugin` in this mirror is template/mirror content only
