#!/usr/bin/env python3
"""
Git Commit 深度分析工具
用于解析 Git 对象文件并生成详细的代码变更分析报告
"""

import zlib
import os
import re
import json
from datetime import datetime
from pathlib import Path
from typing import Dict, List, Tuple, Optional, Any
from dataclasses import dataclass, field
from collections import defaultdict


@dataclass
class GitObject:
    """Git 对象基类"""
    obj_type: str
    content: bytes
    raw_data: bytes


@dataclass
class CommitInfo:
    """提交信息"""
    hash: str
    parent: Optional[str]
    tree: str
    author: str
    email: str
    timestamp: int
    timezone: str
    message: str
    changes: List[Dict] = field(default_factory=list)
    stats: Dict = field(default_factory=dict)


@dataclass
class FileChange:
    """文件变更信息"""
    path: str
    change_type: str  # added, modified, deleted, renamed
    old_path: Optional[str] = None
    additions: int = 0
    deletions: int = 0
    diff_content: str = ""


class GitObjectParser:
    """Git 对象解析器"""

    def __init__(self, repo_path: str):
        self.repo_path = Path(repo_path)
        self.objects_path = self.repo_path / ".git" / "objects"
        self.commit_cache: Dict[str, CommitInfo] = {}
        self.tree_cache: Dict[str, Dict[str, str]] = {}

    def read_object(self, obj_hash: str) -> Optional[GitObject]:
        """读取并解压缩 Git 对象"""
        if len(obj_hash) < 4:
            return None

        obj_dir = obj_hash[:2]
        obj_file = obj_hash[2:]
        obj_path = self.objects_path / obj_dir / obj_file

        if not obj_path.exists():
            return None

        try:
            with open(obj_path, 'rb') as f:
                compressed_data = f.read()

            # 解压缩 zlib
            decompressed = zlib.decompress(compressed_data)

            # 解析对象头和内容
            null_idx = decompressed.index(b'\x00')
            header = decompressed[:null_idx].decode('utf-8')
            content = decompressed[null_idx + 1:]

            obj_type = header.split()[0]

            return GitObject(obj_type=obj_type, content=content, raw_data=decompressed)
        except Exception as e:
            print(f"Error reading object {obj_hash}: {e}")
            return None

    def parse_commit(self, commit_hash: str) -> Optional[CommitInfo]:
        """解析 commit 对象"""
        if commit_hash in self.commit_cache:
            return self.commit_cache[commit_hash]

        obj = self.read_object(commit_hash)
        if not obj or obj.obj_type != 'commit':
            return None

        try:
            content = obj.content.decode('utf-8', errors='replace')
            lines = content.split('\n')

            parent = None
            tree = None
            author = None
            email = None
            timestamp = None
            timezone = None
            message_lines = []

            in_message = False
            for line in lines:
                if in_message:
                    message_lines.append(line)
                elif line.startswith('tree '):
                    tree = line[5:].strip()
                elif line.startswith('parent '):
                    parent = line[7:].strip()
                elif line.startswith('author '):
                    # author name <email> timestamp timezone
                    match = re.match(r'author (.+) <(.+)> (\d+) ([+-]\d+)', line)
                    if match:
                        author = match.group(1)
                        email = match.group(2)
                        timestamp = int(match.group(3))
                        timezone = match.group(4)
                elif line == '':
                    in_message = True

            message = '\n'.join(message_lines).strip()

            commit_info = CommitInfo(
                hash=commit_hash,
                parent=parent,
                tree=tree,
                author=author or "Unknown",
                email=email or "",
                timestamp=timestamp or 0,
                timezone=timezone or "",
                message=message
            )

            self.commit_cache[commit_hash] = commit_info
            return commit_info

        except Exception as e:
            print(f"Error parsing commit {commit_hash}: {e}")
            return None

    def parse_tree(self, tree_hash: str) -> Dict[str, str]:
        """解析 tree 对象，返回文件路径到 blob hash 的映射"""
        if tree_hash in self.tree_cache:
            return self.tree_cache[tree_hash]

        obj = self.read_object(tree_hash)
        if not obj or obj.obj_type != 'tree':
            return {}

        entries = {}
        content = obj.content
        idx = 0

        while idx < len(content):
            # 查找空格分隔符
            space_idx = content.find(b' ', idx)
            if space_idx == -1:
                break

            mode = content[idx:space_idx].decode('utf-8')

            # 查找 null 分隔符
            null_idx = content.find(b'\x00', space_idx)
            if null_idx == -1:
                break

            name = content[space_idx + 1:null_idx].decode('utf-8', errors='replace')

            # 读取 20 字节的 SHA
            sha_start = null_idx + 1
            sha_end = sha_start + 20
            if sha_end > len(content):
                break

            sha = content[sha_start:sha_end].hex()
            entries[name] = sha

            idx = sha_end

        self.tree_cache[tree_hash] = entries
        return entries

    def get_blob_content(self, blob_hash: str) -> Optional[str]:
        """获取 blob 对象的内容"""
        obj = self.read_object(blob_hash)
        if not obj or obj.obj_type != 'blob':
            return None
        try:
            return obj.content.decode('utf-8', errors='replace')
        except:
            return None

    def compare_trees(self, old_tree: str, new_tree: str) -> List[FileChange]:
        """比较两个 tree 对象，返回文件变更列表"""
        old_files = self.parse_tree(old_tree) if old_tree else {}
        new_files = self.parse_tree(new_tree) if new_tree else {}

        changes = []

        # 查找新增和修改的文件
        for path, new_hash in new_files.items():
            if path not in old_files:
                changes.append(FileChange(path=path, change_type='added'))
            elif old_files[path] != new_hash:
                changes.append(FileChange(path=path, change_type='modified'))

        # 查找删除的文件
        for path in old_files:
            if path not in new_files:
                changes.append(FileChange(path=path, change_type='deleted'))

        return changes

    def get_commit_changes(self, commit_hash: str) -> Tuple[List[FileChange], Dict]:
        """获取提交的所有变更"""
        commit = self.parse_commit(commit_hash)
        if not commit:
            return [], {}

        # 获取当前提交的 tree
        current_tree = self.parse_tree(commit.tree)

        # 获取父提交的 tree
        parent_tree = {}
        if commit.parent:
            parent_commit = self.parse_commit(commit.parent)
            if parent_commit:
                parent_tree = self.parse_tree(parent_commit.tree)

        changes = []
        stats = {'added': 0, 'modified': 0, 'deleted': 0, 'total_additions': 0, 'total_deletions': 0}

        # 比较 tree
        all_paths = set(current_tree.keys()) | set(parent_tree.keys())

        for path in all_paths:
            if path in current_tree and path not in parent_tree:
                # 新增文件
                changes.append(FileChange(path=path, change_type='added'))
                stats['added'] += 1
            elif path not in current_tree and path in parent_tree:
                # 删除文件
                changes.append(FileChange(path=path, change_type='deleted'))
                stats['deleted'] += 1
            elif current_tree.get(path) != parent_tree.get(path):
                # 修改文件
                changes.append(FileChange(path=path, change_type='modified'))
                stats['modified'] += 1

        return changes, stats


