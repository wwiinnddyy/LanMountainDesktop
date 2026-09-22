"""实例成员死约定普查：非静态类型上"定义了、生产里没人调"的实例方法。

为什么要有这把尺子（2026-09-22 实测的两条盲区，都不是推测）：
① ZeroUseMemberRatchetTests 的口径是 `static class` 上的 `public/internal static` 方法，
   实例方法一条都不在它视野里 —— 当天两个死约定（PendingAirAppUpgradeService.AddPendingUpgrade、
   ComponentSettingsService.ApplyScopedContextToTarget）是靠手工 grep 抓到的，不可重跑。
② 编译器也不管：Directory.Build.props 里 EnforceCodeStyleInBuild=false，IDE0051（未使用私有成员）
   在构建里一条都不出。实测往宿主里塞一个零调用的 private 方法，--no-incremental 重建后
   错误 0、IDE0051 计数 0。所以"连 private 都没人管"，这把尺子按可见性全收。

判据（保守方向：宁可漏报，不可误删）：
- A 级＝方法名在生产语料（含 airapp/mobile/Plonds 这些跨二进制可达目录）里的出现次数 == 声明条数，即除声明本身外生产语料零出现（含 .axaml、
  含字符串与注释、含 nameof）。零出现即零调用点，包括本类内部。
- T 级＝生产零出现、但 tests/ 里有出现 → "只有测试在调"，属实现了没接线，登记不删（同 G1-AM 口径）。
- 只数方法不数属性：属性能被 x:Static / Binding / 对象初始化器引用，裸名计数会把活的判成死的。

已实测到的假阳性来源，已做成豁免（不豁免就是一把误删活的尺子）：
① 接口实现：名字在任一 `interface` 块里出现过 → 由接口调度，不算死；
② override / virtual / abstract / partial / extern：虚派发的调用点是基类那个名字；
③ 构造器（拿不到返回类型且名字 == 所在类型名）、`Main`、`operator`；
④ 源生成器喂的：`[RelayCommand]` / `[ObservableProperty]` 这类特性在方法上时，调用点由生成代码
   提供、不在源文件里 → 带这些特性的豁免；
⑤ `*ForTests` 是约定的测试入口，与静态棘轮同口径；
⑥ BCL/框架约定名（ToString/Equals/Dispose/OnNext/…）列在 CONVENTION_NAMES，
   每条都是"框架会调、源码里数不到"。

漏报方向（已知、可接受）：① 只被另一个死方法调用的方法进不了 A 级；② 名字同时是类型名/属性名时
会被无关出现喂饱；③ **跨二进制同名**：裸名计数会把别的工程里同名方法的调用点当成本条的调用点——
实测样本 `LanMountainDesktopIpcClient.GetCatalogAsync`（Core 公开面，本仓只有测试在调）被 Plonds 树里
`IPlondsManifestStore.GetCatalogAsync` 的自有调用喂成"活的"，因此它现在既进不了 A 级、也不该挂名单。
要彻底分开得靠类型解析，文本尺子不做；这类条目靠"Core / SDK 公开面"这条人工口径单独盯。
所以 A 级是线索不是终判——终判是删除法（删掉后重建，编译红不红）。

用法：
    python scripts/dump-dead-instance-methods.py            # 报未解释的 A/T 级 + 汇总，未解释>0 时退出码 1
    python scripts/dump-dead-instance-methods.py --all      # 连已登记的一起报
    python scripts/dump-dead-instance-methods.py desktop/LanMountainDesktop/Services   # 只看这个范围
"""

import os
import re
import sys
from collections import Counter, defaultdict

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), os.pardir))

# 声明只从这些目录收（与 ZeroUseMemberRatchetTests 的 HostDirectories 同口径）。
PROD_DIRS = [
    os.path.join("desktop", "LanMountainDesktop"),
    os.path.join("desktop", "LanMountainDesktop.Launcher"),
    "core",
    "install",
    "platform",
    "packaging",
    "scripts",
]
# 调用点要按"跨二进制可达"数，不是按"当前这个目录"数。实测漏报样本：
# RssReaderService.ProbeAsync 唯一的调用点在 airapp/…/RssReaderAirAppView.axaml.cs:241，
# 只数 PROD_DIRS 会把它判成死码——删了就是破坏 SDK 宿主。AGENTS.md 里"只 grep core desktop
# 量不到 airapp/"是同一条教训，这条轴上也一样。
REACH_DIRS = PROD_DIRS + ["airapp", "mobile", "PenguinLogisticsOnlineNetworkDistributionSystem"]
TEST_DIR = "tests"
SKIP_DIR_NAMES = {"bin", "obj", ".git", ".vs", ".idea", "node_modules", "artifacts", "publish"}
# 不收 .py：本文件自己的 EXPLAINED 名单里写满了成员名，收进来就是自证清白。
# .md 照收（同静态棘轮）：只被文档提到、代码里没人调的成员会因此漏报，是保守方向。
CORPUS_EXTENSIONS = {".cs", ".axaml", ".xaml", ".json", ".md", ".props", ".csproj", ".yml",
                     ".yaml", ".iss", ".ps1"}

