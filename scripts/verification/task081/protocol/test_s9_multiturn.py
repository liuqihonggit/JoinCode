import json, subprocess, os, urllib.request, glob

JCC = "artifacts/bin/JoinCode/Debug/net10.0/jcc.exe"

def sse(obj):
    return f"data: {json.dumps(obj)}\n\n"

env = os.environ.copy()
env["JCC_ENDPOINT"] = "http://localhost:9901/v1"
env["OPENAI_API_KEY"] = "test-key"
env["JCC_SKIP_CODE_INDEX"] = "1"
env["JCC_DISABLE_STREAMING_FALLBACK"] = "1"

# Clear state
req = urllib.request.Request("http://localhost:9901/__control__/clear", data=b"", method="POST")
urllib.request.urlopen(req)

# Inject 1: first conversation turn
stream_chunks = [
    sse({"choices": [{"index": 0, "delta": {"role": "assistant", "content": "Hello from first conversation"}, "finish_reason": None}]}),
    sse({"choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}]}),
    sse({"choices": [], "usage": {"prompt_tokens": 10, "completion_tokens": 5, "total_tokens": 15}}),
    "data: [DONE]\n\n"
]
inject1 = {"delay_ms": 0, "status_code": 200, "is_stream": True, "stream_chunks": stream_chunks}
req = urllib.request.Request("http://localhost:9901/__control__/inject", data=json.dumps(inject1).encode(), headers={"Content-Type": "application/json"}, method="POST")
print("Inject1:", urllib.request.urlopen(req).read().decode())

# Run first conversation
result1 = subprocess.run(
    [JCC, "--non-interactive", "--prompt", "Remember this: hello123", "--format", "ndjson", "--trust"],
    capture_output=True, text=True, env=env, timeout=30
)

print("=== First conversation ===")
print(result1.stdout[:1500])

# Extract session_id from first conversation
session_id = None
for line in result1.stdout.strip().splitlines():
    try:
        msg = json.loads(line)
        if msg.get("type") == "system":
            session_id = msg.get("session_id")
            break
    except json.JSONDecodeError:
        pass

print(f"\nSession ID: {session_id}")

# Inject 2: second conversation turn
stream_chunks2 = [
    sse({"choices": [{"index": 0, "delta": {"role": "assistant", "content": "Hello from second conversation"}, "finish_reason": None}]}),
    sse({"choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}]}),
    sse({"choices": [], "usage": {"prompt_tokens": 20, "completion_tokens": 5, "total_tokens": 25}}),
    "data: [DONE]\n\n"
]
inject2 = {"delay_ms": 0, "status_code": 200, "is_stream": True, "stream_chunks": stream_chunks2}
req = urllib.request.Request("http://localhost:9901/__control__/inject", data=json.dumps(inject2).encode(), headers={"Content-Type": "application/json"}, method="POST")
print("Inject2:", urllib.request.urlopen(req).read().decode())

# Run second conversation with --continue
result2 = subprocess.run(
    [JCC, "--non-interactive", "--prompt", "What did I tell you to remember?", "--format", "ndjson", "--trust", "-c"],
    capture_output=True, text=True, env=env, timeout=30
)

print("\n=== Second conversation (with -c) ===")
print(result2.stdout[:2000])
print("=== EXIT CODE ===", result2.returncode)

# Check if second conversation has history
print("\n=== Analysis ===")
msgs = []
for line in result2.stdout.strip().splitlines():
    try:
        msgs.append(json.loads(line))
    except json.JSONDecodeError:
        pass

assistant_msgs = [m for m in msgs if m.get("type") == "assistant"]
print(f"assistant messages: {len(assistant_msgs)}")
for i, msg in enumerate(assistant_msgs):
    content = msg.get("message", {}).get("content", [])
    for block in content:
        if block.get("type") == "text":
            print(f"  assistant[{i}] text: '{block.get('text', '')[:80]}'")

# Check if the second conversation's LLM call included history
# (We can't directly verify this from NDJSON output, but if -c worked,
# the session_id should be the same as the first conversation)
session_id2 = None
for msg in msgs:
    if msg.get("type") == "system":
        session_id2 = msg.get("session_id")
        break

print(f"\nSession ID 1: {session_id}")
print(f"Session ID 2: {session_id2}")
if session_id and session_id2 and session_id == session_id2:
    print("PASS: -c 恢复了同一会话（session_id 匹配）")
else:
    print("INFO: session_id 不同（可能 -c 创建了新会话或 transcript 未生成）")
