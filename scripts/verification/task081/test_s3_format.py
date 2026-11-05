import json, subprocess, os, urllib.request

JCC = r"D:\project\w1\artifacts\bin\JoinCode\Debug\net10.0\jcc.dll"

def sse(obj):
    return f"data: {json.dumps(obj)}\n\n"

# Clear state
req = urllib.request.Request("http://127.0.0.1:8091/__control__/clear", data=b"", method="POST")
urllib.request.urlopen(req)

# Inject 1: two parallel tool calls (bash + read) in one LLM response
stream_chunks = [
    sse({"choices": [{"index": 0, "delta": {"role": "assistant", "tool_calls": [
        {"index": 0, "id": "call_bash_001", "function": {"name": "bash", "arguments": ""}},
        {"index": 1, "id": "call_read_002", "function": {"name": "read", "arguments": ""}
    }]}, "finish_reason": None}]}),
    sse({"choices": [{"index": 0, "delta": {"tool_calls": [
        {"index": 0, "function": {"arguments": "{\"command\":\"echo hello\"}"}},
        {"index": 1, "function": {"arguments": "{\"file_path\":\"test.txt\"}"}
    }]}, "finish_reason": None}]}),
    sse({"choices": [{"index": 0, "delta": {}, "finish_reason": "tool_calls"}]}),
    sse({"choices": [], "usage": {"prompt_tokens": 10, "completion_tokens": 5, "total_tokens": 15}}),
    "data: [DONE]\n\n"
]
inject1 = {"delay_ms": 0, "status_code": 200, "is_stream": True, "stream_chunks": stream_chunks}
req = urllib.request.Request("http://127.0.0.1:8091/__control__/inject", data=json.dumps(inject1).encode(), headers={"Content-Type": "application/json"}, method="POST")
print("Inject1:", urllib.request.urlopen(req).read().decode())

# Inject 2: final text (turn 2)
tool_result_chunks = [
    sse({"choices": [{"index": 0, "delta": {"role": "assistant", "content": "Done"}, "finish_reason": None}]}),
    sse({"choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}]}),
    sse({"choices": [], "usage": {"prompt_tokens": 20, "completion_tokens": 3, "total_tokens": 23}}),
    "data: [DONE]\n\n"
]
inject2 = {"delay_ms": 0, "status_code": 200, "is_stream": True, "stream_chunks": tool_result_chunks}
req = urllib.request.Request("http://127.0.0.1:8091/__control__/inject", data=json.dumps(inject2).encode(), headers={"Content-Type": "application/json"}, method="POST")
print("Inject2:", urllib.request.urlopen(req).read().decode())

env = os.environ.copy()
env["JCC_ENDPOINT"] = "http://127.0.0.1:8091/v1"
env["OPENAI_API_KEY"] = "test-key"
env["JCC_DISABLE_STREAMING_FALLBACK"] = "1"

result = subprocess.run(
    ["dotnet", JCC, "--non-interactive", "--prompt", "Run echo hello and read test.txt", "--format", "ndjson", "--trust", "--permission-mode", "bypass"],
    capture_output=True, text=True, env=env, timeout=30
)

print("=== STDOUT ===")
print(result.stdout[:4000])
print("=== EXIT CODE ===", result.returncode)

# Parse and check message structure
messages = []
for line in result.stdout.strip().splitlines():
    try:
        msg = json.loads(line)
        messages.append(msg)
    except json.JSONDecodeError:
        pass

print("\n=== Message Structure ===")
for i, msg in enumerate(messages):
    msg_type = msg.get("type", "?")
    if msg_type in ("assistant", "user"):
        content = msg.get("message", {}).get("content", [])
        block_types = [b.get("type", "?") for b in content]
        print(f"  msg[{i}]: {msg_type} blocks={block_types}")
    elif msg_type == "result":
        print(f"  msg[{i}]: result num_turns={msg.get('num_turns')}")

# Check: should have ONE assistant message with 2 tool_use blocks, ONE user message with 2 tool_result blocks
assistant_msgs = [m for m in messages if m.get("type") == "assistant"]
user_msgs = [m for m in messages if m.get("type") == "user"]

tool_use_assistant = [m for m in assistant_msgs if any(b.get("type") == "tool_use" for b in m.get("message", {}).get("content", []))]
tool_result_user = [m for m in user_msgs if any(b.get("type") == "tool_result" for b in m.get("message", {}).get("content", []))]

print(f"\n=== Result ===")
print(f"assistant msgs with tool_use: {len(tool_use_assistant)} (expected 1)")
print(f"user msgs with tool_result: {len(tool_result_user)} (expected 1)")

if len(tool_use_assistant) == 1 and len(tool_result_user) == 1:
    tool_use_count = sum(1 for b in tool_use_assistant[0]["message"]["content"] if b.get("type") == "tool_use")
    tool_result_count = sum(1 for b in tool_result_user[0]["message"]["content"] if b.get("type") == "tool_result")
    print(f"tool_use blocks in single assistant msg: {tool_use_count} (expected 2)")
    print(f"tool_result blocks in single user msg: {tool_result_count} (expected 2)")
    if tool_use_count == 2 and tool_result_count == 2:
        print("PASS: 多 tool_use 合并到同一 assistant 消息，多 tool_result 合并到同一 user 消息")
    else:
        print("FAIL: block 数量不对")
else:
    print("FAIL: 消息结构不对齐基准")
