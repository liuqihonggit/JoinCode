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
# 1. 基础工具调用测试
# ============================================================
print("\n=== 1. 基础工具调用测试 ===")

def test_file_read():
    inject_tool_call("read_file", {"filePath": "README.md"}, "文件内容读取完成")
    code, out, err, elapsed = run_jcc("读取 README.md 文件")
    assert_eq(code, 0, f"exit code, stderr={err[:200]}")
test("file_read 工具调用", test_file_read)

def test_file_write():
    inject_tool_call("write_file", {"filePath": "/tmp/test.txt", "content": "hello"}, "文件写入完成")
    code, out, err, elapsed = run_jcc("写入测试文件")
    assert_eq(code, 0, f"exit code, stderr={err[:200]}")
test("file_write 工具调用", test_file_write)

def test_search():
    inject_tool_call("grep", {"pattern": "class", "include": "*.cs"}, "搜索完成，找到 10 个匹配")
    code, out, err, elapsed = run_jcc("搜索 class 关键字")
    assert_eq(code, 0, f"exit code, stderr={err[:200]}")
test("grep 搜索工具", test_search)

def test_glob():
    inject_tool_call("glob", {"pattern": "**/*.cs"}, "glob 完成，找到 50 个文件")
    code, out, err, elapsed = run_jcc("查找所有 cs 文件")
    assert_eq(code, 0, f"exit code, stderr={err[:200]}")
test("glob 工具", test_glob)

def test_git_status():
    inject_tool_call("git_status", {}, "git status 完成，有 3 个修改文件")
    code, out, err, elapsed = run_jcc("查看 git 状态")
    assert_eq(code, 0, f"exit code, stderr={err[:200]}")
test("git_status 工具", test_git_status)

def test_bash():
    inject_tool_call("bash", {"command": "echo hello"}, "hello\n")
    code, out, err, elapsed = run_jcc("运行 echo hello")
    assert_eq(code, 0, f"exit code, stderr={err[:200]}")
test("bash 工具", test_bash)

# ============================================================
# 2. 多工具链式调用
# ============================================================
print("\n=== 2. 多工具链式调用 ===")

def test_two_tool_chain():
    # 第一轮：tool_calls 两个工具
    chunks1 = [
        {"choices":[{"delta":{"content": ""}}]},
        {"choices":[{"delta":{"tool_calls":[
            {"index":0,"id":"call_1","function":{"name":"read_file","arguments":json.dumps({"filePath":"test.cs"})}},
            {"index":1,"id":"call_2","function":{"name":"grep","arguments":json.dumps({"pattern":"class"})}}
        ]}}]},
        {"choices":[{"delta":{}}],"finish_reason":"tool_calls"},
        "[DONE]"
    ]
    inject(chunks1)
    inject("两个工具都执行完成")
    code, out, err, elapsed = run_jcc("读取文件并搜索内容")
    assert_eq(code, 0, f"exit code, stderr={err[:200]}")
test("双工具并行调用", test_two_tool_chain)

def test_three_tool_chain():
    chunks1 = [
        {"choices":[{"delta":{"content": ""}}]},
        {"choices":[{"delta":{"tool_calls":[
            {"index":0,"id":"call_1","function":{"name":"read_file","arguments":json.dumps({"filePath":"a.cs"})}},
            {"index":1,"id":"call_2","function":{"name":"glob","arguments":json.dumps({"pattern":"**/*.ts"})}},
            {"index":2,"id":"call_3","function":{"name":"git_status","arguments":json.dumps({})}}
        ]}}]},
        {"choices":[{"delta":{}}],"finish_reason":"tool_calls"},
        "[DONE]"
    ]
    inject(chunks1)
    inject("三个工具全部完成")
    code, out, err, elapsed = run_jcc("同时执行三个工具")
    assert_eq(code, 0, f"exit code, stderr={err[:200]}")
test("三工具并行调用", test_three_tool_chain)

# ============================================================
# 3. 边缘条件测试
# ============================================================
print("\n=== 3. 边缘条件测试 ===")

def test_empty_content():
    inject("")
    code, out, err, elapsed = run_jcc("你好")
    assert_eq(code, 0, f"exit code, stderr={err[:200]}")
test("空内容响应", test_empty_content)

def test_very_long_content():
    long_text = "这是一段很长的文本。" * 500  # ~5000 字
    inject(long_text)
    code, out, err, elapsed = run_jcc("生成长文本")
    assert_eq(code, 0, f"exit code, stderr={err[:200]}")
test("超长内容响应(5000字)", test_very_long_content)

def test_unicode_emoji():
    inject("🎉🚀✨💡🔥\n\n你好世界！\n\n\t缩进\t制表符\n\"引号\" '单引号'")
    code, out, err, elapsed = run_jcc("测试 Unicode")
    assert_eq(code, 0, f"exit code, stderr={err[:200]}")
