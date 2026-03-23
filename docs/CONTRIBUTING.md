# 协作文档 / Contributing

## 中文

### 适用范围

本文件适用于本仓库内的代码、文档、规格与测试协作。

### 基本流程

1. 先阅读 `README.md`、`docs/ARCHITECTURE.md` 和 `docs/DEVELOPMENT.md`
2. 如果是新功能、行为变更或跨模块调整，先检查是否需要补 `.trae/specs/`
3. 实现代码改动时，尽量同时补测试和必要文档
4. 提交 PR 前，至少确认构建、测试和相关文档链接可用

### 什么时候必须更新 spec

以下改动默认要补或更新 `.trae/specs/<feature>/`：

- 新增用户可见功能
- 修改已有功能行为、交互或规则
- 调整设置页信息架构或主要视觉结构
- 修改插件宿主集成方式、共享契约或 SDK 使用模式

如果只是小范围重构、纯修复拼写、或不改变行为的内部清理，可以不新增 spec，但仍要补必要测试。

### 什么时候必须更新文档

- 产品定位、版本阶段、生态边界变化：更新 `docs/PRODUCT.md`
- 仓库结构、模块职责、运行时边界变化：更新 `docs/ARCHITECTURE.md`
- 构建、运行、测试、打包步骤变化：更新 `docs/DEVELOPMENT.md`
- AI 协作入口、代码地图、执行约束变化：更新 `AGENTS.md` 或 `docs/ai/`
- 视觉或圆角规则变化：更新对应专题文档

### PR 预期

PR 说明至少要覆盖：

- 改了什么
- 为什么要改
- 如何验证
- 是否影响文档、spec 或迁移说明

如果改动涉及 UI、插件、设置页、打包或共享契约，建议明确列出受影响区域。

### 测试预期

默认至少执行与改动相关的验证：

- `dotnet build LanMountainDesktop.slnx -c Debug`
- `dotnet test LanMountainDesktop.slnx -c Debug`

无法运行的检查要在 PR 里说明原因。

### 文档原则

- 每类事实只保留一个权威来源
- 根目录 `README.md` 面向人类入口，`AGENTS.md` 面向 AI 入口
- 不要在多个文件里复制同一段说明，只保留索引和跳转

## English

Keep the documentation model simple: `README.md` is the human entry point, `AGENTS.md` is the AI entry point, `docs/` stores durable project docs, and `.trae/specs/` stores feature-level specs. If a change affects behavior, boundaries, or workflows, update the corresponding source-of-truth document in the same PR.
