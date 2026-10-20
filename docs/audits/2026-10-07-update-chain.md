# 更新链路审计：2026-10-07

本轮已覆盖 GitHub Release → PLONDS Comparator → S3 Publisher → 客户端检测/选择 → 下载/校验 → 宿主与在线安装器两条安装路径 → 启动/回滚。

原始结论：存在能造成“安装成功但无法启动”、混用新旧二进制、下载主源失效和取消失效的问题。以下问题表保留审计时的证据；本轮修复状态见下一节。

## 本轮修复

- 安装器两条部署入口共用完整文件校验、根目录文件事务、活动标记提交及回滚快照；缺文件/哈希错误/复制失败保留旧部署。
- Host 遇到根目录升级不再只复制 app 后返回成功，而是启动包内 NativeAOT 维护程序，退出后完整安装，读取完成/失败回执；普通应用部署完成后使用现有生命周期重启。
- 维护进程在提升权限后保留原 Host 用户令牌，防止把桌面以管理员身份重启；无法取得原令牌时保留完成回执，日志记录从普通快捷方式启动的说明。
- 补齐 pending 恢复、真实取消、暂停/进度/续传配置、版本锁定、基线匹配及增量安装失败后的完整回退。
- 用户确认默认 stable，允许 preview；UI、检测与包选择一致过滤所选通道及本进程架构。GitHub 使用 release API 查找匹配清单，不依赖只支持正式版本的 latest 链接。
- 修正公开 S3 前缀，并能按对象 key 兼容修复旧清单中的错误地址；Release 明确 dispatch Comparator，x64/x86 分别发布，S3 latest 按平台与通道隔离。
- 发布兼容的旧版清单，要求旧客户端走现有 EXE 完整安装回退；新版清单继续使用完整包及维护安装器，避免旧客户端忽略根目录载荷。
- Publisher 验证匿名公开 URL 后才提升 latest，并拒绝版本倒退；上传续传按内容 SHA256 与大小判断，修复同大小文件被误跳过。
- 新在线安装流程安装缺少的 .NET 运行时，并检查微软签名及安装退出码；历史 EXE 回退固定目标目录、等待 Host 退出，等待实际安装结束并验证部署。

最终验证包含新增真实目录部署、根目录失败回滚、取消竞态、pending 恢复、维护回执、通道/架构选择、公开下载失败不提升指针及同大小不同内容上传等回归，共 250 项（含 SDK 公共接口基线）通过，0 失败。NativeAOT 维护程序已实际运行并拒绝损坏包、写出失败回执，未创建部署。工作流 YAML 及 12 个 Bash 脚本语法通过。原始结果在 `artifacts/update-chain-hardening/verification/`。

这些是本地代码、脚本与构建修复。线上 GitHub/S3 的旧资产和指针需经过修复后的发布工作流才能更新；真实 UAC、干净虚拟机和实际网络发布仍属于发布验收。

## 取证范围

- 源码基线：当前工作区，HEAD 为 `a66adb397704742806731a8dd3dff0c284851432`。
- 线上样本：GitHub/S3 的 `v0.9.1.1`，GitHub 发布时间为 2026-09-16 14:48:57 UTC，标记为 prerelease。
- 本地行为复现使用当前源码编译的程序、模拟 HTTP、内存设置及新建临时安装目录；不执行真实安装器，不覆盖本机安装或用户配置。这些复现证明当前源码的问题，不直接等同于对已发布二进制的实机测试。
- 实际下载 `Files.zip`，共 96,034,368 字节；MD5 与线上清单一致，清单中 142 个应用文件的哈希全部匹配。
- 当前源码的相关测试：166 项通过，0 失败，0 跳过；命令与原始结果见末尾。
- 复现脚本已实际运行，六组场景结果保存在 `artifacts/update-chain-audit/evidence/local-probes.json`。

## 问题与修复顺序

