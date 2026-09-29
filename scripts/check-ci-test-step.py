"""把 code-quality.yml 的 Test 步骤拿出来，用假 dotnet 喂七种输出形状，验它的过/不过判据。

这条闸门 2026-09-26 与 09-30 各坏过一次，两种病都看不见：一次是 runner 的 shell: bash 自带 -e，
第一趟红就中止（语法与 YAML 解析全过）；一次是只认"某一趟全绿"，于是按 #G1-I 的偶发率永远挡人。
改 Test 步骤之后就跑一次：python scripts/check-ci-test-step.py（不进 CI——这台与 runner 都不假设装了 python）。

它验的是判据而不是测试结果：期望值写在 SCENARIOS 的第二个元组里（退出码 / dotnet 被调用几次）。
"""

import io
import os
import shutil
import stat
import subprocess
import tempfile

workflow = io.open(".github/workflows/code-quality.yml", encoding="utf-8-sig").read()
start = workflow.index("      - name: Test\n")
end = workflow.index("\n      - name: ", start)
body = workflow[start:end]
body = body[body.index("          set -uo pipefail"):]
script = "\n".join(line[10:] if line.startswith(" " * 10) else line for line in body.splitlines()).rstrip() + "\n"

tmp = tempfile.mkdtemp(prefix="ci-test-step-")
os.makedirs(os.path.join(tmp, "bin"))
io.open(os.path.join(tmp, "step.sh"), "w", encoding="utf-8", newline="\n").write(script)

SUMMARY = "  - Failed: {failed}, Passed: 1290, Skipped: 0, Total: 1296, Duration: 1 m"


def shape(failures):
    """CI 上 dotnet test 的真实形状：受害者一行 + 错误消息一行 + 汇总行（英文，带 Failed:/Total:）。"""
    if failures == 0:
        return "Passed!" + SUMMARY.format(failed=0) + "\n"
    cases = "\n".join(
        "[xUnit.net 00:00:37.80]     [Test Case Cleanup Failure (Some.AvaloniaTests.Case%d)] System.InvalidOperationException"
        "\n   [Test Case Cleanup Failure (Some.AvaloniaTests.Case%d)]: System.InvalidOperationException : "
        "The calling thread cannot access this object because a different thread owns it." % (i, i)
        for i in range(failures))
    return cases + "\nFailed!" + SUMMARY.format(failed=failures) + "\n"


# 场景 -> (逐次调用要吐出的形状（最后一份重复使用）, 期望的 exit 码与 dotnet 调用次数)。
# 名字里的"绿/红"说的是 dotnet test 的退出码，不是这一趟的结论。
SCENARIOS = {
    "第1趟全绿": ([shape(0)], (0, 1)),
    "第1趟纯签名就该放行（不再要一趟全绿）": ([shape(1), shape(0)], (0, 1)),
    "三趟都纯签名且都在信封内": ([shape(3)], (0, 1)),
    "混进真缺陷（3 红、只有 1 条带签名）": ([shape(1).replace("Failed: 1,", "Failed: 3,")], (1, 1)),
    "纯签名但超出信封（5 条 x 3 趟）": ([shape(5)], (1, 3)),
    "退出码非 0 但读不到失败计数": (["dotnet test 自己崩了\n"], (1, 1)),
    "红但一条签名都没有（真缺陷）": ([("Failed!" + SUMMARY.format(failed=1))], (1, 1)),
}

fake_dir = os.path.join(tmp, "bin")
for name, (shapes, (want_exit, want_calls)) in SCENARIOS.items():
    counter = os.path.join(tmp, "calls-%s.txt" % name)
    if os.path.exists(counter):
        os.remove(counter)

    lines = [
        "#!/bin/bash",
        "n=$(cat '%s' 2>/dev/null || echo 0); n=$((n+1)); echo $n > '%s'" % (counter, counter),
        "case $n in",
    ]
    for index, text in enumerate(shapes, start=1):
        lines.append("  %d) printf %%s %s; %s ;;" % (
            index, subprocess.list2cmdline([text.rstrip("\n")]),
            "exit 0" if text.startswith("Passed!") else "exit 1"))
    # 最后一份形状重复使用：三趟同形的场景就靠它
    lines += ["  *) printf %%s %s; exit %s;;" % (
        subprocess.list2cmdline([shapes[-1].strip()]),
        "0" if shapes[-1].startswith("Passed!") else "1"), "esac"]
    fake = os.path.join(fake_dir, "dotnet")
    io.open(fake, "w", encoding="utf-8", newline="\n").write("\n".join(lines) + "\n")
    os.chmod(fake, os.stat(fake).st_mode | stat.S_IEXEC | stat.S_IXGRP)

    env = dict(os.environ, PATH=fake_dir + os.pathsep + os.environ["PATH"],
               Solution_Name="LanMountainDesktop.slnx", RUNNER_TEMP=tmp)
    proc = subprocess.run(["bash", "-e", "-o", "pipefail", os.path.join(tmp, "step.sh")],
                          capture_output=True, text=True, encoding="utf-8", errors="replace", env=env)
    calls = int(io.open(counter, encoding="utf-8").read().strip()) if os.path.exists(counter) else 0
    mark = "OK  " if (proc.returncode, calls) == (want_exit, want_calls) else "不匹配"
    print("== %s %-34s exit=%d 调用=%d（期望 %d/%d）" % (mark, name, proc.returncode, calls, want_exit, want_calls))
    for entry in [line for line in proc.stdout.splitlines() if "::error" in line or "::notice" in line][:2]:
        print("     " + entry[:170])

shutil.rmtree(tmp, ignore_errors=True)
