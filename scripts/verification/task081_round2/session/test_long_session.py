"""
长会话记忆测试 — 交互模式多轮对话
用 --force-interactive + stdin 管道
"""
import subprocess, urllib.request, json, time, os
JCC_EXE = r"D:\project\w3\artifacts\bin\JoinCode\Debug\net10.0\jcc.exe"
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
mock_inject(build_stream("你好！我是 AI 助手，记住这个数字：42"))
mock_inject(build_stream("你刚才让我记住的数字是 42。"))
mock_inject(build_stream("是的，我确认数字是 42。上下文保持正确。"))

env = os.environ.copy()
env.update({"OPENAI_API_KEY": "sk-test", "JCC_DEBUGLOG": "1",
            "JCC_ENDPOINT": "http://localhost:9901/v1", "JCC_VENDOR": "openai", "JCC_MODEL_ID": "gpt-4o"})

stdin_input = "你好，记住数字42\n我刚才让你记住什么数字？\n确认一下数字\n/exit\n"
print("=== 长会话记忆测试 (交互模式 3 轮) ===")
t0 = time.time()
proc = subprocess.Popen([JCC_EXE, "--trust", "--force-interactive", "--await", "40"],
    stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
    env=env, text=True, encoding="utf-8", errors="replace")
try:
    stdout, stderr = proc.communicate(input=stdin_input, timeout=45)
    t_done = (time.time() - t0) * 1000
    exit_code = proc.returncode
except subprocess.TimeoutExpired:
    proc.kill(); stdout, stderr = proc.communicate()
    t_done = (time.time() - t0) * 1000; exit_code = -1
print(f"exit={exit_code} time={t_done:.0f}ms")
print(f"\n=== STDOUT ===")
print(stdout[:3000])
print(f"\n=== STDERR (WIRE 相关) ===")
for l in stderr.splitlines():
    if "WIRE " in l:
        print(f"  {l[:180]}")