MODIFIER_WORDS = ("public", "private", "protected", "internal", "static", "sealed", "override",
                  "virtual", "abstract", "async", "extern", "unsafe", "new", "partial", "readonly", "ref")
# 声明行：一串修饰符 + 返回类型（可含泛型/可空/数组/点号，不许含 '=' 与 '{'）+ 名字 + '('
DECL = re.compile(
    r"^[ \t]*(?P<mods>(?:(?:" + "|".join(MODIFIER_WORDS) + r")[ \t]+)+)"
    r"(?P<ret>[A-Za-z_][A-Za-z0-9_<>,\[\]\?\. ]*[ \t])?"
    r"(?P<name>[A-Za-z_]\w*)[ \t]*(?:<[^>\r\n]*>)?[ \t]*\(")
TYPE_DECL = re.compile(
    r"^[ \t]*(?:(?:public|private|protected|internal|static|sealed|abstract|partial|file|readonly|"
    r"record)[ \t]+)*(?:class|struct|record|interface)[ \t]+(?P<name>\w+)")
INTERFACE_HEAD = re.compile(
    r"^[ \t]*(?:(?:public|private|protected|internal|new|static|partial|unsafe|file)[ \t]+)*"
    r"interface[ \t]+(?P<name>\w+)")
IDENTIFIER = re.compile(r"[A-Za-z_]\w*")

GENERATED_ATTRIBUTES = ("RelayCommand", "ObservableProperty", "JsonConstructor", "MessagePackObject")
# 实现"本仓之外的接口"（Avalonia / Microsoft.Extensions / BCL）时，接口成员名不在本仓，
# iface_names 收不到 → 会被误报成死码。这里只列**实测命中的**，不做先验清单：
# HexToBrushConverter/HexToColorConverter.ConvertBack（IValueConverter，双向绑定由框架回调）、
# NoOpHostApplicationLifetime.StopApplication（IHostApplicationLifetime，宿主关停机调）。
EXTERNAL_INTERFACE_MEMBERS = {
    "Convert", "ConvertBack", "StopApplication", "StartApplication",
}
CONVENTION_NAMES = {
    # 框架按约定回调，源码里永远数不到调用点。
    "ToString", "Equals", "GetHashCode", "GetType", "Finalize", "Dispose", "DisposeAsync",
    "CompareTo", "GetEnumerator", "MoveNext", "Reset", "OnNext", "OnCompleted", "OnError",
    "OnApplyTemplate", "OnAttachedToVisualTree", "OnDetachedFromVisualTree", "OnLoaded",
    "OnUnloaded", "Initialize", "Setup", "Teardown", "Main",
}

# 已逐条读过、写清"为什么还留着"的条目。键 = 类型路径.方法名。
# 只许缩短；这次没再命中的条目会进"名单失效"（防名单烂掉）。
# 同名重载共用一个键（如 RelayCommand 与 RelayCommand<T>），理由要覆盖两者。
EXPLAINED = {
    "MainWindow.OnComponentLibraryCategoryViewportPointerPressed":
        "组件库分类条的一整套拖拽手势（配套字段 _isComponentLibraryCategoryGestureActive 等只被这四个"
        "方法读写）。MainWindow.axaml:681 的 ComponentLibraryCategoryViewport 存在，但四个指针事件"
        "一个都没接上 → 删掉等于丢掉一个写完了的交互，接不接是产品决定：待办 G1-AW",
    "MainWindow.OnComponentLibraryCategoryViewportPointerMoved": "同上，G1-AW 的一套手势",
    "MainWindow.OnComponentLibraryCategoryViewportPointerReleased": "同上，G1-AW 的一套手势",
    "MainWindow.OnComponentLibraryCategoryViewportPointerCaptureLost": "同上，G1-AW 的一套手势",
    "RelayCommand.RaiseCanExecuteChanged":
        "ICommand 不含这个方法，靠宿主在条件变化时主动调；全仓（含 .axaml）零调用＝命令可用态从不重算。"
        "与 G1-AU（RefreshFromSettings 从不被推）同族：这是缺口不是死码，接线与否等拍板",
    "PublicIpcHostService.PublishLoadingStateAsync":
        "Core 是已发布包：删公开成员属跨二进制破坏性变更，跟 SDK 版本号一起定：待办 G1-U",
    "LanMountainDesktopIpcClient.GetSessionInfoAsync": "同上，Core 公开面：待办 G1-U",
}