| 编号 | 优先级 | 阶段 | 问题及用户影响 | 证据 |
| --- | --- | --- | --- | --- |
| U01 | P0 | 在线安装器增量安装 | 对嵌套的完整包使用应用内相对路径提取文件，缺文件时静默跳过，随后移除旧活动标记并激活不完整的新目录；也未按 `RequiresCleanInstall` 转完整安装 | 临时目录复现：返回成功，新活动目录缺少主程序，旧 `.current` 已移除 |
| U02 | P0 | 宿主完整/清洁更新 | 全量包只复制 `app-*`，没有更新安装根目录中的 Launcher、AirAppRuntime 及其依赖；`RequiresCleanInstall` 被当成“多下载一个全量 zip” | 复现：成功返回，主程序已换，Launcher/Runtime 仍为旧内容 |
| U03 | P1 | S3 发布与下载 | 公开 URL 生成时错误移除 `lanmountain/update` 前缀；已上传的包被写成不存在的地址。最新目录的推导 URL 也没有对应包 | 线上清单里的包 URL 为 404；保留完整前缀的版本 URL 为 200 |
| U04 | P1 | GitHub 工作流衔接 | Release 用 `GITHUB_TOKEN` 创建/更新 release，却依赖 release 事件启动 Comparator；该事件不能自动启动下一工作流 | 源码、GitHub 官方规则；最近五次 Comparator 全为手动 dispatch |
| U05 | P1 | 通道、系统与架构选择 | UI/设置固定 stable，实际按最高版本选择任何清单；不校验 `Channel`、`Platform`。PLONDS 只产 windows-x64 清单，其他平台/架构也会消费 | 线上最新清单为 preview/windows-x64；复现：stable/x64 输入选择了更高版本的 x86 清单 |
| U06 | P1 | 增量包选择与回退 | 未比较安装版本与 `PreviousVersion`；跳版本仍选 delta。准备包成功后的安装失败没有完整包回退 | 复现：安装 1.0.0、基线 1.5.0，调用 delta 一次、full 零次 |
| U07 | P1 | 下载暂停/取消 | 只改状态，没有停止在途操作；恢复可启动第二次下载，旧任务完成会写回已下载状态 | 复现：取消后 token 未取消，状态从 Idle 又变为 Downloaded |
| U08 | P1 | 已下载状态恢复与回滚 | 只保存 manifest 路径，没有重新构建 prepared package/阶段；主安装路径不写快照，却使用另一套回滚网关 | 复现：重建服务后为 Idle；更新后回滚返回 `no_snapshot` |
| U09 | P1 | 全量包安装校验 | 宿主 full 分支不加载清单、不验证落地文件；宿主下载器允许缺包校验和，却向上返回已校验 | 复现：落地文件与清单哈希不一致，安装仍成功 |
| U10 | P1 | EXE 回退与安装结果 | 没传当前安装目录；便携/未注册安装可能落到其他目录。仅启动进程便报告已安装，不检查实际安装结果 | 源码确认；Inno 已有注册安装目录恢复，便携/多安装及失败安装需实机验证 |
| U11 | P1 | 在线安装前置条件 | 新在线安装器没有补齐 .NET 运行时准备步骤，载荷中的 Host/Runtime 是框架依赖程序 | 源码确认；Launcher 对缺运行时返回 `dotnet_runtime_missing`，干净虚拟机待验证 |
| U12 | P2 | 发布一致性与传输体验 | latest 指针不分通道/架构、没有版本单调检查；主 PLONDS 下载器缺实际进度、分段、续传与停滞控制 | 源码确认；并发发布覆盖较新版本尚未做线上重演 |

P0 应先修复，避免把不完整或混用版本的目录报告为安装成功。随后修 U03–U06，确保新包能自动发布、能从用户网络下载、能选对目标及基线；再补齐取消、恢复、回滚和安装结果确认。

## 1. GitHub 发布链路

当前链路为：

```text
Release → GitHub assets
         → release event → PLONDS Comparator
                          → workflow_run → PLONDS Publisher
                                         → S3 latest PLONDS.json
```

### U04：Release 到 Comparator 的自动触发断开

`.github/workflows/release.yml:1197` 用 release-action 创建 Release，`:1223` 使用 `secrets.GITHUB_TOKEN`。Comparator 在 `.github/workflows/plonds-comparator.yml:8` 监听 release/published、prereleased，Release 完成后没有主动 dispatch Comparator。

