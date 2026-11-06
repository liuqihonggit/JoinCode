#!/usr/bin/env python3
"""S15: 超时场景测试 — 验证 Bug 29 是否仍存在

Bug 29: 超时(3s)+慢服务器(30s) 场景下，jcc 返回 rc=1 (GeneralError) + "模型返回空回复"
预期: rc=1240 (LlmCallTimeout), 耗时 ~3s

测试矩阵:
  T1: JCC_DISABLE_STREAMING_FALLBACK=1 + HttpClient 超时（5min 太长，跳过）
  T2: fallback 启用 + JCC_STREAM_IDLE_TIMEOUT_MS=3000 + MockServer delay=30000
      → idle timeout 3s 触发 → fallback 非流式 → 非流式也延迟 → 最终超时
  T3: fallback 启用 + JCC_STREAM_IDLE_TIMEOUT_MS=3000 + JCC_NON_STREAMING_TIMEOUT_MS=3000 + MockServer delay=30000
      → idle timeout 3s → fallback 非流式 → 非流式超时 3s → 总计 ~6s
"""
import json
import subprocess
import time
import urllib.request

JCC = "artifacts/bin/JoinCode/Debug/net10.0/jcc.exe"
MOCK_CTRL = "http://localhost:9901/__control__"

def clear_mock():
    req = urllib.request.Request(f"{MOCK_CTRL}/clear", method="POST")
    urllib.request.urlopen(req, timeout=5)

def inject_delayed_stream(delay_ms=30000):
    """注入一个延迟的流式响应（正常文本但延迟发送）"""
    chunks = [
        'data: {"choices":[{"delta":{"content":"Hello"},"index":0}]}\n\n',
        'data: {"choices":[{"delta":{},"finish_reason":"stop","index":0}]}\n\n',
        'data: [DONE]\n\n',
    ]
    payload = {
        "is_stream": True,
        "content_type": "text/event-stream",
        "stream_chunks": chunks,
        "delay_ms": delay_ms,
    }
    body = json.dumps(payload).encode()
    req = urllib.request.Request(
        f"{MOCK_CTRL}/inject",
        data=body,
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    urllib.request.urlopen(req, timeout=5)

def run_jcc(env_extra, timeout=60):
    """运行 jcc，返回 (exit_code, elapsed, stdout, stderr)"""
    env = {
        "JCC_ENDPOINT": "http://localhost:9901/v1",
        "OPENAI_API_KEY": "test-key",
        "JCC_DEBUGLOG": "1",
    }
    env.update(env_extra)

    cmd = [
        "dotnet", JCC,
        "--non-interactive",
        "--prompt", "Say hello",
        "--format", "ndjson",
        "--trust",
    ]

    start = time.time()
    proc = subprocess.run(
        cmd,
        capture_output=True,
        text=True,
        timeout=timeout,
        env={**__import__("os").environ, **env},
        cwd=r"D:\project\w1",
    )
    elapsed = time.time() - start
    return proc.returncode, elapsed, proc.stdout, proc.stderr

def test_t2():
    """T2: fallback 启用 + idle timeout 3s + delay 30s"""
    print("\n=== T2: fallback 启用 + idle_timeout=3s + delay=30s ===")
    clear_mock()
    inject_delayed_stream(delay_ms=30000)

    rc, elapsed, stdout, stderr = run_jcc({
        "JCC_STREAM_IDLE_TIMEOUT_MS": "3000",
    }, timeout=120)

    print(f"exit_code={rc}, elapsed={elapsed:.1f}s")
    print(f"stdout (last 500): ...{stdout[-500:]}")
    # 检查 stderr 中的关键日志
    for line in stderr.splitlines():
        if any(k in line for k in ["FALLBACK", "timeout", "Timeout", "TIMEOUT", "idle", "Watchdog", "空回复", "STEP"]):
            print(f"  stderr: {line}")

    # 分析
    if rc == 1240:
        print("✅ 退出码 1240 (LlmCallTimeout) — Bug 29 已修复")
    elif rc == 1:
        if "空回复" in stdout or "空回复" in stderr:
            print("🔴 Bug 29 仍存在: rc=1 + '模型返回空回复' (应 rc=1240)")
        else:
            print(f"⚠️ rc=1 但非空回复错误")
    else:
        print(f"⚠️ 意外退出码 rc={rc}")

    return rc, elapsed

def test_t3():
    """T3: fallback 启用 + idle timeout 3s + non-stream timeout 3s + delay 30s"""
    print("\n=== T3: fallback 启用 + idle=3s + nonstream=3s + delay=30s ===")
    clear_mock()
    inject_delayed_stream(delay_ms=30000)

    rc, elapsed, stdout, stderr = run_jcc({
        "JCC_STREAM_IDLE_TIMEOUT_MS": "3000",
        "JCC_NON_STREAMING_TIMEOUT_MS": "3000",
    }, timeout=120)

    print(f"exit_code={rc}, elapsed={elapsed:.1f}s")
    print(f"stdout (last 500): ...{stdout[-500:]}")
    for line in stderr.splitlines():
        if any(k in line for k in ["FALLBACK", "timeout", "Timeout", "TIMEOUT", "idle", "Watchdog", "空回复", "STEP", "INF001"]):
            print(f"  stderr: {line}")

    if rc == 1240:
        print("✅ 退出码 1240 (LlmCallTimeout)")
    elif rc == 1:
        if "空回复" in stdout or "空回复" in stderr:
            print("🔴 Bug 29: rc=1 + '模型返回空回复' (应 rc=1240)")
        else:
            print(f"⚠️ rc=1 但非空回复")
    else:
        print(f"⚠️ 意外退出码 rc={rc}")

    return rc, elapsed

if __name__ == "__main__":
    print("S15: 超时场景测试 — Bug 29 验证")
    t2_rc, t2_time = test_t2()
    t3_rc, t3_time = test_t3()

    print("\n=== S15 总结 ===")
    print(f"T2 (idle=3s, delay=30s):           rc={t2_rc}, time={t2_time:.1f}s")
    print(f"T3 (idle=3s, ns=3s, delay=30s):    rc={t3_rc}, time={t3_time:.1f}s")
