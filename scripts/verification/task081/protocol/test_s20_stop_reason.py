#!/usr/bin/env python3
"""S20: finish_reason=length → stop_reason=max_tokens 验证"""
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
    cmd = [JCC, "--non-interactive", "--prompt", prompt, "--format", "ndjson", "--trust", "--permission-mode", "bypass"]
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

# 测试 finish_reason=length
print("=== S20: finish_reason=length → stop_reason=max_tokens ===")
clear_mock()

chunks = [
    'data: {"choices":[{"delta":{"content":"Truncated text"},"index":0}]}\n\n',
    'data: {"choices":[{"delta":{},"finish_reason":"length","index":0}]}\n\n',
    'data: [DONE]\n\n',
]
inject({"is_stream": True, "content_type": "text/event-stream", "stream_chunks": chunks})

rc, elapsed, stdout, stderr = run_jcc("Test max tokens")
events = parse_ndjson(stdout)

print(f"exit_code={rc}, elapsed={elapsed:.1f}s")
result = next((e for e in events if e.get("type") == "result"), None)
if result:
    stop_reason = result.get('stop_reason')
    print(f"stop_reason: {stop_reason}")
    if stop_reason == "max_tokens":
        print("✅ finish_reason=length → stop_reason=max_tokens 正确")
    else:
        print(f"🔴 stop_reason={stop_reason}, 期望 max_tokens")
else:
    print("🔴 无 result 事件")
    print(stdout[:500])

# 测试 finish_reason=content_filter
print("\n=== S20b: finish_reason=content_filter → stop_reason=content_filter ===")
clear_mock()

chunks = [
    'data: {"choices":[{"delta":{"content":"Filtered"},"index":0}]}\n\n',
    'data: {"choices":[{"delta":{},"finish_reason":"content_filter","index":0}]}\n\n',
    'data: [DONE]\n\n',
]
inject({"is_stream": True, "content_type": "text/event-stream", "stream_chunks": chunks})

rc, elapsed, stdout, stderr = run_jcc("Test content filter")
events = parse_ndjson(stdout)

result = next((e for e in events if e.get("type") == "result"), None)
if result:
    stop_reason = result.get('stop_reason')
    print(f"stop_reason: {stop_reason}")
    if stop_reason == "content_filter":
        print("✅ finish_reason=content_filter → stop_reason=content_filter 正确")
    else:
        print(f"🔴 stop_reason={stop_reason}, 期望 content_filter")

# 测试 finish_reason=tool_calls → stop_reason=tool_calls
print("\n=== S20c: finish_reason=tool_calls → stop_reason=tool_calls (最终轮) ===")
clear_mock()

# 第一轮：工具调用
inject({"is_stream": True, "content_type": "text/event-stream", "stream_chunks": [
    'data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_s20_001","function":{"name":"bash","arguments":"{\\"command\\":\\"echo hi\\"}"}}]}}]}\n\n',
    'data: {"choices":[{"delta":{},"finish_reason":"tool_calls","index":0}]}\n\n',
    'data: [DONE]\n\n',
]})
# 第二轮：最终文本（finish_reason=stop）
inject({"is_stream": True, "content_type": "text/event-stream", "stream_chunks": [
    'data: {"choices":[{"delta":{"content":"Done"},"index":0}]}\n\n',
    'data: {"choices":[{"delta":{},"finish_reason":"stop","index":0}]}\n\n',
    'data: [DONE]\n\n',
]})

rc, elapsed, stdout, stderr = run_jcc("Test tool calls stop reason")
events = parse_ndjson(stdout)

result = next((e for e in events if e.get("type") == "result"), None)
if result:
    stop_reason = result.get('stop_reason')
    print(f"stop_reason: {stop_reason}")
    # 最终轮是 finish_reason=stop，所以 stop_reason 应该是 end_turn
    if stop_reason == "end_turn":
        print("✅ 最终轮 finish_reason=stop → stop_reason=end_turn 正确（工具调用中间轮的 tool_calls 不影响最终 stop_reason）")
    else:
        print(f"⚠️ stop_reason={stop_reason}, 期望 end_turn（最终轮）")
