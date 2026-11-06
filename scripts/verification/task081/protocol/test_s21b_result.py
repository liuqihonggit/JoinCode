#!/usr/bin/env python3
"""验证 S21b result 字段是否包含所有 text 内容"""
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

clear_mock()
chunks = [
    'data: {"choices":[{"delta":{"content":"Before"},"index":0}]}\n\n',
    'data: {"choices":[{"delta":{"reasoning_content":"thinking"},"index":0}]}\n\n',
    'data: {"choices":[{"delta":{"content":" after"},"index":0}]}\n\n',
    'data: {"choices":[{"delta":{},"finish_reason":"stop","index":0}]}\n\n',
    'data: [DONE]\n\n',
]
inject({"is_stream": True, "content_type": "text/event-stream", "stream_chunks": chunks})

env = {"JCC_ENDPOINT": "http://localhost:9901/v1", "JCC_SKIP_CODE_INDEX": "1", "OPENAI_API_KEY": "test-key", "JCC_DISABLE_STREAMING_FALLBACK": "1"}
cmd = [JCC, "--non-interactive", "--prompt", "Mixed", "--format", "ndjson", "--trust", "--permission-mode", "bypass"]
proc = subprocess.run(cmd, capture_output=True, text=True, timeout=30, env={**os.environ, **env}, cwd=".")

for line in proc.stdout.strip().splitlines():
    if line.startswith("{"):
        e = json.loads(line)
        if e.get("type") == "result":
            result = e.get("result", "")
            print(f"result field: {repr(result)}")
            if "Before" in result and "after" in result:
                print("✅ result 包含所有 text 内容")
            else:
                print("🔴 result 缺失部分 text 内容")
            if "thinking" in result.lower():
                print("⚠️ result 包含 thinking 内容（可能不应包含）")
            else:
                print("✅ result 不包含 thinking 内容")
