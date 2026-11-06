#!/usr/bin/env python3
"""jcc 扩展测试 v3 — 连接断开/超大参数/多轮/MCP工具"""
import subprocess, json, time, urllib.request, sys, os, threading, uuid, socket
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
    return f"data: {json.dumps(obj, ensure_ascii=False)}\n\n"

def make_stream_chunks(content_text=None, tool_calls=None, reasoning=None, finish_reason="stop", no_done=False):
    chunks = []
    cid = f"chatcmpl-{uuid.uuid4().hex[:20]}"
    chunks.append(sse_line({"id": cid, "object": "chat.completion.chunk", "choices": [{"index": 0, "delta": {"role": "assistant"}}]}))
    if reasoning:
        if isinstance(reasoning, str):
            chunks.append(sse_line({"id": cid, "choices": [{"index": 0, "delta": {"reasoning_content": reasoning}}]}))
        else:
            for r in reasoning:
                chunks.append(sse_line({"id": cid, "choices": [{"index": 0, "delta": {"reasoning_content": r}}]}))
    if content_text is not None:
        if isinstance(content_text, str):
            chunks.append(sse_line({"id": cid, "choices": [{"index": 0, "delta": {"content": content_text}}]}))
        else:
            for c in content_text:
                chunks.append(sse_line({"id": cid, "choices": [{"index": 0, "delta": {"content": c}}]}))
    if tool_calls:
        for i, tc in enumerate(tool_calls):
            chunks.append(sse_line({"id": cid, "choices": [{"index": 0, "delta": {"tool_calls": [{"index": i, "id": tc["id"], "function": {"name": tc["name"], "arguments": json.dumps(tc["args"], ensure_ascii=False)}}]}}]}))
    chunks.append(sse_line({"id": cid, "choices": [{"index": 0, "delta": {}, "finish_reason": finish_reason}]}))
    if not no_done:
        chunks.append("data: [DONE]\n\n")
    return chunks

def inject_stream(chunks, status=200, headers=None, delay_ms=0):
    body = {"status_code": status, "is_stream": True, "stream_chunks": chunks, "content_type": "text/event-stream"}
    if delay_ms > 0: body["delay_ms"] = delay_ms
    if headers: body["headers"] = headers
    data = json.dumps(body).encode()
    req = urllib.request.Request(f"{MOCK}/__control__/inject", data=data, method="POST", headers={"Content-Type": "application/json"})
    try: urllib.request.urlopen(req, timeout=5)
    except Exception as e: print(f"  INJECT ERROR: {e}")

def inject_non_stream(body_text, status=200, headers=None):
    body = {"status_code": status, "is_stream": False, "body": body_text, "content_type": "application/json"}
    if headers: body["headers"] = headers
    data = json.dumps(body).encode()
    req = urllib.request.Request(f"{MOCK}/__control__/inject", data=data, method="POST", headers={"Content-Type": "application/json"})
    try: urllib.request.urlopen(req, timeout=5)
    except Exception as e: print(f"  INJECT ERROR: {e}")

def inject_simple(text="完成", **kwargs):
    inject_stream(make_stream_chunks(content_text=text, **kwargs))

def inject_tool_call(tool_name, tool_args, result_text="工具执行完成"):
    inject_stream(make_stream_chunks(tool_calls=[{"id": "call_1", "name": tool_name, "args": tool_args}], finish_reason="tool_calls"))
    inject_simple(result_text)

def clear():
    req = urllib.request.Request(f"{MOCK}/__control__/clear", data=b"", method="POST")
    try: urllib.request.urlopen(req, timeout=5)
    except: pass

