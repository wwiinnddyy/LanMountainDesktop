# PLONDS Comparator 改造设计

> 日期：2026-05-30
> 状态：待审批

## 1. 背景与动机

PLONDS（Penguin Logistics Online Network Distribution System）是 LanMountainDesktop 的文件驱动式分布式更新系统。当前 Comparator 工作流存在以下问题：

1. **产出物过于复杂**：生成 `update-{platform}.zip`、`plonds-filemap-{platform}.json`、`plonds-filemap-{platform}.json.sig`、`platform-summary-{platform}.json`、`plonds-static.zip` 等多个文件，客户端消费困难
2. **模型定义重复**：`Plonds.Shared`、`Plonds.Core`、宿主侧、Launcher 侧各自定义独立的 DTO，字段名不一致
3. **签名机制过重**：RSA 签名增加了 CI 复杂度（需要管理密钥），且对文件驱动式更新系统而言 SHA256 哈希校验已足够
4. **平台覆盖不当**：Linux 平台不需要 PLONDS 支持，macOS 尚未接入，但代码中硬编码了三个平台
5. **工作流间 artifact 传递脆弱**：Comparator → Publisher 通过 artifact 传递 `plonds-static.zip`，容易断裂

## 2. 设计目标

- 产出物精简为两个文件：`changed.zip` + `PLONDS.json`
- 去掉 RSA 签名，只用 SHA256/MD5 校验
- 只关注 Windows 平台
- 统一模型定义，消除 DTO 重复
- 保持 Comparator 和 Publisher 两个工作流的职责分离

## 3. 新产出物定义

### 3.1 changed.zip

只包含与上一版本有差异的文件（action 为 `add` 或 `replace` 的文件），目录结构与部署目录一致。

### 3.2 PLONDS.json

```json
{
  "formatVersion": "2.0",
  "currentVersion": "1.2.0",
  "previousVersion": "1.1.0",
  "isFullUpdate": false,
  "requiresCleanInstall": false,
  "channel": "stable",
  "platform": "windows-x64",
  "updatedAt": "2026-05-30T12:00:00Z",

  "filesMap": {
    "LanMountainDesktop.exe": {
      "action": "replace",
      "sha256": "abc123...",
      "size": 1024000
    },
    "LanMountainDesktop.dll": {
      "action": "reuse",
      "sha256": "def456...",
      "size": 512000
    },
    "OldModule.dll": {
      "action": "delete",
      "sha256": "",
      "size": 0
    }
  },

  "changedFilesMap": {
    "LanMountainDesktop.exe": {
      "archivePath": "LanMountainDesktop.exe",
      "sha256": "abc123...",
      "size": 1024000
    }
  },

  "checksums": {
    "changed.zip": "md5:9a8b7c6d..."
  }
}
```

### 3.3 字段语义

| 字段 | 类型 | 说明 |
|------|------|------|
| `formatVersion` | string | 协议版本，固定 `"2.0"` |
| `currentVersion` | string | 当前发布版本 |
| `previousVersion` | string | 基线版本（全量更新时为 `"0.0.0"`） |
| `isFullUpdate` | bool | 是否为全量更新（找不到基线版本时为 true） |
| `requiresCleanInstall` | bool | 启动器是否也更新了——如果是，客户端不走增量流程，让用户重新运行安装器 |
| `channel` | string | 更新通道：`stable` 或 `preview` |
| `platform` | string | 平台标识：`windows-x64` |
| `updatedAt` | string | ISO 8601 时间戳 |
| `filesMap` | object | 全量文件图：每个文件的 action + sha256 + size |
| `changedFilesMap` | object | 变更文件图：只包含需要从 changed.zip 解压的文件 |
| `checksums` | object | 产出物的 MD5 值 |

### 3.4 filesMap 中 action 的值

| Action | 含义 | changed.zip 中是否包含 |
|--------|------|----------------------|
| `add` | 新增文件 | ✅ |
| `replace` | 替换文件 | ✅ |
| `reuse` | 复用上一版本文件 | ❌ |
| `delete` | 删除文件 | ❌ |

### 3.5 requiresCleanInstall 判断逻辑

比较 `LanMountainDesktop.Launcher.exe` 在当前版本和基线版本中的 SHA256：
- 如果 SHA256 不同 → `requiresCleanInstall = true`
- 如果 SHA256 相同或没有基线版本 → `requiresCleanInstall = false`

## 4. Plonds.Tool build-delta 命令改造

### 4.1 新命令签名

