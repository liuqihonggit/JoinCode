#!/usr/bin/env python3
"""jcc 全面功能测试 v2 — 修正注入格式"""
import subprocess, json, time, urllib.request, sys, os, threading, uuid
from pathlib import Path

JCC = "artifacts/bin/JoinCode/Debug/net10.0/jcc.exe"
MOCK = "http://localhost:9901"
MODEL = "gpt-5.6-sol"
ENV = {
    **os.environ,
    "JCC_ENDPOINT": "http://localhost:9901/v1",
    "JCC_VENDOR": "openai",
    "JCC_MODEL_ID": MODEL,
    "OPENAI_API_KEY": "test-key",
    "JCC_DEBUGLOG": "1",
    "JCC_SKIP_CODE_INDEX": "1",
    "JCC_SKIP_MODEL_FETCH": "1",
}

results = []
lock = threading.Lock()

def sse_line(obj):
    """构造 SSE 行: data: {json}\n\n"""
    return f"data: {json.dumps(obj, ensure_ascii=False)}\n\n"

def make_stream_chunks(content_text=None, tool_calls=None, reasoning=None, finish_reason="stop", no_done=False):
    """构造 SSE 流式 chunk 列表"""
    chunks = []
    cid = f"chatcmpl-{uuid.uuid4().hex[:20]}"
    
    # 首个 role chunk
    chunks.append(sse_line({"id": cid, "object": "chat.completion.chunk", "choices": [{"index": 0, "delta": {"role": "assistant"}}]}))
    
    # reasoning_content
    if reasoning:
        if isinstance(reasoning, str):
            chunks.append(sse_line({"id": cid, "choices": [{"index": 0, "delta": {"reasoning_content": reasoning}}]}))
        else:
            for r in reasoning:
                chunks.append(sse_line({"id": cid, "choices": [{"index": 0, "delta": {"reasoning_content": r}}]}))
    
    # content
    if content_text is not None:
        if isinstance(content_text, str):
            chunks.append(sse_line({"id": cid, "choices": [{"index": 0, "delta": {"content": content_text}}]}))
        else:
            for c in content_text:
                chunks.append(sse_line({"id": cid, "choices": [{"index": 0, "delta": {"content": c}}]}))
    
    # tool_calls
    if tool_calls:
        for i, tc in enumerate(tool_calls):
            chunks.append(sse_line({"id": cid, "choices": [{"index": 0, "delta": {"tool_calls": [{"index": i, "id": tc["id"], "function": {"name": tc["name"], "arguments": json.dumps(tc["args"], ensure_ascii=False)}}]}}]}))
    
    # finish
    chunks.append(sse_line({"id": cid, "choices": [{"index": 0, "delta": {}, "finish_reason": finish_reason}]}))
    
    if not no_done:
        chunks.append("data: [DONE]\n\n")
    
    return chunks

def inject_stream(chunks, status=200, headers=None, delay_ms=0):
    """注入流式响应"""
    body = {"status_code": status, "is_stream": True, "stream_chunks": chunks, "content_type": "text/event-stream"}
    if delay_ms > 0:
        body["delay_ms"] = delay_ms
    if headers:
        body["headers"] = headers
    data = json.dumps(body).encode()
    req = urllib.request.Request(f"{MOCK}/__control__/inject", data=data, method="POST",
                                 headers={"Content-Type": "application/json"})
    try:
        urllib.request.urlopen(req, timeout=5)
    except Exception as e:
        print(f"  INJECT ERROR: {e}")

def inject_non_stream(body_text, status=200, headers=None):
    """注入非流式响应"""
    body = {"status_code": status, "is_stream": False, "body": body_text, "content_type": "application/json"}
    if headers:
        body["headers"] = headers
    data = json.dumps(body).encode()
    req = urllib.request.Request(f"{MOCK}/__control__/inject", data=data, method="POST",
                                 headers={"Content-Type": "application/json"})
    try:
        urllib.request.urlopen(req, timeout=5)
    except Exception as e:
        print(f"  INJECT ERROR: {e}")

def inject_simple(text="完成", **kwargs):
    """注入简单流式文本响应"""
    inject_stream(make_stream_chunks(content_text=text, **kwargs))