def walk(directory):
    for current, dirs, files in os.walk(directory):
        dirs[:] = [d for d in dirs if d not in SKIP_DIR_NAMES]
        for name in files:
            yield os.path.join(current, name)


def read(path):
    with open(path, encoding="utf-8-sig", errors="replace") as handle:
        return handle.read().splitlines()


def corpus_files(root, directory):
    base = os.path.join(root, directory)
    if not os.path.isdir(base):
        return
    for path in walk(base):
        if os.path.splitext(path)[1].lower() not in CORPUS_EXTENSIONS:
            continue
        name = os.path.basename(path)
        # 棘轮/守卫自己的名单里写满了成员名，拿它当语料会自证清白。
        if name.endswith("RatchetTests.cs") or name == "SourceIntegrityTests.cs":
            continue
        yield path


def preceding_attributes(lines, index):
    """往上收集紧邻的特性行（源生成器的调用点就藏在这里，数不到）。"""
    collected = []
    cursor = index - 1
    while cursor >= 0:
        stripped = lines[cursor].strip()
        if stripped == "" or stripped.startswith("//"):
            cursor -= 1
            continue
        if stripped.startswith("["):
            collected.append(stripped)
            cursor -= 1
            continue
        if collected and not collected[-1].endswith("]"):
            collected[-1] = collected[-1] + " " + stripped   # 特性跨行
            cursor -= 1
            continue
        break
    return collected


def interface_member_names(root):
    """任一 interface 块里出现过的标识符：这些名字按接口调度，不算"没人调"。"""
    names = set()
    for directory in REACH_DIRS:
        for path in corpus_files(root, directory):
            if not path.endswith(".cs"):
                continue
            lines = read(path)
            depth = 0
            inside = False
            for line in lines:
                if not inside:
                    if INTERFACE_HEAD.match(line):
                        inside = True
                        depth = 0
                    else:
                        continue
                names.update(IDENTIFIER.findall(line.split("//")[0]))
                depth += line.count("{") - line.count("}")
                if depth <= 0 and "}" in line:
                    inside = False
    return names


def collect_declarations(root, iface_names):
    declarations = defaultdict(list)
    # 每个名字在**全语料**里有几条声明行。裸名计数要把它整条减掉：
    # 实测漏报样本——Plonds 树自带 IPlondsManifestStore.GetCatalogAsync（三处声明行），
    # 只减宿主这条声明时那三处会把 Core 的 LanMountainDesktopIpcClient.GetCatalogAsync 喂成"活的"。
    decl_lines = Counter()
    scanned = 0
    for directory in REACH_DIRS:
        for path in sorted(corpus_files(root, directory)):
            if not path.endswith(".cs"):
                continue
            scanned += 1
            relative = os.path.relpath(path, root).replace("\\", "/")
            lines = read(path)
            stack = []
            markers = []
            pending = []
            depth = 0
            for number, raw in enumerate(lines, start=0):
                line = raw.split("//")[0]
                opened = line.count("{")
                closed = line.count("}")

                # 无主体的类型声明（`record X(...);` / `interface I;`）不是容器：
                # 实测漏掉这条时，三个并列 record 会被当成三层嵌套，键变成
                # RssFeedProbe.RssOpmlImportResult.RssReaderService.ProbeAsync。
                type_match = TYPE_DECL.match(line)
                bodyless = line.strip().endswith(";") and not opened
                if type_match and not bodyless:
                    pending.append(type_match.group("name"))
                elif bodyless:
                    pending = []

                if opened:
                    for name in pending:
                        stack.append(name)
                        markers.append(depth + 1)   # 主体内的深度下限
                    pending = []

                match = DECL.match(line)
                if match:
                    name = match.group("name")
                    decl_lines[name] += 1
                    mods = match.group("mods")
                    ret = (match.group("ret") or "").strip()
                    owner = ".".join(stack) if stack else "<top>"
                    if directory not in PROD_DIRS:
                        depth += opened - closed
                        continue
                    entry = classify(relative, number + 1, owner, name, mods, ret,
                                     preceding_attributes(lines, number), iface_names)
                    if entry:
                        declarations[name].append(entry)

                depth += opened - closed
                while stack and depth < markers[-1]:
                    stack.pop()
                    markers.pop()
    return declarations, decl_lines, scanned


