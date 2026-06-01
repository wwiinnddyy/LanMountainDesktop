#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
运行 Git 提交分析
"""

import os
import sys
import subprocess
from datetime import datetime, date


def run_command(cmd, shell=False):
    """运行命令并返回结果"""
    try:
        result = subprocess.run(
            cmd,
            capture_output=True,
            text=True,
            encoding='utf-8',
            errors='replace',
            shell=shell,
            timeout=30
        )
        return result.returncode, result.stdout, result.stderr
    except subprocess.TimeoutExpired:
        return -2, "", "命令超时"
    except Exception as e:
        return -1, "", str(e)


def main():
    print("=" * 60)
    print("Git 提交分析工具")
    print("=" * 60)
    
    # 创建输出目录
    output_dir = "docs/auto_commit_md"
    try:
        os.makedirs(output_dir, exist_ok=True)
        print(f"输出目录: {os.path.abspath(output_dir)}")
    except Exception as e:
        print(f"创建目录失败: {e}")
        return 1
    
    # 检查是否是 Git 仓库
    code, _, stderr = run_command(["git", "rev-parse", "--is-inside-work-tree"])
    if code != 0:
        print(f"错误: 不是 Git 仓库: {stderr}")
        return 1
    
    # 设置日期范围
    today = date.today()
    today_start = datetime.combine(today, datetime.min.time())
    today_end = datetime.now()
    
    since_str = today_start.strftime("%Y-%m-%d %H:%M:%S")
    until_str = today_end.strftime("%Y-%m-%d %H:%M:%S")
    
    print(f"\n分析日期范围: {since_str} 到 {until_str}")
    
    # 获取提交列表
    print("\n正在获取提交列表...")
    cmd = [
        "git", "log",
        f"--since={since_str}",
        f"--until={until_str}",
        "--pretty=format:%H|%an|%ai|%s",
        "--no-merges"
    ]
    
    code, stdout, stderr = run_command(cmd)
    if code != 0:
        print(f"获取提交失败: {stderr}")
        return 1
    
    if not stdout.strip():
        print("当天没有新的提交。")
        return 0
    
    commits = [line.strip() for line in stdout.strip().split('\n') if line.strip()]
    print(f"找到 {len(commits)} 个提交\n")
    
    # 处理每个提交
    for i, line in enumerate(commits, 1):
        parts = line.split('|', 3)
        if len(parts) < 4:
            continue
        
        hash_full = parts[0]
        author = parts[1]
        commit_date = parts[2]
        message = parts[3]
        
        short_hash = hash_full[:7]
        
        try:
            date_obj = datetime.fromisoformat(commit_date.replace('Z', '+00:00'))
            date_str = date_obj.strftime("%Y%m%d")
        except:
            date_str = today.strftime("%Y%m%d")
        
        output_file = os.path.join(output_dir, f"{date_str}_{short_hash}.md")
        
        print(f"[{i}/{len(commits)}] 处理: {short_hash}")
        print(f"  作者: {author}")
        print(f"  信息: {message[:60]}{'...' if len(message) > 60 else ''}")
        
        # 获取统计信息
        cmd_stat = ["git", "show", "--stat", "--stat-width=120", "--stat-name-width=80", hash_full]
        _, stat_out, _ = run_command(cmd_stat)
        
        # 获取完整 diff
        cmd_diff = ["git", "show", hash_full]
        _, diff_out, _ = run_command(cmd_diff)
        
        # 构建 Markdown
        markdown = f"""# Git 提交分析报告

## 基本信息

| 项目 | 内容 |
|------|------|
| **提交哈希** | {hash_full} |
| **短哈希** | {short_hash} |
| **作者** | {author} |
| **提交时间** | {commit_date} |

## 提交信息

{message}

## 变更统计

```
{stat_out}
```

## 详细变更

```diff
{diff_out}
```

## 代码审查要点

> 本部分由系统自动生成，需要人工审查确认。

- 请检查代码变更是否符合项目规范
- 请检查是否有潜在的 bug 或安全问题
- 请检查测试是否覆盖了新代码
- 请检查文档是否需要更新

---
*报告生成时间: {datetime.now().strftime("%Y-%m-%d %H:%M:%S")}*
"""
        
        # 保存文件
        try:
            with open(output_file, 'w', encoding='utf-8') as f:
                f.write(markdown)
            print(f"  已保存: {os.path.basename(output_file)}")
        except Exception as e:
            print(f"  保存失败: {e}")
        
        print()
    
    print("=" * 60)
    print(f"完成！共生成 {len(commits)} 份报告")
    print(f"报告位置: {os.path.abspath(output_dir)}")
    print("=" * 60)
    
    return 0


if __name__ == "__main__":
    sys.exit(main())
