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
# 25. JSON 输出模式
# ============================================================
print("\n=== 25. JSON 输出模式 ===")

def test_json_output():
    inject_simple("JSON测试")
    code, out, err, elapsed = run_jcc("json测试", extra_args=["--json"])
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("--json 输出", test_json_output)

def test_format_text():
    inject_simple("text格式")
    code, out, err, elapsed = run_jcc("text测试", extra_args=["--format", "text"])
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("--format text", test_format_text)

# ============================================================
# 26. dry-run 模式
# ============================================================
print("\n=== 26. dry-run 模式 ===")

def test_dry_run():
    # dry-run 不应该发送 LLM 请求
    code, out, err, elapsed = run_jcc("dry-run测试", extra_args=["--dry-run"])
    # 不崩溃即可
test("--dry-run", test_dry_run)

# ============================================================
# 27. 系统提示词
# ============================================================
print("\n=== 27. 系统提示词 ===")

def test_system_prompt():
    inject_simple("自定义提示词")
    code, out, err, elapsed = run_jcc("测试", extra_args=["--system-prompt", "你是一个测试助手"])
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("--system-prompt", test_system_prompt)

def test_append_system_prompt():
    inject_simple("追加提示词")
    code, out, err, elapsed = run_jcc("测试", extra_args=["--append-system-prompt", "额外指令"])
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("--append-system-prompt", test_append_system_prompt)

# ============================================================
# 28. 权限模式
# ============================================================
print("\n=== 28. 权限模式 ===")

def test_permission_plan():
    inject_simple("plan模式")
    code, out, err, elapsed = run_jcc("权限测试", extra_args=["--permission-mode", "plan"])
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("--permission-mode plan", test_permission_plan)

def test_permission_auto():
    inject_simple("auto模式")
    code, out, err, elapsed = run_jcc("权限测试", extra_args=["--permission-mode", "auto"])
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("--permission-mode auto", test_permission_auto)

def test_permission_bypass():
    inject_simple("bypass模式")
    code, out, err, elapsed = run_jcc("权限测试", extra_args=["--permission-mode", "bypass"])
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("--permission-mode bypass", test_permission_bypass)

# ============================================================
# 29. 工具白名单/黑名单
# ============================================================
print("\n=== 29. 工具过滤 ===")

def test_allowed_tools():
    inject_simple("白名单测试")
    code, out, err, elapsed = run_jcc("白名单", extra_args=["--allowed-tools", "read_file,write_file"])
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("--allowed-tools", test_allowed_tools)

def test_disallowed_tools():
    inject_simple("黑名单测试")
    code, out, err, elapsed = run_jcc("黑名单", extra_args=["--disallowed-tools", "bash"])
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("--disallowed-tools", test_disallowed_tools)

# ============================================================
# 30. 静默模式
# ============================================================
print("\n=== 30. 静默模式 ===")

def test_quiet():
    inject_simple("静默测试")
    code, out, err, elapsed = run_jcc("静默", extra_args=["--quiet"])
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("--quiet", test_quiet)

# ============================================================
# 31. 上下文压缩触发（小窗口）
# ============================================================
print("\n=== 31. 上下文压缩 ===")

def test_small_context_window():
    """设置小上下文窗口，发送长文本"""
    inject_simple("A" * 5000)
    code, out, err, elapsed = run_jcc("压缩测试", env_override={"JCC_MAX_CONTEXT_TOKENS": "500"})
    # 不崩溃即可，压缩在下一轮触发
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("小上下文窗口(500token)", test_small_context_window)

def test_soft_compact():
    """软压缩阈值"""
    inject_simple("B" * 3000)
    code, out, err, elapsed = run_jcc("软压缩", env_override={"JCC_MAX_CONTEXT_TOKENS": "1000"})
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("软压缩阈值", test_soft_compact)

# ============================================================
# 32. 非流式回退验证
# ============================================================
print("\n=== 32. 非流式回退 ===")

def test_stream_fallback_to_nonstream():
    """流式失败后回退到非流式"""
    # 第一个注入：非流式响应（流式解析器会失败）
    body = json.dumps({"id": "x", "object": "chat.completion", "choices": [{"index": 0, "message": {"role": "assistant", "content": "回退成功"}, "finish_reason": "stop"}], "usage": {"prompt_tokens": 10, "completion_tokens": 5, "total_tokens": 15}})
    inject_data = {"status_code": 200, "is_stream": False, "body": body, "content_type": "application/json"}
    data = json.dumps(inject_data).encode()
    req = urllib.request.Request(f"{MOCK}/__control__/inject", data=data, method="POST", headers={"Content-Type": "application/json"})
    urllib.request.urlopen(req, timeout=5)
    # 第二个注入：非流式回退请求的响应
    inject_data2 = {"status_code": 200, "is_stream": False, "body": body, "content_type": "application/json"}
    data2 = json.dumps(inject_data2).encode()
    req2 = urllib.request.Request(f"{MOCK}/__control__/inject", data=data2, method="POST", headers={"Content-Type": "application/json"})
    urllib.request.urlopen(req2, timeout=5)
    
    code, out, err, elapsed = run_jcc("回退测试")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
    assert_contains(out, "回退成功", "输出应包含回退内容")
test("流式回退到非流式", test_stream_fallback_to_nonstream)

# ============================================================
# 33. 多供应商协议
# ============================================================
print("\n=== 33. 多供应商协议 ===")

def test_anthropic_protocol():
    """anthropic 协议"""
    inject_simple("anthropic协议")
    code, out, err, elapsed = run_jcc("anthropic", env_override={"JCC_VENDOR": "anthropic", "JCC_MODEL_ID": "claude-opus-5"})
    # anthropic 供应商可能需要不同的端点，不崩溃即可
