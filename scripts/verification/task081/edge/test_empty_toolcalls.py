"""空 tool_calls 数组测试 — 检测 jcc 是否正确处理空工具调用"""
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
def sse_chunk(cid, created, model, delta, finish=None):
    return "data: " + json.dumps({
        "id": cid, "object": "chat.completion.chunk", "created": created, "model": model,
        "choices": [{"index": 0, "delta": delta, "finish_reason": finish}]
    }) + "\n\n"

# 空 tool_calls 数组
mock_clear()
created = int(time.time())
cid = f"chatcmpl-{int(time.time()*1000)%100000}"
chunks = [sse_chunk(cid, created, "gpt-4o", {"role": "assistant", "content": ""}, None)]
chunks.append(sse_chunk(cid, created, "gpt-4o", {"content": "文本内容"}, None))
chunks.append(sse_chunk(cid, created, "gpt-4o", {"tool_calls": []}, None))
chunks.append(sse_chunk(cid, created, "gpt-4o", {}, "stop"))
chunks.append("data: [DONE]\n\n")
mock_inject({"status_code": 200, "content_type": "text/event-stream", "body": "{}",
    "is_stream": True, "stream_chunks": chunks, "delay_ms": 0})

env = os.environ.copy()
env.update({"OPENAI_API_KEY": "sk-test", "JCC_DEBUGLOG": "1",
            "JCC_ENDPOINT": "http://localhost:9901/v1", "JCC_VENDOR": "openai", "JCC_MODEL_ID": "gpt-4o"})
print("=== 空 tool_calls 数组测试 ===")
t0 = time.time()
proc = subprocess.Popen([JCC_EXE, "--trust", "-p", "测试空tool_calls", "--await", "20"],
    stdout=subprocess.PIPE, stderr=subprocess.PIPE, env=env, text=True, encoding="utf-8", errors="replace")
try:
    stdout, stderr = proc.communicate(timeout=25)
    t_done = (time.time() - t0) * 1000
    exit_code = proc.returncode
except subprocess.TimeoutExpired:
    proc.kill(); stdout, stderr = proc.communicate()
    t_done = (time.time() - t0) * 1000; exit_code = -1
print(f"exit={exit_code} time={t_done:.0f}ms")
print(f"\n=== STDOUT ===")
print(stdout[:1500])
print(f"\n=== STDERR (WIRE) ===")
for l in stderr.splitlines():
    if "WIRE " in l:
        print(f"  {l[:180]}")
