#!/usr/bin/env python3
"""检测 Task.Delay 等待 fire-and-forget 副作用的 flaky 风险。

分类规则：
- RISK: Task.Delay 前 5 行内有 fire-and-forget 调用(TellAsync/TellBroadcastAsync/
  SendAsync/TrySend/RegisterAgentAsync/UnregisterAgentAsync/Tell)，且不在 while/for 循环内
- POLL: 在 while/for 循环内(轮询模式)
- OTHER: 其他(模拟时间/退避/超时测试等)

输出: RISK 列表(人工确认) + 统计
"""
import re, sys, pathlib

FIRE_FORGET = re.compile(
    r'\b(TellAsync|TellBroadcastAsync|SendAsync|TrySend|RegisterAgentAsync|UnregisterAgentAsync|\.Tell\b|\.Ask\b)\s*\('
)
TASK_DELAY = re.compile(r'\bTask\.Delay\s*\(')
LOOP_INDENT = re.compile(r'^\s*(while|for|do)\b')

def classify(lines, delay_idx):
    # 检查是否在循环内(往前找最近的缩进小于等于当前的开括号行)
    cur_indent = len(lines[delay_idx]) - len(lines[delay_idx].lstrip())
    for i in range(delay_idx - 1, max(-1, delay_idx - 30), -1):
        ln = lines[i]
        if LOOP_INDENT.match(ln):
            indent = len(ln) - len(ln.lstrip())
            if indent < cur_indent:
                return "POLL"
    # 检查前 5 行是否有 fire-and-forget 调用
    start = max(0, delay_idx - 5)
    for i in range(start, delay_idx):
        if FIRE_FORGET.search(lines[i]):
            return "RISK"
    return "OTHER"

def main():
    root = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else pathlib.Path("D:/project/w2")
    test_dirs = [p for p in root.rglob("*.cs") if ".tests" in str(p).replace("\\", "/").lower()
                 or "/test/" in str(p).replace("\\", "/").lower()
                 and "/obj/" not in str(p) and "/bin/" not in str(p)]
    risk, poll, other = [], 0, 0
    for f in test_dirs:
        try:
            lines = f.read_text(encoding="utf-8").splitlines()
        except Exception:
            continue
        for idx, ln in enumerate(lines):
            if not TASK_DELAY.search(ln):
                continue
            cat = classify(lines, idx)
            if cat == "RISK":
                ctx = lines[max(0,idx-3):idx+2]
                risk.append((str(f).replace("D:/project/w2/",""), idx+1, ln.strip(), ctx))
            elif cat == "POLL":
                poll += 1
            else:
                other += 1
    print(f"=== RISK(疑似 fire-and-forget 等待): {len(risk)} ===")
    for path, line, code, ctx in risk:
        print(f"\n[{path}:{line}] {code}")
        for c in ctx:
            print(f"    {c.rstrip()}")
    print(f"\n=== POLL(轮询循环内): {poll} ===")
    print(f"=== OTHER(模拟时间/退避等): {other} ===")

if __name__ == "__main__":
    main()
