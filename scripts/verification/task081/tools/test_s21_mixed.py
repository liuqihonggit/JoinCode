#!/usr/bin/env python3
"""S21: thinking + tool_use 混合 + content block 顺序"""
import json
import subprocess
import time
import urllib.request
import os

JCC = "artifacts/bin/JoinCode/Debug/net10.0/jcc.exe"
MOCK_CTRL = "http://localhost:9901/__control__"

def clear_mock():
    req = urllib.request.Request(f"{MOCK_CTRL}/clear", method="POST")
    urllib.request.urlopen(req, timeout=5)

def inject(data):
    body = json.dumps(data).encode()
    req = urllib.request.Request(f"{MOCK_CTRL}/inject", data=body, headers={"Content-Type": "application/json"}, method="POST")
    urllib.request.urlopen(req, timeout=5)

def run_jcc(prompt="test", timeout=30):
    env = {"JCC_ENDPOINT": "http://localhost:9901/v1", "OPENAI_API_KEY": "test-key", "JCC_DISABLE_STREAMING_FALLBACK": "1"}
    cmd = ["dotnet", JCC, "--non-interactive", "--prompt", prompt, "--format", "ndjson", "--trust", "--permission-mode", "bypass"]
    start = time.time()
    proc = subprocess.run(cmd, capture_output=True, text=True, timeout=timeout, env={**os.environ, **env}, cwd=".")
    return proc.returncode, time.time() - start, proc.stdout, proc.stderr

def parse_ndjson(stdout):
    results = []
    for line in stdout.strip().splitlines():
        if line.startswith("{"):
            try: results.append(json.loads(line))
            except: pass
    return results

# === S21: thinking + tool_use 混合 ===
print("=== S21: thinking + tool_use 混合 ===")
clear_mock()

# 第一轮：thinking + tool_call
chunks1 = [
    'data: {"choices":[{"delta":{"reasoning_content":"Let me think about this"},"index":0}]}\n\n',
    'data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_s21_001","function":{"name":"bash","arguments":"{\\"command\\":\\"echo hello\\"}"}}]}}]}\n\n',
    'data: {"choices":[{"delta":{},"finish_reason":"tool_calls","index":0}]}\n\n',
    'data: [DONE]\n\n',
]
inject({"is_stream": True, "content_type": "text/event-stream", "stream_chunks": chunks1})

# 第二轮：最终文本
chunks2 = [
    'data: {"choices":[{"delta":{"content":"Done"},"index":0}]}\n\n',
    'data: {"choices":[{"delta":{},"finish_reason":"stop","index":0}]}\n\n',
    'data: [DONE]\n\n',
]
inject({"is_stream": True, "content_type": "text/event-stream", "stream_chunks": chunks2})

rc, elapsed, stdout, stderr = run_jcc("Think then act")
events = parse_ndjson(stdout)

print(f"exit_code={rc}, elapsed={elapsed:.1f}s")
for e in events:
    if e.get("type") == "assistant":
        content = e.get("message", {}).get("content", [])
        block_types = [b.get("type") for b in content]
        print(f"  assistant content blocks: {block_types}")
        for b in content:
            if b.get("type") == "thinking":
                print(f"    thinking: {repr(b.get('thinking', '')[:50])}")
            elif b.get("type") == "tool_use":
                print(f"    tool_use: name={b.get('name')}, id={b.get('id')}")
            elif b.get("type") == "text":
                print(f"    text: {repr(b.get('text', '')[:50])}")

# 验证 thinking + tool_use 在同一 assistant 消息
assistant_msgs = [e for e in events if e.get("type") == "assistant"]
if len(assistant_msgs) >= 1:
    first = assistant_msgs[0]
    content = first.get("message", {}).get("content", [])
    types = [b.get("type") for b in content]
    if "thinking" in types and "tool_use" in types:
        print("✅ thinking + tool_use 在同一 assistant 消息")
        # 检查顺序：thinking 应该在 tool_use 之前
        thinking_idx = types.index("thinking")
        tool_use_idx = types.index("tool_use")
        if thinking_idx < tool_use_idx:
            print("✅ thinking 在 tool_use 之前")
        else:
            print("🔴 thinking 在 tool_use 之后 — 顺序错误")
    elif "tool_use" in types and "thinking" not in types:
        print("⚠️ thinking 未出现在 assistant 消息 — 可能被分离到单独消息或丢失")
    else:
        print(f"⚠️ content types: {types}")

# === S21b: text + thinking + text 混合 ===
print("\n=== S21b: text + thinking + text 混合 ===")
clear_mock()

chunks = [
    'data: {"choices":[{"delta":{"content":"Before thinking"},"index":0}]}\n\n',
    'data: {"choices":[{"delta":{"reasoning_content":"Internal reasoning"},"index":0}]}\n\n',
    'data: {"choices":[{"delta":{"content":" after thinking"},"index":0}]}\n\n',
    'data: {"choices":[{"delta":{},"finish_reason":"stop","index":0}]}\n\n',
    'data: [DONE]\n\n',
]
inject({"is_stream": True, "content_type": "text/event-stream", "stream_chunks": chunks})

rc, elapsed, stdout, stderr = run_jcc("Mixed content")
events = parse_ndjson(stdout)

print(f"exit_code={rc}, elapsed={elapsed:.1f}s")
for e in events:
    if e.get("type") == "assistant":
        content = e.get("message", {}).get("content", [])
        block_types = [b.get("type") for b in content]
        print(f"  assistant content blocks: {block_types}")
        for b in content:
            if b.get("type") == "thinking":
                print(f"    thinking: {repr(b.get('thinking', ''))}")
            elif b.get("type") == "text":
                print(f"    text: {repr(b.get('text', ''))}")

# 检查 text 是否合并
assistant_msgs = [e for e in events if e.get("type") == "assistant"]
if assistant_msgs:
    content = assistant_msgs[0].get("message", {}).get("content", [])
    text_blocks = [b for b in content if b.get("type") == "text"]
    thinking_blocks = [b for b in content if b.get("type") == "thinking"]
    if len(text_blocks) == 1 and len(thinking_blocks) == 1:
        combined_text = text_blocks[0].get("text", "")
        if "Before thinking" in combined_text and "after thinking" in combined_text:
            print(f"✅ 两个 text block 合并: {repr(combined_text)}")
        else:
            print(f"⚠️ text 合并但内容不完整: {repr(combined_text)}")
    elif len(text_blocks) == 2:
        print(f"⚠️ 两个 text block 未合并: {[b.get('text') for b in text_blocks]}")
    else:
        print(f"⚠️ text_blocks={len(text_blocks)}, thinking_blocks={len(thinking_blocks)}")