class CommitAnalyzer:
    """提交分析器"""

    def __init__(self, repo_path: str):
        self.parser = GitObjectParser(repo_path)
        self.repo_path = Path(repo_path)

    def analyze_commit(self, commit_hash: str) -> Dict[str, Any]:
        """分析单个提交"""
        commit = self.parser.parse_commit(commit_hash)
        if not commit:
            return {}

        changes, stats = self.parser.get_commit_changes(commit_hash)

        # 分析文件类型
        file_types = defaultdict(int)
        for change in changes:
            ext = Path(change.path).suffix or 'no_extension'
            file_types[ext] += 1

        # 分析变更的重要性
        importance = self._assess_importance(commit.message, changes, stats)

        # 提取关键代码片段
        key_snippets = self._extract_key_snippets(changes)

        return {
            'commit_hash': commit_hash,
            'message': commit.message,
            'author': commit.author,
            'email': commit.email,
            'timestamp': commit.timestamp,
            'date': datetime.fromtimestamp(commit.timestamp).strftime('%Y-%m-%d %H:%M:%S'),
            'parent': commit.parent,
            'changes': [
                {
                    'path': c.path,
                    'type': c.change_type,
                    'additions': c.additions,
                    'deletions': c.deletions
                }
                for c in changes
            ],
            'stats': stats,
            'file_types': dict(file_types),
            'importance': importance,
            'key_snippets': key_snippets,
            'impact_analysis': self._analyze_impact(changes, commit.message),
            'review_points': self._generate_review_points(changes, commit.message)
        }

    def _assess_importance(self, message: str, changes: List[FileChange], stats: Dict) -> str:
        """评估提交的重要性"""
        message_lower = message.lower()

        # 检查关键关键词
        critical_keywords = ['fix', 'bug', 'security', 'crash', 'memory leak', 'deadlock']
        feature_keywords = ['feat', 'feature', 'add', 'implement', 'new']
        refactor_keywords = ['refactor', 'restructure', 'cleanup', 'optimize']

        if any(kw in message_lower for kw in critical_keywords):
            return 'critical'
        elif any(kw in message_lower for kw in feature_keywords):
            return 'feature'
        elif stats.get('added', 0) + stats.get('modified', 0) + stats.get('deleted', 0) > 20:
            return 'major'
        elif any(kw in message_lower for kw in refactor_keywords):
            return 'refactor'
        else:
            return 'minor'

    def _extract_key_snippets(self, changes: List[FileChange]) -> List[Dict]:
        """提取关键代码片段"""
        snippets = []

        for change in changes[:10]:  # 限制分析的文件数量
            if change.change_type == 'deleted':
                continue

            # 尝试读取文件内容
            file_path = self.repo_path / change.path
            if file_path.exists() and file_path.is_file():
                try:
                    with open(file_path, 'r', encoding='utf-8', errors='replace') as f:
                        content = f.read()

                    # 提取文件的基本信息
                    lines = content.split('\n')
                    snippet = {
                        'file': change.path,
                        'type': change.change_type,
                        'lines_count': len(lines),
                        'preview': '\n'.join(lines[:30]) if len(lines) > 30 else content
                    }
                    snippets.append(snippet)
                except Exception:
                    pass

        return snippets

    def _analyze_impact(self, changes: List[FileChange], message: str) -> List[str]:
        """分析变更对项目的影响"""
        impacts = []

        # 分析受影响的模块
        affected_modules = set()
        for change in changes:
            parts = change.path.split('/')
            if len(parts) > 1:
                affected_modules.add(parts[0])

        if affected_modules:
            impacts.append(f"受影响的模块: {', '.join(sorted(affected_modules))}")

        # 分析文件类型影响
        file_types = defaultdict(int)
        for change in changes:
            ext = Path(change.path).suffix
            if ext:
                file_types[ext] += 1

        if '.cs' in file_types:
            impacts.append(f"涉及 {file_types['.cs']} 个 C# 文件变更")
        if '.axaml' in file_types or '.xaml' in file_types:
            impacts.append("涉及 UI/XAML 文件变更")
        if '.md' in file_types:
            impacts.append("涉及文档更新")

        # 根据提交消息分析
        message_lower = message.lower()
        if 'fix' in message_lower:
            impacts.append("这是一个修复性提交，可能解决现有问题")
        if 'feat' in message_lower or 'feature' in message_lower:
            impacts.append("这是一个功能新增提交，扩展了项目能力")
        if 'refactor' in message_lower:
            impacts.append("这是一个重构提交，改善了代码结构")
        if 'test' in message_lower:
            impacts.append("涉及测试相关变更")

        return impacts

    def _generate_review_points(self, changes: List[FileChange], message: str) -> List[str]:
        """生成代码审查要点"""
        points = []

        # 检查大文件变更
        large_files = [c for c in changes if c.additions + c.deletions > 100]
        if large_files:
            points.append(f"注意: 有 {len(large_files)} 个文件变更超过 100 行，需要仔细审查")

        # 检查关键文件
        critical_patterns = ['Program.cs', 'App.axaml', 'MainWindow', 'Core', 'Service']
        for change in changes:
            for pattern in critical_patterns:
                if pattern in change.path:
                    points.append(f"关键文件变更: {change.path} - 需要特别关注")
                    break

        # 检查提交消息质量
        if len(message) < 10:
            points.append("提交消息较短，建议提供更详细的变更说明")

        if 'wip' in message.lower() or 'todo' in message.lower():
            points.append("提交包含 WIP/TODO 标记，确认是否已完成")

        # 检查文件删除
        deleted = [c for c in changes if c.change_type == 'deleted']
        if deleted:
            points.append(f"删除了 {len(deleted)} 个文件，确认是否有其他代码依赖这些文件")

        return points


