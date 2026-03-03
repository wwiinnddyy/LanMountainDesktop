# LanMontainDesktop CI/CD 评估与实现方案

## 📊 项目分析

### 项目特征
| 特性 | 详情 |
|------|------|
| **框架** | Avalonia 11 (.NET 10) |
| **主平台** | Windows (x64/x86) |
| **项目数量** | 2个主项目 + 1个测试项目 |
| **后端** | ASP.NET Core Web API (RecommendationBackend) |
| **特殊能力** | 视频壁纸支持 (LibVLC) |
| **发布方式** | PowerShell脚本 (scripts/package.ps1) |

### 与ClassIsland的对比

| 方面 | LanMontainDesktop | ClassIsland |
|------|-------------------|-------------|
| 平台覆盖 | 🔷 主要Windows | 🟢 多平台(Win/Linux/macOS) |
| 构建复杂度 | 🔷 中等 | 🔴 很高(专业级) |
| 发布周期 | 🔷 按需 | 🔴 频繁release |
| CI配置 | 🔴 无 | 🟢 完整 |
| 代码质量检查 | 🔴 无 | 🟢 Qodana集成 |

## 🎯 推荐方案（渐进式）

### **阶段1：基础CI（已完成 ✅）**
实现核心构建验证，支持每次commit自动检查

- ✅ **Build Workflow** - 每次push/PR自动构建
  - Debug + Release 配置
  - 两个项目同时构建
  - 保存build artifacts
  
- ✅ **Code Quality** - 代码检查
  - dotnet format检查
  - 编译警告/错误检测
  - Qodana集成（可选）

- ✅ **PR Template** - 统一pull request规范
  - 变更类型分类
  - 测试清单
  - 截图/视频支持

- ✅ **Issue Templates** - GitHub问题规范
  - 🐛 Bug Report
  - ✨ Feature Request  
  - ⚙️ Configuration Issues

### **阶段2：多平台发布自动化（已完成 ✅）**
完整的跨平台构建与自动化发布

- ✅ **多平台Release Workflow** - Git tag触发全平台构建
  - 🪟 **Windows**: x64 + x86 自包含可执行文件
  - 🐧 **Linux**: x64 tar.gz包（支持自定义架构）
  - 🍎 **macOS**: x64 + arm64 (Apple Silicon) tar.gz包
  - 自动版本号更新
  - 统一artifact命名
  - GitHub Release自动创建详细说明

- ✅ **构建脚本支持**
  - PowerShell脚本处理Windows特定依赖
  - Bash脚本统一处理Linux/macOS
  - .gitattributes确保行尾兼容

- ✅ **灵活的手动触发**
  - 支持选择性构建（仅Windows/Linux/macOS）
  - 预发布标记支持
  - 版本号手动指定

- ✅ **Issue Management** - 自动化问题管理
  - 自动标记过期问题
  - 自动关闭无活动PR
  - 可自定义时间阈值

### **阶段3：建议（未来优化）**
- 🔲 **安装程序生成** - MSI/EXE (Windows), .deb (Linux), DMG (macOS)
- 🔲 **代码签名** - macOS notarization, Windows签名
- 🔲 **AppImage/Snap** - Linux现代打包格式
- 🔲 **Docker镜像** - 后端容器化
- 🔲 **测试覆盖报告** - Codecov集成
- 🔲 **性能基准测试** - PR性能对比
- 🔲 **自动更新检查** - 依赖版本检查

## 🔧 创建的GitHub工作流与脚本

### 工作流文件（4个）

1. **[Build & Test](/.github/workflows/build.yml)** - 核心构建验证
   - 在每个push和PR时自动运行
   - Debug + Release 两种配置
   - 支持两个项目同时构建
   - 保存build artifacts用于检查

2. **[Code Quality](/.github/workflows/code-quality.yml)** - 代码质量检查
   - 代码格式检查(`dotnet format`)
   - 编译错误/警告检测
   - 预留Qodana集成位置（可选）

3. **[Release & Publish (多平台)](/.github/workflows/release.yml)** ⭐ 升级版
   - **Windows**: x64 + x86
   - **Linux**: x64
   - **macOS**: x64 + arm64 (Apple Silicon)
   - 基于Git标签自动触发
   - 支持手动选择构建平台
   - 自动创建GitHub Release

4. **[Issue Management](/.github/workflows/issue-management.yml)** - 自动化问题管理
   - 每日标记过期问题
   - 自动关闭无活动PR
   - 可自定义时间阈值

### 构建脚本

| 脚本 | 平台 | 功能 |
|------|------|------|
| `LanMontainDesktop\scripts\package.ps1` | Windows | 现有PowerShell打包脚本 |
| `scripts\build.sh` | Linux/macOS | 新增跨平台构建脚本 |

