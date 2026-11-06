"""
C/S 闭环动态测试 v3 — TASK081 扩展场景
测试: 工具调用、多轮对话、长文本、Unicode、思考链、异常边界
"""
import subprocess, urllib.request, json, time, os, sys
JCC_EXE = "artifacts/bin/JoinCode/Debug/net10.0/jcc.exe"
MOCK_BASE = "http://localhost:9901"

def ts(): return time.strftime("%H:%M:%S.") + f"{int((time.time()%1)*1000):03d}"
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

def build_stream(text, model="gpt-4o", tool_calls=None, reasoning=None):
    created = int(time.time())
    cid = f"chatcmpl-{int(time.time()*1000)%100000}"
    chunks = [sse_chunk(cid, created, model, {"role": "assistant", "content": ""}, None)]
    if reasoning:
        for i in range(0, len(reasoning), 8):
            chunks.append(sse_chunk(cid, created, model, {"reasoning_content": reasoning[i:i+8]}, None))
    if tool_calls:
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

def build_error(status=500, msg="Internal Server Error"):
    return {"status_code": status, "content_type": "application/json",
        "body": json.dumps({"error": {"message": msg, "type": "server_error", "code": "internal_error"}}),
        "is_stream": False, "stream_chunks": [], "delay_ms": 0}

def run_round(prompt, injection, timeout=25, label="R", multi_turn=None):
    print(f"\n{'='*60}")
    print(f"[{ts()}] === {label} | prompt={prompt!r} ===")
    mock_clear()
    if multi_turn:
        for inj in multi_turn:
            mock_inject(inj)
            print(f"[{ts()}] 预注入(多轮) -> ok")
    else:
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
    print(f"[{ts()}] jcc 退出 (T+{t_done:.0f}ms) exit={exit_code} reqs={state['total_requests']}")
    ai_text = ""
    if "[AI助手]" in stdout:
        ai_text = stdout.split("[AI助手]", 1)[1].strip()
    return {"label": label, "ok": exit_code == 0, "exit_code": exit_code,
            "stdout": stdout, "stderr": stderr, "ai_text": ai_text, "t_done_ms": t_done, "mock_state": state}

def print_result(r):
    print(f"\n--- {r['label']} 结果 ---")
    print(f"exit={r['exit_code']} time={r['t_done_ms']:.0f}ms reqs={r['mock_state']['total_requests']}")
    lines = r['stdout'].splitlines()
    for l in lines[:2]:
        print(f"  banner: {l}")
    if r['ai_text']:
        print(f"  AI: {r['ai_text'][:200]}")
    else:
        print(f"  AI: <空>")
    key = [l for l in r['stderr'].splitlines() if any(k in l for k in ["WIRE ","FALLBACK","error","Error","exception","Exception","fail","Fail","timeout","Timeout","tool_call","ToolCall","Tool"])]
    if key:
        print(f"  stderr key:")
        for l in key[:6]:
            print(f"    {l[:140]}")

if __name__ == "__main__":
    results = []
    # R6: 长文本响应 (测试分块渲染)
    long_text = "这是一段长文本测试。" * 30 + "用于验证流式分块渲染是否正确。"
    r6 = run_round("请输出一段长文本", build_stream(long_text), label="R6-长文本")
    print_result(r6); results.append(r6)

    # R7: Unicode 特殊字符 (emoji + 中文标点 + 换行)
    unicode_text = "你好！🎉\n这是第二行。\n\t缩进行。\n\"引号\" '单引号' 【中文括号】\n结束 ✓"
    r7 = run_round("测试特殊字符", build_stream(unicode_text), label="R7-Unicode特殊字符")
    print_result(r7); results.append(r7)

    # R8: 404 错误 (端点不存在场景)
    r8 = run_round("测试404", build_error(404, "Not Found"), label="R8-404错误")
    print_result(r8); results.append(r8)

    # R9: 429 限流
    r9 = run_round("测试限流", build_error(429, "Rate Limit Exceeded"), label="R9-429限流")
    print_result(r9); results.append(r9)

    # R10: 思考链 (reasoning_content)
    r10 = run_round("思考一下", build_stream("基于思考，答案是42。", reasoning="用户问的是意义，我需要哲学思考...答案是42。"), label="R10-思考链")
    print_result(r10); results.append(r10)

    print(f"\n{'='*60}")
    print(f"=== 汇总 ===")
    for r in results:
        status = "✓" if r['ok'] else "✗"
        ai_preview = r['ai_text'][:50] if r['ai_text'] else "<空>"
        print(f"  {status} {r['label']}: exit={r['exit_code']} time={r['t_done_ms']:.0f}ms AI={ai_preview!r}")
