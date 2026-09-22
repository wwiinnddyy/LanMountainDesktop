"""谁 new 了需要释放的东西，却既不 Dispose 也不交出去。

范围是生产代码（不含 Views/Components，那条轴由 check-component-pairs.py 管）。
按"拥有者类"聚合：类里 new 了这些类型就记一笔"起"，类里出现 Dispose/Close/Stop/using/字段交接记一笔"收"。
只报线索：交出去（返回给调用方、塞进别人的构造参数、由 using 包住）在文本上很难区分，
所以每条命中都要读代码再判——上一轮那条"窗口扫描 12 命中 9 假阳性"的教训就写在这儿。
两个**已实测到的假阳性来源**：
① 释放走了 `CancellationHelper.CancelAndDispose(...)` 这种名字（判据里的 `Dispose` 不能要求词边界）；
② 工厂方法 `Create()` 把资源**交给调用方**持有，本类无物可释——这类要登记进下面的 EXPLAINED，
   并写清"谁最终持有、活多久"，不然每次跑都要重读一遍。

用法：
    python scripts/check-resource-ownership.py
    python scripts/check-resource-ownership.py desktop/LanMountainDesktop/Services
"""

import os
import re
import sys
from collections import defaultdict

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), os.pardir))
DEFAULT_DIRS = [
    os.path.join("desktop", "LanMountainDesktop", "Services"),
    os.path.join("desktop", "LanMountainDesktop", "ViewModels"),
    os.path.join("desktop", "LanMountainDesktop", "AirApps"),
    os.path.join("desktop", "LanMountainDesktop"),
    os.path.join("core", "LanMountainDesktop.Core"),
    os.path.join("install", "LanDesktopPLONDS.installer"),
    os.path.join("platform", "LanMountainDesktop.Platform"),
]

RESOURCE = re.compile(r"new\s+(HttpClient|CancellationTokenSource|DispatcherTimer|Timer|FileSystemWatcher|PeriodicTimer|Process|BinaryReader|MemoryStream|FileStream|StreamWriter|SemaphoreSlim)\s*\(")
# 注意 `Dispose` 前面不能加 `\b`：释放常常走 `CancellationHelper.CancelAndDispose(...)` 这类名字，
# `\b` 会把这种全放过、报成假阳性（2026-09-22 实测两处 CTS 就是这么被误报的）。
RELEASE = re.compile(r"Dispose|\.Close\s*\(|\.Stop\s*\(|Unsubscribe|\bDetach|\busings?\b")
CLASS = re.compile(r"^\s*(?:\[[^\]]*\]\s*)*(?:public|internal|private|protected)[\w \t]*(?:sealed|static|partial|abstract|file)?[\w \t]*\bclass\s+(?P<name>\w+)")

# 已逐处读过、确认"本类无物可释（资源交给调用方持有）"的拥有者类。
# 值要写清交给谁、活多久；这条交接一旦改掉（比如变成每请求建一个 client），这里就该红。
EXPLAINED = {
    "PlondsHttpClientFactory": "Create() 把 HttpClient 交回调用方：PlondsClientServiceFactory.CreateDefault 把它塞进 "
        "PlondsManifestClient / PlondsHttpPackageDownloader；CreateDefault 只在 UpdateSettingsService 构造时调一次"
        "（SettingsDomainServices.cs:2070 new UpdateSettingsService，应用级容器里的单例）"
        "→ 一次进程一个 client，PlondsService 不实现 IDisposable 是有意的，不是泄漏",
}


def class_spans(lines):
    """返回 [(类名, 起始行, 结束行)]，按大括号配平粗切。"""
    spans = []
    depth = 0
    current = None
    for index, raw in enumerate(lines):
        match = CLASS.match(raw)
        if match and current is None:
            current = (match.group("name"), index + 1, 0)
            depth = 0
        if current is not None:
            depth += raw.count("{") - raw.count("}")
            if depth <= 0 and "{" in "".join(lines[max(0, current[1] - 1):index + 1]):
                spans.append((current[0], current[1], index + 1))
                current = None
    return spans


def scan(paths):
    leads = []
    for path in paths:
        lines = open(path, encoding="utf-8-sig", errors="replace").read().splitlines()
        spans = class_spans(lines)
        for cls_name, start, end in spans:
            body = lines[start - 1:end]
            created = [(i + start, m.group(1), body[i].strip()) for i, line in enumerate(body) for m in [RESOURCE.search(line)] if m]
            if not created:
                continue
            released = sum(1 for line in body if RELEASE.search(line))
            hands_off = sum(1 for line in body if re.search(r"return new |=\s*new \w+\([^)]*new |this\.\w+\s*=\s*new \w+\(\s*\)", line))
            if released == 0:
                for line_no, kind, raw in created:
                    leads.append((os.path.relpath(path, ROOT), line_no, cls_name, kind, raw, hands_off))
    return leads


def main():
    directories = sys.argv[1:] or DEFAULT_DIRS
    seen = set()
    files = []
    for directory in directories:
        base = directory if os.path.isabs(directory) else os.path.join(ROOT, directory)
        if not os.path.isdir(base):
            continue
        for dirpath, _, names in os.walk(base):
            if re.search(r"[\\/](obj|bin|artifacts|node_modules)([\\/]|$)", dirpath):
                continue
            for name in names:
                if name.endswith(".cs"):
                    full = os.path.normpath(os.path.join(dirpath, name))
                    if full in seen:
                        continue
                    seen.add(full)
                    files.append(full)

    leads = scan(files)
    unexplained = [lead for lead in leads if lead[2] not in EXPLAINED]
    explained = [lead for lead in leads if lead[2] in EXPLAINED]
    stale = sorted(key for key in EXPLAINED if key not in {lead[2] for lead in leads})

    print(f"扫了 {len(files)} 个文件：{len(leads)} 处线索，其中未解释 {len(unexplained)} 处、已登记交接 {len(explained)} 处")
    for path, line, cls, kind, raw, hands_off in sorted(unexplained):
        note = "（可能是交接出去，需读代码）" if hands_off else ""
        print(f"   未解释 {path}:{line}  {cls} 起 {kind}{note}: {raw[:90]}")
    for _, _, cls, _, _, _ in sorted(explained):
        print(f"   已登记 {cls}: {EXPLAINED[cls][:120]}")
    for key in stale:
        print(f"   登记已失效 {key}: 这个类不再出现在线索里（交接改掉了？还是判据变了？），把这条删掉或重新核")


if __name__ == "__main__":
    main()
