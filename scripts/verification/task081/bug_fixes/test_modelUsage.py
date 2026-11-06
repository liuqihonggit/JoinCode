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
    'data: {"choices":[{"delta":{"content":"Hello"},"index":0}]}\n\n',
    'data: {"choices":[{"delta":{},"finish_reason":"stop","index":0}]}\n\n',
    'data: {"choices":[],"usage":{"prompt_tokens":50,"completion_tokens":30,"total_tokens":80}}\n\n',
    'data: [DONE]\n\n',
]})

env = {"JCC_ENDPOINT": "http://localhost:9901/v1", "JCC_SKIP_CODE_INDEX": "1", "OPENAI_API_KEY": "test-key", "JCC_DISABLE_STREAMING_FALLBACK": "1"}
cmd = [JCC, "--non-interactive", "--prompt", "hi", "--format", "ndjson", "--trust", "--permission-mode", "bypass"]
proc = subprocess.run(cmd, capture_output=True, text=True, timeout=30, env={**os.environ, **env}, cwd=".")

for line in proc.stdout.strip().splitlines():
    if line.startswith("{"):
        obj = json.loads(line)
        if obj.get("type") == "result":
            mu = obj.get("modelUsage", {})
            print(f"  modelUsage = {json.dumps(mu, indent=2)}")
            if mu:
                for model, usage in mu.items():
                    print(f"  ✅ 模型 '{model}': inputTokens={usage.get('inputTokens')}, outputTokens={usage.get('outputTokens')}, costUSD={usage.get('costUSD')}")
            else:
                print(f"  ⚠️ modelUsage 为空（可能 usage 数据未传递）")
