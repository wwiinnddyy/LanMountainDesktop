"""组件自己定义的"把尺寸换成样式"的方法，有没有真的被调用过。

判据：定义了 `ApplyCellSize` / `UpdateAdaptiveLayout` / `ApplyLayoutMetrics` / `ApplyResponsiveLayout` /
`ApplyTypographyByBackground` / `ApplyChrome` 这类方法，但本文件与全仓都没有调用点。
`override` / `abstract` 例外——它们由基类或接口派发（`WeatherWidgetBase.cs:86` 就是这样调子类的
`ApplyResponsiveLayout`），按"本文件必须有调用"去判会全是假阳性。
症状（真的漏调时）：组件按 XAML 默认样式画出来、缩放应用不上；不报错，只是不对。

用法：`python scripts/check-widget-layout-applied.py`（可传目录，只扫那些目录）。
**报 0 之前先跑正对照**：临时在 Views/Components 下放一个只定义
`private void UpdateAdaptiveLayout() { }` 的文件，它必须被报出来；报完删掉。
"""

import os
import re
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), os.pardir))
NAMES = ("ApplyCellSize|ApplyResponsiveLayout|UpdateAdaptiveLayout|ApplyLayoutMetrics|"
         "ApplyAdaptiveLayout|ApplyTypographyByBackground|ApplyChrome")
DEFINE = re.compile(r"\b(?:public|private|protected|internal)[\w ]*?\b(?P<name>" + NAMES + r")\s*\(")
DISPATCHED = re.compile(r"\b(?:override|abstract)\b")
CALL = re.compile(r"(?P<name>" + NAMES + r")\s*\(")
SCAN_DIRS = ["core", "desktop", "airapp", "install", "platform", "mobile"]


def component_files(directories):
    out = []
    for directory in directories:
        base = directory if os.path.isabs(directory) else os.path.join(ROOT, directory)
        if not os.path.isdir(base):
            print(f"跳过不存在的目录：{directory}")
            continue
        for name in sorted(os.listdir(base)):
            if name.endswith(".cs"):
                out.append(os.path.join(base, name))
    return out


def whole_repo_text():
    for directory in SCAN_DIRS:
        base = os.path.join(ROOT, directory)
        if not os.path.isdir(base):
            continue
        for dirpath, dirnames, names in os.walk(base):
            if re.search(r"[\\/](obj|bin|artifacts|node_modules)([\\/]|$)", dirpath):
                continue
            for name in names:
                if name.endswith(".cs"):
                    path = os.path.join(dirpath, name)
                    yield os.path.relpath(path, ROOT), open(
                        path, encoding="utf-8-sig", errors="replace").read()


def main():
    directories = sys.argv[1:] or [
        os.path.join("desktop", "LanMountainDesktop", "Views", "Components"),
        os.path.join("desktop", "LanMountainDesktop", "ComponentSystem"),
    ]
    files = component_files(directories)
    defined = {}
    called = {}

    for path in files:
        rel = os.path.relpath(path, ROOT)
        text = open(path, encoding="utf-8-sig", errors="replace").read()
        for index, raw in enumerate(text.splitlines(), 1):
            if raw.strip().startswith("//"):
                continue
            declaration_spans = [(m.start("name"), m.end("name")) for m in DEFINE.finditer(raw)]
            for start, end in declaration_spans:
                defined[(rel, raw[start:end])] = (index, bool(DISPATCHED.search(raw)))
            for match in CALL.finditer(raw):
                if any(a <= match.start("name") < b for a, b in declaration_spans):
                    continue
                key = (rel, match.group("name"))
                called[key] = called.get(key, 0) + 1

    repo = list(whole_repo_text())
    offenders = []
    for (rel, name), (line, dispatched) in sorted(defined.items(), key=lambda item: (item[0][0], item[0][1])):
        if dispatched:
            continue
        if called.get((rel, name), 0):
            continue
        # 宿主也可能按具体类型调它（如 DesktopComponentRuntimeRegistry 统一推 ApplyCellSize）
        external = sum(len(re.findall(r"\." + re.escape(name) + r"\s*\(", text)) for _, text in repo)
        if external == 0:
            offenders.append(f"{rel}:{line}  {name}()  定义后无人调用")

    overrides = sum(1 for (_, (_, dispatched)) in defined.items() if dispatched)
    print(f"扫了 {len(files)} 个组件文件，定义 {len(defined)} 个布局/样式应用方法"
          f"（其中 {overrides} 个是 override/abstract，按基类派发不计）")
    print(f"未被调用的：{len(offenders)}")
    for offender in offenders:
        print(f"   {offender}")


if __name__ == "__main__":
    main()
