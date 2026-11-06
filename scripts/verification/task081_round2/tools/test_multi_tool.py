"""
多工具链式调用测试 — Read→Glob→AI总结
预注入三轮响应：第一轮 Read 工具，第二轮 Glob 工具，第三轮 AI 总结
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
def mock_state():
    return json.loads(urllib.request.urlopen(f"{MOCK_BASE}/__control__/state", timeout=3).read())
def mock_requests():
    return json.loads(urllib.request.urlopen(f"{MOCK_BASE}/__control__/requests", timeout=3).read())

def sse_chunk(cid, created, model, delta, finish=None):
    return "data: " + json.dumps({
        "id": cid, "object": "chat.completion.chunk", "created": created, "model": model,
        "choices": [{"index": 0, "delta": delta, "finish_reason": finish}]
    }) + "\n\n"

def build_tool_call_stream(tool_name, args_json, model="gpt-4o"):
    created = int(time.time())
    cid = f"chatcmpl-{int(time.time()*1000)%100000}"
    chunks = [sse_chunk(cid, created, model, {"role": "assistant", "content": ""}, None)]
    chunks.append(sse_chunk(cid, created, model, {
        "tool_calls": [{"index": 0, "id": f"call_{tool_name}_{int(time.time())%10000}", "type": "function",
            "function": {"name": tool_name, "arguments": args_json}}]
    }, None))
    chunks.append(sse_chunk(cid, created, model, {}, "tool_calls"))
    chunks.append("data: [DONE]\n\n")
    return {"status_code": 200, "content_type": "text/event-stream", "body": "{}",
            "is_stream": True, "stream_chunks": chunks, "delay_ms": 0}

def build_text_stream(text, model="gpt-4o"):
    created = int(time.time())
    cid = f"chatcmpl-{int(time.time()*1000)%100000}"
    chunks = [sse_chunk(cid, created, model, {"role": "assistant", "content": ""}, None)]
    for i in range(0, len(text), 8):
        chunks.append(sse_chunk(cid, created, model, {"content": text[i:i+8]}, None))
    chunks.append(sse_chunk(cid, created, model, {}, "stop"))
    chunks.append("data: [DONE]\n\n")
    return {"status_code": 200, "content_type": "text/event-stream", "body": "{}",
            "is_stream": True, "stream_chunks": chunks, "delay_ms": 0}

# 预注入三轮响应
mock_clear()
# 第一轮：Read 工具调用
mock_inject(build_tool_call_stream("Read", json.dumps({"file_path": "LICENSE"})))
# 第二轮：Glob 工具调用
mock_inject(build_tool_call_stream("Glob", json.dumps({"pattern": "**/*.md"})))
# 第三轮：AI 总结
mock_inject(build_text_stream("已读取 LICENSE 文件并搜索了所有 .md 文件。项目包含 README.md、AGENTS.md 等文档，LICENSE 是 MIT 许可证。"))

env = os.environ.copy()
env.update({"OPENAI_API_KEY": "sk-test", "JCC_DEBUGLOG": "1",
            "JCC_ENDPOINT": "http://localhost:9901/v1", "JCC_VENDOR": "openai", "JCC_MODEL_ID": "gpt-4o"})

print("=== 多工具链式调用测试 (Read→Glob→AI总结) ===")
t0 = time.time()
proc = subprocess.Popen([JCC_EXE, "--trust", "-p", "读取 LICENSE 并搜索所有 md 文件然后总结", "--await", "40"],
    stdout=subprocess.PIPE, stderr=subprocess.PIPE, env=env, text=True, encoding="utf-8", errors="replace")
try:
    stdout, stderr = proc.communicate(timeout=45)
    t_done = (time.time() - t0) * 1000
    exit_code = proc.returncode
except subprocess.TimeoutExpired:
    proc.kill(); stdout, stderr = proc.communicate()
    t_done = (time.time() - t0) * 1000; exit_code = -1

state = mock_state()
reqs = mock_requests()
print(f"jcc 退出 (T+{t_done:.0f}ms) exit={exit_code} reqs={state['total_requests']}")
print(f"\n=== STDOUT ===")
print(stdout[:3000])
print(f"\n=== 请求详情 ===")
for i, r in enumerate(reqs):
    print(f"  请求 {i+1}: body_len={len(r.get('body',''))}")
print(f"\n=== STDERR (WIRE/Tool 相关) ===")
for l in stderr.splitlines():
    if any(k in l for k in ["WIRE ","Tool","tool","error","Error","RETRY"]):
        print(f"  {l[:180]}")
