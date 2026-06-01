#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
分析当天的 Git 提交并生成 Markdown 报告
"""

import os
import subprocess
from datetime import datetime, date


def run_git_command(cmd):
    """运行 git 命令并返回输出"""
    try:
        result = subprocess.run(
            cmd,
            capture_output=True,
            text=True,
            encoding='utf-8',
            errors='replace'
        )
        return result.returncode, result.stdout, result.stderr
    except Exception as e:
        return -1, "", str(e)


def main():
    # 设置日期范围
    today_start = datetime.combine(date.today(), datetime.min.time())
    today_end = datetime.now()
    
    output_dir = "docs/auto_commit_md"
    
    # 创建输出目录
    if not os.path.exists(output_dir):
        os.makedirs(output_dir, exist_ok=True)
        print(f"创建目录: {output_dir}")
    
    # 获取当天的所有提交
    since_str = today_start.strftime("%Y-%m-%d %H:%M:%S")
    until_str = today_end.strftime("%Y-%m-%d %H:%M:%S")
    
    print(f"正在获取 {since_str} 到 {until_str} 之间的提交...")
    
    cmd = [
        "git", "log",
        f"--since={since_str}",
        f"--until={until_str}",
        "--pretty=format:%H|%an|%ai|%s"
    ]
    
    code, stdout, stderr = run_git_command(cmd)
    
    if code != 0:
        print(f"错误: 获取提交失败: {stderr}")
        return
    
    if not stdout.strip():
        print("当天没有新的提交。")
        return
    
    commits = stdout.strip().split('\n')
    print(f"找到 {len(commits)} 个提交")
    
    for line in commits:
        line = line.strip()
        if not line:
            continue
        
        parts = line.split('|', 3)
        if len(parts) < 4:
            continue
        
        hash_full = parts[0]
        author = parts[1]
        commit_date = parts[2]
        message = parts[3]
        
        short_hash = hash_full[:7]
        date_obj = datetime.fromisoformat(commit_date.replace('Z', '+00:00'))
        date_str = date_obj.strftime("%Y%m%d")
        output_file = os.path.join(output_dir, f"{date_str}_{short_hash}.md")
        
        print(f"处理提交: {short_hash} - {message}")
        
        # 获取统计信息
        cmd_stat = ["git", "show", "--stat", "--stat-width=120", "--stat-name-width=80", hash_full]
        _, stat_out, _ = run_git_command(cmd_stat)
        
        # 获取完整 diff
        cmd_diff = ["git", "show", hash_full]
        _, diff_out, _ = run_git_command(cmd_diff)
        
        # 构建 Markdown 内容
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
        with open(output_file, 'w', encoding='utf-8') as f:
            f.write(markdown)
        
        print(f"已保存: {output_file}")
    
    print(f"\n完成！共生成 {len(commits)} 份报告。")


if __name__ == "__main__":
    main()
