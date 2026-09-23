"""列出全仓"逐字相同的方法体"，给重复真源收口排队用。

用法：
    python scripts/dump-dup-methods.py                       # 扫全部二进制
    python scripts/dump-dup-methods.py desktop/LanMountainDesktop/Services   # 只扫给定目录
    NAMES=Foo,Bar python scripts/dump-dup-methods.py         # 只查这两个方法名

**这条探针必须先拿已知样本验过再用**（同一个坑这一轮踩过两次）：先从某个历史提交里
把已知有逐字重复的那几个文件取到临时目录，带上 NAMES 扫它，必须报出 Nx 才算有效；
再拿它扫真树。按名字数一遍不等于逐字相同，所以每个候选都要打开两三份看一眼再动手。
本仓是 Allman 大括号（签名一行、`{` 单独一行）：按 K&R 写的解析器一条都匹配不上，
会在真树上报"0 组"——那种 0 是假的，别当结论。
"""

import hashlib
import io
import os
import re
import sys
from collections import defaultdict

DEFAULT_ROOTS = ["core", "desktop", "airapp", "install", "platform", "mobile", "packaging"]
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), os.pardir))
NAMES = os.environ.get("NAMES")
NAMES = [n.strip() for n in NAMES.split(",") if n.strip()] if NAMES else None
SKIP_DIRS = {"obj", "bin", "node_modules"}

name_alt = "|".join(re.escape(n) for n in NAMES) if NAMES else r"[A-Za-z_]\w*"
SIG = re.compile(
    r"^\s*(private|internal|public)\s+(static\s+)?(?:async\s+)?"
    r"[\w<>?\[\],\. ]+?\b(" + name_alt + r")\s*\(([^)]*)\)\s*$"
)
BRACE = re.compile(r"^\s*\{\s*$")

_WORD = set("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_$")


def squeeze(text):
    """压掉排版：只在两个标识符字符之间留一个空格。

    判据面不许因为"同一行代码换行写"就认成两份不同实现（2026-09-23 收 AwaitWinRtOperationAsync
    时量到的盲区：`taskObject` 换行再 `.GetType()` 的两份抄本在逐字面不显形）。
    """
    out = []
    i = 0
    n = len(text)
    while i < n:
        ch = text[i]
        if not ch.isspace():
            out.append(ch)
            i += 1
            continue
        j = i
        while j < n and text[j].isspace():
            j += 1
        left = text[i - 1]
        right = text[j] if j < n else ""
        if left in _WORD and right in _WORD:
            out.append(" ")
        i = j
    return "".join(out)


def logical_lines(lines):
    """Same semantics as the gate's LogicalLines and dump-drift-methods.logical_lines:
    join a declaration whose parameter list does not close on the same line.
    Stops as soon as a brace shows up, so a method body is never swallowed into its signature.
    """
    out = []
    i = 0
    n = len(lines)
    while i < n:
        current = lines[i]
        while ("{" not in current
               and sum(c in "([" for c in current) != sum(c in ")]" for c in current)
               and i + 1 < n):
            i += 1
            current = current.rstrip() + " " + lines[i].strip()
        out.append(current)
        i += 1
    return out


def count_statements(body):
    """数深度 0 的分号：一条语句可能写成两行，那不算"复制了一份逻辑"。

    2026-09-22 踩出来的：18 个组件的 ApplyCellSize 收口成
    `ComponentDesignMetrics.ApplyCellSize(ref _currentCellSize, cellSize, UpdateAdaptiveLayout);`
    之后仍是一族"逐字相同的两行体"，按行数它会一直算重复——那是指标在骗人。
    同一棵树上按行数 86 族、按语句数 74 族，差的 12 族全是这种单语句转手。
    """
    statements = 0
    for line in body:
        depth = 0
        in_string = False
        for ch in line:
            if ch == '"':
                in_string = not in_string
            elif in_string:
                continue
            elif ch in "{([":
                depth += 1
            elif ch in "})]":
                depth -= 1
            elif ch == ";" and depth == 0:
                statements += 1
    return statements


files = []
explicit = sys.argv[1:]
for root in (explicit or DEFAULT_ROOTS):
    # 位置参数写错（比如把 NAMES= 当参数传进来——这份工具的 NAMES 是环境变量）
    # 以前会被静默忽略，扫 0 个文件、报"0 组"——一个假的干净结果。宁可当场退出。
    target = root if os.path.isabs(root) else os.path.join(ROOT, root)
    if not os.path.exists(target):
        if explicit:
            sys.exit(f"给定的路径不存在：{root}（仓库根 {ROOT}）——宁可不报，也不报假的 0")
        continue
    for dirpath, dirnames, filenames in os.walk(target):
        dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
        files.extend(os.path.join(dirpath, f) for f in filenames if f.endswith(".cs"))
if not files:
    sys.exit("一个 .cs 都没扫到：目录参数或 NAMES 写错了，这次的 0 不可信")

groups = defaultdict(list)
for path in files:
    lines = logical_lines(io.open(path, encoding="utf-8", errors="replace").read().split("\n"))
    i = 0
    while i < len(lines) - 1:
        m = SIG.match(lines[i])
        if not m or not BRACE.match(lines[i + 1]):
            i += 1
            continue
        depth, seen, end, j = 0, False, -1, i + 1
        while j < len(lines):
            for ch in lines[j]:
                if ch == "{":
                    depth += 1
                    seen = True
                elif ch == "}":
                    depth -= 1
            if seen and depth == 0:
                end = j
                break
            j += 1
        if end < 0:
            i += 1
            continue
        body = [l.strip() for l in lines[i + 2:end] if l.strip() and not l.strip().startswith("//")]
        if count_statements(body) >= 2:
            digest = hashlib.sha1(squeeze(" ".join(body)).encode("utf-8")).hexdigest()[:8]
            groups[(m.group(3), digest, len(body))].append((path, i + 1, m.group(1), bool(m.group(2))))
        i = end + 1

by_content = {}
for (name, digest, nlines), sites in groups.items():
    key = (name, digest)
    if key not in by_content:
        by_content[key] = [nlines, []]
    by_content[key][0] = max(by_content[key][0], nlines)
    by_content[key][1].extend(sites)
rows = [(k, v[1]) for k, v in by_content.items() if len(v[1]) >= 2]
nlines_of = {k: v[0] for k, v in by_content.items()}
rows.sort(key=lambda kv: (-len(kv[1]), kv[0][0]))
print("== 逐字相同的方法体（份数 >= 2）==")
for (name, digest), sites in rows:
    nlines = nlines_of[(name, digest)]
    touched = len({s[0] for s in sites})
    print(f"{len(sites)}x in {touched} files  {name}  ({nlines} 行, body#{digest})")
    for path, line, access, is_static in sites:
        print(f"      {path}:{line}  [{access}{' static' if is_static else ''}]")
print(f"\n扫了 {len(files)} 个 .cs，命中 {len(rows)} 组")
