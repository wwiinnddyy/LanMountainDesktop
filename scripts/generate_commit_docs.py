#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
解析 Git HEAD 日志文件并生成 Markdown 提交分析报告
"""

import re
import os
from datetime import datetime, timezone, timedelta
from pathlib import Path

def parse_head_log(log_content):
    """
    解析 HEAD 日志内容，提取所有 commit 类型的提交

    格式：old_hash new_hash author_name <author_email> timestamp timezone\taction: message
    """
    commits = []

    # 匹配 commit 类型的行（包括 commit, commit (merge) 等）
    # 注意：message 部分可能包含中文，使用 .* 匹配
    pattern = r'^([a-f0-9]{40}) ([a-f0-9]{40}) (.+) <([^>]+)> (\d+) ([+-]\d{4})\tcommit.*?: (.+)$'

    for line in log_content.strip().split('\n'):
        line = line.strip()
        if not line:
            continue

        match = re.match(pattern, line)
        if match:
            old_hash, new_hash, author_name, author_email, timestamp, tz_offset, message = match.groups()

            # 解析时间戳
            ts = int(timestamp)
            # 解析时区偏移
            tz_hours = int(tz_offset[:3])
            tz_mins = int(tz_offset[0] + tz_offset[3:5])
            tz = timezone(timedelta(hours=tz_hours, minutes=tz_mins))
            dt = datetime.fromtimestamp(ts, tz)

            commits.append({
                'old_hash': old_hash,
                'new_hash': new_hash,
                'short_hash': new_hash[:7],
                'author_name': author_name,
                'author_email': author_email,
                'timestamp': ts,
                'datetime': dt,
                'date_str': dt.strftime('%Y-%m-%d'),
                'time_str': dt.strftime('%H:%M:%S'),
                'timezone': tz_offset,
                'message': message.strip()
            })

    return commits


def analyze_commit_type(message):
    """
    分析提交类型

    支持的类型：
    - feat: 新功能
    - fix: 修复
    - docs: 文档
    - style: 格式
    - refactor: 重构
    - perf: 性能优化
    - test: 测试
    - chore: 构建/工具
    - ci: CI/CD
    - revert: 回滚
    - change/changed: 变更
    - remove/removed: 移除
    """
    message_lower = message.lower()

    # 定义类型映射
    type_patterns = [
        (r'^feat[.:\s]', 'feat', '新功能 (Feature)', '添加新功能或特性'),
        (r'^fix[.:\s]', 'fix', '修复 (Bug Fix)', '修复问题或缺陷'),
        (r'^docs[.:\s]', 'docs', '文档 (Documentation)', '文档更新'),
        (r'^style[.:\s]', 'style', '格式 (Style)', '代码格式调整'),
        (r'^refactor[.:\s]', 'refactor', '重构 (Refactor)', '代码重构'),
        (r'^perf[.:\s]', 'perf', '性能优化 (Performance)', '性能改进'),
        (r'^test[.:\s]', 'test', '测试 (Test)', '测试相关'),
        (r'^chore[.:\s]', 'chore', '构建/工具 (Chore)', '构建流程或工具更新'),
        (r'^ci[.:\s]', 'ci', 'CI/CD', '持续集成/部署'),
        (r'^revert[.:\s]', 'revert', '回滚 (Revert)', '撤销之前的提交'),
        (r'^change[d]?[.:\s]', 'change', '变更 (Change)', '功能或行为变更'),
        (r'^remove[d]?[.:\s]', 'remove', '移除 (Remove)', '删除代码或功能'),
        (r'^update[.:\s]', 'update', '更新 (Update)', '更新依赖或配置'),
        (r'^add[.:\s]', 'add', '添加 (Add)', '添加新内容'),
        (r'^introduce[.:\s]', 'introduce', '引入 (Introduce)', '引入新模块或概念'),
        (r'^support[.:\s]', 'support', '支持 (Support)', '增加支持'),
        (r'^migrate[.:\s]', 'migrate', '迁移 (Migrate)', '迁移或升级'),
        (r'^bump[.:\s]', 'bump', '版本升级 (Bump)', '依赖版本升级'),
        (r'^enable[.:\s]', 'enable', '启用 (Enable)', '启用功能'),
        (r'^use[.:\s]', 'use', '使用 (Use)', '使用某技术或方法'),
        (r'^make[.:\s]', 'make', '调整 (Make)', '调整实现'),
        (r'^lock[.:\s]', 'lock', '锁定 (Lock)', '锁定特定行为'),
        (r'^stamp[.:\s]', 'stamp', '标记 (Stamp)', '版本标记'),
        (r'^harden[.:\s]', 'harden', '加固 (Harden)', '安全性/稳定性加固'),
        (r'^resolve[.:\s]', 'resolve', '解决 (Resolve)', '解决问题'),
        (r'^simplify[.:\s]', 'simplify', '简化 (Simplify)', '简化实现'),
        (r'^move[.:\s]', 'move', '移动 (Move)', '文件或代码移动'),
        (r'^rebuild[.:\s]', 'rebuild', '重建 (Rebuild)', '重建系统或流程'),
        (r'^refresh[.:\s]', 'refresh', '刷新 (Refresh)', '刷新内容'),
        (r'^normalize[.:\s]', 'normalize', '规范化 (Normalize)', '规范化处理'),
        (r'^redesign[.:\s]', 'redesign', '重新设计 (Redesign)', 'UI/架构重新设计'),
    ]

    for pattern, code, name, description in type_patterns:
        if re.match(pattern, message_lower):
            return {
                'code': code,
                'name': name,
                'description': description
            }

    # 版本号提交（如 0.7.9.1, 0.8.0 等）
    if re.match(r'^\d+\.\d+', message):
        return {
            'code': 'release',
            'name': '版本发布 (Release)',
            'description': '版本号更新或发布'
        }

    # 默认类型
    return {
        'code': 'other',
        'name': '其他 (Other)',
        'description': '其他类型的提交'
    }


def generate_commit_markdown(commit):
    """为单个提交生成 Markdown 文档"""

    commit_type = analyze_commit_type(commit['message'])

    # 提取提交摘要（第一行或前50个字符）
    summary = commit['message'].split('\n')[0][:100]

    # 生成分析内容
    md_content = f"""# 提交分析报告

