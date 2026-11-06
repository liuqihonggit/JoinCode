"""
C/S 闭环动态测试脚手架 v2 — TASK081
预注入策略: jcc 启动前注入响应 → MockServer ConsumeInjection 立即返回 → 无时序问题
snake_case 字段名 (MockServerJsonContext 用 SnakeCaseLower)
"""
import subprocess, urllib.request, json, time, sys, os

JCC_EXE = "artifacts/bin/JoinCode/Debug/net10.0/jcc.exe"
MOCK_BASE = "http://localhost:9901"

def ts():
    return time.strftime("%H:%M:%S.") + f"{int((time.time()%1)*1000):03d}"

def mock_clear():
    req = urllib.request.Request(f"{MOCK_BASE}/__control__/clear", data=b"", method="POST")
    urllib.request.urlopen(req, timeout=3).read()

def mock_state():
    return json.loads(urllib.request.urlopen(f"{MOCK_BASE}/__control__/state", timeout=3).read())

def mock_requests():
    return json.loads(urllib.request.urlopen(f"{MOCK_BASE}/__control__/requests", timeout=3).read())

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

def build_stream(text, model="gpt-4o", tool_calls=None):
    """构造 OpenAI 流式 SSE 响应 (snake_case InjectedResponse)"""
    created = int(time.time())
    cid = f"chatcmpl-{int(time.time()*1000)%100000}"
    chunks = [sse_chunk(cid, created, model, {"role": "assistant", "content": ""}, None)]
    if tool_calls:
        # 工具调用流式
        for i, tc in enumerate(tool_calls):
            chunks.append(sse_chunk(cid, created, model, {
                "tool_calls": [{"index": i, "id": tc["id"], "type": "function",
                    "function": {"name": tc["name"], "arguments": tc["arguments"]}}]
            }, None))
        chunks.append(sse_chunk(cid, created, model, {}, "tool_calls"))
    else:
        for i in range(0, len(text), 8):
            chunks.append(sse_chunk(cid, created, model, {"content": text[i:i+8]}, None))
        chunks.append(sse_chunk(cid, created, model, {}, "stop"))
    chunks.append("data: [DONE]\n\n")
    return {"status_code": 200, "content_type": "text/event-stream", "body": "{}",
            "is_stream": True, "stream_chunks": chunks, "delay_ms": 0}