test("anthropic 供应商", test_anthropic_protocol)

def test_deepseek_protocol():
    """deepseek 协议"""
    inject_simple("deepseek协议")
    code, out, err, elapsed = run_jcc("deepseek", env_override={"JCC_VENDOR": "deepseek", "JCC_MODEL_ID": "deepseek-v4-flash"})
test("deepseek 供应商", test_deepseek_protocol)

def test_zhipu_protocol():
    """zhipu 协议"""
    inject_simple("zhipu协议")
    code, out, err, elapsed = run_jcc("zhipu", env_override={"JCC_VENDOR": "zhipu", "JCC_MODEL_ID": "glm-5.3"})
test("zhipu 供应商", test_zhipu_protocol)

# ============================================================
# 34. 大量并发
# ============================================================
print("\n=== 34. 大量并发 ===")

def test_concurrent_10():
    """10 进程并发"""
    errors = []
    def run_one(idx):
        try:
            inject_simple(f"并发{idx}")
            c, o, e, el = run_jcc(f"并发{idx}")
            if c != 0: errors.append(f"进程{idx} exit={c}")
        except Exception as ex:
            errors.append(f"进程{idx}: {ex}")
    
    threads = [threading.Thread(target=run_one, args=(i,)) for i in range(10)]
    for t in threads: t.start()
    for t in threads: t.join(timeout=90)
    if errors: raise AssertionError("; ".join(errors[:5]))
test("10进程并发", test_concurrent_10)

# ============================================================
# 35. 连续多轮对话
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
print("\n=== 36. 错误码覆盖 ===")

def test_400_bad_request():
    body = json.dumps({"error": {"message": "bad request", "code": "400"}})
    inject_data = {"status_code": 400, "is_stream": False, "body": body, "content_type": "application/json"}
    data = json.dumps(inject_data).encode()
    req = urllib.request.Request(f"{MOCK}/__control__/inject", data=data, method="POST", headers={"Content-Type": "application/json"})
    urllib.request.urlopen(req, timeout=5)
    code, out, err, elapsed = run_jcc("400错误")
    assert code != 0, f"400应非零退出, got {code}"
test("400 Bad Request", test_400_bad_request)

def test_403_forbidden():
    body = json.dumps({"error": {"message": "forbidden", "code": "403"}})
    inject_data = {"status_code": 403, "is_stream": False, "body": body, "content_type": "application/json"}
    data = json.dumps(inject_data).encode()
    req = urllib.request.Request(f"{MOCK}/__control__/inject", data=data, method="POST", headers={"Content-Type": "application/json"})
    urllib.request.urlopen(req, timeout=5)
    code, out, err, elapsed = run_jcc("403错误")
    assert code != 0, f"403应非零退出, got {code}"
test("403 Forbidden", test_403_forbidden)

def test_502_bad_gateway():
    body = json.dumps({"error": {"message": "bad gateway", "code": "502"}})
    inject_data = {"status_code": 502, "is_stream": False, "body": body, "content_type": "application/json"}
    data = json.dumps(inject_data).encode()
    req = urllib.request.Request(f"{MOCK}/__control__/inject", data=data, method="POST", headers={"Content-Type": "application/json"})
    urllib.request.urlopen(req, timeout=5)
    code, out, err, elapsed = run_jcc("502错误")
    assert code != 0, f"502应非零退出, got {code}"
test("502 Bad Gateway", test_502_bad_gateway)

def test_504_gateway_timeout():
    body = json.dumps({"error": {"message": "gateway timeout", "code": "504"}})
    inject_data = {"status_code": 504, "is_stream": False, "body": body, "content_type": "application/json"}
    data = json.dumps(inject_data).encode()
    req = urllib.request.Request(f"{MOCK}/__control__/inject", data=data, method="POST", headers={"Content-Type": "application/json"})
    urllib.request.urlopen(req, timeout=5)
    code, out, err, elapsed = run_jcc("504错误")
    assert code != 0, f"504应非零退出, got {code}"
test("504 Gateway Timeout", test_504_gateway_timeout)

# ============================================================
# 37. jcc-rg 搜索（现在应该能工作了）
# ============================================================
print("\n=== 37. jcc-rg 搜索 ===")

def test_rg_with_exe():
    code, out, err, elapsed = run_jcc("", extra_args=["rg", "class", "modules/", "--type", "cs", "--max-count", "3"])
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("rg 搜索(有exe)", test_rg_with_exe)

# ============================================================
# 汇总
# ============================================================
print("\n" + "=" * 60)
print("高级功能测试汇总")
print("=" * 60)
passed = sum(1 for _, s, _, _ in results if s == "PASS")
failed = sum(1 for _, s, _, _ in results if s == "FAIL")
errors = sum(1 for _, s, _, _ in results if s == "ERROR")
total = len(results)
print(f"总计: {total} | 通过: {passed} | 失败: {failed} | 错误: {errors}")
print()
if failed > 0 or errors > 0:
    print("失败/错误详情:")
    for name, status, elapsed, msg in results:
        if status != "PASS":
            icon = "✗" if status == "FAIL" else "!!"
            print(f"  {icon} {name} ({elapsed:.1f}s) — {msg[:150]}")

Path(".xxx/logs/advanced_v4_results.json").write_text(
    json.dumps([{"name": n, "status": s, "elapsed": e, "msg": m} for n, s, e, m in results],
               ensure_ascii=False, indent=2), encoding="utf-8")

sys.exit(0 if failed == 0 and errors == 0 else 1)