## 1. 提交基本信息

| 属性 | 值 |
|------|-----|
| **完整哈希** | `{commit['new_hash']}` |
| **短哈希** | `{commit['short_hash']}` |
| **作者** | {commit['author_name']} <{commit['author_email']}> |
| **提交日期** | {commit['date_str']} |
| **提交时间** | {commit['time_str']} |
| **时区** | {commit['timezone']} |
| **父提交** | `{commit['old_hash']}` |

## 2. 提交信息摘要

```
{commit['message']}
```

**摘要**: {summary}

## 3. 变更类型分析

| 属性 | 值 |
|------|-----|
| **类型代码** | `{commit_type['code']}` |
| **类型名称** | {commit_type['name']} |
| **类型说明** | {commit_type['description']} |

## 4. 提交内容解读

"""

    # 根据提交类型添加解读内容
    if commit_type['code'] == 'feat':
        md_content += f"""
这是一个**新功能**提交，引入了新的功能或特性。

**可能涉及的变更**:
- 新增功能模块或组件
- 新增 API 接口
- 新增用户界面元素
- 新增配置选项

**建议关注**:
- 新功能的实现方式
- 是否包含相应的测试用例
- 文档是否同步更新
"""
    elif commit_type['code'] == 'fix':
        md_content += f"""
这是一个**问题修复**提交，修复了系统中的某个问题或缺陷。

**可能涉及的变更**:
- 修复程序错误 (Bug)
- 修复 UI 显示问题
- 修复性能问题
- 修复兼容性问题

**建议关注**:
- 修复的问题描述
- 修复方案是否合理
- 是否引入了回归风险
"""
    elif commit_type['code'] == 'docs':
        md_content += f"""
这是一个**文档更新**提交，更新了项目文档。

**可能涉及的变更**:
- README 更新
- API 文档更新
- 注释完善
- 新增文档文件

**建议关注**:
- 文档内容准确性
- 文档格式规范性
"""
    elif commit_type['code'] == 'refactor':
        md_content += f"""