```
build-delta --platform <platform>
            --current-version <version>
            --current-zip <file>
            --output-dir <dir>
            --channel <channel>
            [--baseline-version <version>]
            [--baseline-zip <file>]
            [--launcher-path <relative-path>]
```

### 4.2 参数说明

| 参数 | 必需 | 说明 |
|------|------|------|
| `--platform` | 是 | 平台标识，如 `windows-x64` |
| `--current-version` | 是 | 当前发布版本号 |
| `--current-zip` | 是 | 当前版本的 payload zip 路径 |
| `--output-dir` | 是 | 输出目录 |
| `--channel` | 是 | 更新通道 |
| `--baseline-version` | 否 | 基线版本号（省略则视为全量更新） |
| `--baseline-zip` | 否 | 基线版本的 payload zip 路径（省略则视为全量更新） |
| `--launcher-path` | 否 | Launcher 可执行文件的相对路径，默认 `LanMountainDesktop.Launcher.exe` |

### 4.3 删除的参数

| 参数 | 原因 |
|------|------|
| `--current-tag` | 不再需要，version 就够了 |
| `--private-key` | 去掉签名 |
| `--is-full-payload` | 自动判断：没有 baseline-zip 就是全量 |
| `--static-output-dir` | 不再生成 S3 静态布局 |
| `--update-base-url` | 不再生成 S3 URL |
| `--baseline-tag` | 不再需要 |

### 4.4 内部逻辑

```
1. 解压 current-zip → currentDir
2. 如果有 baseline-zip → 解压 → baselineDir
   否则 → baselineDir = 空（全量更新）

3. 扫描 currentDir → 计算 SHA256
4. 扫描 baselineDir → 计算 SHA256（如果有）

5. 对比生成 filesMap:
   - 两个版本都有且 SHA256 相同 → reuse
   - 两个版本都有但 SHA256 不同 → replace
   - 只在新版本中存在 → add
   - 只在旧版本中存在 → delete

6. 从 filesMap 提取 changedFilesMap:
   - 只包含 action=add/replace 的条目
   - 添加 archivePath（在 changed.zip 中的路径）

7. 打包 changed.zip:
   - 只包含 add/replace 的文件
   - 保持原始目录结构

8. 判断 requiresCleanInstall:
   - 比较 Launcher 可执行文件在两个版本中的 SHA256
   - 如果不同 → requiresCleanInstall=true

9. 计算 changed.zip 的 MD5

10. 生成 PLONDS.json

11. 输出到 output-dir:
    - changed.zip
    - PLONDS.json
```

### 4.5 不再生成的产物

| 旧产物 | 处置 |
|--------|------|
| `update-{platform}.zip` | 被 `changed.zip` 替代 |
| `plonds-filemap-{platform}.json` | 被 `PLONDS.json` 替代 |
| `plonds-filemap-{platform}.json.sig` | 去掉签名 |
| `platform-summary-{platform}.json` | 不再需要 |
| `plonds-static.zip` | 不再生成 S3 静态布局 |
| `meta/channels/...` | 不再由 Tool 生成，由 Publisher 负责 |

## 5. Plonds.Shared 模型改造

### 5.1 删除的模型

| 模型 | 原因 |
|------|------|
| `PlondsFileMap` | 被新的 `PlondsManifest` 替代 |
| `PlondsFileEntry` | 被新的 `PlondsFileEntry` 替代 |
| `PlondsComponent` | 不再有组件概念 |
| `PlondsDistributionInfo` | 不再生成分发文档 |
| `PlondsChannelPointer` | 由 Publisher 用脚本生成 |
| `PlondsReleaseManifest` | 不再需要 |
| `PlondsReleasePlatformEntry` | 不再需要 |
| `PlondsSignatureDescriptor` | 去掉签名 |
| `PlondsMirrorAsset` | 由 Publisher 处理 |
| `PlondsMirrorEntry` | 由 Publisher 处理 |
| `PlondsMetadataCatalog` | 不再需要 |
| `PlondsAssetEntry` | 不再需要 |

### 5.2 新模型定义