test("Unicode+Emoji+特殊字符", test_unicode_emoji)

def test_null_content_chunk():
    chunks = [
        {"choices":[{"delta":{"content": None}}]},
        {"choices":[{"delta":{"content": "有效内容"}}]},
        {"choices":[{"delta":{}}],"finish_reason":"stop"},
        "[DONE]"
    ]
    inject(chunks)
    code, out, err, elapsed = run_jcc("测试 null content chunk")
    assert_eq(code, 0, f"exit code, stderr={err[:200]}")
test("null content chunk", test_null_content_chunk)

def test_missing_done():
    chunks = [
        {"choices":[{"delta":{"content": "没有 DONE 标记"}}]},
        {"choices":[{"delta":{}}],"finish_reason":"stop"},
    ]
    inject(chunks)
    code, out, err, elapsed = run_jcc("测试无 [DONE] 标记")
    assert_eq(code, 0, f"exit code, stderr={err[:200]}")
test("无 [DONE] 标记", test_missing_done)

def test_empty_tool_calls_array():
    chunks = [
        {"choices":[{"delta":{"content": "开始"}}]},
        {"choices":[{"delta":{"tool_calls": []}}]},
        {"choices":[{"delta":{"content": "结束"}}]},
        {"choices":[{"delta":{}}],"finish_reason":"stop"},
        "[DONE]"
    ]
    inject(chunks)
    code, out, err, elapsed = run_jcc("测试空 tool_calls 数组")
    assert_eq(code, 0, f"exit code, stderr={err[:200]}")
test("空 tool_calls 数组", test_empty_tool_calls_array)

def test_malformed_json_chunk():
    chunks = [
        {"choices":[{"delta":{"content": "正常"}}]},
        "NOT_VALID_JSON",
        {"choices":[{"delta":{"content": "恢复"}}]},
        {"choices":[{"delta":{}}],"finish_reason":"stop"},
        "[DONE]"
    ]
    inject(chunks)
    code, out, err, elapsed = run_jcc("测试畸形 JSON chunk")
    # 畸形 chunk 应被跳过，不应崩溃
    assert_eq(code, 0, f"exit code, stderr={err[:200]}")
test("畸形 JSON chunk 容错", test_malformed_json_chunk)

def test_404_error():
    inject("Not Found", is_stream=False, status=404)
    code, out, err, elapsed = run_jcc("触发 404 错误")
    assert code != 0, f"404 应该非零退出, got exit={code}"
test("404 错误处理", test_404_error)

def test_401_unauthorized():
    inject("Unauthorized", is_stream=False, status=401)
    code, out, err, elapsed = run_jcc("触发 401 错误")
    assert code != 0, f"401 应该非零退出, got exit={code}"
test("401 未授权错误", test_401_unauthorized)

def test_500_server_error():
    inject("Internal Server Error", is_stream=False, status=500)
    code, out, err, elapsed = run_jcc("触发 500 错误")
    # 500 会重试，最终非零退出
    assert code != 0, f"500 应该非零退出, got exit={code}"
test("500 服务器错误", test_500_server_error)

def test_429_rate_limit():
    inject("Rate Limited", is_stream=False, status=429, headers={"Retry-After": "1"})
    code, out, err, elapsed = run_jcc("触发 429 限流")
    # 429 会重试后失败
    assert code != 0, f"429 重试后应非零退出, got exit={code}"
test("429 限流处理", test_429_rate_limit)

# ============================================================
# 4. 思考链 (reasoning_content) 测试
# ============================================================
print("\n=== 4. 思考链测试 ===")

def test_reasoning_content():
    chunks = [
        {"choices":[{"delta":{"reasoning_content": "让我思考一下..."}}]},
        {"choices":[{"delta":{"reasoning_content": "这个问题的答案是..."}}]},
        {"choices":[{"delta":{"content": "答案是42"}}]},
        {"choices":[{"delta":{}}],"finish_reason":"stop"},
        "[DONE]"
    ]
    inject(chunks)
    code, out, err, elapsed = run_jcc("思考并回答问题")
    assert_eq(code, 0, f"exit code, stderr={err[:200]}")
test("reasoning_content 思考链", test_reasoning_content)

def test_large_reasoning():
    # 10KB 思考链
    big_reasoning = "思考" * 5000
    chunks = [
        {"choices":[{"delta":{"reasoning_content": big_reasoning}}]},
        {"choices":[{"delta":{"content": "结论"}}]},
        {"choices":[{"delta":{}}],"finish_reason":"stop"},
        "[DONE]"
    ]
    inject(chunks)
    code, out, err, elapsed = run_jcc("大量思考后回答")
    assert_eq(code, 0, f"exit code, stderr={err[:200]}")
