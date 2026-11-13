# CI/CD 配置

本文列出 `.github/workflows/` 下每个工作流的触发条件与作用。**以文件为准**——流程改动后请同步本页。

最后核对：2026-11-14。

---

## 总览

| 工作流 | 触发 | 作用 |
|---|---|---|
| `build.yml` | push（非 tag）/ PR / 手动 | 多平台构建 + 轻应用 SDK 打包 |
| `code-quality.yml` | PR / push（main、master、dev、develop）/ 手动 | 构建 + 测试 + 格式与未使用成员闸门 |
| `release.yml` | tag `v*` / 手动 | 发布正式版本，并触发增量生成 |
| `installer-build.yml` | push / PR（限 `install/**`、`core/**` 等路径）/ 手动 | 打在线安装器与二次安装器 |
| `plonds-comparator.yml` | `release: published` / `prereleased` / 手动 | 生成增量包与 `PLONDS.json` |
| `plonds-uploader.yml` | 手动（需 `tag` 输入） | 把清单与包推到 GitHub 通道与 S3 |
| `airapp-sdk-publish.yml` | 由发布相关流程驱动 | 发布 `LanMountainDesktop.AirAppSdk` / `Core` 到 NuGet |
| `issue-management.yml` | issue 事件 | 议题管理 |

---

## build.yml

- **触发**：push（非 tag）/ PR / `workflow_dispatch`
- **内容**：Windows 的 Debug + Release 构建；Linux / macOS / Android 构建；`Pack-AirAppPackages.ps1` 打轻应用相关 nupkg
- **注意**：这是"能不能编过"的闸门，不含测试

---

## code-quality.yml

- **触发**：PR / push 到 main、master、dev、develop / `workflow_dispatch`
- **构建**：`-m:1`（禁用并行，避免偶发争用）
- **测试**：`--filter 'Category!=EcosystemProbe'`
  - 用例数下限 `MIN_CASES=1000`（低于下限即红，防止 filter 打错导致空跑）
  - 最多 3 趟重试，只放行已登记的偶发失败签名（信封 4）
- **格式闸门**：`dotnet format`，其中 IDE0051 / IDE0052（未使用成员）是硬门禁

**两个必须知道的边界**：

1. **`EcosystemProbe` 被有意排除**。这一类跑的是同级仓库里的外部轻应用产物，CI 上没有这些产物。所以"CI 绿"**不能**作为"外部轻应用可用"的证据——那条证据只在本地成立（`dotnet test --filter "Category=EcosystemProbe"`）。
2. **重试额度意味着"绿"不等于"用例全过"**。它等于"没有超过签名阈值的红灯"。

---

## release.yml

- **触发**：tag `v*` / `workflow_dispatch`
- **job 划分**：
  - `prepare`：从 tag 解析 `version` 与 4 段 `assembly_version`，判定频道
  - 三个 OS 的 build job
  - `github-release`：用 `ncipollo/release-action` 建 Release，并**显式 dispatch `plonds-comparator.yml`**（`file-compare` / `sha256`）
- **产物**：见 [打包与构建](03-打包与构建.md)
- **说明**：Release 说明里明确写了增量资产由后续工作流生成——所以发布完 Release 还有一步才算完整

---

## installer-build.yml

- **触发**：push / PR（路径过滤：`install/**`、`core/**` 等）/ `workflow_dispatch`
- **内容**：发布 NativeAOT 单文件在线安装器，并**断言输出只有 exe、没有 JIT 文件**；随后发布 `LanDesktop.MewInstaller`
- **看点**：那条"只允许一个 exe"的断言是防 AOT 配置退化的，不要为了加文件而放开它

---

## plonds-comparator.yml

- **触发**：`release: published` / `prereleased` / `workflow_dispatch`
- **内容**：对 x64、x86 各跑一次差量构建（`build-delta` 或 `build-delta-from-commits`），产物 `changed-windows-{arch}.zip` 与 `PLONDS-windows-{arch}.json` 上传到 Release；末段 `notify-publisher` 显式 `gh workflow run plonds-uploader.yml`
- **注意**：这一步是增量的**唯一正规生成路径**

---

## plonds-uploader.yml（PLONDS Publisher）

- **触发**：仅 `workflow_dispatch`（需要 `tag` 输入）
- **内容**：
  1. `enrich-manifest --write-legacy-copy` → 发 GitHub 通道
  2. `publish-s3`（限速 / 分片 / 重试）→ 发 S3
- **注意**：S3 那步是 `continue-on-error: true`——**S3 失败不会让这次运行变红**，需要单独确认

---

## airapp-sdk-publish.yml

- 负责把 `LanMountainDesktop.AirAppSdk` 与 `LanMountainDesktop.Core` 发到 NuGet
- 里面有一条 `--filter AirAppSdkVersionConsistencyTests` 的版本一致性检查
- 发版前必须递增 `AirAppSdkInfo.SdkVersion` / `ApiVersion`（见 [AirApp 运行时](../04-架构与实现/04-AirApp运行时.md)）

---

## 已知薄弱点

| 项 | 说明 |
|---|---|
| CI 不跑生态探针 | `EcosystemProbe` 被排除，外部轻应用加载证据只在本地成立 |
| 重试额度掩盖红灯 | 失败签名白名单 + 最多 3 趟，使"绿"的语义弱于"全过" |
| S3 发布容错过宽 | `continue-on-error: true` 会吞掉上传失败 |
| 无签名步骤 | 见 [打包与构建](03-打包与构建.md) 第 6 节 |
| 本地脚本与 CI 脱节 | `Sign-FileMap.ps1` / `Generate-PlondsArtifacts.ps1` 调用的子命令不存在，且无 workflow 引用 |

---

## 相关文档

- [发布流程](05-发布流程.md) · [打包与构建](03-打包与构建.md) · [增量更新实现](02-增量更新实现.md)
