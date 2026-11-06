#!/usr/bin/env python3
"""S9b 测试: -c 恢复会话验证"""
import json, subprocess, os, urllib.request

ENDPOINT = "http://localhost:9901"
SESSION_ID = "20261105-1327-w1-w1-parent-2"

def clear_mock():
    req = urllib.request.Request(f"{ENDPOINT}/__control__/clear", method="POST")
    urllib.request.urlopen(req)

def inject_followup():
    """注入第二轮响应"""
    chunks = [
        'data: {"choices":[{"delta":{"content":"Welcome back! Previous response was Hello from S9 test"},"index":0}]}\n\n',
        'data: {"choices":[{"finish_reason":"stop","index":0}],"usage":{"prompt_tokens":15,"completion_tokens":8}}\n\n',
        'data: [DONE]\n\n',
    ]
    payload = {"is_stream": True, "content_type": "text/event-stream", "stream_chunks": chunks}
    data = json.dumps(payload).encode()
    req = urllib.request.Request(f"{ENDPOINT}/__control__/inject", data=data, method="POST", headers={"Content-Type": "application/json"})
    urllib.request.urlopen(req)

def run_jcc_continue(session_id, prompt="What did I say before?"):
    """运行 jcc -c 恢复会话"""
    env = os.environ.copy()
    env["OPENAI_API_KEY"] = "test-key"
    env["JCC_ENDPOINT"] = f"{ENDPOINT}/v1"
    env["JCC_DEBUGLOG"] = "1"
env["JCC_SKIP_CODE_INDEX"] = "1"
    env["JCC_DISABLE_STREAMING_FALLBACK"] = "1"
    cmd = [
        "artifacts/bin/JoinCode/Debug/net10.0/jcc.exe",
        "--non-interactive", "--prompt", prompt,
        "--format", "ndjson", "--trust",
        "--permission-mode", "bypass",
        "--continue", session_id,
    ]
    result = subprocess.run(cmd, capture_output=True, text=True, env=env, timeout=30, cwd=".")
    return result

def main():
    print("=== S9b: -c 恢复会话测试 ===")
    print(f"[1] Using session_id: {SESSION_ID}")

    # 检查 transcript 文件存在
    transcript_path = os.path.expanduser(f"~/.jcc/sessions/{SESSION_ID}/transcript.jsonl")
    if os.path.exists(transcript_path):
        with open(transcript_path, 'r', encoding='utf-8') as f:
            lines = f.readlines()
        print(f"[2] Transcript exists, {len(lines)} entries")
        for i, line in enumerate(lines):
            entry = json.loads(line)
            content_preview = (entry.get("content") or "")[:60]
            print(f"  [{i}] role={entry['role']}, content={content_preview}")
    else:
        print(f"[2] Transcript NOT found at {transcript_path}")
        return

    # 清空 MockServer + 注入响应
    clear_mock()
    inject_followup()
    print("[3] MockServer cleared + injected followup response")

    # 运行 -c 恢复会话
    result = run_jcc_continue(SESSION_ID)
    print(f"[4] jcc exit code: {result.returncode}")

    if result.stderr:
        diag_lines = [l for l in result.stderr.splitlines() if any(k in l.lower() for k in ["resume", "session", "transcript", "step", "error", "exception"])]
        if diag_lines:
            print("[4a] Relevant diag lines:")
            for l in diag_lines[:15]:
                print(f"  {l}")

    if result.stdout:
        print(f"[4b] stdout:")
        for line in result.stdout.splitlines():
            if line.strip():
                try:
                    data = json.loads(line)
                    print(f"  type={data.get('type')}, session_id={data.get('session_id')}")
                    if data.get("type") == "assistant":
                        content = data.get("message", {}).get("content", [])
                        for block in content:
                            if block.get("type") == "text":
                                print(f"    text: {block.get('text', '')[:100]}")
                except:
                    print(f"  raw: {line[:100]}")

if __name__ == "__main__":
    main()
