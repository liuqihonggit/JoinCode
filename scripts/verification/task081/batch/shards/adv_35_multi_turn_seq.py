#!/usr/bin/env python3
"""jcc 高级功能测试 v4 — JSON输出/dry-run/会话/交互模式/压缩"""
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

def sse_line(obj):
    return f"data: {json.dumps(obj, ensure_ascii=False)}\n\n"

def make_stream_chunks(content_text=None, tool_calls=None, finish_reason="stop", no_done=False):
    chunks = []
    cid = f"chatcmpl-{uuid.uuid4().hex[:20]}"
    chunks.append(sse_line({"id": cid, "object": "chat.completion.chunk", "choices": [{"index": 0, "delta": {"role": "assistant"}}]}))
    if content_text is not None:
        chunks.append(sse_line({"id": cid, "choices": [{"index": 0, "delta": {"content": content_text}}]}))
    if tool_calls:
        for i, tc in enumerate(tool_calls):
            chunks.append(sse_line({"id": cid, "choices": [{"index": 0, "delta": {"tool_calls": [{"index": i, "id": tc["id"], "function": {"name": tc["name"], "arguments": json.dumps(tc["args"], ensure_ascii=False)}}]}}]}))
    chunks.append(sse_line({"id": cid, "choices": [{"index": 0, "delta": {}, "finish_reason": finish_reason}]}))
    if not no_done: chunks.append("data: [DONE]\n\n")
    return chunks

def inject_stream(chunks, status=200, delay_ms=0):
    body = {"status_code": status, "is_stream": True, "stream_chunks": chunks, "content_type": "text/event-stream"}
    if delay_ms > 0: body["delay_ms"] = delay_ms
    data = json.dumps(body).encode()
    req = urllib.request.Request(f"{MOCK}/__control__/inject", data=data, method="POST", headers={"Content-Type": "application/json"})
    try: urllib.request.urlopen(req, timeout=5)
    except Exception as e: print(f"  INJECT ERROR: {e}")

def inject_simple(text="完成"):
    inject_stream(make_stream_chunks(content_text=text))

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
        results.append((name, "PASS", elapsed, ""))
        print(f"  ✓ {name} ({elapsed:.1f}s)")
    except AssertionError as e:
        elapsed = time.time() - t0
        results.append((name, "FAIL", elapsed, str(e)))
        print(f"  ✗ {name} ({elapsed:.1f}s): {str(e)[:200]}")
    except Exception as e:
        elapsed = time.time() - t0
        results.append((name, "ERROR", elapsed, str(e)))
        print(f"  !! {name} ({elapsed:.1f}s): {str(e)[:200]}")
    clear()

def assert_eq(a, b, msg=""):
    if a != b: raise AssertionError(f"{msg}: expected {b!r}, got {a!r}")

def assert_contains(text, substr, msg=""):
    if substr not in text: raise AssertionError(f"{msg}: '{substr}' not in output (len={len(text)})")

# ============================================================
# 拆分自 test_advanced_v4.py — 35. 连续多轮
# ============================================================
print("\n=== 35. 连续多轮 ===")

def test_continuous_5_turns():
    """模拟5轮连续对话（每轮独立 jcc 进程）"""
    for i in range(5):
        inject_simple(f"第{i+1}轮回复")
        code, out, err, elapsed = run_jcc(f"第{i+1}轮问题")
        assert_eq(code, 0, f"第{i+1}轮 exit code, stderr={err[-100:]}")
test("5轮连续对话", test_continuous_5_turns)

# ============================================================
# 36. 错误码覆盖
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