GitHub 的规则是：由工作流的 `GITHUB_TOKEN` 产生的事件通常不创建新工作流运行；workflow_dispatch、repository_dispatch 等是例外。参见 [GitHub 官方触发规则](https://docs.github.com/en/actions/how-tos/writing-workflows/choosing-when-your-workflow-runs/triggering-a-workflow)。

本次现场核验的最近一次链路：

- [Release 35110146817](https://github.com/wwiinnddyy/LanMountainDesktop/actions/runs/35110146817)：workflow_dispatch，success，14:49:33 UTC 完成。
- [Comparator 35111535491](https://github.com/wwiinnddyy/LanMountainDesktop/actions/runs/35111535491)：workflow_dispatch，success，14:52:48 UTC 手动启动。
- [Publisher 35111626828](https://github.com/wwiinnddyy/LanMountainDesktop/actions/runs/35111626828)：workflow_run，success，16:31:19 UTC 完成。

因此不能把 Release 的绿色状态当成镜像及智慧更新已发布；本例中两个完成时间相隔约 1 小时 42 分钟。

修复建议：将 Comparator 明确接到成功的 Release（workflow_run/reusable workflow 或主动 workflow_dispatch），传递 release tag、通道和源提交。Publisher 完成匿名公开下载校验后，再将这次发行视为智慧更新可用。

### 增量策略容易退化成每次全量

Release 给 Launcher 写入与应用相同的 Version/AssemblyVersion/FileVersion（`release.yml:140` 起）；`PlondsDeltaBuilder.cs:66` 起通过 Launcher 文件哈希变化判断 `RequiresCleanInstall`。仅版本戳变化就可能触发清洁安装。本次线上样本的该标志确实为 true。

这与“宿主完整包安装不更新根目录”的 U02 组合，尤其危险。应先明确 bootstrap 文件升级契约，再决定 Launcher 是否采用独立版本或以维护进程执行完整根目录升级，不能仅扩大下载包就声称清洁安装已经完成。

Comparator 自动基线也只是选择 release 列表中首个同通道且含 x64 zip 的条目（`plonds-comparator.yml:125`），没有保证它早于目标版本；重跑历史 tag 时可能选到更高版本基线。

## 2. 下载地址、通道与架构

### U03：线上 S3 链接已经失效

本次匿名 HEAD/GET 核验：

| 地址 | 结果 |
| --- | --- |
| `.../lmdesktop/lanmountain/update/plonds/PLONDS.json` | GET 200，版本 0.9.1.1，preview/windows-x64 |
| `.../lmdesktop/plonds/0.9.1.1/Files.zip`（清单声明） | HEAD 404 |
| `.../lmdesktop/lanmountain/update/plonds/0.9.1.1/Files.zip`（实际文件） | HEAD/实际下载成功，96,034,368 字节 |
| `.../lmdesktop/plonds/0.9.1.1/changed.zip`（清单声明） | HEAD 404 |
| `.../lmdesktop/lanmountain/update/plonds/0.9.1.1/changed.zip`（实际文件） | HEAD 200，35,269,385 字节 |
| `.../lmdesktop/lanmountain/update/plonds/Files.zip`（客户端推导） | HEAD 404 |
| GitHub `releases/latest/download/PLONDS.json`（内置备源） | GET 404 |

根因在 `plonds-uploader.yml:23,106,124`：公开 base 默认停在 bucket 根目录，却设置 `PublicBaseKeyPrefix=lanmountain/update`；`PlondsS3Client.cs:407` 将该前缀从对象 key 中移除。Publisher 的 EnsureObjectExists 只证明 S3 对象存在，无法证明清单中的匿名公开 URL 可用。

宿主与在线安装器会进一步尝试 GitHub 的**版本专属包 URL**，所以不能说所有下载必然失败；但 S3 主源已经失效，只能靠 GitHub 包地址救场。GitHub 受限网络中的用户尤其容易卡住。

修复建议：使公开 base 与被剥离的 key 前缀成对定义；用用户实际看到的 URL 做公开校验；客户端可根据 manifest 对象 key 构建正确的版本包地址，不能把“latest 清单所在目录”当成“latest 包目录”。

### U05：稳定通道和架构没有约束

`SettingsDomainServices.cs:839` 固定返回不含预发布、stable；`PlondsManifestSelector.cs:5` 只解析版本、排序，`InstallerPlondsClient.cs` 的选择也未过滤 channel/platform。

线上最近查询到的 100 条 release 均为 prerelease；GitHub latest API 与 latest 清单地址均返回 404。S3 的共享 latest 清单却返回 preview。当前“stable”设置并不能提供稳定通道语义。

修复建议：按通道和 platform/RID 发布及发现清单，先过滤允许的平台、架构和通道，再选最高版本；最高**兼容**版本失败时仅尝试同版本镜像，禁止用另一架构或另一通道救场。

## 3. 下载和任务生命周期

### U06：跨版本错误地选择 delta

`PlondsService.cs:75` 调 planner 时没有带入当前安装版本；`PlondsDownloadPlanner.cs:14` 只看 forceFull、RequiresCleanInstall、IsFullUpdate。PreviousVersion 没有用于判定可应用性。

复现结果：安装 1.0.0，清单目标 2.0.0、基线 1.5.0，实际选 Delta。复用文件可能在安装的哈希校验阶段才暴露不匹配，而 `InstallPlondsAsync` 失败直接返回，不重新下载 full。

修复建议：基线不匹配直接选 full；安装前验证复用文件及暂存结果；只有完整回退也失败才对用户报告失败。检测后固定目标版本和清单，避免下载阶段重新发现另一目标。

### U07：取消会被后台完成覆盖

`SettingsDomainServices.cs:940` 的 CancelAsync 只清字段和改 Idle；`:1264` 的 Pause 只改 phase。ViewModel 的动作又使用 CancellationToken.None。主 PLONDS 下载器没有自己的受控操作 token，也没有 operation gate/version 防止过期回写。

真实服务方法复现结果：

```text
Pause → PausedDownloading，下载 token 未取消
Cancel → Idle，下载 token 未取消
旧下载完成 → Success=true，状态重新变成 Downloaded，pending 路径被再次写入
```

Resume 可以启动另一个下载，而 PackageStore 为相同版本/source/mode 清理相同 staging 目录；存在删除另一个在途下载文件的竞态。

此外，主 PLONDS 下载器始终从头下载，失败/取消时删除 partial；没有采用另一套 ResumableDownloadService，也不使用设置中的分段线程数或上报字节进度。UI 的续传/线程数描述与主路径行为不一致。

## 4. 安装、激活、启动与回滚

### U01：在线增量更新会激活缺少主程序的目录

`OnlineInstallService.cs:195` 下载完整包。真实 Windows zip 的结构是 `app-<version>/LanMountainDesktop.exe`，文件图中的路径是 `LanMountainDesktop.exe`。

`:263` 将包根目录作为 extractDir，`:271` 找不到文件时直接跳过，`:307` 未验证新目录的主程序/哈希便激活。整条分支还绕过了 FilesPackageInstaller 的运行进程检查及根目录载荷复制。

带 RequiresCleanInstall=true 的嵌套包复现：方法正常返回，新 `app-2.0.0-0` 有 `.current`，没有 `LanMountainDesktop.exe`，旧活动标记已移除。当前测试主要验证计划构建器，没有对这条方法的最终可启动目录做断言。

修复建议：清洁安装强制进入完整安装；增量分支使用解析后的应用目录，缺文件立即失败，应用计划后校验完整 file map；验证通过后才提交活动标记；失败保留旧目录。若暂时不能做到，先让这个入口采用已验证的完整安装路径。

### U02/U09：宿主完整安装遗漏根目录载荷且不验证落地内容

`PlondsPreparedPackageInstaller.cs:49` 只复制解析出的 app 目录，然后激活；Full 分支在加载 manifest 前就返回到这条路径。当前完整包实际含根目录 Launcher、AirAppRuntime 及运行时配置，因此这不是包里没提供那些文件。

复现同时看到：

```text
安装返回 Success=true
Launcher=old-launcher，Runtime=old-runtime
应用文件与 file map 的 SHA256 不一致，仍被激活
旧版本被标记 .destroy
```

应由维护进程在权限和进程状态允许时处理根目录升级，并返回可验证的安装结果；所有分支都应验证目标目录，而不是把“下载成功/进程启动成功”当成“安装成功”。

### U08：下载后重启不能恢复；回滚没有对应快照

prepared package 保存的是 manifest 路径（`SettingsDomainServices.cs:1416`），构造服务却不读出 pending package、Mode、目录及已下载 phase；重启后变成 Idle。退出静默安装只识别内存 package 或 full EXE lock，不能恢复已保存的 PLONDS package。

主路径安装也没有写 UpdateSnapshotStore。复现中宿主安装成功后，UpdateRollbackGateway 立即返回 `no_snapshot`。UI 的回滚方法调用另一套 orchestrator，而该 orchestrator 通常没参与此次安装；它处于不可回滚阶段时直接返回，ViewModel 却会显示回滚完成。

修复建议：持久化可恢复的 prepared-package/state 契约并校验缓存；安装前记录真正可用的旧部署；所有阶段和回滚都归属于同一个任务及状态源。

### U10/U11：安装器的交接条件没有闭合

`UpdateInstallGateway.cs:233` 只传静默参数，没有 `/DIR`。补查确认：虽然 Inno 配置 `UsePreviousAppDir=no`，`TryLoadExistingInstallation` 和 `ApplySelectedInstallDirectory` 仍会从卸载注册信息恢复已有安装目录，因此不能把“所有自定义安装目录都会装错”作为结论。实际路径风险限于便携、未注册、注册信息失效或存在另一份注册安装的场景。网关仅 Process.Start 便返回 success，未等待退出码或读取结果回执，这一结果确认缺口已由源码确认。

在线安装器与 Inno 也没有相同的前置条件准备：只有 Inno 准备 .NET 运行时；在线安装器复制框架依赖的 Host/Runtime 后报告完成，Launcher 对缺运行时仍返回 `dotnet_runtime_missing`。

这两项应补实机矩阵：已注册自定义目录、便携/未注册目录、多份安装、每用户/Program Files 安装、拒绝 UAC、运行中升级、缺 .NET 的干净 Windows、x86/x64 及安装失败后的原版本启动。

## 发布策略建议

1. 使用一个明确的发行目标：tag、提交、通道、架构、版本、基线和包哈希，贯穿工作流及客户端。按通道/架构隔离 latest，禁止旧版本或 preview 覆盖 stable 指针。
2. 基线完全匹配才应用 delta；完整更新明确包含应用部署和 bootstrap 根目录维护。需要维护进程时显式交接，不把普通 app 目录复制伪装成清洁安装。
3. 由一套可恢复任务负责暂停、取消、下载、校验、安装、结果确认和回滚。旧操作不能在取消后改变当前任务。
4. 以可启动且哈希正确的新部署、完成的根目录维护及保留的回滚点作为安装成功判据；以匿名公开下载通过作为发布成功判据。

## 复跑与验收

运行本地六组复现场景：

```powershell
pwsh -File ./scripts/audit-update-chain.ps1
```

脚本重建当前源码，并使用模拟 HTTP、内存设置和新建临时目录，结果输出到 `artifacts/update-chain-audit/evidence/local-probes.json`。输出是实际行为记录，脚本退出 0 仅表示探针成功运行，不表示更新系统通过验收。

现有测试命令：

```powershell
dotnet test tests/LanMountainDesktop.Tests/LanMountainDesktop.Tests.csproj -c Debug --no-build --no-restore --filter 'FullyQualifiedName~Plonds|FullyQualifiedName~Installer|FullyQualifiedName~ResumableDownload|FullyQualifiedName~Update|FullyQualifiedName~Deployment' --logger 'trx;LogFileName=baseline-tests.trx' --results-directory artifacts/update-chain-audit
```

实际结果为 166/166 通过。原始 TRX、日志及线上取证文件均在 `artifacts/update-chain-audit/`；该目录被 git 忽略，不作为发布产物上传。

修复后的必要验收包括：嵌套完整包的在线增量更新、Launcher/Runtime 改动、跨基线跳版本、错误架构/通道、公开镜像 404/超时、下载取消/恢复竞态、重启恢复 pending、目标文件缺失/哈希不符、安装失败保留旧部署、回滚实际恢复及首次启动。线上发布与真实 UAC/干净虚拟机验证仍需在修复阶段执行。
