"""变异自检：故意改坏轮次闸门生产代码，验证测试能否抓住。

每个变异：读原文 -> 精确替换（单行片段，避免换行符差异） -> 跑 Gate 测试 -> 无论结果都按原字节写回。
退出码非 0 视为"测试抓住了"（期望），退出码 0 视为"测试没抓住"（自检失败）。
"""
from __future__ import annotations

import subprocess
import sys
from pathlib import Path

ROOT = Path(r"D:\project\w1")
SESSION = ROOT / "modules/abstractions/abstractions_utils/core/misc/RoundGateSession.cs"
ENGINE = ROOT / "modules/cognition/brain_query/query2/token_budget/core/QueryEngine.cs"
PROJECT = ROOT / "modules/cognition/brain_other_tests/Brain.Other.Tests.csproj"
FILTER = "FullyQualifiedName~Core.Tests.Query.Gate"
MCP_PROJECT = ROOT / "modules/mcp/mcp_tests/Mcp.Tests.csproj"
MCP_FILTER = "FullyQualifiedName~RoundGate"

MUTATIONS = [
    ("M1 自验判断恒真", SESSION,
     "SelfAcceptPassed = invocation.Verdict == RoundGateVerdict.Pass;",
     "SelfAcceptPassed = true;", PROJECT, FILTER),
    ("M2 空转计数失效", SESSION, "IdleStreak++;", "IdleStreak += 0;", PROJECT, FILTER),
    ("M3 去掉报错调用守卫", ENGINE,
     "if (!toolResult.IsError && RoundGatePolicy.TryReadInvocation(",
     "if (RoundGatePolicy.TryReadInvocation(", PROJECT, FILTER),
    ("M4 关掉完成需自验的拦截", SESSION,
     "if (!SelfAcceptPassed)", "if (false)", PROJECT, FILTER),
    ("M5 关掉熔断绕过拦截", SESSION,
     "if (Fuse is { } fuse && recorded != fuse.ForcedChoice)", "if (false)", PROJECT, FILTER),
    ("M6 推荐改回迁移前计算", SESSION,
     "var recommendation = Recommend();", "var recommendation = baseline;", PROJECT, FILTER),
    ("M7 失败依据恒真", SESSION,
     "public bool HasFailureBasis => FailedSelfAccepts > 0;",
     "public bool HasFailureBasis => true;", PROJECT, FILTER),
    ("M8 关掉放弃必须写报告的校验",
     ROOT / "modules/mcp/mcp_tool_handlers/agent/core/RoundGateToolHandlers.cs",
     "if (parsed == RoundGateChoice.Abandon && string.IsNullOrWhiteSpace(note))",
     "if (false)", MCP_PROJECT, MCP_FILTER),
]


def run_tests(project: Path, flt: str) -> int:
    return subprocess.run(
        ["dotnet", "test", str(project), "--filter", flt, "-v", "q", "--nologo"],
        cwd=str(ROOT), capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=600,
    ).returncode


def main() -> int:
    caught = 0
    applied = 0
    for name, path, old, new, project, flt in MUTATIONS:
        raw = path.read_bytes()
        original = raw.decode("utf-8")
        if old not in original:
            print(f"[SKIP] {name}: 未找到目标片段")
            continue
        mutated = original.replace(old, new, 1)
        try:
            path.write_bytes(mutated.encode("utf-8"))
            code = run_tests(project, flt)
        finally:
            path.write_bytes(raw)
        applied += 1
        caught += 1 if code != 0 else 0
        print(f"[{'OK' if code != 0 else 'GAP'}] {name}: exit={code} {'抓住了(红)' if code != 0 else '没抓住(仍绿) ⚠'}")

    code = run_tests(PROJECT, FILTER)
    print(f"[RESTORE] 还原后全量 Gate 测试: exit={code} {'绿' if code == 0 else '红 ⚠'}")
    print(f"变异被抓住 {caught}/{applied}（应用 {applied}/{len(MUTATIONS)}）")
    return 0 if caught == applied and code == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
