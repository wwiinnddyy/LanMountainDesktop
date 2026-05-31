#!/usr/bin/env python3
import subprocess
import os
import re
from datetime import datetime
from pathlib import Path
import sys


def run_git_command(cmd, cwd=None, timeout=5):
    try:
        result = subprocess.run(
            cmd, 
            shell=True, 
            capture_output=True, 
            text=True, 
            cwd=cwd,
            timeout=timeout
        )
        if result.returncode != 0:
            print(f"Git command failed: {cmd}")
            print(f"Error: {result.stderr}")
            return None
        return result.stdout
    except subprocess.TimeoutExpired:
        print(f"Git command timed out: {cmd}")
        return None


def get_commits_since(since_date, until_date, cwd=None):
    cmd = f'git log --since="{since_date}" --until="{until_date}" --pretty=format:"%H|%an|%ae|%ai|%s" --date=iso'
    output = run_git_command(cmd, cwd)
    if not output:
        return []
    
    commits = []
    for line in output.strip().split('\n'):
        if line and '|' in line:
            parts = line.split('|', 4)
            if len(parts) == 5:
                commits.append({
                    'hash': parts[0],
                    'author': parts[1],
                    'email': parts[2],
                    'date': parts[3],
                    'message': parts[4]
                })
    return commits


def get_commit_diff(commit_hash, cwd=None):
    cmd = f'git show {commit_hash} --format="" -- patches'
    return run_git_command(cmd, cwd)


def get_commit_stat(commit_hash, cwd=None):
    cmd = f'git show {commit_hash} --stat'
    return run_git_command(cmd, cwd)


def analyze_diff(diff_text):
    file_changes = []
    current_file = None
    changes = {'insertions': 0, 'deletions': 0, 'files': 0}
    
    if not diff_text:
        return [], changes
    
    lines = diff_text.split('\n')
    for line in lines:
        if line.startswith('diff --git'):
            if current_file:
                file_changes.append(current_file)
            filename = line.split(' ')[2][2:]
            current_file = {'name': filename, 'insertions': 0, 'deletions': 0, 'hunks': []}
            changes['files'] += 1
        elif line.startswith('+') and not line.startswith('+++'):
            if current_file:
                current_file['insertions'] += 1
                changes['insertions'] += 1
        elif line.startswith('-') and not line.startswith('---'):
            if current_file:
                current_file['deletions'] += 1
                changes['deletions'] += 1
    
    if current_file:
        file_changes.append(current_file)
    
    return file_changes, changes


def generate_markdown_report(commit, diff_text, stat_text, output_dir):
    file_changes, summary = analyze_diff(diff_text if diff_text else "")
    
    short_hash = commit['hash'][:7]
    date_str = datetime.now().strftime('%Y%m%d')
    
    filename = f"{date_str}_{short_hash}.md"
    filepath = Path(output_dir) / filename
    
    markdown = f"""# Git 提交分析报告

## 基本信息

| 项目 | 内容 |
|------|------|
| 提交哈希 | `{commit['hash']}` |
| 短哈希 | `{short_hash}` |
| 作者 | {commit['author']} <{commit['email']}> |
| 提交时间 | {commit['date']} |

## 提交信息

{commit['message']}

## 变更统计

"""
    
    if stat_text:
        markdown += "```\n"
        markdown += stat_text
        markdown += "\n```\n\n"
    else:
        markdown += f"- **变更文件数**: {summary['files']}\n"
        markdown += f"- **新增行数**: +{summary['insertions']}\n"
        markdown += f"- **删除行数**: -{summary['deletions']}\n\n"

    markdown += "## 详细变更\n\n"

    if file_changes:
        markdown += "### 文件变更列表\n\n"
        for fc in sorted(file_changes, key=lambda x: x['name']):
            markdown += f"- `{fc['name']}` - 新增: +{fc['insertions']} 行, 删除: -{fc['deletions']} 行\n"
    else:
        markdown += "*无详细变更信息*\n"

    markdown += "\n## 完整 Diff\n\n"
    if diff_text:
        markdown += "```diff\n"
        markdown += diff_text
        markdown += "\n```\n"
    else:
        markdown += "*无法获取详细的 diff 信息*\n"

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(markdown)
    
    print(f"Generated: {filepath}")
    return filepath


def main():
    repo_dir = Path(__file__).parent.parent
    output_dir = repo_dir / 'docs' / 'auto_commit_md'
    output_dir.mkdir(parents=True, exist_ok=True)
    
    today = datetime.now()
    since_date = today.strftime('%Y-%m-%d 00:00:00')
    until_date = today.strftime('%Y-%m-%d 23:59:59')
    
    print(f"Fetching commits from {since_date} to {until_date}...")
    commits = get_commits_since(since_date, until_date, str(repo_dir))
    
    if not commits:
        print("No commits found for today.")
        print("\nLet's check the latest commits instead...")
        cmd = 'git log -3 --pretty=format:"%H|%an|%ae|%ai|%s" --date=iso'
        output = run_git_command(cmd, str(repo_dir))
        if output:
            commits = []
            for line in output.strip().split('\n'):
                if line and '|' in line:
                    parts = line.split('|', 4)
                    if len(parts) == 5:
                        commits.append({
                            'hash': parts[0],
                            'author': parts[1],
                            'email': parts[2],
                            'date': parts[3],
                            'message': parts[4]
                        })
    
    if commits:
        print(f"\nFound {len(commits)} commit(s) to analyze:\n")
        for commit in commits:
            print(f"  - {commit['hash'][:7]}: {commit['message']}")
        
        print("\nGenerating reports...\n")
        for commit in commits:
            diff = get_commit_diff(commit['hash'], str(repo_dir))
            stat = get_commit_stat(commit['hash'], str(repo_dir))
            generate_markdown_report(commit, diff, stat, str(output_dir))
        
        print(f"\nDone! Reports saved to {output_dir}")
    else:
        print("No commits found.")


if __name__ == '__main__':
    main()
