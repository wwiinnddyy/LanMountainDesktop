#!/usr/bin/env python3
"""
分析当天 Git 提交并生成 Markdown 报告的脚本
"""

import os
import subprocess
import sys
from datetime import datetime
import re


def run_command(cmd, cwd=None):
    """运行命令并返回输出"""
    try:
        result = subprocess.run(
            cmd,
            shell=True,
            capture_output=True,
            text=True,
            cwd=cwd,
            encoding='utf-8',
            errors='replace'
        )
        return result.stdout, result.stderr, result.returncode
    except Exception as e:
        return "", str(e), 1


def get_today_commits(repo_path):
    """获取当天的所有提交"""
    today_start = datetime.now().strftime('%Y-%m-%dT00:00:00')
    today_end = datetime.now().strftime('%Y-%m-%dT23:59:59')
    
    cmd = f'git log --since="{today_start}" --until="{today_end}" --pretty=format:"%H|%an|%ae|%ad|%s" --date=iso'
    stdout, stderr, code = run_command(cmd, cwd=repo_path)
    
    if code != 0:
        print(f"Error getting commits: {stderr}")
        return []
    
    commits = []
    for line in stdout.strip().split('\n'):
        if not line:
            continue
        parts = line.split('|', 4)
        if len(parts) == 5:
            commits.append({
                'hash': parts[0],
                'author_name': parts[1],
                'author_email': parts[2],
                'date': parts[3],
                'message': parts[4]
            })
    return commits


def get_commit_diff(repo_path, commit_hash):
    """获取提交的详细 diff"""
    cmd = f'git show --stat {commit_hash}'
    stdout, _, _ = run_command(cmd, cwd=repo_path)
    return stdout


def get_commit_details(repo_path, commit_hash):
    """获取提交的详细信息"""
    cmd = f'git show {commit_hash}'
    stdout, _, _ = run_command(cmd, cwd=repo_path)
    return stdout


def parse_diff_stats(diff_stat):
    """解析 diff --stat 的输出"""
    files_changed = []
    total_insertions = 0
    total_deletions = 0
    
    lines = diff_stat.strip().split('\n')
    for line in lines:
        match = re.search(r'(\d+) insertion', line)
        if match:
            total_insertions += int(match.group(1))
        match = re.search(r'(\d+) deletion', line)
        if match:
            total_deletions += int(match.group(1))
        
        file_match = re.match(r'^\s*(.*?)\s*\|\s*(\d+)', line)
        if file_match:
            files_changed.append({
                'file': file_match.group(1),
                'lines': int(file_match.group(2))
            })
    
    return {
        'files': files_changed,
        'insertions': total_insertions,
        'deletions': total_deletions
    }


def generate_markdown_report(commit, diff_stat, diff_details):
    """生成 Markdown 报告"""
    short_hash = commit['hash'][:7]
    date_str = commit['date'].split(' ')[0].replace('-', '')
    
    report = f"""# 提交分析报告 - {short_hash}

## 基本信息

| 项目 | 内容 |
|------|------|
| 提交哈希 | `{commit['hash']}` |
| 作者 | {commit['author_name']} ({commit['author_email']}) |
| 提交时间 | {commit['date']} |

## 提交信息

{commit['message']}

## 变更统计

| 指标 | 数值 |
|------|------|
| 修改文件数 | {len(diff_stat['files'])} |
| 新增行数 | +{diff_stat['insertions']} |
| 删除行数 | -{diff_stat['deletions']} |

## 修改文件列表

"""
    
    for file_info in diff_stat['files']:
        report += f"- {file_info['file']} ({file_info['lines']} 行)\n"
    
    report += f"""
## 详细变更

```diff
{diff_details}
```

## 代码审查要点

> 此部分为自动生成，建议人工审查确认

- [ ] 检查是否有潜在的 bug
- [ ] 确认代码符合项目规范
- [ ] 验证是否有需要测试的新功能
- [ ] 检查是否有安全问题

---

*报告生成时间: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}*
"""
    
    return report, f"{date_str}_{short_hash}.md"


def main():
    repo_path = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    output_dir = os.path.join(repo_path, 'docs', 'auto_commit_md')
    
    # 创建输出目录
    os.makedirs(output_dir, exist_ok=True)
    
    print(f"分析仓库: {repo_path}")
    print(f"输出目录: {output_dir}")
    print()
    
    # 获取当天的提交
    commits = get_today_commits(repo_path)
    
    if not commits:
        print("今天没有新提交。")
        return 0
    
    print(f"找到 {len(commits)} 个今天的提交。")
    print()
    
    for commit in commits:
        print(f"处理提交: {commit['hash'][:7]} - {commit['message']}")
        
        # 获取提交详情
        diff_stat = get_commit_diff(repo_path, commit['hash'])
        diff_details = get_commit_details(repo_path, commit['hash'])
        
        # 解析统计信息
        stats = parse_diff_stats(diff_stat)
        
        # 生成报告
        report_content, filename = generate_markdown_report(commit, stats, diff_details)
        
        # 保存文件
        output_path = os.path.join(output_dir, filename)
        with open(output_path, 'w', encoding='utf-8') as f:
            f.write(report_content)
        
        print(f"  报告已保存: {output_path}")
    
    print()
    print("完成！")
    return 0


if __name__ == '__main__':
    sys.exit(main())
