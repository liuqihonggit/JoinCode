"""
多轮工具调用闭环测试 — 工具调用→结果回传→AI 总结
预注入两轮响应：第一轮工具调用，第二轮 AI 总结
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
        "tool_calls": [{"index": 0, "id": "call_abc123", "type": "function",
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

# 预注入两轮响应
mock_clear()
# 第一轮：Read 工具调用
mock_inject(build_tool_call_stream("Read", json.dumps({"file_path": "README.md"})))
# 第二轮：AI 总结（基于工具结果）
mock_inject(build_text_stream("README.md 是 JoinCode 项目的说明文件，包含项目简介、技术栈（.NET 10.0 + NativeAOT）、徽章和工程规则链接。"))

env = os.environ.copy()
env.update({"OPENAI_API_KEY": "sk-test", "JCC_DEBUGLOG": "1",
    "JCC_SKIP_CODE_INDEX": "1",
            "JCC_ENDPOINT": "http://localhost:9901/v1", "JCC_VENDOR": "openai", "JCC_MODEL_ID": "gpt-4o"})

print("=== 多轮工具调用闭环测试 ===")
print("预注入: 第一轮 Read 工具调用 + 第二轮 AI 总结")
t0 = time.time()
proc = subprocess.Popen([JCC_EXE, "--trust", "-p", "读取 README.md 并总结内容", "--await", "30"],
    stdout=subprocess.PIPE, stderr=subprocess.PIPE, env=env, text=True, encoding="utf-8", errors="replace")
try:
    stdout, stderr = proc.communicate(timeout=35)
    t_done = (time.time() - t0) * 1000
    exit_code = proc.returncode
except subprocess.TimeoutExpired:
    proc.kill(); stdout, stderr = proc.communicate()
    t_done = (time.time() - t0) * 1000; exit_code = -1

state = mock_state()
reqs = mock_requests()
print(f"jcc 退出 (T+{t_done:.0f}ms) exit={exit_code} reqs={state['total_requests']}")
print(f"\n=== STDOUT ===")
print(stdout)
print(f"\n=== 请求详情 ===")
for i, r in enumerate(reqs):
    print(f"  请求 {i+1}: {r.get('method','?')} {r.get('path','?')} body_len={len(r.get('body',''))}")
print(f"\n=== STDERR (WIRE/Tool 相关) ===")
for l in stderr.splitlines():
    if any(k in l for k in ["WIRE","Tool","tool","error","Error","exception","Exception","RETRY"]):
        print(f"  {l[:180]}")
