"""
思考链+工具调用组合测试 — reasoning + tool_calls
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
def sse_chunk(cid, created, model, delta, finish=None):
    return "data: " + json.dumps({
        "id": cid, "object": "chat.completion.chunk", "created": created, "model": model,
        "choices": [{"index": 0, "delta": delta, "finish_reason": finish}]
    }) + "\n\n"

# 思考链 + 工具调用组合
mock_clear()
created = int(time.time())
cid = f"chatcmpl-{int(time.time()*1000)%100000}"
chunks = [sse_chunk(cid, created, "gpt-4o", {"role": "assistant", "content": ""}, None)]
# 先输出思考链
for i in range(0, 40, 8):
    chunks.append(sse_chunk(cid, created, "gpt-4o", {"reasoning_content": "用户要读取文件，我需要调用 Read 工具"[i:i+8]}, None))
# 再输出工具调用
chunks.append(sse_chunk(cid, created, "gpt-4o", {
    "tool_calls": [{"index": 0, "id": "call_reason_123", "type": "function",
        "function": {"name": "Read", "arguments": json.dumps({"file_path": "CHANGELOG.md"})}}]
}, None))
chunks.append(sse_chunk(cid, created, "gpt-4o", {}, "tool_calls"))
chunks.append("data: [DONE]\n\n")
mock_inject({"status_code": 200, "content_type": "text/event-stream", "body": "{}",
    "is_stream": True, "stream_chunks": chunks, "delay_ms": 0})
# 第二轮：AI 总结
chunks2 = [sse_chunk("c2", created, "gpt-4o", {"role": "assistant", "content": ""}, None)]
for i in range(0, 48, 8):
    chunks2.append(sse_chunk("c2", created, "gpt-4o", {"content": "已读取 CHANGELOG.md，这是项目变更日志文件。"[i:i+8]}, None))
chunks2.append(sse_chunk("c2", created, "gpt-4o", {}, "stop"))
chunks2.append("data: [DONE]\n\n")
mock_inject({"status_code": 200, "content_type": "text/event-stream", "body": "{}",
    "is_stream": True, "stream_chunks": chunks2, "delay_ms": 0})

env = os.environ.copy()
env.update({"OPENAI_API_KEY": "sk-test", "JCC_DEBUGLOG": "1",
    "JCC_SKIP_CODE_INDEX": "1",
            "JCC_ENDPOINT": "http://localhost:9901/v1", "JCC_VENDOR": "openai", "JCC_MODEL_ID": "gpt-4o"})
print("=== 思考链+工具调用组合测试 ===")
t0 = time.time()
proc = subprocess.Popen([JCC_EXE, "--trust", "-p", "思考一下然后读取 CHANGELOG.md", "--await", "30"],
    stdout=subprocess.PIPE, stderr=subprocess.PIPE, env=env, text=True, encoding="utf-8", errors="replace")
try:
    stdout, stderr = proc.communicate(timeout=35)
    t_done = (time.time() - t0) * 1000
    exit_code = proc.returncode
except subprocess.TimeoutExpired:
    proc.kill(); stdout, stderr = proc.communicate()
    t_done = (time.time() - t0) * 1000; exit_code = -1
print(f"exit={exit_code} time={t_done:.0f}ms")
print(f"\n=== STDOUT ===")
print(stdout[:2500])
print(f"\n=== STDERR (WIRE 相关) ===")
for l in stderr.splitlines():
    if "WIRE " in l:
        print(f"  {l[:180]}")
