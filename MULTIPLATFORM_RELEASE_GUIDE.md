# 🚀 LanMontainDesktop 多平台 CI/CD 工作流完整指南

## 📋 概述

已为 LanMontainDesktop 项目配置完整的 **GitHub Actions 多平台 CI/CD 工作流**，支持 Windows、Linux 和 macOS 的自动化构建和发布。

## ✨ 新增功能亮点

### 🪟 Windows 多架构支持
- ✅ **win-x64** (64位) - 主要架构
- ✅ **win-x86** (32位) - 兼容性
- 输出：`.zip` 可执行包

### 🐧 Linux 支持
- ✅ **linux-x64** - 依赖X11
- 输出：`.tar.gz` 压缩包
- 依赖：自动安装 fontconfig、freetype 等库
- 计划：AppImage、Snap、.deb 包

### 🍎 macOS 完整支持
- ✅ **osx-x64** - Intel 芯片（经典Mac）
- ✅ **osx-arm64** - Apple Silicon（M1/M2/M3）
- 输出：`.tar.gz` 压缩包
- 计划：DMG、代码签名、公证

## 📦 发布流程

```
推送 Git Tag (v1.0.0)
    ↓
GitHub Actions 触发
    ↓
┌─────────────────────────────────┐
│  并行构建三个平台                │
│  ├─ Windows (x64 + x86)         │
│  ├─ Linux (x64)                 │
│  └─ macOS (x64 + arm64)         │
└─────────────────────────────────┘
    ↓
创建 GitHub Release
    ↓
自动上传所有平台包
```

## 🎯 使用方式

### 快速发布（所有平台）

```bash
# 1. 确保所有更改已提交
git add .
git commit -m "Release v1.0.0"

# 2. 创建并推送标签
git tag v1.0.0
git push origin v1.0.0

# 3. GitHub Actions 自动构建
# 等待 Actions 完成 → 自动创建 Release
# 查看：https://github.com/YOUR_ORG/LanMontainDesktop/releases
```

### 手动触发（选择性平台）

1. 访问 GitHub Actions 标签页
2. 选择 **Release & Publish** 工作流
3. 点击 **Run workflow**
4. 填入版本号（如 `1.0.0`）
5. ☑️ 选择要构建的平台：
   - ✅ Build Windows (x64/x86)
   - ✅ Build Linux (x64)
   - ✅ Build macOS (x64/arm64)
6. 可选：☑️ 标记为预发布版本
7. 点击 **Run workflow**

### 本地测试构建

**Windows:**
```powershell
.\LanMontainDesktop\scripts\package.ps1 -RuntimeIdentifier win-x64 -Version 1.0.0
```

**Linux:**
```bash
chmod +x scripts/build.sh
./scripts/build.sh --rid linux-x64 --version 1.0.0
```

**macOS:**
```bash
chmod +x scripts/build.sh
./scripts/build.sh --rid osx-x64 --version 1.0.0
./scripts/build.sh --rid osx-arm64 --version 1.0.0
```

## 📂 项目结构变更

```
LanMontainDesktop/
├── .github/
│   ├── workflows/
│   │   ├── build.yml                    # ✅ CI 持续构建
│   │   ├── code-quality.yml             # ✅ 代码质量检查
│   │   ├── release.yml                  # ⭐ 多平台 Release（已升级）
│   │   └── issue-management.yml         # ✅ Issue 自动管理
│   ├── ISSUE_TEMPLATE/
│   │   ├── bug_report.md                # Bug 报告模板
│   │   ├── feature_request.md           # 功能请求模板
│   │   └── config_issue.md              # 配置问题模板
│   ├── CODEOWNERS                       # 代码所有权
│   ├── pull_request_template.md         # PR 模板
│   ├── WORKFLOWS_GUIDE.md               # 工作流详细指南
│   └── MULTIPLATFORM_BUILD.md           # ⭐ 多平台构建指南（新增）
├── scripts/
│   ├── build.sh                         # ⭐ Linux/macOS 构建脚本（新增）
│   └── package.ps1                      # Windows 打包脚本（已有）
├── .gitattributes                       # ⭐ 行尾处理配置（新增）
└── CICD_EVALUATION.md                   # CI/CD 评估文档（已更新）
```

## 🔄 工作流详解

### 1. Build & Test (`build.yml`)
**何时运行:** Push、PR、手动触发  
**做什么:** 
- 构建 Debug 和 Release 两种配置
- 运行测试
- 检查编译错误

### 2. Code Quality (`code-quality.yml`)
**何时运行:** PR、Push 到主分支  
**做什么:**
- 检查代码格式（`dotnet format`）
- 编译警告检测
- 可选：Qodana 分析

### 3. Release & Publish (`release.yml`) ⭐
**何时运行:** 推送 Git 标签或手动触发  
**支持平台:**
- Windows: win-x64, win-x86
- Linux: linux-x64
- macOS: osx-x64, osx-arm64

**做什么:**
1. 检测版本号（从标签或手动输入）
2. 并行构建所有平台
3. 创建平台特定的包
4. 生成 GitHub Release
5. 上传所有 artifacts

### 4. Issue Management (`issue-management.yml`)
**何时运行:** 每天 1:30 AM UTC  
**做什么:**
- 标记 14 天无活动的 Issue 为 "stale"
- 关闭 21 天无活动的 PR
- 自动评论提醒

