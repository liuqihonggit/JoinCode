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
    'data: {"choices":[{"delta":{"content":"Hi"},"index":0}]}\n\n',
    'data: {"choices":[{"delta":{},"finish_reason":"stop","index":0}]}\n\n',
    'data: [DONE]\n\n',
]})

env = {"JCC_ENDPOINT": "http://localhost:9901/v1", "OPENAI_API_KEY": "test-key", "JCC_DISABLE_STREAMING_FALLBACK": "1"}
cmd = ["dotnet", JCC, "--non-interactive", "--prompt", "hi", "--format", "ndjson", "--trust", "--permission-mode", "bypass"]
proc = subprocess.run(cmd, capture_output=True, text=True, timeout=30, env={**os.environ, **env}, cwd=r"D:\project\w1")

for line in proc.stdout.strip().splitlines():
    if line.startswith("{"):
        obj = json.loads(line)
        if obj.get("type") == "system" and obj.get("subtype") == "init":
            model = obj.get("model", "MISSING")
            print(f"  system/init model = {model}")
            if model != "unknown":
                print(f"  ✅ model 从配置获取（非 unknown）")
            else:
                print(f"  ⚠️ model=unknown（配置中未设置模型名，fallback 正常）")
