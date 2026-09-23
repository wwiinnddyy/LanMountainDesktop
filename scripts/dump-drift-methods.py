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
    r"(?:[A-Za-z_][\w<>\[\]?,\. ]*?\s+)?(?P<name>[A-Za-z_]\w*)\s*(?:<[^>]*>)?\s*\((?P<args>[^)]*)\)\s*(?:=>.*)?\{?\s*\}?\s*$")
KEYWORDS = {
    "if", "for", "foreach", "while", "switch", "catch", "using", "lock", "return",
    "get", "set", "add", "remove", "init", "when", "where", "select", "from",
}


def logical_lines(lines):
    """把"签名换行写"的成员声明并成一条逻辑行（与 C# 闸门里的 LogicalLines 同口径）。

    实测本仓有 919 行左括号在本行不闭合；此前这些声明在两个面上都不被认出来。
    返回 <c>(原始行号, 文本)</c>：并行之后的下标不等于文件行号，
    直接拿它当行号印出去会指错位置（实测 <c>PlondsPackageStore.cs</c> 差 8 行，指到了隔壁成员上）。
    """
    out = []
    i = 0
    n = len(lines)
    while i < n:
        start = i
        current = lines[i]
        while ("{" not in current
               and sum(c in "([" for c in current) != sum(c in ")]" for c in current)
               and i + 1 < n):
            i += 1
            current = current.rstrip() + " " + lines[i].strip()
        out.append((start, current))
        i += 1
    return out


def next_non_empty(lines, start):
    for cursor in range(start, len(lines)):
        if lines[cursor].strip():
            return lines[cursor].strip()
    return ""


def arrow_tail(lines, start, head):
    """把表达式体（`=> …`）收成一条语句。

    两头都收：`=>` 可能写在签名行尾，也可能另起一行（本仓 Allman 风格就这么写：
    MusicControlViewModel.cs:277 的 `private string L(...)`，下一行才是 `=> _localization…;`）。
    只认一行会把这两种成员判成"没有体"，往下扫又会把后面成员的体甚至整个类尾巴吞进来——
    实测两种错法各错掉 53 处声明 / 5 个整文件（账见 AGENTS.md 尺子 2）。
    """
    parts = [head.strip()] if head.strip() else []
    depth = sum(part.count("{") - part.count("}") for part in parts)
    if depth <= 0 and parts and parts[-1].endswith(";"):
        return " ".join(parts).rstrip(";").strip()
    cursor = start
    while cursor < len(lines):
        text = lines[cursor].strip()
        cursor += 1
        if not text:
            continue
        if depth <= 0 and text.startswith(("}", "//", "/*", "*")):
            # 没等到 `;` 就撞到别的成员或文档注释：到此为止，绝不继续往下吞。
            break
        parts.append(text)
        depth += text.count("{") - text.count("}")
        if depth <= 0 and text.endswith(";"):
            break
    return " ".join(parts).rstrip(";").strip()


def method_bodies(path):
    """返回该文件里每个"有实现的成员"：(方法名, 签名行号, 归一化后的体)。

    两种"看起来没有体"必须分开，混起来就会把**空实现**当成不存在（第一版就这么瞎过一次，
    把 `public void ApplyCellSize(double cellSize) { }` 报成"没有这个方法"）：
    ① 接口方法 / abstract 的声明——压根没有实现，不算一种体；
    ② `void Foo() { }` 这种**有实现、但什么都不做**——它是站点，体记成空串。
       "声明了契约却不执行"正是这条尺子要抓的东西。
    """
    try:
        raw = open(path, encoding="utf-8-sig", errors="replace").read().splitlines()
    except OSError:
        return []
    lines = logical_lines(raw)
    starts = [start for start, _ in lines]
    lines = [text for _, text in lines]
    collected = []
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
        ahead = next_non_empty(lines, index + 1)
        arrow = raw.find("=>")
        allman_brace = "{" not in raw and ahead.startswith("{")
        # 表达式体优先，且**先于**"本行有没有大括号"的判断：插值字符串里的 `{message}` 也是大括号，
        # 拿它当方法体的开括号会一路扫到类的末尾（实测 AirAppRuntimeLogger.cs:7 的 `Info`）。
        # 认"这一行以 `;` 或 `=>` 收尾 + 括号配平"，才不会把 K&R 写的 `{ …() => …; }` 误当成表达式体。
        if arrow >= 0 and not allman_brace and line.endswith((";", "=>")) \
                and raw.count("{") == raw.count("}"):
            emit(collected, name, starts[index] + 1,
                 normalise(arrow_tail(lines, index + 1, raw[arrow + 2:])), implementation=True)
            continue
        if "{" not in raw and not allman_brace:
            # 没有大括号、下一行也不是 `{`：要么 `=>` 另起一行，要么是无体的声明
            # （接口方法、abstract）——后者不算一种实现，不计。
            if ahead.startswith("=>"):
                emit(collected, name, starts[index] + 1,
                     normalise(arrow_tail(lines, index + 2, ahead[2:])), implementation=True)
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
        # 走到这里说明本行或下一行有 `{`：这是一个实现，哪怕它是 `{ }`。
        emit(collected, name, starts[index] + 1, normalise(" ".join(body)), implementation=True)
    return collected


def emit(collected, name, line, body, implementation=False):
    if empty_body(body):
        body = ""
    # implementation=False 且体是空串＝没有实现的声明（接口方法、abstract），不算站点。
    if implementation or body:
        collected.append((name, line, body))


def empty_body(text):
    """`{ }` 与 `{}` 统一成空串：空实现要有**一个**规范形状，否则同一种写法会被数成两种体。"""
    collapsed = re.sub(r"\s+", "", text)
    return collapsed in ("", "{}")


def normalise(text):
    return re.sub(r'"[^"]*"', '"S"', squeeze(text)).strip()

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
            # 目录写错就静默扫 0 个文件，会报出一个假的"0 族"——宁可不报。
            if positional:
                sys.exit(f"给定的目录不存在：{directory}（仓库根 {ROOT}）")
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
