#!/usr/bin/env python3
"""S18: 多轮工具调用 + num_turns 递增 + result 事件细节

测试矩阵:
  T1: 串行双工具调用（bash → read → 最终文本）→ num_turns=3
  T2: 三轮工具调用 → num_turns=4
  T3: usage chunk 含完整字段 → 检查 result.usage
"""
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

def inject_stream(chunks):
    inject({"is_stream": True, "content_type": "text/event-stream", "stream_chunks": chunks})

def run_jcc(prompt="test", timeout=30):
    env = {
        "JCC_ENDPOINT": "http://localhost:9901/v1",
        "OPENAI_API_KEY": "test-key",
        "JCC_DEBUGLOG": "1",
        "JCC_DISABLE_STREAMING_FALLBACK": "1",
    }
    cmd = ["dotnet", JCC, "--non-interactive", "--prompt", prompt, "--format", "ndjson", "--trust", "--permission-mode", "bypass"]
    start = time.time()
    proc = subprocess.run(cmd, capture_output=True, text=True, timeout=timeout, env={**os.environ, **env}, cwd=".")
    return proc.returncode, time.time() - start, proc.stdout, proc.stderr

def parse_ndjson(stdout):
    results = []
    for line in stdout.strip().splitlines():
        if line.startswith("{"):
            try:
                results.append(json.loads(line))
            except:
                pass
    return results

def test_t1_serial_tools():
    """T1: 串行双工具调用（bash → read → 最终文本）→ num_turns=3"""
    print("\n=== T1: 串行双工具调用 ===")
    clear_mock()

    # 第一轮：bash 工具调用
    inject_stream([
        'data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_s1_001","function":{"name":"bash","arguments":"{\\"command\\":\\"echo step1\\"}"}}]}}]}\n\n',
        'data: {"choices":[{"delta":{},"finish_reason":"tool_calls","index":0}]}\n\n',
        'data: [DONE]\n\n',
    ])
    # 第二轮：read 工具调用
    inject_stream([
        'data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_s1_002","function":{"name":"bash","arguments":"{\\"command\\":\\"echo step2\\"}"}}]}}]}\n\n',
        'data: {"choices":[{"delta":{},"finish_reason":"tool_calls","index":0}]}\n\n',
        'data: [DONE]\n\n',
    ])
    # 第三轮：最终文本
    inject_stream([
        'data: {"choices":[{"delta":{"content":"All steps done"},"index":0}]}\n\n',
        'data: {"choices":[{"delta":{},"finish_reason":"stop","index":0}]}\n\n',
        'data: [DONE]\n\n',
    ])

    rc, elapsed, stdout, stderr = run_jcc("Run two steps")
    events = parse_ndjson(stdout)

    print(f"exit_code={rc}, elapsed={elapsed:.1f}s")
    types = [e.get("type") for e in events]
    print(f"事件序列: {types}")

    # 统计 tool_use 和 tool_result
    tool_uses = sum(1 for e in events if e.get("type") == "assistant"
                    for b in e.get("message", {}).get("content", []) if b.get("type") == "tool_use")
    tool_results = sum(1 for e in events if e.get("type") == "user"
                       for b in e.get("message", {}).get("content", []) if b.get("type") == "tool_result")

    # 获取 result 事件
    result = next((e for e in events if e.get("type") == "result"), None)
    if result:
        num_turns = result.get("num_turns")
        print(f"num_turns={num_turns}, tool_uses={tool_uses}, tool_results={tool_results}")
        if num_turns == 3:
            print("✅ num_turns=3 — 串行双工具调用正确递增")
        else:
            print(f"🔴 Bug: num_turns={num_turns}, 期望 3")

    return rc

def test_t2_triple_tools():
    """T2: 三轮工具调用 → num_turns=4"""
    print("\n=== T2: 三轮工具调用 ===")
    clear_mock()

    for i in range(3):
        inject_stream([
            f'data: {{"choices":[{{"delta":{{"tool_calls":[{{"index":0,"id":"call_s2_{i:03d}","function":{{"name":"bash","arguments":"{{\\"command\\":\\"echo step{i+1}\\"}}"}}]}}]}}}}\n\n',
            'data: {"choices":[{"delta":{},"finish_reason":"tool_calls","index":0}]}\n\n',
            'data: [DONE]\n\n',
        ])
    inject_stream([
        'data: {"choices":[{"delta":{"content":"All three done"},"index":0}]}\n\n',
        'data: {"choices":[{"delta":{},"finish_reason":"stop","index":0}]}\n\n',
        'data: [DONE]\n\n',
    ])

    rc, elapsed, stdout, stderr = run_jcc("Run three steps")
    events = parse_ndjson(stdout)

    print(f"exit_code={rc}, elapsed={elapsed:.1f}s")
    result = next((e for e in events if e.get("type") == "result"), None)
    if result:
        num_turns = result.get("num_turns")
        print(f"num_turns={num_turns}")
        if num_turns == 4:
            print("✅ num_turns=4 — 三轮工具调用正确递增")
        else:
            print(f"🔴 Bug: num_turns={num_turns}, 期望 4")

    return rc

def test_t3_usage_fields():
    """T3: usage chunk 含完整字段 → 检查 result.usage"""
    print("\n=== T3: usage 完整字段 ===")
    clear_mock()

    chunks = [
        'data: {"choices":[{"delta":{"content":"Hello"},"index":0}]}\n\n',
        'data: {"choices":[{"delta":{},"finish_reason":"stop","index":0}]}\n\n',
        'data: {"choices":[],"usage":{"prompt_tokens":100,"completion_tokens":50,"total_tokens":150,"prompt_tokens_details":{"cached_tokens":30},"completion_tokens_details":{"reasoning_tokens":10}}}\n\n',
        'data: [DONE]\n\n',
    ]
    inject_stream(chunks)

    rc, elapsed, stdout, stderr = run_jcc("Test usage")
    events = parse_ndjson(stdout)

    print(f"exit_code={rc}, elapsed={elapsed:.1f}s")
    result = next((e for e in events if e.get("type") == "result"), None)
    if result:
        usage = result.get("usage", {})
        print(f"usage: {json.dumps(usage, indent=2)}")
        print(f"duration_ms: {result.get('duration_ms')}")
        print(f"duration_api_ms: {result.get('duration_api_ms')}")
        print(f"total_cost_usd: {result.get('total_cost_usd')}")

        if usage.get("input_tokens") == 100:
            print("✅ input_tokens=100 正确")
        else:
            print(f"⚠️ input_tokens={usage.get('input_tokens')}, 期望 100")

        if usage.get("output_tokens") == 50:
            print("✅ output_tokens=50 正确")
        else:
            print(f"⚠️ output_tokens={usage.get('output_tokens')}, 期望 50")

        if usage.get("cache_read_input_tokens") == 30:
            print("✅ cache_read_input_tokens=30 正确")
        else:
            print(f"⚠️ cache_read_input_tokens={usage.get('cache_read_input_tokens')}, 期望 30")

    return rc

if __name__ == "__main__":
    print("S18: 多轮工具调用 + num_turns + usage")
    t1 = test_t1_serial_tools()
    t2 = test_t2_triple_tools()
    t3 = test_t3_usage_fields()

    print("\n=== S18 总结 ===")
    print(f"T1 串行双工具: rc={t1}")
    print(f"T2 三轮工具:   rc={t2}")
    print(f"T3 usage字段:  rc={t3}")
