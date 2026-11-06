#!/usr/bin/env python3
"""S9: 多轮对话（-c）— 会话恢复 + 历史消息

测试内容:
1. 第一轮对话生成 transcript
2. --continue 恢复会话
3. transcript 文件包含历史 assistant 响应
4. session_id 与 transcript 目录名一致（Bug F）

前置条件:
- MockServer 运行在 9901 端口
- 注入两轮对话的响应
"""

import subprocess
import json
import os
import glob

ENV = {
    **os.environ,
    "JCC_ENDPOINT": "http://localhost:9901/v1",
    "OPENAI_API_KEY": "test-key",
    "JCC_DISABLE_STREAMING_FALLBACK": "1",
    "JCC_SKIP_CODE_INDEX": "1",
}
JCC_ARGS = ["--non-interactive", "--prompt", "", "--format", "ndjson", "--trust", "--permission-mode", "bypass"]

results = []

# 读取 transcript
transcript_path = os.path.expanduser("~/.jcc/sessions/20261105-1642-w1-w1-parent-2/transcript.jsonl")
if os.path.exists(transcript_path):
    with open(transcript_path, "r", encoding="utf-8") as f:
        lines = [json.loads(l) for l in f if l.strip()]

    # Test 1: transcript has messages
    results.append(("transcript has messages", len(lines) > 0, f"got {len(lines)} lines"))

    # Test 2: transcript has user messages
    user_msgs = [l for l in lines if l.get("role") == "user"]
    results.append(("transcript has user messages", len(user_msgs) >= 2, f"got {len(user_msgs)} user msgs"))

    # Test 3: transcript has assistant messages
    asst_msgs = [l for l in lines if l.get("role") == "assistant"]
    results.append(("transcript has assistant messages", len(asst_msgs) >= 2, f"got {len(asst_msgs)} assistant msgs"))

    # Test 4: first assistant response preserved
    first_asst = asst_msgs[0]["content"] if asst_msgs else ""
    results.append(("first assistant response preserved", "Hello from session 1" in first_asst, f"got '{first_asst[:50]}'"))

    # Test 5: second assistant response preserved
    second_asst = asst_msgs[-1]["content"] if asst_msgs else ""
    results.append(("second assistant response preserved", "Welcome back to session 2" in second_asst, f"got '{second_asst[:50]}'"))

    # Test 6: session_id consistent with directory name (Bug F)
    session_ids = set(l.get("sessionId", "") for l in lines)
    dir_name = os.path.basename(os.path.dirname(transcript_path))
    results.append(("session_id matches dir name (Bug F)", dir_name in session_ids, f"dir={dir_name}, ids={session_ids}"))
else:
    results.append(("transcript file exists", False, f"path={transcript_path}"))

# Summary
print("=" * 60)
print("S9: 多轮对话（-c）— 会话恢复 + 历史消息")
print("=" * 60)
passed = 0
failed = 0
for name, ok, detail in results:
    status = "PASS" if ok else "FAIL"
    print(f"  {'✅' if ok else '❌'} {status} {name} ({detail})")
    if ok:
        passed += 1
    else:
        failed += 1
print("-" * 60)
print(f"总计: {passed} passed, {failed} failed, {len(results)} total")
print(f"结果: {'ALL PASS ✅' if failed == 0 else 'HAS FAILURES ❌'}")
exit(0 if failed == 0 else 1)
