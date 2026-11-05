#!/usr/bin/env python3
"""S17: system/init 消息内容调查 — claude_code_version/model/tools 字段"""
import json
import subprocess
import time
import urllib.request
import os

JCC = "artifacts/bin/JoinCode/Debug/net10.0/jcc.dll"
MOCK_CTRL = "http://127.0.0.1:8091/__control__"

def clear_mock():
    req = urllib.request.Request(f"{MOCK_CTRL}/clear", method="POST")
    urllib.request.urlopen(req, timeout=5)

def inject(data):
    body = json.dumps(data).encode()
    req = urllib.request.Request(f"{MOCK_CTRL}/inject", data=body, headers={"Content-Type": "application/json"}, method="POST")
    urllib.request.urlopen(req, timeout=5)

def run_jcc(prompt="test", timeout=30):
    env = {
        "JCC_ENDPOINT": "http://127.0.0.1:8091/v1",
        "OPENAI_API_KEY": "test-key",
        "JCC_DEBUGLOG": "1",
        "JCC_DISABLE_STREAMING_FALLBACK": "1",
    }
    cmd = ["dotnet", JCC, "--non-interactive", "--prompt", prompt, "--format", "ndjson", "--trust", "--permission-mode", "bypass"]
    start = time.time()
    proc = subprocess.run(cmd, capture_output=True, text=True, timeout=timeout, env={**os.environ, **env}, cwd=r"D:\project\w1")
    return proc.returncode, time.time() - start, proc.stdout, proc.stderr

clear_mock()
chunks = [
    'data: {"choices":[{"delta":{"content":"Hello"},"index":0}]}\n\n',
    'data: {"choices":[{"delta":{},"finish_reason":"stop","index":0}]}\n\n',
    'data: [DONE]\n\n',
]
inject({"is_stream": True, "content_type": "text/event-stream", "stream_chunks": chunks})

rc, elapsed, stdout, stderr = run_jcc("Test init message")
print(f"exit_code={rc}, elapsed={elapsed:.1f}s\n")

# 解析所有事件
events = []
for line in stdout.strip().splitlines():
    if line.startswith("{"):
        try:
            events.append(json.loads(line))
        except:
            pass

# 详细检查 system/init
for e in events:
    if e.get("type") == "system":
        print("=== system/init 消息 ===")
        print(json.dumps(e, indent=2, ensure_ascii=False))

        # 检查关键字段
        version = e.get("claude_code_version", "")
        model = e.get("model", "")
        tools = e.get("tools", [])
        mcp_servers = e.get("mcp_servers", [])
        permission_mode = e.get("permission_mode", "")
        skills = e.get("skills", [])

        print(f"\n--- 字段检查 ---")
        print(f"claude_code_version: {repr(version)} {'✅' if version else '🔴 空字符串'}")
        print(f"model: {repr(model)} {'✅' if model and model != 'unknown' else '🔴 unknown 或空'}")
        print(f"tools: {len(tools)} 个工具 {'✅' if tools else '🔴 空数组'}")
        if not tools:
            print("  🔴 tools 为空 — 应包含可用工具列表（bash/read/write/glob/grep 等）")
        else:
            for t in tools[:5]:
                print(f"    - {t}")
        print(f"mcp_servers: {len(mcp_servers)} 个")
        print(f"permission_mode: {repr(permission_mode)}")
        print(f"skills: {len(skills)} 个")

# 检查 result 事件
for e in events:
    if e.get("type") == "result":
        print("\n=== result 事件 ===")
        print(json.dumps(e, indent=2, ensure_ascii=False))
        print(f"\n--- 字段检查 ---")
        print(f"duration_ms: {e.get('duration_ms')} {'✅' if e.get('duration_ms', 0) > 0 else '🔴 0 或缺失'}")
        print(f"duration_api_ms: {e.get('duration_api_ms')} {'⚠️ 0' if e.get('duration_api_ms') == 0 else '✅'}")
        print(f"total_cost_usd: {e.get('total_cost_usd')} {'⚠️ 0' if e.get('total_cost_usd') == 0 else '✅'}")
        print(f"num_turns: {e.get('num_turns')}")
        usage = e.get("usage", {})
        print(f"usage: {usage}")
