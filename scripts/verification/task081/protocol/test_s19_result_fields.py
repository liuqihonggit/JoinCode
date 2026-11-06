#!/usr/bin/env python3
"""S19: 多轮工具调用下 result 事件字段验证"""
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

def make_tool_chunk(call_id, tool_name, args_dict):
    chunk = {"choices": [{"delta": {"tool_calls": [{"index": 0, "id": call_id, "function": {"name": tool_name, "arguments": json.dumps(args_dict)}}]}}]}
    return f"data: {json.dumps(chunk)}\n\n"

def make_text_chunk(text):
    return f'data: {json.dumps({"choices": [{"delta": {"content": text}, "index": 0}]})}\n\n'

def make_stop_chunk(finish_reason="stop"):
    return f'data: {json.dumps({"choices": [{"delta": {}, "finish_reason": finish_reason, "index": 0}]})}\n\n'

def make_usage_chunk(prompt_tokens, completion_tokens, cached=0):
    usage = {"prompt_tokens": prompt_tokens, "completion_tokens": completion_tokens, "total_tokens": prompt_tokens + completion_tokens}
    if cached > 0:
        usage["prompt_tokens_details"] = {"cached_tokens": cached}
    return f'data: {json.dumps({"choices": [], "usage": usage})}\n\n'

DONE = 'data: [DONE]\n\n'

def inject_tool_call(call_id, tool_name, args_dict):
    inject({"is_stream": True, "content_type": "text/event-stream",
            "stream_chunks": [make_tool_chunk(call_id, tool_name, args_dict), make_stop_chunk("tool_calls"), DONE]})

def inject_text_response(text, with_usage=False, pt=100, ct=50, cached=0):
    chunks = [make_text_chunk(text), make_stop_chunk("stop")]
    if with_usage:
        chunks.insert(1, make_usage_chunk(pt, ct, cached))
    inject({"is_stream": True, "content_type": "text/event-stream", "stream_chunks": chunks})

def run_jcc(prompt="test", timeout=30):
    env = {"JCC_ENDPOINT": "http://localhost:9901/v1", "OPENAI_API_KEY": "test-key", "JCC_DEBUGLOG": "1", "JCC_DISABLE_STREAMING_FALLBACK": "1"}
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

# === S19: 多轮工具调用 result 字段 ===
print("=== S19: 多轮工具调用 result 字段 ===")
clear_mock()

# 双工具调用 + usage
inject_tool_call("call_s19_001", "bash", {"command": "echo step1"})
inject_tool_call("call_s19_002", "bash", {"command": "echo step2"})
inject_text_response("All done", with_usage=True, pt=200, ct=80, cached=50)

rc, elapsed, stdout, stderr = run_jcc("Run two steps with usage")
events = parse_ndjson(stdout)

print(f"exit_code={rc}, elapsed={elapsed:.1f}s")
result = next((e for e in events if e.get("type") == "result"), None)
if result:
    print(f"\n--- result 事件字段 ---")
    print(f"subtype: {result.get('subtype')}")
    print(f"duration_ms: {result.get('duration_ms')}")
    print(f"duration_api_ms: {result.get('duration_api_ms')}")
    print(f"total_cost_usd: {result.get('total_cost_usd')}")
    print(f"stop_reason: {result.get('stop_reason')}")
    print(f"num_turns: {result.get('num_turns')}")
    print(f"usage: {json.dumps(result.get('usage', {}))}")
    print(f"errors: {result.get('errors')}")

    # 验证
    print(f"\n--- 验证 ---")
    if result.get('duration_api_ms', 0) > 0:
        print(f"✅ duration_api_ms={result.get('duration_api_ms')} > 0")
    else:
        print(f"🔴 duration_api_ms=0")

    if result.get('total_cost_usd', 0) > 0:
        print(f"✅ total_cost_usd={result.get('total_cost_usd')} > 0")
    else:
        print(f"🔴 total_cost_usd=0")

    if result.get('stop_reason'):
        print(f"✅ stop_reason={result.get('stop_reason')}")
    else:
        print(f"🔴 stop_reason 未设置")

    if result.get('num_turns') == 3:
        print(f"✅ num_turns=3")
    else:
        print(f"⚠️ num_turns={result.get('num_turns')}, 期望 3")

    usage = result.get('usage', {})
    if usage.get('input_tokens') == 200:
        print(f"✅ usage.input_tokens=200")
    else:
        print(f"⚠️ usage.input_tokens={usage.get('input_tokens')}, 期望 200")

    if usage.get('cache_read_input_tokens') == 50:
        print(f"✅ usage.cache_read_input_tokens=50")
    else:
        print(f"⚠️ usage.cache_read_input_tokens={usage.get('cache_read_input_tokens')}, 期望 50")

    # duration_ms 应该 >= duration_api_ms
    if result.get('duration_ms', 0) >= result.get('duration_api_ms', 0):
        print(f"✅ duration_ms({result.get('duration_ms')}) >= duration_api_ms({result.get('duration_api_ms')})")
    else:
        print(f"🔴 duration_ms < duration_api_ms — 逻辑错误")
else:
    print("🔴 无 result 事件")
    print(stdout[:500])
