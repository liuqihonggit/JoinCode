"""
测试工具调用场景 — 验证 jcc 能正确处理流式 tool_calls 响应
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

def build_tool_call_stream(tool_name, args_json, model="gpt-4o"):
    """构造工具调用流式响应"""
    created = int(time.time())
    cid = f"chatcmpl-{int(time.time()*1000)%100000}"
    chunks = [sse_chunk(cid, created, model, {"role": "assistant", "content": ""}, None)]
    chunks.append(sse_chunk(cid, created, model, {
        "tool_calls": [{"index": 0, "id": "call_abc123", "type": "function",
            "function": {"name": tool_name, "arguments": args_json}}]
    }, None))
    chunks.append(sse_chunk(cid, created, model, {}, "tool_calls"))
    chunks.append("data: [DONE]\n\n")
    return {"status_code": 200, "content_type": "text/event-stream", "body": "{}",
            "is_stream": True, "stream_chunks": chunks, "delay_ms": 0}

def run_round(prompt, injection, timeout=25, label="R"):
    print(f"\n{'='*60}")
    print(f"=== {label} | prompt={prompt!r} ===")
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
        stdout, stderr = proc.communicate(timeout=timeout)
        t_done = (time.time() - t0) * 1000
        exit_code = proc.returncode
    except subprocess.TimeoutExpired:
        proc.kill(); stdout, stderr = proc.communicate()
        t_done = (time.time() - t0) * 1000; exit_code = -1
    state = mock_state()
    print(f"jcc 退出 (T+{t_done:.0f}ms) exit={exit_code} reqs={state['total_requests']}")
    return {"label": label, "exit_code": exit_code, "stdout": stdout, "stderr": stderr, "t_done_ms": t_done}

# T1: Read 工具调用
print("=== T1: Read 工具调用 ===")
r1 = run_round("读取 README.md 文件",
    build_tool_call_stream("Read", json.dumps({"file_path": "README.md"})), label="T1-Read工具")
print("STDOUT:")
print(r1['stdout'][:1500])
print("STDERR key:")
for l in r1['stderr'].splitlines():
    if any(k in l for k in ["WIRE","tool","Tool","error","Error","exception","Exception","call_abc"]):
        print(f"  {l[:150]}")
