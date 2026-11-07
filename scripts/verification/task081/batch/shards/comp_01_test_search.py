#!/usr/bin/env python3
"""jcc 全面功能测试 — 覆盖工具调用、斜杠命令、边缘条件、上下文压缩等"""
import subprocess, json, time, urllib.request, sys, os, threading
from pathlib import Path

JCC = "artifacts/bin/JoinCode/Debug/net10.0/jcc.exe"
MOCK = "http://localhost:9901"
ENV = {
    **os.environ,
    "JCC_ENDPOINT": "http://localhost:9901/v1",
    "JCC_VENDOR": "openai",
    "JCC_MODEL_ID": "gpt-4o",
    "OPENAI_API_KEY": "test-key",
    "JCC_DEBUGLOG": "1",
    "JCC_SKIP_CODE_INDEX": "1",
}

results = []
lock = threading.Lock()

def sse_line(obj):
    """构造 SSE 格式行: data: {json}\n\n"""
    return "data: " + json.dumps(obj) + "\n\n"

_chunk_seq = 0
def _auto_complete_chunk(obj):
    """自动补全 chunk 缺少的字段（id/object/created/model/choices/delta）"""
    global _chunk_seq
    _chunk_seq += 1
    cid = f"chatcmpl-{int(time.time()*1000)%100000}"
    base = {"id": cid, "object": "chat.completion.chunk", "created": int(time.time()), "model": "gpt-4o"}
    result = {**base, **obj}
    if "choices" in result:
        for ch in result["choices"]:
            ch.setdefault("index", 0)
            if "delta" in ch and isinstance(ch["delta"], dict):
                ch["delta"].setdefault("role", "assistant")
    return result

def inject(response_text, is_stream=True, status=200, headers=None, delay_ms=0):
    """预注入响应到 MockServer（stream_chunks 是 List<string>，每个元素是完整 SSE 行）"""
    body = {"status_code": status, "is_stream": is_stream}
    if is_stream:
        chunks = []
        if isinstance(response_text, list):
            for c in response_text:
                if isinstance(c, dict):
                    chunks.append(sse_line(_auto_complete_chunk(c)))
                elif c == "[DONE]":
                    chunks.append("data: [DONE]\n\n")
                else:
                    chunks.append(c)
        else:
            cid = f"chatcmpl-{int(time.time()*1000)%100000}"
            created = int(time.time())
            chunks.append(sse_line({"id": cid, "object": "chat.completion.chunk", "created": created, "model": "gpt-4o",
                "choices": [{"index": 0, "delta": {"role": "assistant", "content": ""}, "finish_reason": None}]}))
            words = response_text.split()
            for w in words:
                chunks.append(sse_line({"id": cid, "object": "chat.completion.chunk", "created": created, "model": "gpt-4o",
                    "choices": [{"index": 0, "delta": {"content": w + " "}, "finish_reason": None}]}))
            chunks.append(sse_line({"id": cid, "object": "chat.completion.chunk", "created": created, "model": "gpt-4o",
                "choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}]}))
            chunks.append("data: [DONE]\n\n")
        body["stream_chunks"] = chunks
    else:
        body["body"] = response_text
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

def inject_tool_call(tool_name, tool_args, result_text="完成"):
    """注入一个工具调用响应：第一轮返回 tool_calls，第二轮返回结果"""
    cid = f"chatcmpl-{int(time.time()*1000)%100000}"
    created = int(time.time())
    base = {"id": cid, "object": "chat.completion.chunk", "created": created, "model": "gpt-4o"}
    chunks = [
        {**base, "choices": [{"index": 0, "delta": {"role": "assistant", "content": ""}, "finish_reason": None}]},
        {**base, "choices": [{"index": 0, "delta": {"tool_calls": [{"index": 0, "id": "call_1", "type": "function", "function": {"name": tool_name, "arguments": json.dumps(tool_args)}}]}, "finish_reason": None}]},
        {**base, "choices": [{"index": 0, "delta": {}, "finish_reason": "tool_calls"}]},
        "[DONE]"
    ]
    inject(chunks)
    inject(result_text)

def clear():
    req = urllib.request.Request(f"{MOCK}/__control__/clear", data=b"", method="POST")
    try:
        urllib.request.urlopen(req, timeout=5)
    except: pass

def get_requests():
    try:
        req = urllib.request.Request(f"{MOCK}/__control__/requests")
        resp = urllib.request.urlopen(req, timeout=5)
        return json.loads(resp.read())
    except:
        return []

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
        return -999, e.stdout or "", e.stderr or "", elapsed

def test(name, func):
    """运行一个测试用例"""
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
        print(f"  ✗ {name} ({elapsed:.1f}s): {e}")
    except Exception as e:
        elapsed = time.time() - t0
        with lock:
            results.append((name, "ERROR", elapsed, str(e)))
        print(f"  !! {name} ({elapsed:.1f}s): {e}")
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
# 拆分自 test_comprehensive.py — 1. 基础工具调用测试
# ============================================================
print("\n=== 1. 基础工具调用测试 ===")


# ============================================================
# 单测试拆分: grep 搜索工具 (test_search)
# ============================================================
def test_search():
    inject_tool_call("grep", {"pattern": "class", "include": "*.cs"}, "搜索完成，找到 10 个匹配")
    code, out, err, elapsed = run_jcc("搜索 class 关键字")
    assert_eq(code, 0, f"exit code, stderr={err[:200]}")
test("grep 搜索工具", test_search)


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