def inject_tool_call(tool_name, tool_args, result_text="工具执行完成"):
    """注入工具调用响应：第一轮返回 tool_calls，第二轮返回结果"""
    inject_stream(make_stream_chunks(
        tool_calls=[{"id": "call_1", "name": tool_name, "args": tool_args}],
        finish_reason="tool_calls"
    ))
    inject_simple(result_text)

def clear():
    req = urllib.request.Request(f"{MOCK}/__control__/clear", data=b"", method="POST")
    try:
        urllib.request.urlopen(req, timeout=5)
    except: pass

def get_state():
    try:
        req = urllib.request.Request(f"{MOCK}/__control__/state")
        resp = urllib.request.urlopen(req, timeout=5)
        return json.loads(resp.read())
    except:
        return {}

def run_jcc(prompt, await_sec=30, extra_args=None):
    """运行 jcc 并返回 (exit_code, stdout, stderr, elapsed)"""
    args = [JCC, "--trust", "--non-interactive", "-p", prompt, "--await", str(await_sec)]
    if extra_args:
        args.extend(extra_args)
    t0 = time.time()
    try:
        proc = subprocess.run(args, env=ENV, capture_output=True, text=True, timeout=await_sec+10)
        elapsed = time.time() - t0
        return proc.returncode, proc.stdout, proc.stderr, elapsed
    except subprocess.TimeoutExpired as e:
        elapsed = time.time() - t0
        return -999, (e.stdout or b"").decode() if isinstance(e.stdout, bytes) else (e.stdout or ""), (e.stderr or b"").decode() if isinstance(e.stderr, bytes) else (e.stderr or ""), elapsed

def test(name, func):
    clear()
    t0 = time.time()
    try:
        func()
        elapsed = time.time() - t0
        with lock:
            results.append((name, "PASS", elapsed, ""))
        print(f"  ✓ {name} ({elapsed:.1f}s)")
    except AssertionError as e:
        elapsed = time.time() - t0
        with lock:
            results.append((name, "FAIL", elapsed, str(e)))
        print(f"  ✗ {name} ({elapsed:.1f}s): {str(e)[:150]}")
    except Exception as e:
        elapsed = time.time() - t0
        with lock:
            results.append((name, "ERROR", elapsed, str(e)))
        print(f"  !! {name} ({elapsed:.1f}s): {str(e)[:150]}")
    clear()

def assert_eq(a, b, msg=""):
    if a != b:
        raise AssertionError(f"{msg}: expected {b!r}, got {a!r}")

def assert_contains(text, substr, msg=""):
    if substr not in text:
        raise AssertionError(f"{msg}: '{substr}' not in output (len={len(text)})")

def assert_not_contains(text, substr, msg=""):
    if substr in text:
        raise AssertionError(f"{msg}: '{substr}' found in output")

# ============================================================
# 拆分自 test_comprehensive_v2.py — 4. 边缘条件
# ============================================================
print("\n=== 4. 边缘条件 ===")

def test_empty_content():
    inject_stream(make_stream_chunks(content_text="", finish_reason="stop"))
    code, out, err, elapsed = run_jcc("空内容")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("空内容响应", test_empty_content)