```csharp
// PlondsManifest — 对应 PLONDS.json
public sealed record PlondsManifest(
    string FormatVersion,
    string CurrentVersion,
    string PreviousVersion,
    bool IsFullUpdate,
    bool RequiresCleanInstall,
    string Channel,
    string Platform,
    DateTimeOffset UpdatedAt,
    IReadOnlyDictionary<string, PlondsFileEntry> FilesMap,
    IReadOnlyDictionary<string, PlondsChangedFileEntry> ChangedFilesMap,
    IReadOnlyDictionary<string, string> Checksums);

// PlondsFileEntry — filesMap 中的条目
public sealed record PlondsFileEntry(
    string Action,       // add | replace | reuse | delete
    string Sha256,
    long Size);

// PlondsChangedFileEntry — changedFilesMap 中的条目
public sealed record PlondsChangedFileEntry(
    string ArchivePath,  // 在 changed.zip 中的路径
    string Sha256,
    long Size);
```

### 5.3 设计决策

- `FilesMap` 和 `ChangedFilesMap` 用 `IReadOnlyDictionary<string, T>` 而非 `IReadOnlyList<T>`，key 就是文件相对路径，查找 O(1)
- 去掉 `Component` 概念——当前只有一个 `app` 组件，分层没有实际意义
- `FormatVersion` 固定为 `"2.0"`，与旧格式区分

## 6. Comparator 工作流改造

### 6.1 保留两个工作流

- **Comparator**（`plonds-comparator.yml`）：比较文件生成器，只负责生成 `changed.zip` + `PLONDS.json`
- **Publisher**（`plonds-uploader.yml`）：发布器，负责用仓库内 C# S3 客户端上传 `changed.zip`、`PLONDS.json` 和解压后的 `<version>-changed/` 目录，并把 GitHub/S3 下载信息写回 `PLONDS.json`
- **Rollback**：独立 rollback 工作流已废弃，不再维护

### 6.2 Comparator 改造后步骤

```yaml
# plonds-comparator.yml
触发: release.published / release.prereleased / workflow_dispatch

jobs:
  compare:
    runs-on: ubuntu-latest
    steps:
      - Checkout

      - 解析发布上下文
        → RELEASE_TAG, RELEASE_VERSION, RELEASE_CHANNEL

      - Setup .NET

      - 构建 PLONDS Tool

      - 解析基线版本
        → 查找上一个同频道 Release
        → 如果有 → 记录 baseline_tag, baseline_version
        → 如果没有 → is_full_update=true

      - 下载 payload zips
        → 下载当前版本 files-windows-x64.zip
        → 下载基线版本 files-windows-x64.zip (如果有)

      - 运行 build-delta
        → dotnet run Plonds.Tool -- build-delta \
            --platform windows-x64 \
            --current-version $VERSION \
            --current-zip files-windows-x64.zip \
            --output-dir plonds-output \
            --channel $CHANNEL \
            [--baseline-version $BASELINE_VERSION] \
            [--baseline-zip baseline-files-windows-x64.zip]

      - 上传到 GitHub Release
        → gh release upload changed.zip PLONDS.json

      - 传递元数据给 Publisher
        → 上传 artifact: plonds-run-metadata (tag.txt)
```

### 6.3 Publisher 改造后步骤

```yaml
# plonds-uploader.yml
触发: PLONDS Comparator completed / workflow_dispatch

jobs:
  publish:
    runs-on: ubuntu-latest
    steps:
      - Checkout
      - 解析 release tag
      - Setup .NET
      - 构建 PLONDS Tool
      - 从 GitHub Release 下载 changed.zip + PLONDS.json
      - 调用 dotnet run Plonds.Tool -- publish-s3
        → 使用仓库内 C# S3 客户端上传，不依赖 aws CLI
        → S3 目录布局：
            <prefix>/<version>/PLONDS.json
            <prefix>/<version>/changed.zip
            <prefix>/<version>/<version>-changed/**
        → 回写 PLONDS.json downloads 字段：
            downloads.github.releaseUrl
            downloads.github.manifestUrl
            downloads.github.changedZipUrl
            downloads.s3.manifestUrl
            downloads.s3.changedZipUrl
            downloads.s3.changedFolderUrl
      - 将回写后的 PLONDS.json 重新上传到 GitHub Release
```

### 6.4 与当前步骤的差异

| 当前步骤 | 改造后 |
|---------|--------|
| 准备签名密钥 | ❌ 删除 |
| 解析基线计划 (pwsh，三平台) | ✅ 简化：只找 Windows，逻辑简化 |
| 下载 payload zips (pwsh，三平台) | ✅ 简化：只下载 Windows |
| 构建增量资产 (pwsh，含 build-index + 静态布局验证 + plonds-static.zip 打包) | ✅ 简化：只调用 build-delta |
| 上传 PLONDS assets 到 release | ✅ 简化：只上传 changed.zip + PLONDS.json |
| 传递元数据 | ✅ 保留，但 artifact 内容简化 |
| Publisher 中使用 aws CLI / plonds-static / build-plonds / plonds.json.sig | ❌ 删除，改为 C# `publish-s3` |
| 独立 rollback workflow | ❌ 删除 |

