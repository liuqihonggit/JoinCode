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

clear_mock()
inject({"is_stream": True, "content_type": "text/event-stream", "stream_chunks": [
    'data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_d2_001","function":{"name":"bash","arguments":"{\\"command\\":\\"echo hello\\"}"}}]}}]}\n\n',
    'data: {"choices":[{"delta":{},"finish_reason":"tool_calls","index":0}]}\n\n',
    'data: [DONE]\n\n',
]})
inject({"is_stream": True, "content_type": "text/event-stream", "stream_chunks": [
    'data: {"choices":[{"delta":{"content":"Done"},"index":0}]}\n\n',
    'data: {"choices":[{"delta":{},"finish_reason":"stop","index":0}]}\n\n',
    'data: [DONE]\n\n',
]})

env = {"JCC_ENDPOINT": "http://localhost:9901/v1", "OPENAI_API_KEY": "test-key", "JCC_DISABLE_STREAMING_FALLBACK": "1"}
cmd = [JCC, "--non-interactive", "--prompt", "run echo", "--format", "ndjson", "--trust", "--permission-mode", "bypass"]
proc = subprocess.run(cmd, capture_output=True, text=True, timeout=30, env={**os.environ, **env}, cwd=".")

for line in proc.stdout.strip().splitlines():
    if line.startswith("{"):
        obj = json.loads(line)
        if obj.get("type") == "assistant":
            msg = obj.get("message", {})
            for block in (msg.get("content") or []):
                if block.get("type") == "tool_use":
                    inp = block.get("input")
                    if isinstance(inp, dict):
                        print(f"  ✅ tool_use.input 是 JSON 对象: {inp}")
                    elif isinstance(inp, str):
                        print(f"  🔴 tool_use.input 是字符串: {inp}")
                    else:
                        print(f"  🔴 tool_use.input 类型异常: {type(inp)} = {inp}")