def run_jcc(prompt, await_sec=30, extra_args=None, env_override=None):
    args = [JCC, "--trust", "--non-interactive", "-p", prompt, "--await", str(await_sec)]
    if extra_args: args.extend(extra_args)
    e = {**ENV, **(env_override or {})}
    t0 = time.time()
    try:
        proc = subprocess.run(args, env=e, capture_output=True, text=True, timeout=await_sec+10)
        return proc.returncode, proc.stdout, proc.stderr, time.time()-t0
    except subprocess.TimeoutExpired as ex:
        out = ex.stdout or b""
        err = ex.stderr or b""
        if isinstance(out, bytes): out = out.decode(errors="replace")
        if isinstance(err, bytes): err = err.decode(errors="replace")
        return -999, out, err, time.time()-t0

def test(name, func):
    clear()
    t0 = time.time()
    try:
        func()
        elapsed = time.time() - t0
        with lock: results.append((name, "PASS", elapsed, ""))
        print(f"  ✓ {name} ({elapsed:.1f}s)")
    except AssertionError as e:
        elapsed = time.time() - t0
        with lock: results.append((name, "FAIL", elapsed, str(e)))
        print(f"  ✗ {name} ({elapsed:.1f}s): {str(e)[:150]}")
    except Exception as e:
        elapsed = time.time() - t0
        with lock: results.append((name, "ERROR", elapsed, str(e)))
        print(f"  !! {name} ({elapsed:.1f}s): {str(e)[:150]}")
    clear()

def assert_eq(a, b, msg=""):
    if a != b: raise AssertionError(f"{msg}: expected {b!r}, got {a!r}")

def assert_contains(text, substr, msg=""):
    if substr not in text: raise AssertionError(f"{msg}: '{substr}' not in output (len={len(text)})")

# ============================================================
# 拆分自 test_extended_v3.py — 22. 流式中断恢复
# ============================================================
print("\n=== 22. 流式中断恢复 ===")

def test_stream_with_empty_choices():
    """chunk 中 choices 为空数组"""
    chunks = [
        sse_line({"choices": []}),
        sse_line({"choices": [{"index": 0, "delta": {"content": "内容"}}]}),
        sse_line({"choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}]}),
        "data: [DONE]\n\n"
    ]
    inject_stream(chunks)
    code, out, err, elapsed = run_jcc("空choices")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("空choices数组", test_stream_with_empty_choices)

def test_delta_null():
    """delta 为 null"""
    chunks = [
        sse_line({"choices": [{"index": 0, "delta": None}]}),
        sse_line({"choices": [{"index": 0, "delta": {"content": "内容"}}]}),
        sse_line({"choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}]}),
        "data: [DONE]\n\n"
    ]
    inject_stream(chunks)
    code, out, err, elapsed = run_jcc("null delta")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("delta为null", test_delta_null)

def test_model_name_change():
    """流中 model 名变化"""
    chunks = [
        sse_line({"model": "model-a", "choices": [{"index": 0, "delta": {"content": "内容"}}]}),
        sse_line({"model": "model-b", "choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}]}),
        "data: [DONE]\n\n"
    ]
    inject_stream(chunks)
    code, out, err, elapsed = run_jcc("model名变化")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("流中model名变化", test_model_name_change)

def test_tool_call_index_jump():
    """tool_call index 跳跃（0, 2 缺 1）"""
    chunks = [
        sse_line({"choices": [{"index": 0, "delta": {"tool_calls": [{"index": 0, "id": "call_1", "function": {"name": "read_file", "arguments": "{}"}}]}}]}),
        sse_line({"choices": [{"index": 0, "delta": {"tool_calls": [{"index": 2, "id": "call_3", "function": {"name": "grep", "arguments": "{}"}}]}}]}),
        sse_line({"choices": [{"index": 0, "delta": {}, "finish_reason": "tool_calls"}]}),
        "data: [DONE]\n\n"
    ]
    inject_stream(chunks)
    inject_simple("完成")
    code, out, err, elapsed = run_jcc("index跳跃")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("tool_call index跳跃", test_tool_call_index_jump)

# ============================================================
# 23. usage 统计
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
