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
    body = {"status": status, "is_stream": True, "stream_chunks": chunks, "content_type": "text/event-stream"}
    if delay_ms > 0: body["delay_ms"] = delay_ms
    if headers: body["headers"] = headers
    data = json.dumps(body).encode()
    req = urllib.request.Request(f"{MOCK}/__control__/inject", data=data, method="POST", headers={"Content-Type": "application/json"})
    try: urllib.request.urlopen(req, timeout=5)
    except Exception as e: print(f"  INJECT ERROR: {e}")

def inject_non_stream(body_text, status=200, headers=None):
    body = {"status": status, "is_stream": False, "body": body_text, "content_type": "application/json"}
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
# 14. 连接断开/超时
# ============================================================
print("\n=== 14. 连接断开/超时 ===")

def test_empty_stream():
    """流式连接立即关闭（无任何数据）"""
    inject_stream([])
    code, out, err, elapsed = run_jcc("空流")
    # 应该回退到非流式或报错
    # 不崩溃即可
test("空流(立即关闭)", test_empty_stream)

def test_partial_stream():
    """流式只发一半就断开"""
    chunks = [
        sse_line({"choices": [{"index": 0, "delta": {"content": "部分内容"}}]}),
        # 没有 finish_reason 和 [DONE]
    ]
    inject_stream(chunks)
    code, out, err, elapsed = run_jcc("部分流")
    # 不崩溃即可
test("部分流(无finish)", test_partial_stream)

