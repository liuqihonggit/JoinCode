#!/usr/bin/env python3
"""S16: 工具执行失败 + 特殊字符 + 嵌套JSON 参数测试

测试矩阵:
  T1: 工具执行失败（bash exit 1）→ tool_result is_error=true
  T2: 特殊字符（Unicode/emoji/换行）→ 正确传递
  T3: 嵌套 JSON 参数 → 正确传递
  T4: tool_call 无 finish_reason（finish_reason=stop 但有 tool_calls）
  T5: 多个 thinking block
"""
import json
import subprocess
import time
import urllib.request
import os

JCC = "artifacts/bin/JoinCode/Debug/net10.0/jcc.dll"
MOCK_CTRL = "http://127.0.0.1:8091/__control__"

def clear_mock():
    req = urllib.request.Request(f"{MOCK_CTRL}/clear", method="POST")
    urllib.request.urlopen(req, timeout=5)

def inject(data):
    body = json.dumps(data).encode()
    req = urllib.request.Request(
        f"{MOCK_CTRL}/inject",
        data=body,
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    urllib.request.urlopen(req, timeout=5)

def run_jcc(prompt="test", env_extra=None, timeout=30):
    env = {
        "JCC_ENDPOINT": "http://127.0.0.1:8091/v1",
        "OPENAI_API_KEY": "test-key",
        "JCC_DEBUGLOG": "1",
        "JCC_DISABLE_STREAMING_FALLBACK": "1",
    }
    if env_extra:
        env.update(env_extra)

    cmd = [
        "dotnet", JCC,
        "--non-interactive",
        "--prompt", prompt,
        "--format", "ndjson",
        "--trust",
        "--permission-mode", "bypass",
    ]

    start = time.time()
    proc = subprocess.run(
        cmd, capture_output=True, text=True, timeout=timeout,
        env={**os.environ, **env}, cwd=r"D:\project\w1",
    )
    elapsed = time.time() - start
    return proc.returncode, elapsed, proc.stdout, proc.stderr

def parse_ndjson(stdout):
    """解析 NDJSON 输出"""
    results = []
    for line in stdout.strip().splitlines():
        if line.startswith("{"):
            try:
                results.append(json.loads(line))
            except:
                pass
    return results

def test_t1_tool_failure():
    """T1: 工具执行失败（bash exit 1）→ tool_result is_error=true"""
    print("\n=== T1: 工具执行失败 (bash 'exit 1') ===")
    clear_mock()

    # 第一轮：工具调用 bash "exit 1"
    chunks1 = [
        'data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_fail_001","function":{"name":"bash","arguments":"{\\"command\\":\\"exit 1\\"}"}}]}}]}\n\n',
        'data: {"choices":[{"delta":{},"finish_reason":"tool_calls","index":0}]}\n\n',
        'data: [DONE]\n\n',
    ]
    inject({"is_stream": True, "content_type": "text/event-stream", "stream_chunks": chunks1})

    # 第二轮：最终文本
    chunks2 = [
        'data: {"choices":[{"delta":{"content":"Tool failed as expected"},"index":0}]}\n\n',
        'data: {"choices":[{"delta":{},"finish_reason":"stop","index":0}]}\n\n',
        'data: [DONE]\n\n',
    ]
    inject({"is_stream": True, "content_type": "text/event-stream", "stream_chunks": chunks2})

    rc, elapsed, stdout, stderr = run_jcc("Run failing command")
    events = parse_ndjson(stdout)

    print(f"exit_code={rc}, elapsed={elapsed:.1f}s")
    for e in events:
        if e.get("type") == "user":
            content = e.get("message", {}).get("content", [])
            for block in content:
                if block.get("type") == "tool_result":
                    is_error = block.get("is_error")
                    content_val = block.get("content", "")
                    print(f"  tool_result: is_error={is_error}, content={str(content_val)[:200]}")

    # 检查
    tool_results = []
    for e in events:
        if e.get("type") == "user":
            for block in e.get("message", {}).get("content", []):
                if block.get("type") == "tool_result":
                    tool_results.append(block)

    if tool_results:
        tr = tool_results[0]
        if tr.get("is_error") == True:
            print("✅ tool_result is_error=true — 工具失败正确标记")
        elif tr.get("is_error") == False:
            print("🔴 Bug: 工具失败但 is_error=false")
        else:
            print(f"⚠️ is_error 字段缺失或为 {tr.get('is_error')}")
    else:
        print("⚠️ 未找到 tool_result")

    return rc

def test_t2_special_chars():
    """T2: 特殊字符（Unicode/emoji/换行）"""
    print("\n=== T2: 特殊字符 (中文/emoji/换行) ===")
    clear_mock()

    text = "你好世界 🌍\n换行\t制表符 \"引号\" \\反斜杠\\"
    chunks = [
        f'data: {{"choices":[{{"delta":{{"content":{json.dumps(text)}}},"index":0}}]}}\n\n',
        'data: {"choices":[{"delta":{},"finish_reason":"stop","index":0}]}\n\n',
        'data: [DONE]\n\n',
    ]
    inject({"is_stream": True, "content_type": "text/event-stream", "stream_chunks": chunks})

    rc, elapsed, stdout, stderr = run_jcc("Test special chars")
    events = parse_ndjson(stdout)

    print(f"exit_code={rc}, elapsed={elapsed:.1f}s")
    for e in events:
        if e.get("type") == "assistant":
            for block in e.get("message", {}).get("content", []):
                if block.get("type") == "text":
                    actual = block.get("text", "")
                    print(f"  期望: {repr(text)}")
                    print(f"  实际: {repr(actual)}")
                    if actual == text:
                        print("✅ 特殊字符正确传递")
                    else:
                        print("🔴 Bug: 特殊字符传递错误")
                        # 找出差异
                        for i, (a, b) in enumerate(zip(text, actual)):
                            if a != b:
                                print(f"    首个差异位置 {i}: 期望 {repr(a)} 实际 {repr(b)}")
                                break
                        if len(text) != len(actual):
                            print(f"    长度差异: 期望 {len(text)} 实际 {len(actual)}")

    return rc

def test_t3_nested_json():
    """T3: 嵌套 JSON 参数"""
    print("\n=== T3: 嵌套 JSON 参数 ===")
    clear_mock()

    nested_args = {"command": "echo test", "options": {"verbose": True, "timeout": 30, "env": {"PATH": "/usr/bin"}}}
    args_str = json.dumps(nested_args)

    chunks1 = [
        f'data: {{"choices":[{{"delta":{{"tool_calls":[{{"index":0,"id":"call_nested_001","function":{{"name":"bash","arguments":{json.dumps(args_str)}}}}}]}}]}}}}\n\n',
        'data: {"choices":[{"delta":{},"finish_reason":"tool_calls","index":0}]}\n\n',
        'data: [DONE]\n\n',
    ]
    inject({"is_stream": True, "content_type": "text/event-stream", "stream_chunks": chunks1})

    chunks2 = [
        'data: {"choices":[{"delta":{"content":"Done"},"index":0}]}\n\n',
        'data: {"choices":[{"delta":{},"finish_reason":"stop","index":0}]}\n\n',
        'data: [DONE]\n\n',
    ]
    inject({"is_stream": True, "content_type": "text/event-stream", "stream_chunks": chunks2})

    rc, elapsed, stdout, stderr = run_jcc("Test nested JSON")
    events = parse_ndjson(stdout)

    print(f"exit_code={rc}, elapsed={elapsed:.1f}s")
    for e in events:
        if e.get("type") == "assistant":
            for block in e.get("message", {}).get("content", []):
                if block.get("type") == "tool_use":
                    input_val = block.get("input", "")
                    print(f"  input: {str(input_val)[:300]}")
                    try:
                        parsed = json.loads(input_val) if isinstance(input_val, str) else input_val
                        if isinstance(parsed, dict) and "options" in parsed:
                            print("✅ 嵌套 JSON 正确传递")
                        else:
                            print(f"⚠️ JSON 结构不完整: {parsed}")
                    except Exception as ex:
                        print(f"🔴 JSON 解析失败: {ex}")

    return rc

def test_t4_no_finish_reason():
    """T4: tool_call 但 finish_reason=stop（非 tool_calls）"""
    print("\n=== T4: tool_call + finish_reason=stop ===")
    clear_mock()

    chunks = [
        'data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_nofin_001","function":{"name":"bash","arguments":"{\\"command\\":\\"echo hi\\"}"}}]}}]}\n\n',
        'data: {"choices":[{"delta":{},"finish_reason":"stop","index":0}]}\n\n',
        'data: [DONE]\n\n',
    ]
    inject({"is_stream": True, "content_type": "text/event-stream", "stream_chunks": chunks})

    rc, elapsed, stdout, stderr = run_jcc("Test no finish reason")
    events = parse_ndjson(stdout)

    print(f"exit_code={rc}, elapsed={elapsed:.1f}s")
    types = [e.get("type") for e in events]
    print(f"  事件类型序列: {types}")

    has_tool_use = any(
        block.get("type") == "tool_use"
        for e in events if e.get("type") == "assistant"
        for block in e.get("message", {}).get("content", [])
    )
    if has_tool_use:
        print("⚠️ finish_reason=stop 但仍输出了 tool_use — 可能正常（有 tool_calls delta）")
    else:
        print("ℹ️ finish_reason=stop 时未输出 tool_use — tool_calls 被忽略")

    return rc

def test_t5_multiple_thinking():
    """T5: 多个 thinking block"""
    print("\n=== T5: 多个 thinking block ===")
    clear_mock()

    chunks = [
        'data: {"choices":[{"delta":{"reasoning_content":"First thought "},"index":0}]}\n\n',
        'data: {"choices":[{"delta":{"reasoning_content":"second thought"},"index":0}]}\n\n',
        'data: {"choices":[{"delta":{"content":"Final answer"},"index":0}]}\n\n',
        'data: {"choices":[{"delta":{},"finish_reason":"stop","index":0}]}\n\n',
        'data: [DONE]\n\n',
    ]
    inject({"is_stream": True, "content_type": "text/event-stream", "stream_chunks": chunks})

    rc, elapsed, stdout, stderr = run_jcc("Test multiple thinking")
    events = parse_ndjson(stdout)

    print(f"exit_code={rc}, elapsed={elapsed:.1f}s")
    for e in events:
        if e.get("type") == "assistant":
            for block in e.get("message", {}).get("content", []):
                btype = block.get("type")
                if btype == "thinking":
                    print(f"  thinking: {repr(block.get('thinking', ''))}")
                elif btype == "text":
                    print(f"  text: {repr(block.get('text', ''))}")

    thinking_blocks = []
    text_blocks = []
    for e in events:
        if e.get("type") == "assistant":
            for block in e.get("message", {}).get("content", []):
                if block.get("type") == "thinking":
                    thinking_blocks.append(block.get("thinking", ""))
                elif block.get("type") == "text":
                    text_blocks.append(block.get("text", ""))

    if thinking_blocks:
        combined = "".join(thinking_blocks)
        if "First thought" in combined and "second thought" in combined:
            print(f"✅ 多 thinking block 合并: {repr(combined)}")
        else:
            print(f"⚠️ thinking 内容不完整: {repr(combined)}")
    else:
        print("🔴 无 thinking block")

    return rc

if __name__ == "__main__":
    print("S16: 工具失败 + 特殊字符 + 嵌套JSON + 边缘场景")
    t1 = test_t1_tool_failure()
    t2 = test_t2_special_chars()
    t3 = test_t3_nested_json()
    t4 = test_t4_no_finish_reason()
    t5 = test_t5_multiple_thinking()

    print("\n=== S16 总结 ===")
    print(f"T1 工具失败:        rc={t1}")
    print(f"T2 特殊字符:        rc={t2}")
    print(f"T3 嵌套JSON:        rc={t3}")
    print(f"T4 无finish_reason: rc={t4}")
    print(f"T5 多thinking:      rc={t5}")
