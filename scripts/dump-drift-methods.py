"""按"同名不同体"找重复真源漂移族。

与 dump-dup-methods.py 的分工：那份只报"逐字相同的方法体"（N 份复制），
这一份报"同一个方法名有几种实现"——家被人绕开、各组件各写一份的漂移族只有这里能看见。
用法：
    python scripts/dump-drift-methods.py            # 全部生产二进制
    python scripts/dump-drift-methods.py desktop    # 只扫给定目录
    python scripts/dump-drift-methods.py NAMES=ApplyCellSize,L    # 只看这些方法名
先看"处数多且不同体数也多"的行；判红之前先拿一个已知样本对数（见 AGENTS.md 里 ApplyCellSize 的实测值）。
"""

import collections
import os
import re
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), os.pardir))
DEFAULT_DIRS = ["core", "desktop", "airapp", "install", "platform", "packaging", "mobile"]
SKIP = ("\\obj\\", "\\bin\\", "\\artifacts\\", "\\node_modules\\")

SIGNATURE = re.compile(
    r"^\s*(?:public|private|protected|internal)?\s*"
    r"(?:static\s+|sealed\s+|override\s+|virtual\s+|async\s+|partial\s+|new\s+)*"
    r"(?:[A-Za-z_][\w<>\[\]?,\. ]*?\s+)?(?P<name>[A-Za-z_]\w*)\s*(?:<[^>]*>)?\s*\((?P<args>[^)]*)\)\s*(?:=>.*)?\{?\s*$")
KEYWORDS = {
    "if", "for", "foreach", "while", "switch", "catch", "using", "lock", "return",
    "get", "set", "add", "remove", "init", "when", "where", "select", "from",
}


def method_bodies(path):
    try:
        lines = open(path, encoding="utf-8-sig", errors="replace").read().splitlines()
    except OSError:
        return
    for index, raw in enumerate(lines):
        line = raw.strip()
        if not line or line.startswith(("//", "/*", "*")):
            continue
        match = SIGNATURE.match(raw)
        if not match:
            continue
        name = match.group("name")
        if name in KEYWORDS:
            continue
        depth = 0
        started = False
        body = []
        cursor = index
        while cursor < len(lines):
            text = lines[cursor]
            depth += text.count("{") - text.count("}")
            if "{" in text:
                started = True
            if cursor > index:
                body.append(text.strip())
            if started and depth <= 0:
                break
            cursor += 1
        if not started and raw.rstrip().endswith(";"):
            body = [raw.split("=>")[1].strip()] if "=>" in raw else []
        normalized = re.sub(r"\s+", " ", " ".join(body))
        normalized = re.sub(r'"[^"]*"', '"S"', normalized)
        yield name, index + 1, normalized


def main():
    args = sys.argv[1:]
    names_filter = None
    directories = DEFAULT_DIRS
    positional = []
    for arg in args:
        if arg.startswith("NAMES="):
            names_filter = set(arg[len("NAMES="):].split(","))
        else:
            positional.append(arg)
    if positional:
        directories = positional

    groups = collections.defaultdict(list)
    scanned = 0
    for directory in directories:
        base = os.path.join(ROOT, directory)
        if not os.path.isdir(base):
            continue
        for dirpath, _, filenames in os.walk(base):
            if any(token in dirpath + os.sep for token in SKIP):
                continue
            for filename in filenames:
                if not filename.endswith(".cs"):
                    continue
                path = os.path.join(dirpath, filename)
                scanned += 1
                for name, line, body in method_bodies(path):
                    if names_filter and name not in names_filter:
                        continue
                    groups[name].append((os.path.relpath(path, ROOT), line, body))

    print(f"扫了 {scanned} 个 .cs，{len(groups)} 个方法名")
    rows = []
    for name, sites in groups.items():
        variants = collections.Counter(site[2] for site in sites)
        files = {site[0] for site in sites}
        if len(sites) >= 3 and len(variants) >= 2 and len(files) >= 3:
            rows.append((len(sites), len(variants), len(files), name, sites, variants))

    rows.sort(key=lambda row: (-(row[0] - row[1]), -row[0]))
    for count, variant_count, file_count, name, sites, variants in rows:
        biggest = variants.most_common(1)[0][1]
        print(f"{count:3d} 处 / {variant_count:2d} 种体 / {file_count:2d} 文件   {name}   最大同体组={biggest}")
        for text, hits in variants.most_common(3):
            where = ", ".join(f"{site[0]}:{site[1]}" for site in sites if site[2] == text)[:150]
            print(f"      {hits}x {where}")
            print(f"      | {text[:150]}")


if __name__ == "__main__":
    main()
