#!/usr/bin/env python3
"""真机校验「显式顺序表」是否生效 — 启动 jcc 比对实际装配顺序与 YAML 声明。

不做单元测试替代：这里跑的是构建产物 jcc.exe，分别核对
  1. `jcc prompt` 打印的段落顺序 == resources/prompts/sections.yaml
  2. `jcc --debuglog -p` 的模块启动顺序 == resources/app/modules.yaml
  3. 源码里不再有 PromptSection/AppModule 的 Order 数字

用法: python non_deliverables_tools/scripts/Verify-ExplicitOrder.py [jcc.exe 路径]
退出码: 0 全通过 / 1 有漂移。
"""
from __future__ import annotations

import os
import pathlib
import re
import signal
import subprocess
import sys
import tempfile

REPO = pathlib.Path(__file__).resolve().parents[2]
JCC = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else REPO / "artifacts/bin/JoinCode/Debug/net10.0/jcc.exe"
COMMAND_TIMEOUT_SECONDS = 12


def yaml_names(path: pathlib.Path) -> list[str]:
    names: list[str] = []
    for raw in path.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if line and not line.startswith("#") and line.startswith("- "):
            name = line[2:].strip().strip('"')
            if name and name not in names:
                names.append(name)
    return names


def yaml_profiles(path: pathlib.Path) -> dict[str, tuple[str, list[str]]]:
    """解析 modules.yaml —— profiles: → <profile>: → assembly: / modules: → - 名称。"""
    profiles: dict[str, tuple[str, list[str]]] = {}
    profile: str | None = None
    assembly = ""
    modules: list[str] = []
    in_modules = False

    def flush() -> None:
        if profile is not None:
            profiles[profile] = (assembly, list(modules))

    for raw in path.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        if not line.startswith("- "):
            if line.endswith(":"):
                key = line[:-1].strip()
                if key == "modules":
                    in_modules = True
                    continue
                if key == "assembly":
                    continue
                flush()
                profile = key
                assembly = ""
                modules = []
                in_modules = False
                continue
            key, _, value = line.partition(":")
            key = key.strip()
            if key == "assembly":
                assembly = value.strip().strip('"')
                continue
            if key == "modules":
                in_modules = True
                continue
            continue
        if profile is not None and in_modules:
            name = line[2:].strip().strip('"')
            if name and name not in modules:
                modules.append(name)
    flush()
    return profiles


def is_subsequence(actual: list[str], expected: list[str]) -> bool:
    it = iter(expected)
    return all(any(name == candidate for candidate in it) for name in actual)


def run(args: list[str]) -> str:
    """跑 jcc 子命令，最多 12 秒；超时尽力连同子进程树一起终止。

    `-p hi` 的真实耗时取决于 LLM 是否可达，但 `[MODULE] ... start` 在启动阶段打完，
    超时后仍用已产出输出比对顺序。Windows 上 jcc 会拉起 Hub，若用管道捕获输出，
    Hub 继承管道后会让 communicate 等 EOF 到 job 超时；文件承载只等父进程句柄。
    """
    env = dict(os.environ)
    env["JCC_ALLOW_DIRECT_FALLBACK"] = "1"
    with tempfile.TemporaryFile() as capture:
        proc = subprocess.Popen(
            [str(JCC), *args],
            stdout=capture,
            stderr=subprocess.STDOUT,
            env=env,
            start_new_session=os.name != "nt")
        try:
            proc.wait(timeout=COMMAND_TIMEOUT_SECONDS)
        except subprocess.TimeoutExpired:
            if os.name == "nt":
                subprocess.run(
                    ["taskkill", "/PID", str(proc.pid), "/T", "/F"],
                    stdout=subprocess.DEVNULL,
                    stderr=subprocess.DEVNULL,
                    timeout=5,
                    check=False)
            else:
                os.killpg(proc.pid, signal.SIGKILL)
            proc.wait()
            print(f"  ! 子命令超时（jcc {' '.join(args)}，{COMMAND_TIMEOUT_SECONDS}s）—— 通常是 LLM 不可达仍在重试，不是顺序漂移", file=sys.stderr)
        capture.seek(0)
        return capture.read().decode("utf-8", "replace")


def main() -> int:
    if not JCC.is_file():
        print(f"jcc 不存在: {JCC}")
        return 1

    expected_sections = yaml_names(REPO / "resources/prompts/sections.yaml")
    profiles = yaml_profiles(REPO / "resources/app/modules.yaml")
    cli_assembly, expected_modules = profiles.get("cli", ("", []))
    gui_assembly, gui_modules = profiles.get("gui", ("", []))

    prompt_out = run(["prompt"])
    actual_sections = re.findall(r"^\s+([A-Za-z_][\w]*)\s+(?:缓存|动态)\s+\d+", prompt_out, re.M)

    debug_out = run(["--debuglog", "-p", "hi"])
    actual_modules = re.findall(r"\[MODULE\] (\w+) start", debug_out)

    problems: list[str] = []
    # 每个 profile 必须声明归属程序集 —— 没有归属就分不清「名字属于另一个程序集」与「名字打错了」
    if not cli_assembly:
        problems.append("modules.yaml 的 cli profile 缺少 assembly 声明")
    if not gui_assembly:
        problems.append("modules.yaml 的 gui profile 缺少 assembly 声明")
    if cli_assembly == gui_assembly and cli_assembly:
        problems.append(f"cli 与 gui profile 归属同一程序集（{cli_assembly}）：两者会生成同名类型（CS0436）")
    if not gui_modules:
        problems.append("modules.yaml 的 gui profile 为空")
    # 条件注入段落（关键词/AgentMode）不在无输入的首轮列表里，所以只验相对顺序，
    # 不要求 67 个段全部出现在 `jcc prompt` 的第一页。
    if not actual_sections or not is_subsequence(actual_sections, expected_sections):
        problems.append(f"段落顺序漂移: 实际前5={actual_sections[:5]}，YAML 前5={expected_sections[:5]}")
    if actual_modules != expected_modules:
        problems.append(f"模块顺序漂移: 期望 {expected_modules}; 实际 {actual_modules}")

    leftovers: list[str] = []
    for path in (REPO / "modules/cognition/prompts/sections").rglob("*.cs"):
        if re.search(r"\[PromptSection\([^\]]*\bOrder\s*=", path.read_text(encoding="utf-8"), re.S):
            leftovers.append(str(path.relative_to(REPO)))
    for path in (REPO / "modules/apps/engine/app/modules").glob("*Module.cs"):
        if re.search(r"\[AppModule\(Order\s*=", path.read_text(encoding="utf-8")):
            leftovers.append(str(path.relative_to(REPO)))
    if leftovers:
        problems.append(f"仍有特性 Order 数字: {leftovers}")

    print(f"段落 {len(actual_sections)}/{len(expected_sections)} 个（条件段按需注入），"
          f"模块 {len(actual_modules)}/{len(expected_modules)} 个（cli←{cli_assembly}，gui←{gui_assembly} {len(gui_modules)} 个）")
    if problems:
        for problem in problems:
            print("  -", problem)
        return 1
    print("显式顺序表与真机装配一致，特性里无 Order 数字。")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
