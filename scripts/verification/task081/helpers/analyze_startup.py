#!/usr/bin/env python3
"""分析 jcc 启动耗时 — 用 debuglog 时间戳定位瓶颈"""
import subprocess, os, time, json, urllib.request, uuid, threading
from pathlib import Path

env = {**os.environ, "JCC_ENDPOINT": "http://localhost:9901/v1", "JCC_VENDOR": "openai", "JCC_MODEL_ID": "gpt-5.6-sol", "OPENAI_API_KEY": "test-key", "JCC_SKIP_MODEL_FETCH": "1", "JCC_DISABLE_RETRY": "1", "JCC_DEBUGLOG": "1"}
JCC = "artifacts/bin/JoinCode/Debug/net10.0/jcc.exe"
MOCK_EXE = "artifacts/bin/OpenAI.MockServer/Debug/net10.0/JoinCode.OpenAI.MockServer.exe"

mock = subprocess.Popen([MOCK_EXE, "--port", "9901", "--interactive", "--no-open-browser"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
for _ in range(10):
    time.sleep(1)
    try: urllib.request.urlopen("http://localhost:9901/__control__/state", timeout=3); break
    except: pass

cid = f"chatcmpl-{uuid.uuid4().hex[:20]}"
chunks = [f'data: {json.dumps({"id":cid,"object":"chat.completion.chunk","choices":[{"index":0,"delta":{"role":"assistant"}}]})}\n\n', f'data: {json.dumps({"id":cid,"choices":[{"index":0,"delta":{"content":"OK"}}]})}\n\n', f'data: {json.dumps({"id":cid,"choices":[{"index":0,"delta":{},"finish_reason":"stop"}]})}\n\n', 'data: [DONE]\n\n']
body = json.dumps({"status":200,"content_type":"text/event-stream","is_stream":True,"stream_chunks":chunks})
urllib.request.urlopen(urllib.request.Request("http://localhost:9901/__control__/clear", data=b"", method="POST"), timeout=3)
urllib.request.urlopen(urllib.request.Request("http://localhost:9901/__control__/inject", data=body.encode(), method="POST", headers={"Content-Type":"application/json"}), timeout=3)

t0 = time.time()
r = subprocess.run([JCC, "--trust", "--non-interactive", "-p", "test", "--await", "10"], env=env, capture_output=True, text=True, timeout=20)
t1 = time.time()
print(f"总耗时: {t1-t0:.2f}s exit={r.returncode}")

# 解析 stderr 中的 [WIRE] [STEP] [启动完成] 行
lines = r.stderr.splitlines()
for line in lines:
    if any(k in line for k in ['[WIRE]', '[STEP]', '[启动完成]', '[READY]', 'ms]', 'Diag', 'DIAG']):
        print(line.strip()[:200])

# 也看 stdout 的启动诊断
for line in r.stdout.splitlines():
    if any(k in line for k in ['ms', '启动', 'Total', '耗时', '初始化']):
        print(f"[stdout] {line.strip()[:200]}")

mock.kill()
