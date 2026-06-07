# Documentation Sources

## 目标

当多个文档都提到同一主题时，AI 必须知道“到底信哪一份”。本文件定义权威来源，避免引用旧文档或重复维护的文本。

## 权威来源表

| 主题 | 权威文档 | 备注 |
| --- | --- | --- |
| 项目总入口 | `README.md` | 面向人类，提供索引而不展开细节 |
| AI 协作入口 | `AGENTS.md` | 面向 AI 的首读文件 |
| 产品定位与阶段 | `docs/PRODUCT.md` | 不再使用旧根目录产品文档 |
| 架构与模块职责 | `docs/ARCHITECTURE.md` | 包含仓库结构和运行时主线 |
| 构建、运行、测试、打包 | `docs/DEVELOPMENT.md` | 命令以这里为准 |
| 贡献和文档更新规则 | `docs/CONTRIBUTING.md` | PR、spec、文档协作规则 |
| feature 级规格 | `.trae/specs/<feature>/spec.md` | 行为意图和需求场景 |
| feature 任务拆解 | `.trae/specs/<feature>/tasks.md` | 实施步骤与依赖 |
| feature 验收 | `.trae/specs/<feature>/checklist.md` | 回归与验收项 |
| 视觉规范 | `docs/VISUAL_SPEC.md` | 颜色、语义资源、玻璃层级 |
| 圆角规范 | `docs/CORNER_RADIUS_SPEC.md` | 圆角层级与动态规则 |
| 插件生态边界 | `docs/ECOSYSTEM_BOUNDARIES.md` | 仓库边界和 market 所属 |
| SDK v5 迁移 | `docs/PLUGIN_SDK_V5_MIGRATION.md` | Plugin SDK breaking changes |

## 已废弃来源

以下文件内容已迁移，不应继续作为权威来源引用：

- `PRODUCT_BRIEF.md`
- `PRODUCT_DOCUMENT.md`
- `run.md`

## 冲突处理规则

如果发现多个文档内容冲突，按以下优先级处理：

1. 先看本表中的权威来源
2. 再看相关项目内源码、`csproj`、目录 README
3. 如果仍有冲突，以当前仓库源码和项目配置为准，并回写文档