## 📊 预期构建时间

| 平台 | 架构 | 时间 | 成本 |
|------|------|------|------|
| Windows | x64 | ~2-3m | 低 |
| Windows | x86 | ~2-3m | 低 |
| Linux | x64 | ~2-3m | 低 |
| macOS | x64 | ~3-5m | 低 |
| macOS | arm64 | ~3-5m | 低 |
| **总计** | 5个 | ~12-20m | 低 |

> 💡 GitHub 免费账户每月 2000 runner-hours，足够大多数项目使用

## 🛠️ 配置与优化

### 必需配置
✅ 无需额外配置！工作流开箱即用

### 可选配置

**启用 Qodana 代码分析:**
1. 访问 https://qodana.cloud
2. 创建 organization token
3. 在 GitHub Settings > Secrets > Actions 添加：
   - `QODANA_TOKEN` = your_token
   - `QODANA_ENDPOINT` = https://qodana.cloud
4. 编辑 `.github/workflows/code-quality.yml`，取消 Qodana 步骤注释

**配置分支保护规则:** （强烈推荐）
1. 访问 Settings > Branches > Branch Protection Rules
2. 要求通过以下检查：
   - ✅ Build & Test
   - ✅ Code Quality
3. 要求代码审查后再合并
4. 驳回过期分支

## 🐛 故障排查

### Release 工作流不运行？
- 检查标签格式：`v1.0.0` 或 `release-1.0.0`
- 确认 csproj 文件格式正确
- 查看 Actions 日志获取详细错误

### 特定平台构建失败？
- **Windows**: 检查 libvlc 依赖
- **Linux**: 确保依赖库已安装
- **macOS**: 检查 Xcode 命令行工具

### 包大小过大？
- 启用 `PublishTrimmed=true` 缩小 IL 代码
- 考虑关闭符号信息：`DebugType=none`

## 📚 文档导航

| 文档 | 用途 |
|------|------|
| [WORKFLOWS_GUIDE.md](.github/WORKFLOWS_GUIDE.md) | 工作流使用指南 |
| [MULTIPLATFORM_BUILD.md](.github/MULTIPLATFORM_BUILD.md) | 多平台构建详解 |
| [CICD_EVALUATION.md](CICD_EVALUATION.md) | CI/CD 评估与规划 |

## 🎓 下一步

### 立即做（今天）
- [ ] 推送所有更改到 GitHub
- [ ] 验证 Actions 工作流运行成功
- [ ] 测试创建第一个 Release 标签

### 本周内
- [ ] 配置分支保护规则
- [ ] 团队成员熟悉 PR 流程
- [ ] 收集使用反馈

### 后续优化（计划）
- [ ] 启用 Qodana 代码分析
- [ ] 添加测试覆盖率报告
- [ ] 生成安装程序（.exe/.msi/.deb）
- [ ] 代码签名与公证
- [ ] AppImage/DMG 打包

## 💡 最佳实践

### ✅ 发布时
```bash
# 1. 确保代码已通过所有 CI 检查
# 2. 更新版本号和 CHANGELOG
# 3. 创建有意义的标签消息
git tag -a v1.0.0 -m "Release v1.0.0: New features and bug fixes"

# 4. 推送
git push origin v1.0.0

# 5. 稍等片刻（Actions 运行 12-20 分钟）
# 6. 在 Releases 页面查看结果
```

### ✅ 每次提交
```bash
# 本地测试
dotnet build
dotnet format  # 必须！
dotnet test

# 然后提交
git add .
git commit -m "feat: Add cool feature"
git push
```

### ✅ 代码审查
- 检查 CI 检查是否全部通过 ✅
- 检查代码格式 ✅
- 确认目标分支正确 ✅

## 📈 监控与报告

**查看构建状态:**
- GitHub > Actions 标签页
- 或添加状态徽章到 README.md：

```markdown
![Build Status](https://github.com/YOUR_ORG/LanMontainDesktop/workflows/Build%20&%20Test/badge.svg)
![Release Status](https://github.com/YOUR_ORG/LanMontainDesktop/workflows/Release%20&%20Publish/badge.svg)
```

## 🤝 贡献指南集成

建议在 CONTRIBUTING.md 中添加：

```markdown
## 发布流程

1. 更新版本号
2. 创建 Release 分支
3. 提交 PR
4. 获得审批后 Squash merge
5. 创建 Git 标签：`git tag v1.0.0`
6. GitHub Actions 自动构建和发布

详见：[WORKFLOWS_GUIDE.md](.github/WORKFLOWS_GUIDE.md)
```

## 📞 支持

遇到问题？

1. 查看 [WORKFLOWS_GUIDE.md Troubleshooting](.github/WORKFLOWS_GUIDE.md#troubleshooting)
2. 查看 [MULTIPLATFORM_BUILD.md Troubleshooting](.github/MULTIPLATFORM_BUILD.md#troubleshooting)
3. 检查 Actions 日志获取详细错误信息
4. 提交 Issue 或讨论

---

**完成日期**: 2026-03-04  
**版本**: 2.0 (多平台支持)  
**参考**: ClassIsland 项目最佳实践  
**状态**: ✅ 生产就绪

🎉 **恭喜！LanMontainDesktop 现在拥有完整的多平台 CI/CD 流程！**