### 配置文件

| 文件 | 用途 |
|------|------|
| `.gitattributes` | 行尾处理，确保跨平台兼容 |
| `.github/CODEOWNERS` | 代码所有权定义 |
| `.github/pull_request_template.md` | PR提交规范 |
| `.github/ISSUE_TEMPLATE/*.md` | Issue模板 |
| `.github/WORKFLOWS_GUIDE.md` | 工作流使用指南 |
| `.github/MULTIPLATFORM_BUILD.md` | 多平台构建详细指南 ⭐ |

## 🚀 快速开始

### 1. 验证工作流正常运行
```bash
# Push到main分支
git add .github/
git commit -m "feat: Add GitHub CI/CD workflows"
git push origin main

# 检查GitHub Actions是否自动运行
# https://github.com/YOUR_ORG/LanMontainDesktop/actions
```

### 2. 创建第一个Release（可选）
```bash
# 本地修改版本号（如果需要）
# 然后创建tag
git tag v1.0.0
git push origin v1.0.0

# GitHub Actions会自动：
# 1. 更新版本号
# 2. 构建Release版本
# 3. 创建可执行文件
# 4. 生成GitHub Release
```

### 3. 配置分支保护规则（推荐）
在 GitHub > Settings > Branches > Branch Protection Rules：
- 要求CI检查通过再merge
- 要求PR审查
- 要求代码最新

## ⚙️ 配置选项

### 可选：启用Qodana代码分析

1. 访问 https://qodana.cloud
2. 注册并创建organization token
3. 在GitHub > Settings > Secrets > Actions 中添加：
   - `QODANA_TOKEN`
   - `QODANA_ENDPOINT=https://qodana.cloud`
4. 在 `.github/workflows/code-quality.yml` 中取消Qodana步骤注释

### 自定义构建参数

编辑 `.github/workflows/` 中的yml文件：
- `DOTNET_VERSION` - 改变.NET版本
- 分支列表 - 改变监控的分支
- 矩阵配置 - 添加更多构建配置

## 📊 工作流对比与选择

### Build日语言表
| 工作流 | 触发条件 | 运行时间 | 成本 |
|--------|---------|--------|------|
| Build | 每个push/PR | ~2-3分钟 | 低 |
| Code Quality | PR/push | ~2-3分钟 | 低 |
| Release | Tag push | ~5-10分钟 | 中 |
| Issue Mgmt | 每天1次 | ~1分钟 | 很低 |

### 预计月度GitHub Actions使用
- **小项目**(<5贡献者): 100-200 runner hours
- **中等项目**(5-20贡献者): 300-500 runner hours  
- **大项目**（>20贡献者): 800+ runner hours

> 💡 **免费额度**: 2000 runner hours/月 (对大多数开源项目足够)

## 🔍 故障排查

### 工作流不运行？
- [ ] 检查 `.github/workflows/*.yml` 语法（YAML缩进）
- [ ] 确认分支名称配置正确
- [ ] 查看Actions标签查看错误日志
- [ ] 检查分支保护规则是否冲突

### 构建失败？
```bash
# 本地重现CI环境
dotnet clean
dotnet restore
dotnet build LanMontainDesktop/LanMontainDesktop.csproj -c Release
```

### PR检查无法通过？
1. 本地运行 `dotnet format`
2. 确保没有编译警告
3. 所有测试通过

## 📚 参考资源

- [.github/WORKFLOWS_GUIDE.md](.github/WORKFLOWS_GUIDE.md) - 详细使用指南
- [GitHub Actions文档](https://docs.github.com/actions)
- [ClassIsland CI/CD参考](https://github.com/ClassIsland/ClassIsland/.github/workflows)
- [Avalonia发布指南](https://docs.avaloniaui.net/docs/deployment)

## ✅ 完成清单

- [x] 构建工作流
- [x] 代码质量检查
- [x] 发布自动化
- [x] Issue管理
- [x] PR模板
- [x] Issue模板
- [x] CODEOWNERS定义
- [x] 完整文档
- [ ] 调试并测试所有工作流
- [ ] 配置Qodana（可选）
- [ ] 配置分支保护规则（推荐）

## 🎓 下一步建议

1. **立即做**
   - 推送所有文件到GitHub
   - 验证工作流运行成功
   - 配置分支保护规则

2. **本周内**
   - 在贡献指南中记录工作流
   - 团队成员熟悉PR流程
   - 测试第一个Release构建

3. **后续优化**
   - 基于实际运行数据调整参数
   - 添加额外的检查（危险代码扫描等）
   - 集成代码覆盖率报告
   - 准备多平台构建（需要）

---

**创建时间**: 2026-03-04  
**参考项目**: ClassIsland  
**目标**: 提高代码质量和发布效率 🚀