def classify(path, number, owner, name, mods, ret, attributes, iface_names):
    """返回线索字典；None = 按豁免规则不收（每条豁免对应文件头的一条实测假阳性来源）。"""
    if re.search(r"\bstatic\b", mods):
        return None                      # 静态成员归 ZeroUseMemberRatchetTests
    if re.search(r"\b(override|virtual|abstract|partial|extern)\b", mods):
        return None
    if not ret:
        return None                      # 构造器：`public Foo(...)` 拿不到返回类型
    if re.search(r"(class|struct|record|interface|enum)", ret):
        return None                      # 主构造器类型的声明行本身：
                                         # `internal sealed class InstallProgressBridge(IProgress<…>? p)`
                                         # 会被 DECL 读成"返回类型 class InstallProgressBridge 的方法"，
                                         # 实测报出一条根本不存在的 A 级条目 <top>.InstallProgressBridge。
    if name in CONVENTION_NAMES or name in EXTERNAL_INTERFACE_MEMBERS \
            or name.endswith("ForTests") or name == owner.split(".")[-1]:
        return None
    if name in iface_names:
        return None
    if any(token in attribute for attribute in attributes for token in GENERATED_ATTRIBUTES):
        return None
    return {"key": f"{owner}.{name}", "file": path, "line": number,
            "visibility": next((v for v in ("public", "internal", "protected", "private")
                                if v in mods), "?")}


def count_identifiers(root, directory):
    total = Counter()
    for path in corpus_files(root, directory):
        total.update(IDENTIFIER.findall("\n".join(read(path))))
    return total


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    show_all = "--all" in sys.argv
    root = ROOT

    iface_names = interface_member_names(root)
    declarations, decl_lines, scanned = collect_declarations(root, iface_names)
    reach_count = sum((count_identifiers(root, d) for d in REACH_DIRS), Counter())
    test_count = count_identifiers(root, TEST_DIR)

    tier_a = []
    tier_tests_only = []
    for name, entries in declarations.items():
        if reach_count[name] - decl_lines[name] > 0:
            continue                     # 除声明外还有出现：调用点存在（漏报方向，可接受）
        for entry in entries:
            (tier_tests_only if test_count.get(name, 0) else tier_a).append(entry)

    hits = {entry["key"] for entry in tier_a + tier_tests_only}
    unexplained_a = [e for e in tier_a if e["key"] not in EXPLAINED]
    unexplained_t = [e for e in tier_tests_only if e["key"] not in EXPLAINED]

    def in_scope(entry):
        return not args or any(entry["file"].startswith(a.replace("\\", "/")) for a in args)

    print(f"扫了 {scanned} 个 .cs 收声明，语料 {len(REACH_DIRS)} 个目录，接口成员名 {len(iface_names)} 个")
    print(f"A 级（除声明本身外生产语料零出现）：{len(tier_a)} 处，未解释 {len(unexplained_a)} 处")
    for entry in sorted(unexplained_a, key=lambda e: (e["file"], e["line"])):
        if in_scope(entry):
            print(f"  A {entry['file']}:{entry['line']}  {entry['key']}  [{entry['visibility']}]")
    print(f"T 级（生产零调用、只有测试在调）：{len(tier_tests_only)} 处，未解释 {len(unexplained_t)} 处")
    for entry in sorted(unexplained_t, key=lambda e: (e["file"], e["line"])):
        if in_scope(entry):
            print(f"  T {entry['file']}:{entry['line']}  {entry['key']}  [{entry['visibility']}]")

    if show_all:
        print("已登记：")
        for entry in sorted([e for e in tier_a + tier_tests_only if e["key"] in EXPLAINED],
                            key=lambda e: e["key"]):
            print(f"  · {entry['key']}: {EXPLAINED[entry['key']][:120]}")

    stale = sorted(key for key in EXPLAINED if key not in hits)
    print(f"名单失效（登记的条目这次没再报出来，理由该删）：{len(stale)} 条")
    for key in stale:
        print(f"  ! {key}")
    return 1 if (unexplained_a or unexplained_t or stale) else 0


if __name__ == "__main__":
    sys.exit(main())
