import json, subprocess, time, urllib.request, os, threading

JCC = "artifacts/bin/JoinCode/Debug/net10.0/jcc.dll"
MOCK_CTRL = "http://127.0.0.1:8093/__control__"

def clear_mock():
    req = urllib.request.Request(f"{MOCK_CTRL}/clear", method="POST")
    urllib.request.urlopen(req, timeout=5)

def inject(data):
    body = json.dumps(data).encode()
    req = urllib.request.Request(f"{MOCK_CTRL}/inject", data=body, headers={"Content-Type": "application/json"}, method="POST")
    urllib.request.urlopen(req, timeout=5)

def make_chunk(text):
    return [
        f'data: {{"choices":[{{"delta":{{"content":"{text}"}},"index":0}}]}}\n\n',
        'data: {"choices":[{"delta":{},"finish_reason":"stop","index":0}]}\n\n',
        'data: [DONE]\n\n',
    ]

def run_jcc(prompt, result_holder, idx):
    env = {"JCC_ENDPOINT": "http://127.0.0.1:8093/v1", "OPENAI_API_KEY": "test-key", "JCC_DISABLE_STREAMING_FALLBACK": "1"}
    cmd = ["dotnet", JCC, "--non-interactive", "--prompt", prompt, "--format", "ndjson", "--trust", "--permission-mode", "bypass"]
    try:
        proc = subprocess.run(cmd, capture_output=True, text=True, timeout=30, env={**os.environ, **env}, cwd=r"D:\project\w1")
        result_holder[idx] = proc.stdout
    except Exception as e:
        result_holder[idx] = f"ERROR: {e}"

clear_mock()
for i in range(3):
    inject({"is_stream": True, "content_type": "text/event-stream", "stream_chunks": make_chunk(f"Response {i+1}")})

time.sleep(0.5)

results = [None] * 3
threads = []
for i in range(3):
    t = threading.Thread(target=run_jcc, args=(f"Test prompt {i+1}", results, i))
    threads.append(t)

for t in threads: t.start()
for t in threads: t.join(timeout=35)

print("=== S22: 并发请求（3 个 jcc 进程同时运行）===")
success_count = 0
for i, stdout in enumerate(results):
    if stdout and "ERROR" not in str(stdout):
        events = []
        for line in str(stdout).strip().splitlines():
            if line.startswith("{"):
                try: events.append(json.loads(line))
                except: pass
        result = next((e for e in events if e.get("type") == "result"), None)
        if result and result.get("subtype") == "success":
            success_count += 1
            print(f"  进程 {i+1}: ✅ result.subtype=success")
        else:
            print(f"  进程 {i+1}: 🔴 无 success result")
    else:
        print(f"  进程 {i+1}: 🔴 {stdout}")

print(f"\n  {success_count}/3 进程成功")
if success_count == 3:
    print("  ✅ S22 并发请求全部成功")
else:
    print(f"  ⚠️ S22 {success_count}/3 成功（MockServer 注入队列竞争可能导致响应错配）")
