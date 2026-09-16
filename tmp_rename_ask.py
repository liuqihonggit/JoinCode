"""
Query→Ask 重命名脚本 — 明确 Ask 模式语义
QueryXxxAsync → AskXxxAsync (发消息等回复)
"""
import sys
import hashlib

FILE = r"D:\project\w1\llm\agents\Coordinator\Fork\ForkSubAgentManagerActor.cs"

RENAMES = {
    "QueryForkDepthAsync": "AskForkDepthAsync",
    "QueryBuildForkResultAsync": "AskBuildForkResultAsync",
    "QueryForkEntryAsync": "AskForkEntryAsync",
}

def main():
    execute = "--execute" in sys.argv

    with open(FILE, "r", encoding="utf-8-sig") as f:
        lines = f.readlines()
    content = "".join(lines)
    original_hash = hashlib.md5(content.encode("utf-8")).hexdigest()

    print(f"文件: {FILE}")
    print(f"原始 hash: {original_hash}")
    print()

    total = 0
    for old, new in RENAMES.items():
        hits = []
        for i, line in enumerate(lines, 1):
            if old in line:
                hits.append((i, line.strip()[:100]))
        print(f"{old} -> {new}: {len(hits)} 处")
        for line_no, line in hits:
            print(f"  L{line_no}: {line}")
        total += len(hits)
        print()

    print(f"总计: {total} 处替换")

    if not execute:
        print("\n[预览模式] 加 --execute 执行替换")
        return

    for old, new in RENAMES.items():
        content = content.replace(old, new)

    new_hash = hashlib.md5(content.encode("utf-8")).hexdigest()

    with open(FILE, "w", encoding="utf-8-sig") as f:
        f.write(content)

    print(f"\n[执行完成]")
    print(f"  原始 hash: {original_hash}")
    print(f"  新 hash:   {new_hash}")
    print(f"  变化: {'有' if original_hash != new_hash else '无'}")

if __name__ == "__main__":
    main()
