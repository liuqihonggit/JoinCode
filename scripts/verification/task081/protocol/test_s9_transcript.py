#!/usr/bin/env python3
"""S9 测试: 非交互模式 transcript 生成验证"""
import json, subprocess, sys, os, time, urllib.request

ENDPOINT = "http://localhost:9901"

def clear_mock():
    req = urllib.request.Request(f"{ENDPOINT}/__control__/clear", method="POST")
    urllib.request.urlopen(req)

def inject_simple_text():
    """注入简单文本响应"""
    chunks = [
        'data: {"choices":[{"delta":{"content":"Hello from S9 test"},"index":0}]}\n\n',
        'data: {"choices":[{"finish_reason":"stop","index":0}],"usage":{"prompt_tokens":10,"completion_tokens":3}}\n\n',
        'data: [DONE]\n\n',
    ]
    payload = {"is_stream": True, "content_type": "text/event-stream", "stream_chunks": chunks}
    data = json.dumps(payload).encode()
    req = urllib.request.Request(f"{ENDPOINT}/__control__/inject", data=data, method="POST", headers={"Content-Type": "application/json"})
    urllib.request.urlopen(req)

def run_jcc(prompt="Say hello"):
    """运行 jcc 非交互模式"""
    env = os.environ.copy()
    env["OPENAI_API_KEY"] = "test-key"
    env["JCC_ENDPOINT"] = f"{ENDPOINT}/v1"
    env["JCC_DEBUGLOG"] = "1"
    env["JCC_DISABLE_STREAMING_FALLBACK"] = "1"
    cmd = [
        "dotnet", "artifacts/bin/JoinCode/Debug/net10.0/jcc.exe",
        "--non-interactive", "--prompt", prompt,
        "--format", "ndjson", "--trust",
        "--permission-mode", "bypass",
    ]
    result = subprocess.run(cmd, capture_output=True, text=True, env=env, timeout=30, cwd="D:\\project\\w1")
    return result

def main():
    print("=== S9: 非交互模式 transcript 生成测试 ===")

    # 1. 清空 MockServer
    clear_mock()
    print("[1] MockServer cleared")

    # 2. 注入简单文本响应
    inject_simple_text()
    print("[2] Injected simple text response")

    # 3. 运行 jcc 非交互模式
    result = run_jcc("Say hello")
    print(f"[3] jcc exit code: {result.returncode}")
    if result.stderr:
        # 只打印含 transcript/session/STEP 的诊断行
        diag_lines = [l for l in result.stderr.splitlines() if any(k in l.lower() for k in ["transcript", "session", "step", "postprocess", "contextmanager"])]
        if diag_lines:
            print("[3a] Relevant diag lines:")
            for l in diag_lines[:20]:
                print(f"  {l}")
    if result.stdout:
        print(f"[3b] stdout (first 500 chars): {result.stdout[:500]}")

    # 4. 检查 transcript 文件
    # 从 stdout 中提取 session_id
    session_id = None
    for line in result.stdout.splitlines():
        if '"session_id"' in line:
            try:
                data = json.loads(line)
                if "session_id" in data:
                    session_id = data["session_id"]
                    break
            except:
                pass
    print(f"[4] session_id from output: {session_id}")

    # 检查 ~/.jcc/sessions/ 目录
    sessions_dir = os.path.expanduser("~/.jcc/sessions")
    print(f"[5] Sessions dir: {sessions_dir}")
    if os.path.exists(sessions_dir):
        dirs = sorted(os.listdir(sessions_dir), reverse=True)
        print(f"[5a] Recent session dirs: {dirs[:5]}")
        if session_id:
            session_path = os.path.join(sessions_dir, session_id)
            print(f"[5b] Expected session path: {session_path}")
            print(f"[5c] Session path exists: {os.path.exists(session_path)}")
            if os.path.exists(session_path):
                files = os.listdir(session_path)
                print(f"[5d] Files in session dir: {files}")
                for f in files:
                    fp = os.path.join(session_path, f)
                    if os.path.isfile(fp):
                        with open(fp, 'r', encoding='utf-8') as fh:
                            content = fh.read()
                        print(f"[5e] {f} content (first 500 chars): {content[:500]}")
    else:
        print("[5] Sessions dir does NOT exist!")

if __name__ == "__main__":
    main()
