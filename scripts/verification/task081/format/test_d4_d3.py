import json, subprocess, time, urllib.request, os

JCC = "artifacts/bin/JoinCode/Debug/net10.0/jcc.exe"
MOCK_CTRL = "http://localhost:9901/__control__"

def clear_mock():
    req = urllib.request.Request(f"{MOCK_CTRL}/clear", method="POST")
    urllib.request.urlopen(req, timeout=5)

def inject(data):
    body = json.dumps(data).encode()
    req = urllib.request.Request(f"{MOCK_CTRL}/inject", data=body, headers={"Content-Type": "application/json"}, method="POST")
    urllib.request.urlopen(req, timeout=5)

def run_jcc(prompt, timeout=30):
    env = {"JCC_ENDPOINT": "http://localhost:9901/v1", "OPENAI_API_KEY": "test-key", "JCC_DISABLE_STREAMING_FALLBACK": "1"}
    cmd = ["dotnet", JCC, "--non-interactive", "--prompt", prompt, "--format", "ndjson", "--trust", "--permission-mode", "bypass"]
    proc = subprocess.run(cmd, capture_output=True, text=True, timeout=timeout, env={**os.environ, **env}, cwd=".")
    return proc.returncode, proc.stdout, proc.stderr

def parse_ndjson(stdout):
    results = []
    for line in stdout.strip().splitlines():
        if line.startswith("{"):
            try: results.append(json.loads(line))
            except: pass
    return results

print("=== D4+D3: tool_result.is_error=false + stop_reason 验证 ===")
clear_mock()

# Response 1: assistant calls bash tool (finish_reason=tool_calls)
inject({"is_stream": True, "content_type": "text/event-stream", "stream_chunks": [
    'data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_d4_001","function":{"name":"bash","arguments":"{\\"command\\":\\"echo hello\\"}"}}]}}]}\n\n',
    'data: {"choices":[{"delta":{},"finish_reason":"tool_calls","index":0}]}\n\n',
    'data: [DONE]\n\n',
]})
# Response 2: final text (finish_reason=stop -> end_turn)
inject({"is_stream": True, "content_type": "text/event-stream", "stream_chunks": [
    'data: {"choices":[{"delta":{"content":"Done"},"index":0}]}\n\n',
    'data: {"choices":[{"delta":{},"finish_reason":"stop","index":0}]}\n\n',
    'data: [DONE]\n\n',
]})

rc, stdout, stderr = run_jcc("Run echo hello")
events = parse_ndjson(stdout)

print(f"exit_code={rc}")
for e in events:
    t = e.get("type", "")
    if t == "user":
        msg = e.get("message", {})
        for block in (msg.get("content") or []):
            if block.get("type") == "tool_result":
                ie = block.get("is_error", "MISSING")
                status = "✅" if ie == False else "🔴"
                print(f"  {status} tool_result.is_error = {ie} (expect: false)")
    if t == "result":
        sr = e.get("stop_reason")
        print(f"  result.stop_reason = {sr} (最终轮 finish_reason=stop → end_turn)")
        print(f"  result.subtype = {e.get('subtype')}")

if not any(e.get("type") == "user" for e in events):
    print("🔴 无 user 事件（tool_result 未输出）")
    print(stdout[:800])