## 7. 双模式差分生成

### 7.1 概述

Comparator 支持两种差分生成方法，通过 `workflow_dispatch` 的 `compare_method` 输入项选择：

| 方法 | 标识 | 核心思路 |
|------|------|---------|
| 方法一 | `file-compare` | 下载两个版本的 files zip，全量文件哈希对比 |
| 方法二 | `commit-analyze` | 分析两个版本之间的 git commit，映射源码变更到产物文件 |

### 7.2 GitHub Actions 触发器新增输入项

```yaml
workflow_dispatch:
  inputs:
    tag: ...
    baseline_tag: ...
    channel: ...
    compare_method:          # 新增
      description: '比较方法'
      type: choice
      default: file-compare
      options:
        - file-compare
        - commit-analyze
    hash_algorithm:          # 新增（仅方法一）
      description: '哈希算法'
      type: choice
      default: sha256
      options:
        - sha256
        - md5
```

当由 `release` 事件触发时，默认使用 `file-compare` + `sha256`。

### 7.3 方法一：文件对比模式（file-compare）

**流程：**

```
1. 下载当前版本 files-windows-x64.zip
2. 下载基线版本 files-windows-x64.zip（如果有）
3. 解压两个 zip 到临时目录
4. 用指定哈希算法（sha256/md5）扫描两个目录的所有文件
5. 对比哈希值，生成 filesMap（add/replace/reuse/delete）
6. 从当前版本目录中提取 add/replace 的文件 → changed.zip
7. 生成 PLONDS.json
```

**PlondsDeltaBuildOptions 新增参数：**

```csharp
string HashAlgorithm = "sha256"  // "sha256" | "md5"
```

**哈希算法对 PLONDS.json 的影响：**

- `sha256`：`filesMap` 和 `changedFilesMap` 中使用 `sha256` 字段
- `md5`：`filesMap` 和 `changedFilesMap` 中使用 `md5` 字段

### 7.4 方法二：Commit 分析模式（commit-analyze）

**流程：**

```
1. 下载当前版本 files-windows-x64.zip
2. 解压到临时目录
3. git log --name-only baseline_tag..current_tag
   → 得到两个版本之间的 commit 列表和涉及的源码文件
4. 过滤：只保留源码目录下的文件
5. 用简单规则映射源码文件到产物文件
6. 从当前版本的解压目录中提取映射到的产物文件 → changed.zip
7. 生成 PLONDS.json
8. 如果没有源码变更 → 自动回退到方法一
```

**源码目录过滤规则：**

只分析以下目录下的文件变更：

| 目录 | 说明 |
|------|------|
| `LanMountainDesktop/` | 主宿主应用 |
| `LanMountainDesktop.Launcher/` | 启动器 |
| `LanMountainDesktop.Shared.Contracts/` | 共享契约 |
| `LanMountainDesktop.PluginSdk/` | 插件 SDK |
| `LanMountainDesktop.Appearance/` | 外观系统 |
| `LanMountainDesktop.Settings.Core/` | 设置核心 |
| `LanMountainDesktop.ComponentSystem/` | 组件系统 |

忽略的目录：`docs/`、`scripts/`、`.github/`、`.trae/`、`PenguinLogisticsOnlineNetworkDistributionSystem/`

**源码到产物的映射规则：**

| 源码路径模式 | 映射到产物文件 |
|-------------|--------------|
| `LanMountainDesktop/**/*.{cs,axaml,xaml}` | `LanMountainDesktop.dll`, `LanMountainDesktop.exe` |
| `LanMountainDesktop.Launcher/**/*.{cs,axaml,xaml}` | `LanMountainDesktop.Launcher.exe` |
| `LanMountainDesktop.Shared.Contracts/**/*.cs` | `LanMountainDesktop.Shared.Contracts.dll` |
| `LanMountainDesktop.PluginSdk/**/*.cs` | `LanMountainDesktop.PluginSdk.dll` |
| `LanMountainDesktop.Appearance/**/*.cs` | `LanMountainDesktop.Appearance.dll` |
| `LanMountainDesktop.Settings.Core/**/*.cs` | `LanMountainDesktop.Settings.Core.dll` |
| `LanMountainDesktop.ComponentSystem/**/*.cs` | `LanMountainDesktop.ComponentSystem.dll` |
| `**/*.json`（配置文件） | 同路径的 .json |
| 其他无法映射的变更 | 保守标记 → 所有核心 .dll/.exe |

