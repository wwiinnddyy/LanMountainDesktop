# PLONDS Client Service 独立化设计

> 日期：2026-06-01
> 状态：设计中

## 1. 目标

PLONDS 在应用内必须作为独立服务存在，负责分发发现、下载、校验和本地包准备。它不是现有 Update 模块的 provider，也不应把 S3/GitHub/source 选择逻辑混入 `LanMountainDesktop/Services/Update/`。

最终边界：

- PLONDS 服务：寻找最新版本、选择下载源、下载 manifest 和包、校验文件、准备本地 staging。
- 安装程序/安装网关：只消费 PLONDS 已准备好的本地安装输入，执行增量安装或完整安装。
- UI：只展示 PLONDS 服务和安装程序返回的状态；完整包也失败后才处理错误。

## 2. 当前耦合点

当前需要拆离的耦合点：

- `LanMountainDesktop/Services/Settings/SettingsDomainServices.cs`
  - 直接持有 `PlondsStaticUpdateService` 与 `PlondsReleaseUpdateService`
  - 在 `CheckForUpdatesCoreAsync` 中把 PLONDS 和 GitHub Update fallback 逻辑混在一起
- `LanMountainDesktop/Services/Update/UpdateInstallGateway.cs`
  - 直接判断 `UpdatePayloadKind.DeltaPlonds`
  - 直接实例化 `PlondsUpdateApplier`
- `LanMountainDesktop/Services/Update/Plonds*.cs`
  - PLONDS apply/parser/payload resolver 仍位于 Update 命名空间

## 3. Source 发现规则

PLONDS 客户端内置两个初始地址：

1. S3 上的 PLONDS manifest 地址
2. GitHub Release 上的 PLONDS manifest 地址

两个地址读取的是同一种 JSON 文件，当前文件名为 `PLONDS.json`。客户端每次检查增量更新时，会并行或顺序请求所有已知 source 的 `PLONDS.json`。

### 3.1 Source 扩展

`PLONDS.json` 可以声明额外 source。客户端读取到额外 source 后，应把它们加入下一轮寻找列表。

建议 manifest 扩展字段：

```json
{
  "sources": [
    {
      "id": "rainyun-s3",
      "kind": "s3",
      "manifestUrl": "https://example.com/plonds/1.2.3/PLONDS.json",
      "priority": 100
    },
    {
      "id": "github",
      "kind": "github",
      "manifestUrl": "https://github.com/owner/repo/releases/download/v1.2.3/PLONDS.json",
      "priority": 50
    }
  ]
}
```

规则：

- `sources` 为空或缺失时，只使用内置 S3 + GitHub。
- 新 source 不覆盖内置 source，除非 `id` 相同。
- source 列表需要去重，按 `id` 和 `manifestUrl` 双重去重。
- source 持久化到 PLONDS 自己的配置/缓存，不写入 Update 设置。

## 4. 版本选择规则

如果多个 source 返回的版本不一致，客户端选择 `currentVersion` 最高的 manifest。

规则：

- 版本解析使用 `Version` 语义，忽略前导 `v`。
- 版本相同时，优先选择下载可用性更高的 source。
- 如果最高版本 manifest 下载包失败，可以尝试同版本的其他 source。
- 不因为低版本 source 成功而降级，除非用户显式允许。

## 5. 下载与回退规则

PLONDS 服务优先走增量包：

1. 下载所选 manifest。
2. 下载 `changed.zip`。
3. 校验 `changed.zip` 与 manifest 中的 hash/checksum。
4. 解压或准备增量 staging。
5. 交给安装程序执行增量安装。

如果增量流程失败，PLONDS 服务自动改用完整包：

1. 下载 `Files.zip`。
2. 校验 `Files.zip`。
3. 解压或准备完整包 staging。
4. 交给安装程序执行完整包安装。

如果完整包也失败，PLONDS 服务返回失败结果，由 UI 展示错误和重试入口。

## 6. 发布产物布局

Publisher 上传到 S3 的版本目录：

```text
<prefix>/<version>/PLONDS.json
<prefix>/<version>/changed.zip
<prefix>/<version>/<version>-changed/**
<prefix>/<version>/Files.zip
<prefix>/<version>/<version>-Files/**
```

说明：

- `Files.zip` 是上传到 S3 时的完整包标准名。
- `<version>-Files/` 是 S3 上解压后的完整包目录。
- `<prefix>/PLONDS.json` 是 S3 的固定 latest manifest 地址，和 GitHub Release latest manifest 一起作为客户端内置初始 source。
- GitHub Release 仍可保留平台原始文件名，例如 `files-windows-x64.zip`。
- `PLONDS.json` 的 downloads 字段同时包含 GitHub 与 S3 的增量包、完整包位置。

## 7. 建议代码结构

```text
LanMountainDesktop/Services/Plonds/
  IPlondsService.cs
  PlondsService.cs
  Sources/
    IPlondsSource.cs
    PlondsHttpManifestSource.cs
    PlondsSourceRegistry.cs
  Download/
    PlondsDownloader.cs
    PlondsDownloadPlanner.cs
  Verification/
    PlondsVerifier.cs
  Staging/
    PlondsPackageStore.cs
    PlondsPreparedPackage.cs
  Models/
    PlondsClientManifest.cs
    PlondsSourceDescriptor.cs
    PlondsCheckResult.cs
```

后续如果要移植，优先把这棵目录或等价项目抽成独立库。

## 8. 与安装程序的交接契约

PLONDS 服务输出本地 prepared package：

```csharp
public sealed record PlondsPreparedPackage(
    Version Version,
    PlondsPackageMode Mode,
    string ManifestPath,
    string? ChangedZipPath,
    string? ChangedDirectory,
    string? FilesZipPath,
    string? FilesDirectory);
```

安装程序只接受这个结果，不参与 source 发现、下载和校验。

## 9. 实施顺序

1. Publisher 补齐完整包 S3 上传与 manifest downloads 字段。
2. 新增 `Services/Plonds/` 客户端服务骨架和模型。
3. 把 `PlondsStaticUpdateService` / `PlondsReleaseUpdateService` 合并迁移到独立 PLONDS source 体系。
4. 把 `LanMountainDesktop/Services/Update/Plonds*.cs` 迁出 Update 命名空间。
5. `UpdateSettingsService` 改为调用 `IPlondsService`，不再直接组合 S3/GitHub PLONDS fallback。
6. 安装入口只接收 `PlondsPreparedPackage`。
7. 添加单元测试覆盖 source 扩展、最高版本选择、增量失败转完整包、完整包失败交 UI。
