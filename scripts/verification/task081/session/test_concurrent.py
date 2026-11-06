"""并发请求测试 — 同时启动 3 个 jcc 进程"""
import subprocess, urllib.request, json, time, os, threading
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

mock_clear()
mock_inject(build_stream("响应1：第一个请求结果"))
mock_inject(build_stream("响应2：第二个请求结果"))
mock_inject(build_stream("响应3：第三个请求结果"))

env = os.environ.copy()
env.update({"OPENAI_API_KEY": "sk-test", "JCC_DEBUGLOG": "1",
    "JCC_SKIP_CODE_INDEX": "1",
            "JCC_ENDPOINT": "http://localhost:9901/v1", "JCC_VENDOR": "openai", "JCC_MODEL_ID": "gpt-4o"})

results = {}
def run_jcc(idx, prompt):
    t0 = time.time()
    proc = subprocess.Popen([JCC_EXE, "--trust", "-p", prompt, "--await", "25"],
        stdout=subprocess.PIPE, stderr=subprocess.PIPE, env=env, text=True, encoding="utf-8", errors="replace")
    try:
        stdout, stderr = proc.communicate(timeout=30)
        t_done = (time.time() - t0) * 1000
        exit_code = proc.returncode
    except subprocess.TimeoutExpired:
        proc.kill(); stdout, stderr = proc.communicate()
        t_done = (time.time() - t0) * 1000; exit_code = -1
    ai_text = stdout.split("[AI助手]",1)[1].strip() if "[AI助手]" in stdout else ""
    results[idx] = {"exit": exit_code, "time": t_done, "ai": ai_text[:60]}

threads = []
for i, p in enumerate(["并发A", "并发B", "并发C"]):
    t = threading.Thread(target=run_jcc, args=(i, p))
    threads.append(t)
    t.start()

print("=== 并发请求测试 (3 个 jcc 进程) ===")
t0 = time.time()
for t in threads:
    t.join(timeout=35)
t_total = (time.time() - t0) * 1000

print(f"总耗时: {t_total:.0f}ms")
for i in sorted(results.keys()):
    r = results[i]
    status = "✓" if r['exit'] == 0 else "✗"
    print(f"  {status} 进程{i}: exit={r['exit']} time={r['time']:.0f}ms AI={r['ai']!r}")
