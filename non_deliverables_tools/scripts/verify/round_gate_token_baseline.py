"""统计轮次闸门常驻提示词体积：当前 vs 上一版（873067674），字符 + 粗估 token。"""
from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path

ROOT = Path(r"D:\project\w1")
SECTION_REL = "modules/cognition/prompts/sections/workflow/RoundGateSection.cs"
HANDLER_REL = "modules/mcp/mcp_tool_handlers/agent/core/RoundGateToolHandlers.cs"
OLD = "873067674"


def git_show(rev: str, rel: str) -> str:
    out = subprocess.run(["git", "show", f"{rev}:{rel}"], cwd=str(ROOT),
                         capture_output=True, text=True, encoding="utf-8", errors="replace")
    return out.stdout


def section_text(source: str) -> str:
    start = source.find('return $"""')
    if start < 0:
        return ""
    start = source.find('"""', start)
    end = source.find('"""', start + 3)
    return source[start + 3:end]


def handler_description(source: str) -> str:
    start = source.find("RoundGate,")
    end = source.find("MessageRoleEnumConstants.System)]", start)
    return source[start + len("RoundGate,"):end]


def stats(label: str, text: str) -> dict:
    cjk = sum(1 for ch in text if "\u4e00" <= ch <= "\u9fff")
    ascii_chars = sum(1 for ch in text if ord(ch) < 128)
    other = len(text) - cjk - ascii_chars
    return {"label": label, "chars": len(text), "cjk": cjk, "ascii": ascii_chars,
            "other": other, "est_tokens": round(cjk * 1.0 + ascii_chars / 4 + other * 0.5)}


def main() -> int:
    rows = []
    for rev, tag in ((OLD, "旧"), ("HEAD", "新")):
        src = git_show(rev, SECTION_REL)
        rows.append(stats(f"Section {tag}", section_text(src)))
        hsrc = git_show(rev, HANDLER_REL)
        rows.append(stats(f"工具描述 {tag}", handler_description(hsrc)))
    rows.append(stats("新版单次选项提示词均值", "【闸门 · 3 自验】对已有产出做自我阶段性验收（不是\"我觉得差不多\"）：\n1) 逐条对照原始需求；2) 必须项全满足才算通过…"))

    print(json.dumps(rows, ensure_ascii=False, indent=2))
    old = next(r for r in rows if r["label"] == "Section 旧")["est_tokens"]
    new = next(r for r in rows if r["label"] == "Section 新")["est_tokens"]
    print(f"Section 常驻 token 粗估: {old} -> {new} ({(new - old) / old * 100:+.0f}%)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
