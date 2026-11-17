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
import subprocess
import sys

REPO = pathlib.Path(__file__).resolve().parents[2]
JCC = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else REPO / "artifacts/bin/JoinCode/Debug/net10.0/jcc.exe"


def yaml_names(path: pathlib.Path) -> list[str]:
    names: list[str] = []
    for raw in path.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if line and not line.startswith("#") and line.startswith("- "):
            name = line[2:].strip().strip('"')
            if name and name not in names:
                names.append(name)
    return names


def yaml_profiles(path: pathlib.Path) -> dict[str, list[str]]:
    profiles: dict[str, list[str]] = {}
    profile: str | None = None
    for raw in path.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if not line or line.startswith("#") or line.startswith("profiles:"):
            continue
        if not line.startswith("- "):
            profile = line.rstrip(":").strip()
            profiles[profile] = []
            continue
        if profile:
            name = line[2:].strip().strip('"')
            if name and name not in profiles[profile]:
                profiles[profile].append(name)
    return profiles


def is_subsequence(actual: list[str], expected: list[str]) -> bool:
    it = iter(expected)
    return all(any(name == candidate for candidate in it) for name in actual)


def run(args: list[str]) -> str:
    env = dict(os.environ)
    env["JCC_ALLOW_DIRECT_FALLBACK"] = "1"
    proc = subprocess.run([str(JCC), *args], capture_output=True, text=True,
                          encoding="utf-8", errors="replace", env=env, timeout=240)
    return (proc.stdout or "") + (proc.stderr or "")


def main() -> int:
    if not JCC.is_file():
        print(f"jcc 不存在: {JCC}")
        return 1

    expected_sections = yaml_names(REPO / "resources/prompts/sections.yaml")
    profiles = yaml_profiles(REPO / "resources/app/modules.yaml")
    expected_modules = profiles.get("cli", [])

    prompt_out = run(["prompt"])
    actual_sections = re.findall(r"^\s+([A-Za-z_][\w]*)\s+(?:缓存|动态)\s+\d+", prompt_out, re.M)

    debug_out = run(["--debuglog", "-p", "hi"])
    actual_modules = re.findall(r"\[MODULE\] (\w+) start", debug_out)

    problems: list[str] = []
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

    print(f"段落 {len(actual_sections)}/{len(expected_sections)} 个（条件段按需注入），模块 {len(actual_modules)}/{len(expected_modules)} 个")
    if problems:
        for problem in problems:
            print("  -", problem)
        return 1
    print("显式顺序表与真机装配一致，特性里无 Order 数字。")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
