#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Git HEAD 日志解析脚本
读取 .git/logs/HEAD 文件，提取 commit 类型的提交记录并输出为 JSON 格式
"""

import json
import re
from datetime import datetime, timezone, timedelta
from pathlib import Path
from typing import Optional


class GitCommit:
    """表示一个 Git 提交记录"""

    def __init__(
        self,
        parent_hash: str,
        commit_hash: str,
        author: str,
        email: str,
        timestamp: int,
        timezone_str: str,
        message: str
    ):
        self.parent_hash = parent_hash
        self.commit_hash = commit_hash
        self.author = author
        self.email = email
        self.timestamp = timestamp
        self.timezone_str = timezone_str
        self.message = message

    def to_dict(self) -> dict:
        """转换为字典格式"""
        # 将 Unix 时间戳转换为 ISO 8601 格式的时间字符串
        dt = self._parse_timestamp()

        return {
            "parent_hash": self.parent_hash,
            "commit_hash": self.commit_hash,
            "author": self.author,
            "email": self.email,
            "timestamp": self.timestamp,
            "datetime": dt.isoformat() if dt else None,
            "timezone": self.timezone_str,
            "message": self.message
        }

    def _parse_timestamp(self) -> Optional[datetime]:
        """将 Unix 时间戳和时区解析为 datetime 对象"""
        try:
            # 解析时区偏移 (例如 +0800 表示东八区)
            tz_sign = 1 if self.timezone_str[0] == '+' else -1
            tz_hours = int(self.timezone_str[1:3])
            tz_minutes = int(self.timezone_str[3:5])
            tz_offset = timedelta(hours=tz_sign * tz_hours, minutes=tz_sign * tz_minutes)

            # 创建带时区的 datetime
            tz = timezone(tz_offset)
            return datetime.fromtimestamp(self.timestamp, tz)
        except (ValueError, IndexError):
            return None


def parse_git_head_log(log_path: str) -> list[GitCommit]:
    """
    解析 Git HEAD 日志文件

    Args:
        log_path: HEAD 日志文件的路径

    Returns:
        提交记录列表（仅包含 commit 类型的记录）
    """
    commits = []

    # 正则表达式匹配 Git HEAD 日志格式
    # 格式: <父哈希> <当前哈希> <作者> <邮箱> <时间戳> <时区>\t<操作类型>: <提交信息>
    pattern = re.compile(
        r'^(?P<parent_hash>[0-9a-f]{40})\s+'
        r'(?P<commit_hash>[0-9a-f]{40})\s+'
        r'(?P<author>[^<]+)\s+'
        r'<(?P<email>[^>]+)>\s+'
        r'(?P<timestamp>\d+)\s+'
        r'(?P<timezone>[+-]\d{4})\s*'
        r'\t(?P<operation>[^:]+):\s*(?P<message>.+)$'
    )

    # 也匹配带括号操作类型的格式，如 "commit (merge):"
    pattern_with_paren = re.compile(
        r'^(?P<parent_hash>[0-9a-f]{40})\s+'
        r'(?P<commit_hash>[0-9a-f]{40})\s+'
        r'(?P<author>[^<]+)\s+'
        r'<(?P<email>[^>]+)>\s+'
        r'(?P<timestamp>\d+)\s+'
        r'(?P<timezone>[+-]\d{4})\s*'
        r'\t(?P<operation>\w+)\s*\([^)]+\):\s*(?P<message>.+)$'
    )

    path = Path(log_path)
    if not path.exists():
        raise FileNotFoundError(f"日志文件不存在: {log_path}")

    with open(path, 'r', encoding='utf-8') as f:
        for line_num, line in enumerate(f, 1):
            line = line.strip()
            if not line:
                continue

            # 先尝试匹配带括号的格式
            match = pattern_with_paren.match(line)
            if not match:
                match = pattern.match(line)

            if not match:
                continue

            data = match.groupdict()
            operation = data['operation'].lower()

            # 只处理 commit 类型的记录
            if operation not in ('commit',):
                continue

            commit = GitCommit(
                parent_hash=data['parent_hash'],
                commit_hash=data['commit_hash'],
                author=data['author'].strip(),
                email=data['email'],
                timestamp=int(data['timestamp']),
                timezone_str=data['timezone'],
                message=data['message'].strip()
            )
            commits.append(commit)

    return commits


def main():
    """主函数"""
    # 默认日志路径
    default_log_path = r'd:\github\LanMountainDesktop\.git\logs\HEAD'

    # 可以通过命令行参数指定路径
    import sys
    log_path = sys.argv[1] if len(sys.argv) > 1 else default_log_path

    try:
        commits = parse_git_head_log(log_path)

        # 转换为字典列表
        result = {
            "total_commits": len(commits),
            "source": log_path,
            "commits": [commit.to_dict() for commit in commits]
        }

        # 输出为 JSON 格式
        json_output = json.dumps(result, ensure_ascii=False, indent=2)
        print(json_output)

    except FileNotFoundError as e:
        print(f"错误: {e}", file=sys.stderr)
        sys.exit(1)
    except Exception as e:
        print(f"解析失败: {e}", file=sys.stderr)
        sys.exit(1)


if __name__ == '__main__':
    main()