**方法二在 Plonds.Tool 中的新命令：**

```
build-delta-from-commits --platform <platform>
                         --current-version <version>
                         --current-zip <file>
                         --output-dir <dir>
                         --channel <channel>
                         --baseline-tag <tag>
                         --current-tag <tag>
                         [--source-dirs <dir1,dir2,...>]
                         [--fallback-zip <file>]
```

| 参数 | 必需 | 说明 |
|------|------|------|
| `--platform` | 是 | 平台标识 |
| `--current-version` | 是 | 当前发布版本号 |
| `--current-zip` | 是 | 当前版本的 payload zip |
| `--output-dir` | 是 | 输出目录 |
| `--channel` | 是 | 更新通道 |
| `--baseline-tag` | 是 | 基线版本的 git tag |
| `--current-tag` | 是 | 当前版本的 git tag |
| `--source-dirs` | 否 | 要分析的源码目录列表（逗号分隔） |
| `--fallback-zip` | 否 | 回退到方法一时使用的基线 zip |

**回退逻辑：**

如果 `git log` 分析后发现没有源码目录下的文件变更（比如只有 docs/ 变更），则自动回退到方法一：
1. 如果提供了 `--fallback-zip` → 用方法一对比两个 zip
2. 如果没有提供 → 生成全量更新（`isFullUpdate=true`）

### 7.5 方法二的 PLONDS.json 特殊处理

方法二无法像方法一那样生成完整的 `filesMap`（因为不知道哪些文件是 reuse 的），因此：

- `filesMap` 只包含映射到的变更文件（标记为 `add` 或 `replace`）
- 不包含 `reuse` 和 `delete` 条目
- `isFullUpdate` 始终为 `false`（除非回退到方法一且无基线）
- `requiresCleanInstall` 根据 Launcher.exe 是否在映射到的变更文件列表中判断

### 7.6 工作流中的条件分支

```yaml
- name: Run build-delta
  shell: bash
  run: |
    if [[ "$COMPARE_METHOD" == "commit-analyze" ]]; then
      # 方法二
      dotnet run --project ... -- build-delta-from-commits \
        --platform windows-x64 \
        --current-version $RELEASE_VERSION \
        --current-zip $PWD/plonds-input/current-files-windows-x64.zip \
        --output-dir $PWD/plonds-output \
        --channel $RELEASE_CHANNEL \
        --baseline-tag $BASELINE_TAG \
        --current-tag $RELEASE_TAG \
        --fallback-zip $PWD/plonds-input/baseline-files-windows-x64.zip
    else
      # 方法一
      dotnet run --project ... -- build-delta \
        --platform windows-x64 \
        --current-version $RELEASE_VERSION \
        --current-zip $PWD/plonds-input/current-files-windows-x64.zip \
        --output-dir $PWD/plonds-output \
        --channel $RELEASE_CHANNEL \
        --hash-algorithm $HASH_ALGORITHM \
        --baseline-version $BASELINE_VERSION \
        --baseline-zip $PWD/plonds-input/baseline-files-windows-x64.zip
    fi
```

方法二时，基线 zip 仍然需要下载（用于回退），但不需要解压（除非回退）。

### 7.7 两种方法的步骤差异

| 步骤 | 方法一 (file-compare) | 方法二 (commit-analyze) |
|------|----------------------|------------------------|
| 下载基线 zip | ✅ 需要 | ✅ 需要（用于回退） |
| 下载当前 zip | ✅ | ✅ |
| 解压两个 zip | ✅ | ✅ 只解压当前（回退时解压基线） |
| git diff/log | ❌ | ✅ 需要 fetch-depth:0 |
| 哈希对比 | ✅ 两个目录全量扫描 | ❌ 不做（除非回退） |
| 源码→产物映射 | ❌ | ✅ |
| 回退逻辑 | ❌ | ✅ 无源码变更时回退方法一 |

## 8. 不在本次改造范围内的事项

- 宿主侧客户端代码改造（PlondsUpdateApplier 等，后续单独设计）
- Launcher 侧客户端代码改造（后续单独设计）
- Plonds.Api 项目处置（后续决定是否保留）
- `build-index`、`generate`、`publish`、`sign` 等旧 Tool 命令的清理（后续处理）
