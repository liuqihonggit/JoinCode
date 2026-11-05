import json, subprocess, time, urllib.request, os

JCC = "artifacts/bin/JoinCode/Debug/net10.0/jcc.dll"
MOCK_CTRL = "http://127.0.0.1:8093/__control__"

def clear_mock():
    req = urllib.request.Request(f"{MOCK_CTRL}/clear", method="POST")
    urllib.request.urlopen(req, timeout=5)

def inject(data):
    body = json.dumps(data).encode()
    req = urllib.request.Request(f"{MOCK_CTRL}/inject", data=body, headers={"Content-Type": "application/json"}, method="POST")
    urllib.request.urlopen(req, timeout=5)

clear_mock()
inject({"is_stream": True, "content_type": "text/event-stream", "stream_chunks": [
    'data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_s25_001","function":{"name":"bash","arguments":"{\\"command\\":\\"echo hello\\"}"}}]}}]}\n\n',
    'data: {"choices":[{"delta":{},"finish_reason":"tool_calls","index":0}]}\n\n',
    'data: [DONE]\n\n',
]})
inject({"is_stream": True, "content_type": "text/event-stream", "stream_chunks": [
    'data: {"choices":[{"delta":{"content":"Done"},"index":0}]}\n\n',
    'data: {"choices":[{"delta":{},"finish_reason":"stop","index":0}]}\n\n',
    'data: [DONE]\n\n',
]})

env = {"JCC_ENDPOINT": "http://127.0.0.1:8093/v1", "OPENAI_API_KEY": "test-key", "JCC_DISABLE_STREAMING_FALLBACK": "1"}
cmd = ["dotnet", JCC, "--non-interactive", "--prompt", "run echo hello", "--format", "ndjson", "--trust"]
proc = subprocess.run(cmd, capture_output=True, text=True, timeout=30, env={**os.environ, **env}, cwd=r"D:\project\w1")

events = []
for line in proc.stdout.strip().splitlines():
    if line.startswith("{"):
        try: events.append(json.loads(line))
        except: pass

print("=== S25: 权限拒绝（default 模式，不 bypass）===")
print(f"  exit_code={proc.returncode}")
for e in events:
    if e.get("type") == "result":
        pd = e.get("permission_denials", [])
        print(f"  permission_denials = {pd}")
        if pd:
            print(f"  ✅ permission_denials 包含被拒绝的工具")
        else:
            print(f"  ⚠️ permission_denials 为空（工具可能被自动允许或未触发拒绝路径）")
    if e.get("type") == "user":
        msg = e.get("message", {})
        for block in (msg.get("content") or []):
            if block.get("type") == "tool_result":
                ie = block.get("is_error")
                print(f"  tool_result.is_error = {ie}")

if not events:
    print(f"  🔴 无事件输出")
    if proc.stderr:
        print(f"  stderr: {proc.stderr[-300:]}")
