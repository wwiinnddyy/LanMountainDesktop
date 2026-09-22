"""组件生命周期配对检查：Attach/构造里起来的东西，Detach/Dispose 时有没有收回去。

为什么需要它：组件控件是短命的、服务是长命的。控件忘了退订/停表，服务就替它一直持有那棵树
（本仓真发生过两次：组件库预览换选中项、组件浮窗关停，都是"起的地方有、收的地方没有"）。
`dump-dup-methods.py` 和 `dump-drift-methods.py` 都看不见这类问题——它们比的是方法体，
这里比的是"动词配对"。

用法：
    python scripts/check-component-pairs.py                     # 默认查 Views/Components
    python scripts/check-component-pairs.py desktop/LanMountainDesktop/ComponentSystem

只报线索，不报结论：每一处命中都给文件:行与两侧实际动作，判之前必须读代码
（同一文件里"更远处才收"的正当写法、`_timer?.Stop()` 这类形态都曾把纯文本判据骗过去）。
"""

import collections
import os
import re
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), os.pardir))
DEFAULT_DIRS = [os.path.join("desktop", "LanMountainDesktop", "Views", "Components")]

# (家族, 起侧正则, 收侧正则, 是否按键名配对)
FAMILIES = [
    ("timer",
     re.compile(r"\b(?P<key>[\w]*[Tt]imer\w*)\s*\??\.\s*Start\s*\("),
     re.compile(r"\b(?P<key>[\w]*[Tt]imer\w*)\s*\??\.\s*Stop\s*\("),
     True),
    ("subscription",
     re.compile(r"\b(?P<key>[\w.]*[Ss]ervice\w*)\s*\??\.\s*(?P<h>\w+)\s*\+=\s*\w+"),
     re.compile(r"\b(?P<key>[\w.]*[Ss]ervice\w*)\s*\??\.\s*(?P<h>\w+)\s*-=\s*\w+"),
     True),
    ("property-changed",
     re.compile(r"\b(?P<key>[\w.]+)\.(?P<h>PropertyChanged|CollectionChanged|CanExecuteChanged)\s*\+=\s*\w+"),
     re.compile(r"\b(?P<key>[\w.]+)\.(?P<h>PropertyChanged|CollectionChanged|CanExecuteChanged)\s*-=\s*\w+"),
     True),
    ("snapshot",
     re.compile(r"StudySnapshotSubscription\s*\.\s*Subscribe"),
     re.compile(r"StudySnapshotSubscription\s*\.\s*Unsubscribe|StudyComponentLifecycle\s*\.\s*Detach"),
     False),
    ("timezone",
     re.compile(r"\bSet\s*TimeZoneService\s*\("),
     re.compile(r"\bClear\s*TimeZoneService\s*\("),
     False),
    # 监视租约不在这张表里：它走 StudyMonitoringLease.Sync(ref, …, 状态位)，
    # 取/放由家按状态对账，组件侧只有兜底的 Release —— 按动词数会得出假结论。
]


def sites(path, rx):
    found = []
    for index, raw in enumerate(open(path, encoding="utf-8-sig", errors="replace").read().splitlines(), 1):
        line = raw.strip()
        if line.startswith("//"):
            continue
        for match in rx.finditer(line):
            key = "|".join(g for g in match.groupdict().values() if g)
            found.append((index, key, line[:110]))
    return found


def main():
    directories = sys.argv[1:] or DEFAULT_DIRS
    files = []
    for directory in directories:
        base = directory if os.path.isabs(directory) else os.path.join(ROOT, directory)
        if not os.path.isdir(base):
            print(f"跳过不存在的目录：{directory}")
            continue
        for filename in sorted(os.listdir(base)):
            if filename.endswith(".cs"):
                files.append(os.path.join(base, filename))

    total_problems = 0
    family_stats = {name: [0, 0] for name, _, _, _ in FAMILIES}
    checked = 0
    for path in files:
        text = open(path, encoding="utf-8-sig", errors="replace").read()
        if "AttachedToVisualTree" not in text and "DetachedFromVisualTree" not in text:
            continue
        checked += 1
        for name, start_rx, stop_rx, by_key in FAMILIES:
            starts = sites(path, start_rx)
            stops = sites(path, stop_rx)
            family_stats[name][0] += len(starts)
            family_stats[name][1] += len(stops)
            if not starts:
                continue
            stop_keys = collections.Counter(key for _, key, _ in stops)
            for line_no, key, raw in starts:
                unpaired = stop_keys[key] < 1 if by_key else len(starts) > len(stops)
                if unpaired:
                    total_problems += 1
                    print(f"{os.path.relpath(path, ROOT)}:{line_no}  [{name}]  {raw}")

    print(f"\n检查 {checked} 个组件文件，报出 {total_problems} 处待复核线索（是线索不是结论，逐处读代码再判）")
    for name, counts in family_stats.items():
        print(f"  {name}: 起 {counts[0]} 处 / 收 {counts[1]} 处")


if __name__ == "__main__":
    main()