def generate_markdown_report(analysis: Dict[str, Any]) -> str:
    """生成 Markdown 格式的分析报告"""
    lines = []

    # 标题
    lines.append(f"# Commit 深度分析报告")
    lines.append(f"")
    lines.append(f"**提交哈希**: `{analysis['commit_hash']}`")
    lines.append(f"**提交时间**: {analysis['date']}")
    lines.append(f"**作者**: {analysis['author']} <{analysis['email']}>")
    lines.append(f"**重要性**: {analysis['importance'].upper()}")
    lines.append(f"")

    # 提交消息
    lines.append(f"## 提交消息")
    lines.append(f"```")
    lines.append(analysis['message'])
    lines.append(f"```")
    lines.append(f"")

    # 变更统计
    lines.append(f"## 变更统计")
    stats = analysis['stats']
    lines.append(f"- **新增文件**: {stats.get('added', 0)}")
    lines.append(f"- **修改文件**: {stats.get('modified', 0)}")
    lines.append(f"- **删除文件**: {stats.get('deleted', 0)}")
    lines.append(f"")

    # 文件类型分布
    if analysis.get('file_types'):
        lines.append(f"### 文件类型分布")
        for ext, count in sorted(analysis['file_types'].items(), key=lambda x: -x[1]):
            lines.append(f"- `{ext}`: {count} 个文件")
        lines.append(f"")

    # 变更文件列表
    if analysis.get('changes'):
        lines.append(f"## 变更文件列表")
        lines.append(f"| 文件路径 | 变更类型 |")
        lines.append(f"|---------|---------|")
        type_map = {'added': '新增', 'modified': '修改', 'deleted': '删除'}
        for change in analysis['changes'][:50]:  # 限制显示数量
            change_type = type_map.get(change['type'], change['type'])
            lines.append(f"| `{change['path']}` | {change_type} |")
        lines.append(f"")

    # 影响分析
    if analysis.get('impact_analysis'):
        lines.append(f"## 影响分析")
        for impact in analysis['impact_analysis']:
            lines.append(f"- {impact}")
        lines.append(f"")

    # 代码审查要点
    if analysis.get('review_points'):
        lines.append(f"## 代码审查要点")
        for point in analysis['review_points']:
            lines.append(f"- ⚠️ {point}")
        lines.append(f"")

    # 关键代码片段
    if analysis.get('key_snippets'):
        lines.append(f"## 关键代码片段")
        for snippet in analysis['key_snippets'][:5]:
            lines.append(f"### {snippet['file']}")
            lines.append(f"- **类型**: {snippet['type']}")
            lines.append(f"- **行数**: {snippet['lines_count']}")
            lines.append(f"")
            lines.append(f"```")
            lines.append(snippet['preview'][:2000])  # 限制预览长度
            lines.append(f"```")
            lines.append(f"")

    return '\n'.join(lines)


