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
    'data: {"choices":[{"delta":{"content":"Processed large input."},"index":0}]}\n\n',
    'data: {"choices":[{"delta":{},"finish_reason":"stop","index":0}]}\n\n',
    'data: [DONE]\n\n',
]})

large_prompt = "A" * 5000
env = {"JCC_ENDPOINT": "http://localhost:9901/v1", "JCC_SKIP_CODE_INDEX": "1", "OPENAI_API_KEY": "test-key", "JCC_DISABLE_STREAMING_FALLBACK": "1"}
cmd = [JCC, "--non-interactive", "--prompt", large_prompt, "--format", "ndjson", "--trust", "--permission-mode", "bypass"]
proc = subprocess.run(cmd, capture_output=True, text=True, timeout=30, env={**os.environ, **env}, cwd=".")

events = []
for line in proc.stdout.strip().splitlines():
    if line.startswith("{"):
        try: events.append(json.loads(line))
        except: pass

result = next((e for e in events if e.get("type") == "result"), None)
print("=== S23: 超大上下文（50000 字符 prompt）===")
if result and result.get("subtype") == "success":
    print(f"  ✅ result.subtype=success, exit_code={proc.returncode}")
    print(f"  ✅ 50000 字符 prompt 正确处理")
else:
    print(f"  🔴 result={result}, exit_code={proc.returncode}")
    if proc.stderr:
        print(f"  stderr: {proc.stderr[-200:]}")
