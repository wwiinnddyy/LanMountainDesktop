# PLONDS 骨架

Penguin Logistics Online Network Distribution System（企鹅物流在线网络分发系统），简称 PLONDS，是 LanMountainDesktop 的独立更新分发骨架。

本目录有意与主应用和启动器隔离，仅包含新的分发协议、一个轻量级的只读 API，以及示例 S3 风格的元数据文件。

## 目录结构

```text
PenguinLogisticsOnlineNetworkDistributionSystem/
  README.md
  src/
    Plonds.Shared/
    Plonds.Api/
  sample-data/
    meta/
      channels/
        stable/
          windows-x64/
          windows-x86/
          linux-x64/
      distributions/
```

## 项目说明

- `Plonds.Shared` 提供协议常量和数据模型。
- `Plonds.Core` 负责哈希计算、差异生成、对象仓库生成、清单生成、签名和发布编排。
- `Plonds.Tool` 是面向 CI 的命令行入口。PowerShell 脚本应保持为围绕此工具的薄包装层。
- `Plonds.Api` 是一个轻量级只读 API，从类似 S3 布局的本地文件夹中读取元数据。

## 架构设计

PLONDS 有意围绕单一的 C# 实现栈构建，以确保协议和发布行为不会在不同语言之间产生偏差。

```text
宿主应用
  -> 检查更新、下载对象、暂存传入的负载
启动器
  -> 验证签名、应用文件映射、切换部署、回滚

PLONDS.Api
  -> 面向客户端的只读元数据投影
PLONDS.Tool
  -> CI/发布命令界面
PLONDS.Core
  -> 哈希/差异/对象仓库/签名/发布实现
PLONDS.Shared
  -> 协议常量和 DTO
```

## v1 规则

- 核心协议行为应位于 `Plonds.Core` 中，而非 PowerShell 脚本。
- `scripts/*.ps1` 仅可作为 GitHub Actions 和本地便利的薄包装层保留。
- 宿主应用保留下载职责。
- 启动器保留应用、原子切换、快照和回滚职责。

## 存储布局

第一版本保持固定的对象根目录：

```text
lanmountain/update/
  repo/sha256/<前缀>/<哈希>
  meta/channels/<频道>/<平台>/latest.json
  meta/distributions/<分发ID>.json
  installers/<平台>/<版本>/...
```

已规划但 v1 中未启用：

```text
lanmountain/update/repo-compressed/<算法>/<前缀>/<哈希>
lanmountain/update/patches/<算法>/<基础哈希>/<目标哈希>
```

## 公共接口

API 基础路径为 `/api/plonds/v1`。

- `GET /healthz` - 健康检查
- `GET /api/plonds/v1/metadata` - 获取元数据目录
- `GET /api/plonds/v1/channels/{channel}/{platform}/latest?currentVersion=...` - 获取指定频道和平台的最新版本
- `GET /api/plonds/v1/distributions/{distributionId}` - 获取指定分发版本的完整信息

## 本地运行

```powershell
dotnet run --project src/Plonds.Api
```

默认情况下，API 从 `sample-data` 读取元数据。