def main():
    """主函数"""
    repo_path = r"d:\github\LanMountainDesktop"
    output_dir = Path(repo_path) / "docs" / "auto_commit_md"

    # 确保输出目录存在
    output_dir.mkdir(parents=True, exist_ok=True)

    # 读取 HEAD 日志
    head_log_path = Path(repo_path) / ".git" / "logs" / "HEAD"
    if not head_log_path.exists():
        print(f"错误: 找不到 HEAD 日志文件: {head_log_path}")
        return

    # 解析 HEAD 日志获取所有 commit
    commits = []
    with open(head_log_path, 'r', encoding='utf-8') as f:
        for line in f:
            line = line.strip()
            if not line:
                continue

            # 解析日志行
            # 格式: old_hash new_hash name <email> timestamp timezone\taction: message
            parts = line.split('\t')
            if len(parts) < 2:
                continue

            meta_part = parts[0]
            action_part = parts[1]

            meta_tokens = meta_part.split()
            if len(meta_tokens) < 5:
                continue

            new_hash = meta_tokens[1]

            # 只处理 commit 操作
            if 'commit' in action_part or action_part.startswith('commit:'):
                message = action_part.replace('commit:', '').strip()
                commits.append({
                    'hash': new_hash,
                    'message': message
                })

    print(f"找到 {len(commits)} 个 commit")

    # 初始化分析器
    analyzer = CommitAnalyzer(repo_path)

    # 分析每个 commit
    for i, commit_info in enumerate(commits):
        commit_hash = commit_info['hash']
        short_hash = commit_hash[:7]

        print(f"[{i+1}/{len(commits)}] 分析 commit: {short_hash} - {commit_info['message'][:50]}")

        try:
            # 分析提交
            analysis = analyzer.analyze_commit(commit_hash)
            if not analysis:
                print(f"  跳过: 无法解析 commit {short_hash}")
                continue

            # 生成报告
            report = generate_markdown_report(analysis)

            # 保存报告
            date_str = datetime.fromtimestamp(analysis['timestamp']).strftime('%Y%m%d')
            filename = f"{date_str}_{short_hash}_deep_analysis.md"
            output_path = output_dir / filename

            with open(output_path, 'w', encoding='utf-8') as f:
                f.write(report)

            print(f"  已保存: {filename}")

        except Exception as e:
            print(f"  错误: 分析 commit {short_hash} 时出错: {e}")
            import traceback
            traceback.print_exc()

    print("\n分析完成!")


if __name__ == "__main__":
    main()