test("超大思考链(10KB)", test_large_reasoning)

# ============================================================
# 5. 斜杠命令测试
# ============================================================
print("\n=== 5. 斜杠命令测试 ===")

def test_slash_help():
    code, out, err, elapsed = run_jcc("", extra_args=["slash_call", "/help", "{}"])
    # slash_call 不需要 LLM
    assert_eq(code, 0, f"exit code, stderr={err[:200]}")
test("/help 斜杠命令", test_slash_help)

def test_slash_list():
    code, out, err, elapsed = run_jcc("", extra_args=["slash_list"])
    assert_eq(code, 0, f"exit code, stderr={err[:200]}")
test("slash_list 命令", test_slash_list)

def test_slash_schema():
    code, out, err, elapsed = run_jcc("", extra_args=["slash_schema", "/compact"])
    assert_eq(code, 0, f"exit code, stderr={err[:200]}")
test("slash_schema /compact", test_slash_schema)

# ============================================================
# 6. MCP 工具列表测试
# ============================================================
print("\n=== 6. MCP 工具列表测试 ===")

def test_mcp_list():
    code, out, err, elapsed = run_jcc("", extra_args=["mcp_list"])
    assert_eq(code, 0, f"exit code, stderr={err[:200]}")
    assert_contains(out, "tool", "mcp_list 应输出工具列表")
test("mcp_list 全量工具", test_mcp_list)

def test_mcp_list_category():
    code, out, err, elapsed = run_jcc("", extra_args=["mcp_list", "--category", "github"])
    assert_eq(code, 0, f"exit code, stderr={err[:200]}")
test("mcp_list --category github", test_mcp_list_category)

def test_mcp_search():
    code, out, err, elapsed = run_jcc("", extra_args=["mcp_search", "pr check"])
    assert_eq(code, 0, f"exit code, stderr={err[:200]}")
test("mcp_search 搜索", test_mcp_search)

# ============================================================
# 7. 多轮对话测试
# ============================================================
print("\n=== 7. 多轮对话测试 ===")

def test_multi_turn_3():
    for i in range(3):
        inject(f"这是第 {i+1} 轮回复")
    code, out, err, elapsed = run_jcc("进行3轮对话", await_sec=60)
    # 多轮需要交互模式或 --continue，非交互模式只一轮
    # 这里主要测试不崩溃
test("多轮对话(3轮预注入)", test_multi_turn_3)

# ============================================================
# 8. 并发测试
# ============================================================
print("\n=== 8. 并发测试 ===")

def test_concurrent_3():
    for i in range(3):
        inject(f"并发响应 {i+1}")
    
    def run_one(idx):
        inject(f"并发响应 {idx}")
        c, o, e, el = run_jcc(f"并发测试 {idx}")
        assert_eq(c, 0, f"并发 {idx} exit code")
    
    threads = []
    for i in range(3):
        t = threading.Thread(target=run_one, args=(i,))
        threads.append(t)
        t.start()
    for t in threads:
        t.join(timeout=60)
test("3 进程并发", test_concurrent_3)

# ============================================================
# 9. 响应延迟测试
# ============================================================
print("\n=== 9. 响应延迟测试 ===")

def test_slow_response():
    inject("慢响应完成", delay_ms=3000)
    code, out, err, elapsed = run_jcc("等待慢响应", await_sec=30)
    assert_eq(code, 0, f"exit code, stderr={err[:200]}")
test("3秒延迟响应", test_slow_response)

# ============================================================
# 10. 非流式响应测试
# ============================================================
print("\n=== 10. 非流式响应测试 ===")

def test_non_stream():
    body = json.dumps({
        "id": "chatcmpl-1",
        "object": "chat.completion",
        "choices": [{"index": 0, "message": {"role": "assistant", "content": "非流式响应"}, "finish_reason": "stop"}],
        "usage": {"prompt_tokens": 10, "completion_tokens": 5, "total_tokens": 15}
    })
    inject(body, is_stream=False)
    code, out, err, elapsed = run_jcc("测试非流式响应")
    assert_eq(code, 0, f"exit code, stderr={err[:200]}")
test("非流式响应", test_non_stream)

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
    print(f"  {icon} {name} ({elapsed:.1f}s)" + (f" — {msg[:100]}" if msg else ""))

# 保存结果
Path(".xxx/logs/comprehensive_results.json").write_text(
    json.dumps([{"name": n, "status": s, "elapsed": e, "msg": m} for n, s, e, m in results], 
               ensure_ascii=False, indent=2), encoding="utf-8")

sys.exit(0 if failed == 0 and errors == 0 else 1)