这是一个**代码重构**提交，对代码进行了重构优化。

**可能涉及的变更**:
- 代码结构优化
- 提取公共方法
- 重命名变量/类
- 消除重复代码

**建议关注**:
- 重构是否保持功能一致性
- 代码可读性是否提升
"""
    elif commit_type['code'] == 'ci':
        md_content += f"""
这是一个**CI/CD**提交，更新了持续集成/部署配置。

**可能涉及的变更**:
- GitHub Actions 工作流更新
- 构建脚本调整
- 发布流程优化
- 自动化测试配置

**建议关注**:
- CI 流程是否正常执行
- 部署流程是否受影响
"""
    elif commit_type['code'] == 'release':
        md_content += f"""
这是一个**版本发布**提交，标记了版本号更新。

**版本号**: {commit['message']}

**可能涉及的变更**:
- 版本号更新
- 发布打包
- 变更日志更新
- 标签创建

**建议关注**:
- 版本号是否符合语义化版本规范
- 变更日志是否完整
"""
    elif commit_type['code'] == 'chore':
        md_content += f"""
这是一个**构建/工具**提交，更新了构建流程或开发工具。

**可能涉及的变更**:
- 依赖包更新
- 构建配置调整
- 开发工具配置
- 脚本文件更新

**建议关注**:
- 构建是否正常
- 依赖兼容性
"""
    elif commit_type['code'] == 'change':
        md_content += f"""
这是一个**功能变更**提交，修改了现有功能的行为或实现。

**可能涉及的变更**:
- 功能行为调整
- 配置项变更
- 接口变更
- 默认值修改

**建议关注**:
- 变更是否向后兼容
- 是否需要更新文档
"""
    else:
        md_content += f"""
这是一个**{commit_type['name']}**提交。

**提交内容**:
{commit['message']}

**建议**:
- 查看具体代码变更以了解详细内容
- 结合项目上下文理解提交意图
"""

    # 添加页脚
    md_content += f"""

---

*此报告由自动提交分析工具生成*
*生成时间: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}*
"""

    return md_content


def main():
    """主函数"""
    # 项目根目录
    repo_root = Path('d:/github/LanMountainDesktop')

    # 读取 HEAD 日志文件
    head_log_path = repo_root / '.git' / 'logs' / 'HEAD'
    output_dir = repo_root / 'docs' / 'auto_commit_md'

    print(f"读取日志文件: {head_log_path}")

    if not head_log_path.exists():
        print(f"错误: 日志文件不存在: {head_log_path}")
        return

    with open(head_log_path, 'r', encoding='utf-8') as f:
        log_content = f.read()

    # 解析提交记录
    commits = parse_head_log(log_content)
    print(f"解析到 {len(commits)} 个 commit 类型提交")

    # 确保输出目录存在
    output_dir.mkdir(parents=True, exist_ok=True)

    # 统计信息
    generated_count = 0
    skipped_count = 0
    error_count = 0

    # 为每个提交生成 Markdown 文件
    for commit in commits:
        # 文件名格式: YYYYMMDD_<short_hash>.md
        filename = f"{commit['date_str'].replace('-', '')}_{commit['short_hash']}.md"
        filepath = output_dir / filename

        # 如果文件已存在，跳过
        if filepath.exists():
            print(f"跳过 (已存在): {filename}")
            skipped_count += 1
            continue

        try:
            # 生成 Markdown 内容
            md_content = generate_commit_markdown(commit)

            # 写入文件
            with open(filepath, 'w', encoding='utf-8') as f:
                f.write(md_content)

            print(f"生成: {filename} - {commit['message'][:50]}")
            generated_count += 1

        except Exception as e:
            print(f"错误: 生成 {filename} 失败: {e}")
            error_count += 1

    # 打印统计信息
    print("\n" + "="*50)
    print("生成完成!")
    print(f"  - 新生成: {generated_count} 个文件")
    print(f"  - 已跳过: {skipped_count} 个文件")
    print(f"  - 错误: {error_count} 个文件")
    print(f"  - 总计: {len(commits)} 个提交")
    print(f"\n输出目录: {output_dir}")


if __name__ == '__main__':
    main()
