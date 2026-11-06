"""
异常边界压测 — 连接断开、畸形响应、超时、超大响应
"""
import subprocess, urllib.request, json, time, os
JCC_EXE = "artifacts/bin/JoinCode/Debug/net10.0/jcc.exe"
MOCK_BASE = "http://localhost:9901"

def mock_clear():
    req = urllib.request.Request(f"{MOCK_BASE}/__control__/clear", data=b"", method="POST")
    urllib.request.urlopen(req, timeout=3).read()
def mock_inject(injection):
    req = urllib.request.Request(f"{MOCK_BASE}/__control__/inject",
        data=json.dumps(injection).encode("utf-8"),
        headers={"Content-Type": "application/json"}, method="POST")
    return urllib.request.urlopen(req, timeout=5).read().decode()
def mock_state():
    return json.loads(urllib.request.urlopen(f"{MOCK_BASE}/__control__/state", timeout=3).read())

def sse_chunk(cid, created, model, delta, finish=None):
    return "data: " + json.dumps({
        "id": cid, "object": "chat.completion.chunk", "created": created, "model": model,
        "choices": [{"index": 0, "delta": delta, "finish_reason": finish}]
    }) + "\n\n"

def build_stream(text, model="gpt-4o"):
    created = int(time.time())
    cid = f"chatcmpl-{int(time.time()*1000)%100000}"
    chunks = [sse_chunk(cid, created, model, {"role": "assistant", "content": ""}, None)]
    for i in range(0, len(text), 8):
        chunks.append(sse_chunk(cid, created, model, {"content": text[i:i+8]}, None))
    chunks.append(sse_chunk(cid, created, model, {}, "stop"))
    chunks.append("data: [DONE]\n\n")
    return {"status_code": 200, "content_type": "text/event-stream", "body": "{}",
            "is_stream": True, "stream_chunks": chunks, "delay_ms": 0}

def run_edge(label, injection, prompt="测试", timeout=25):
    print(f"\n{'='*60}")
    print(f"=== {label} ===")
    mock_clear()
    mock_inject(injection)
    env = os.environ.copy()
    env.update({"OPENAI_API_KEY": "sk-test", "JCC_DEBUGLOG": "1",
    "JCC_SKIP_CODE_INDEX": "1",
                "JCC_ENDPOINT": "http://localhost:9901/v1", "JCC_VENDOR": "openai", "JCC_MODEL_ID": "gpt-4o"})
    t0 = time.time()
    proc = subprocess.Popen([JCC_EXE, "--trust", "-p", prompt, "--await", str(int(timeout))],
        stdout=subprocess.PIPE, stderr=subprocess.PIPE, env=env, text=True, encoding="utf-8", errors="replace")
    try:
        stdout, stderr = proc.communicate(timeout=timeout+5)
        t_done = (time.time() - t0) * 1000
        exit_code = proc.returncode
    except subprocess.TimeoutExpired:
        proc.kill(); stdout, stderr = proc.communicate()
        t_done = (time.time() - t0) * 1000; exit_code = -1
    state = mock_state()
    print(f"exit={exit_code} time={t_done:.0f}ms reqs={state['total_requests']}")
    lines = stdout.splitlines()
    for l in lines:
        if l and not l.startswith("供应商") and not l.startswith("  端点"):
            print(f"  OUT: {l[:150]}")
    key_err = [l for l in stderr.splitlines() if any(k in l for k in ["error","Error","exception","Exception","fail","Fail","RETRY","FALLBACK","WIRE "])]
    if key_err:
        print(f"  ERR key:")
        for l in key_err[:4]:
            print(f"    {l[:150]}")
    return {"label": label, "exit_code": exit_code, "t_done_ms": t_done}

results = []

# E1: 畸形 JSON 响应
r = run_edge("E1-畸形JSON", {"status_code": 200, "content_type": "application/json",
    "body": "{invalid json!!!", "is_stream": False, "stream_chunks": [], "delay_ms": 0})
results.append(r)

# E2: 503 服务不可用
r = run_edge("E2-503服务不可用", {"status_code": 503, "content_type": "application/json",
    "body": json.dumps({"error": {"message": "Service Unavailable", "type": "server_error"}}),
    "is_stream": False, "stream_chunks": [], "delay_ms": 0})
results.append(r)

# E3: 401 未授权
r = run_edge("E3-401未授权", {"status_code": 401, "content_type": "application/json",
    "body": json.dumps({"error": {"message": "Invalid API Key", "type": "invalid_request_error"}}),
    "is_stream": False, "stream_chunks": [], "delay_ms": 0})
results.append(r)

# E4: 超大响应（10KB 文本）
big_text = "A" * 10000
r = run_edge("E4-超大响应10KB", build_stream(big_text), prompt="输出大文本")
results.append(r)

# E5: 延迟响应（5 秒延迟）
r = run_edge("E5-延迟5秒", {"status_code": 200, "content_type": "text/event-stream",
    "body": "{}", "is_stream": True,
    "stream_chunks": [sse_chunk("c1", int(time.time()), "gpt-4o", {"role":"assistant","content":"延迟后响应"}, None),
                      sse_chunk("c1", int(time.time()), "gpt-4o", {}, "stop"), "data: [DONE]\n\n"],
    "delay_ms": 5000}, timeout=30)
results.append(r)

# E6: 200 但空 body
r = run_edge("E6-200空body", {"status_code": 200, "content_type": "application/json",
    "body": "", "is_stream": False, "stream_chunks": [], "delay_ms": 0})
results.append(r)

print(f"\n{'='*60}")
print("=== 汇总 ===")
for r in results:
    status = "✓" if r['exit_code'] == 0 else "✗"
    print(f"  {status} {r['label']}: exit={r['exit_code']} time={r['t_done_ms']:.0f}ms")
