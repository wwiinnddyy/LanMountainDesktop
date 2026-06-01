# Git 提交分析工具使用说明

## 概述

本工具用于分析当天（2026-06-01）的 Git 提交，并为每个提交生成结构化的 Markdown 分析报告。

## 文件说明

- `run_analysis.py` - 主分析脚本（推荐使用）
- `analyze_commits.py` - Python 版本分析脚本
- `analyze_commits.ps1` - PowerShell 版本分析脚本

## 使用方法

### 方法一：使用 Python 脚本（推荐）

```bash
python run_analysis.py
```

### 方法二：使用 PowerShell 脚本

```powershell
powershell -ExecutionPolicy Bypass -File analyze_commits.ps1
```

## 输出格式

每个提交会生成一个 Markdown 文件，命名格式为：`YYYYMMDD_<commit_short_hash>.md`

报告包含以下内容：
1. **基本信息** - 提交哈希、作者、时间等
2. **提交信息** - 提交说明
3. **变更统计** - 文件变更统计
4. **详细变更** - 完整的 Git diff
5. **代码审查要点** - 人工审查提示

## 输出目录

所有报告保存在：`docs/auto_commit_md/`

## 注意事项

- 确保已安装 Git 并配置好环境
- 确保当前目录是 Git 仓库
- 脚本仅分析当天（2026-06-01）的提交
