<!--
感谢贡献 LanMountainDesktop。
Thank you for contributing to LanMountainDesktop.

请不要在 PR、截图、日志或测试数据中提交 token、密钥、Cookie、真实账号、学生/班级个人信息或其他敏感内容。
Do not include tokens, secrets, cookies, real accounts, student/class personal data, or other sensitive information in this PR, screenshots, logs, or test data.
-->

## 这个 PR 做了什么？ / What does this PR do?

<!--
用 2-5 句话说明改动内容和原因。请说明用户、开发者或维护者能得到什么。
Describe the change and the reason in 2-5 sentences. Mention what users, developers, or maintainers get from it.
-->

## 相关 Issue / Related issues

<!--
如果可以关闭 Issue，请使用：
Fixes #123

If this closes an issue, use:
Fixes #123
-->

## 影响范围 / Affected areas

<!-- 勾选所有适用项。Check all that apply. -->

- [ ] 桌面宿主 / Desktop host
- [ ] 启动器、更新或安装 / Launcher, update, or installation
- [ ] AirApp Runtime
- [ ] 插件运行时或安装 / Plugin runtime or installation
- [ ] Plugin SDK 或共享契约 / Plugin SDK or shared contracts
- [ ] 设置、主题或外观 / Settings, theme, or appearance
- [ ] 桌面组件系统 / Desktop component system
- [ ] 构建、测试、CI 或打包 / Build, test, CI, or packaging
- [ ] 文档或规格 / Documentation or specs

## 行为、兼容性与迁移 / Behavior, compatibility, and migration

<!--
说明是否改变用户可见行为、设置持久化、文件格式、公共 API、Plugin SDK、共享契约、打包产物或跨平台行为。
如果没有，请写“无 / None”。

Describe whether this changes user-visible behavior, persisted settings, file formats, public APIs, Plugin SDK, shared contracts, packaged artifacts, or cross-platform behavior.
If not, write "无 / None".
-->

## 验证 / Verification

<!-- 勾选已完成项，并在下面补充实际命令、平台和结果。Check completed items and add commands, platforms, and results below. -->

- [ ] `dotnet restore`
- [ ] `dotnet build LanMountainDesktop.slnx -c Debug`
- [ ] `dotnet test LanMountainDesktop.slnx -c Debug`
- [ ] 手动运行桌面宿主 / Manually ran the desktop host
- [ ] 验证插件安装、加载或 SDK 场景 / Verified plugin install, loading, or SDK scenarios
- [ ] 验证 Windows / Verified on Windows
- [ ] 验证 Linux / Verified on Linux
- [ ] 验证 macOS / Verified on macOS
- [ ] 未能运行的检查已说明原因 / Explained any checks that could not be run

实际验证说明 / Verification details:

```text

```

## 文档与 spec / Documentation and specs

<!-- 勾选所有适用项。Check all that apply. -->

- [ ] 本 PR 不需要更新文档或 `.trae/specs/` / No documentation or `.trae/specs/` update is needed
- [ ] 已更新权威文档 / Updated source-of-truth documentation
- [ ] 已新增或更新 `.trae/specs/<feature>/` / Added or updated `.trae/specs/<feature>/`
- [ ] 已更新 SDK 迁移说明或共享契约说明 / Updated SDK migration or shared contract notes

## UI 截图或录屏 / UI screenshots or videos

<!--
涉及 UI、主题、设置页、窗口生命周期或组件外观时，请附截图或录屏。
Attach screenshots or videos when changing UI, theme, settings pages, window lifecycle, or component appearance.
-->

## 最终检查 / Final checklist

- [ ] 我已自查代码和文档，移除了调试残留和无关改动。 / I self-reviewed the code and docs and removed debug leftovers and unrelated changes.
- [ ] 我没有提交未脱敏的日志、凭据或个人信息。 / I did not commit unredacted logs, credentials, or personal information.
- [ ] 如果改动涉及 UI，已遵守 `docs/VISUAL_SPEC.md` 和 `docs/CORNER_RADIUS_SPEC.md`。 / If this changes UI, it follows `docs/VISUAL_SPEC.md` and `docs/CORNER_RADIUS_SPEC.md`.
- [ ] 如果改动涉及行为、流程、边界或命令，已同步对应文档。 / If this changes behavior, workflows, boundaries, or commands, the related docs are updated.
- [ ] 如果改动涉及新功能或行为调整，已补齐或更新 `.trae/specs/`，或说明无需更新的原因。 / If this adds a feature or behavior change, `.trae/specs/` is updated, or the reason for not updating is explained.