def build_nonstream(text, model="gpt-4o"):
    return {"status_code": 200, "content_type": "application/json",
        "body": json.dumps({
            "id": f"chatcmpl-{int(time.time()*1000)%100000}",
            "object": "chat.completion", "created": int(time.time()), "model": model,
            "choices": [{"index": 0, "message": {"role": "assistant", "content": text}, "finish_reason": "stop"}],
            "usage": {"prompt_tokens": 10, "completion_tokens": max(1,len(text)//4), "total_tokens": 10+max(1,len(text)//4)},
        }), "is_stream": False, "stream_chunks": [], "delay_ms": 0}

def build_error(status=500, msg="Internal Server Error"):
    return {"status_code": status, "content_type": "application/json",
        "body": json.dumps({"error": {"message": msg, "type": "server_error", "code": "internal_error"}}),
        "is_stream": False, "stream_chunks": [], "delay_ms": 0}

def run_round(prompt, injection, timeout=20, label="R"):
    """预注入策略: 清空 → 预注入 → 启动 jcc → 等待退出 → 收集结果"""
    print(f"\n{'='*60}")
    print(f"[{ts()}] === {label} | prompt={prompt!r} ===")
    mock_clear()
    inj_resp = mock_inject(injection)
    print(f"[{ts()}] 预注入完成 -> {inj_resp}")

    env = os.environ.copy()
    env.update({"OPENAI_API_KEY": "sk-test-1234567890", "JCC_DEBUGLOG": "1",
                "JCC_ENDPOINT": "http://localhost:9901/v1", "JCC_VENDOR": "openai", "JCC_MODEL_ID": "gpt-4o"})

    t0 = time.time()
    proc = subprocess.Popen([JCC_EXE, "--trust", "-p", prompt, "--await", str(int(timeout))],
        stdout=subprocess.PIPE, stderr=subprocess.PIPE, env=env, text=True, encoding="utf-8", errors="replace")
    print(f"[{ts()}] jcc PID={proc.pid} 启动")

    try:
        stdout, stderr = proc.communicate(timeout=timeout)
        t_done = (time.time() - t0) * 1000
        exit_code = proc.returncode
    except subprocess.TimeoutExpired:
        proc.kill(); stdout, stderr = proc.communicate()
        t_done = (time.time() - t0) * 1000; exit_code = -1

    state = mock_state()
    reqs = mock_requests()
    print(f"[{ts()}] jcc 退出 (T+{t_done:.0f}ms) exit={exit_code} reqs={state['total_requests']}")

    # 提取 AI 响应文本 (从 stdout 中 [AI助手] 之后的部分)
    ai_text = ""
    if "[AI助手]" in stdout:
        ai_text = stdout.split("[AI助手]", 1)[1].strip()

    return {
        "label": label, "ok": exit_code == 0, "exit_code": exit_code,
        "stdout": stdout, "stderr": stderr, "ai_text": ai_text,
        "t_done_ms": t_done, "mock_state": state, "requests": reqs,
    }

def print_result(r):
    print(f"\n--- {r['label']} 结果 ---")
    print(f"exit={r['exit_code']} time={r['t_done_ms']:.0f}ms reqs={r['mock_state']['total_requests']}")
    # banner (前2行)
    lines = r['stdout'].splitlines()
    for l in lines[:2]:
        print(f"  banner: {l}")
    # AI 响应
    if r['ai_text']:
        print(f"  AI: {r['ai_text'][:120]}")
    else:
        print(f"  AI: <空>")
    # 关键 stderr
    key = [l for l in r['stderr'].splitlines() if any(k in l for k in ["WIRE ","FALLBACK","Stream","error","Error","exception","Exception","fail","Fail","timeout","Timeout","tool_call","ToolCall"])]
    if key:
        print(f"  stderr key:")
        for l in key[:8]:
            print(f"    {l[:120]}")

if __name__ == "__main__":
    results = []

    # R1: 基础对话 (流式)
    r1 = run_round("你好，请用一句话介绍你自己",
        build_stream("你好！我是 JoinCode AI 助手，可以帮你完成软件工程任务。"), label="R1-基础对话")
    print_result(r1); results.append(r1)

    # R2: 多句话回复
    r2 = run_round("解释什么是递归",
        build_stream("递归是一种函数调用自身的编程技巧。每次调用处理更小的子问题，直到达到基准条件停止。例如阶乘：n! = n * (n-1)!，基准条件是 0! = 1。"), label="R2-多句回复")
    print_result(r2); results.append(r2)

    # R3: 服务器错误响应
    r3 = run_round("测试错误处理", build_error(500, "模拟服务器内部错误"), label="R3-服务器错误")
    print_result(r3); results.append(r3)

    # R4: 空响应 (空数组 bug 验收)
    r4 = run_round("返回空内容", build_stream(""), label="R4-空响应")
    print_result(r4); results.append(r4)

    # R5: 非流式响应
    r5 = run_round("非流式测试", build_nonstream("这是非流式响应内容。"), label="R5-非流式")
    print_result(r5); results.append(r5)

    # 汇总
    print(f"\n{'='*60}")
    print(f"=== 汇总 ===")
    for r in results:
        status = "✓" if r['ok'] else "✗"
        ai_preview = r['ai_text'][:40] if r['ai_text'] else "<空>"
        print(f"  {status} {r['label']}: exit={r['exit_code']} time={r['t_done_ms']:.0f}ms AI={ai_preview!r}")
