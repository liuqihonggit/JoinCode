"""
错误后恢复测试 — 先 500 错误，重试后成功
预注入两轮：第一轮 500 错误，第二轮成功响应
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

def build_error(status=500, msg="Internal Server Error"):
    return {"status_code": status, "content_type": "application/json",
        "body": json.dumps({"error": {"message": msg, "type": "server_error", "code": "internal_error"}}),
        "is_stream": False, "stream_chunks": [], "delay_ms": 0}

# 预注入：第一轮 500 错误，第二轮成功
mock_clear()
mock_inject(build_error(500, "模拟服务器临时错误"))
mock_inject(build_stream("错误已恢复，这是成功响应。"))

env = os.environ.copy()
env.update({"OPENAI_API_KEY": "sk-test", "JCC_DEBUGLOG": "1",
            "JCC_ENDPOINT": "http://localhost:9901/v1", "JCC_VENDOR": "openai", "JCC_MODEL_ID": "gpt-4o"})

print("=== 错误后恢复测试 (500→成功) ===")
t0 = time.time()
proc = subprocess.Popen([JCC_EXE, "--trust", "-p", "测试错误恢复", "--await", "30"],
    stdout=subprocess.PIPE, stderr=subprocess.PIPE, env=env, text=True, encoding="utf-8", errors="replace")
try:
    stdout, stderr = proc.communicate(timeout=35)
    t_done = (time.time() - t0) * 1000
    exit_code = proc.returncode
except subprocess.TimeoutExpired:
    proc.kill(); stdout, stderr = proc.communicate()
    t_done = (time.time() - t0) * 1000; exit_code = -1

state = mock_state()
print(f"jcc 退出 (T+{t_done:.0f}ms) exit={exit_code} reqs={state['total_requests']}")
print(f"\n=== STDOUT ===")
print(stdout)
print(f"\n=== STDERR (RETRY 相关) ===")
for l in stderr.splitlines():
    if any(k in l for k in ["RETRY","error","Error","WIRE ","FALLBACK"]):
        print(f"  {l[:180]}")