def test_only_role_chunk():
    """只有 role chunk，无 content"""
    chunks = [
        sse_line({"choices": [{"index": 0, "delta": {"role": "assistant"}}]}),
        sse_line({"choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}]}),
        "data: [DONE]\n\n"
    ]
    inject_stream(chunks)
    code, out, err, elapsed = run_jcc("只有role")
    # 空回复会报错，但不崩溃
test("只有role chunk", test_only_role_chunk)

def test_10s_delay():
    """10秒延迟响应"""
    inject_stream(make_stream_chunks(content_text="慢响应"), delay_ms=10000)
    code, out, err, elapsed = run_jcc("10秒延迟", await_sec=30)
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("10秒延迟响应", test_10s_delay)

# ============================================================
# 15. 超大参数
# ============================================================
print("\n=== 15. 超大参数 ===")

def test_huge_tool_args():
    """工具参数超大"""
    big_args = {"content": "A" * 10000, "filePath": "test.txt"}
    inject_tool_call("write_file", big_args, "写入完成")
    code, out, err, elapsed = run_jcc("超大工具参数")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("超大工具参数(10KB)", test_huge_tool_args)

def test_huge_content_response():
    """响应内容超大"""
    huge = "X" * 50000
    inject_simple(huge)
    code, out, err, elapsed = run_jcc("超大响应")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("超大响应(50KB)", test_huge_content_response)

def test_many_tool_calls():
    """一次返回很多工具调用"""
    tools = [{"id": f"call_{i}", "name": "read_file", "args": {"filePath": f"file{i}.cs"}} for i in range(10)]
    inject_stream(make_stream_chunks(tool_calls=tools, finish_reason="tool_calls"))
    inject_simple("10个工具完成")
    code, out, err, elapsed = run_jcc("10个工具调用", await_sec=60)
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("10个工具同时调用", test_many_tool_calls)

# ============================================================
# 16. 工具调用错误恢复
# ============================================================
print("\n=== 16. 工具调用错误恢复 ===")

def test_tool_then_error():
    """第一轮工具调用，第二轮错误"""
    inject_tool_call("read_file", {"filePath": "test.cs"})
    # 第二轮注入错误
    inject_non_stream('{"error":{"message":"server error","code":"500"}}', status=500)
    code, out, err, elapsed = run_jcc("工具后错误")
    # 不崩溃即可
test("工具调用后错误", test_tool_then_error)

def test_error_then_success():
    """第一轮500错误重试，第二轮成功"""
    inject_non_stream('{"error":{"message":"server error","code":"500"}}', status=500)
    inject_simple("重试成功")
    code, out, err, elapsed = run_jcc("错误后重试")
    # 重试后可能成功
test("500错误后重试", test_error_then_success)

# ============================================================
# 17. 多轮工具链
# ============================================================
print("\n=== 17. 多轮工具链 ===")

def test_tool_chain_3():
    """3轮工具调用链"""
    # 第1轮：工具调用
    inject_tool_call("read_file", {"filePath": "a.cs"})
    # 第2轮：另一个工具调用
    inject_tool_call("grep", {"pattern": "class"})
    # 第3轮：最终回复
    inject_simple("链式调用完成")
    code, out, err, elapsed = run_jcc("3轮工具链", await_sec=60)
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("3轮工具链", test_tool_chain_3)

def test_tool_chain_5():
    """5轮工具调用链"""
    for i in range(4):
        inject_tool_call("read_file", {"filePath": f"file{i}.cs"})
    inject_simple("5轮链完成")
    code, out, err, elapsed = run_jcc("5轮工具链", await_sec=90)
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("5轮工具链", test_tool_chain_5)

# ============================================================
# 18. 特殊字符在工具参数中
# ============================================================
print("\n=== 18. 特殊字符工具参数 ===")

def test_unicode_tool_args():
    inject_tool_call("write_file", {"filePath": ".xxx/测试/文件.txt", "content": "中文内容🎉"}, "写入完成")
    code, out, err, elapsed = run_jcc("Unicode工具参数")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("Unicode工具参数", test_unicode_tool_args)

def test_json_in_tool_args():
    """工具参数包含嵌套JSON"""
    inject_tool_call("write_file", {"filePath": ".xxx/test.json", "content": json.dumps({"nested": {"key": "value"}})}, "写入完成")
    code, out, err, elapsed = run_jcc("嵌套JSON参数")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("嵌套JSON工具参数", test_json_in_tool_args)

def test_special_chars_in_args():
    """工具参数包含特殊字符"""
    inject_tool_call("write_file", {"filePath": ".xxx/test.txt", "content": '引号"\'\n\t换行\\反斜杠'}, "写入完成")
    code, out, err, elapsed = run_jcc("特殊字符参数")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("特殊字符工具参数", test_special_chars_in_args)

# ============================================================
# 19. jcc 子命令测试
# ============================================================
print("\n=== 19. jcc 子命令 ===")

def test_schema_cmd():
    code, out, err, elapsed = run_jcc("", extra_args=["schema"])
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("schema 子命令", test_schema_cmd)

def test_config_print():
    code, out, err, elapsed = run_jcc("", extra_args=["config-print"])
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("config-print 子命令", test_config_print)

def test_rg_search():
    code, out, err, elapsed = run_jcc("", extra_args=["rg", "class", "modules/", "--type", "cs", "--max-count", "5"])
    # rg 可能有自己的退出码
    # 只要不崩溃即可
test("rg 子命令", test_rg_search)

# ============================================================
# 20. 环境变量测试
# ============================================================
print("\n=== 20. 环境变量 ===")

def test_different_model():
    inject_simple("模型测试")
    code, out, err, elapsed = run_jcc("测试模型", env_override={"JCC_MODEL_ID": "gpt-4o"})
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("切换模型ID", test_different_model)

def test_max_output_chars():
    """测试输出截断"""
    inject_simple("A" * 10000)
    code, out, err, elapsed = run_jcc("截断测试", env_override={"JCC_MAX_OUTPUT_CHARS": "1000"})
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("JCC_MAX_OUTPUT_CHARS 截断", test_max_output_chars)

# ============================================================
# 21. 并发压测
# ============================================================
print("\n=== 21. 并发压测 ===")

def test_concurrent_5():
    errors = []
    def run_one(idx):
        try:
            inject_simple(f"并发{idx}")
            c, o, e, el = run_jcc(f"并发{idx}")
            if c != 0: errors.append(f"进程{idx} exit={c}")
        except Exception as ex:
            errors.append(f"进程{idx}: {ex}")
    
    threads = [threading.Thread(target=run_one, args=(i,)) for i in range(5)]
    for t in threads: t.start()
    for t in threads: t.join(timeout=60)
    if errors: raise AssertionError("; ".join(errors))
test("5进程并发", test_concurrent_5)

# ============================================================
# 22. 流式中断恢复
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
print("\n=== 23. usage 统计 ===")

def test_usage_first_chunk():
    """usage 在首个 chunk"""
    chunks = [
        sse_line({"choices": [{"index": 0, "delta": {"content": "内容"}}], "usage": {"prompt_tokens": 100, "completion_tokens": 50, "total_tokens": 150}}),
        sse_line({"choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}]}),
        "data: [DONE]\n\n"
    ]
    inject_stream(chunks)
    code, out, err, elapsed = run_jcc("usage在首个chunk")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("usage在首个chunk", test_usage_first_chunk)

def test_no_usage():
    """无 usage 字段"""
    inject_simple("无usage")
    code, out, err, elapsed = run_jcc("无usage")
    assert_eq(code, 0, f"exit code, stderr={err[-200:]}")
test("无usage字段", test_no_usage)

# ============================================================
# 24. 供应商切换
# ============================================================
print("\n=== 24. 供应商切换 ===")

def test_vendor_anthropic():
    """切换到 anthropic 供应商"""
    inject_simple("anthropic响应")
    code, out, err, elapsed = run_jcc("anthropic测试", env_override={"JCC_VENDOR": "anthropic", "JCC_MODEL_ID": "claude-opus-5"})
    # 不崩溃即可
test("切换到anthropic", test_vendor_anthropic)

def test_vendor_deepseek():
    """切换到 deepseek 供应商"""
    inject_simple("deepseek响应")
    code, out, err, elapsed = run_jcc("deepseek测试", env_override={"JCC_VENDOR": "deepseek", "JCC_MODEL_ID": "deepseek-v4-flash"})
test("切换到deepseek", test_vendor_deepseek)

# ============================================================
# 汇总
# ============================================================
print("\n" + "=" * 60)
print("扩展测试汇总")
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

Path(".xxx/logs/extended_v3_results.json").write_text(
    json.dumps([{"name": n, "status": s, "elapsed": e, "msg": m} for n, s, e, m in results],
               ensure_ascii=False, indent=2), encoding="utf-8")

sys.exit(0 if failed == 0 and errors == 0 else 1)