def test_null_content_chunk():
    chunks = [
        sse_line({"choices": [{"index": 0, "delta": {"content": None}}]}),
        sse_line({"choices": [{"index": 0, "delta": {"content": "有效"}}]}),
        sse_line({"choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}]}),
        "data: [DONE]\n\n"
    ]
    inject_stream(chunks)
    code, out, err, elapsed = run_jcc("null content")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("null content chunk", test_null_content_chunk)

def test_no_done():
    inject_stream(make_stream_chunks(content_text="无DONE", no_done=True))
    code, out, err, elapsed = run_jcc("无DONE标记")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("无[DONE]标记", test_no_done)

def test_empty_tool_calls():
    chunks = [
        sse_line({"choices": [{"index": 0, "delta": {"content": "开始"}}]}),
        sse_line({"choices": [{"index": 0, "delta": {"tool_calls": []}}]}),
        sse_line({"choices": [{"index": 0, "delta": {"content": "结束"}}]}),
        sse_line({"choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}]}),
        "data: [DONE]\n\n"
    ]
    inject_stream(chunks)
    code, out, err, elapsed = run_jcc("空tool_calls")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("空tool_calls数组", test_empty_tool_calls)

def test_malformed_chunk():
    chunks = [
        sse_line({"choices": [{"index": 0, "delta": {"content": "正常"}}]}),
        "data: NOT_VALID_JSON\n\n",
        sse_line({"choices": [{"index": 0, "delta": {"content": "恢复"}}]}),
        sse_line({"choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}]}),
        "data: [DONE]\n\n"
    ]
    inject_stream(chunks)
    code, out, err, elapsed = run_jcc("畸形JSON")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("畸形JSON容错", test_malformed_chunk)

def test_sse_comment_lines():
    chunks = [
        ": keep-alive comment\n\n",
        sse_line({"choices": [{"index": 0, "delta": {"content": "内容"}}]}),
        ": another comment\n\n",
        sse_line({"choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}]}),
        "data: [DONE]\n\n"
    ]
    inject_stream(chunks)
    code, out, err, elapsed = run_jcc("SSE注释行")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("SSE注释行", test_sse_comment_lines)

def test_done_with_trailing_space():
    chunks = [
        sse_line({"choices": [{"index": 0, "delta": {"content": "内容"}}]}),
        sse_line({"choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}]}),
        "data: [DONE]  \n\n",
    ]
    inject_stream(chunks)
    code, out, err, elapsed = run_jcc("DONE带空格")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("[DONE]带尾随空格", test_done_with_trailing_space)

def test_extra_data_after_done():
    chunks = [
        sse_line({"choices": [{"index": 0, "delta": {"content": "内容"}}]}),
        sse_line({"choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}]}),
        "data: [DONE]\n\n",
        sse_line({"choices": [{"index": 0, "delta": {"content": "额外"}}]}),
    ]
    inject_stream(chunks)
    code, out, err, elapsed = run_jcc("DONE后有数据")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("[DONE]后有额外数据", test_extra_data_after_done)

def test_multi_choices():
    chunks = [
        sse_line({"choices": [
            {"index": 0, "delta": {"content": "选择1"}},
            {"index": 1, "delta": {"content": "选择2"}}
        ]}),
        sse_line({"choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}]}),
        "data: [DONE]\n\n"
    ]
    inject_stream(chunks)
    code, out, err, elapsed = run_jcc("多choices")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("多choices(n>1)", test_multi_choices)

def test_finish_reason_length():
    inject_stream(make_stream_chunks(content_text="部分内容", finish_reason="length"))
    code, out, err, elapsed = run_jcc("finish_reason=length")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("finish_reason=length", test_finish_reason_length)

def test_finish_reason_content_filter():
    inject_stream(make_stream_chunks(content_text="", finish_reason="content_filter"))
    code, out, err, elapsed = run_jcc("content_filter")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("finish_reason=content_filter", test_finish_reason_content_filter)

# ============================================================
# 5. 错误处理
# ============================================================


# ============================================================
# 汇总
# ============================================================
print("\n" + "=" * 60)
print("测试汇总")
print("=" * 60)
passed = sum(1 for _, s, _, _ in results if s == "PASS")
failed = sum(1 for _, s, _, _ in results if s == "FAIL")
errors = sum(1 for _, s, _, _ in results if s == "ERROR")
total = len(results)
print(f"总计: {total} | 通过: {passed} | 失败: {failed} | 错误: {errors}")
print()
for name, status, elapsed, msg in results:
    icon = "✓" if status == "PASS" else "✗" if status == "FAIL" else "!!"
    print(f"  {icon} {name} ({elapsed:.1f}s)" + (f" — {msg[:120]}" if msg else ""))

import json as _json
from pathlib import Path as _Path
_Path(".xxx/logs").mkdir(parents=True, exist_ok=True)
log_name = _Path(__file__).stem + "_results.json"
_Path(f".xxx/logs/{log_name}").write_text(
    _json.dumps([{"name": n, "status": s, "elapsed": e, "msg": m} for n, s, e, m in results],
               ensure_ascii=False, indent=2), encoding="utf-8")

sys.exit(0 if failed == 0 and errors == 0 else 1)
