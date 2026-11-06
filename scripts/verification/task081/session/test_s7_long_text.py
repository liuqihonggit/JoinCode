import json, subprocess, os, urllib.request

JCC = "artifacts/bin/JoinCode/Debug/net10.0/jcc.exe"

def sse(obj):
    return f"data: {json.dumps(obj)}\n\n"

# Clear state
req = urllib.request.Request("http://localhost:9901/__control__/clear", data=b"", method="POST")
urllib.request.urlopen(req)

# Inject: 12 text delta chunks forming a long paragraph
parts = ["Hello", ", ", "this ", "is ", "a ", "long ", "text ", "stream ", "test ", "with ", "multiple ", "chunks."]
stream_chunks = []
# First chunk with role
stream_chunks.append(sse({"choices": [{"index": 0, "delta": {"role": "assistant", "content": parts[0]}, "finish_reason": None}]}))
# Subsequent chunks
for part in parts[1:]:
    stream_chunks.append(sse({"choices": [{"index": 0, "delta": {"content": part}, "finish_reason": None}]}))
# Final chunk
stream_chunks.append(sse({"choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}]}))
stream_chunks.append(sse({"choices": [], "usage": {"prompt_tokens": 10, "completion_tokens": 20, "total_tokens": 30}}))
stream_chunks.append("data: [DONE]\n\n")

inject = {"delay_ms": 0, "status_code": 200, "is_stream": True, "stream_chunks": stream_chunks}
req = urllib.request.Request("http://localhost:9901/__control__/inject", data=json.dumps(inject).encode(), headers={"Content-Type": "application/json"}, method="POST")
print("Inject:", urllib.request.urlopen(req).read().decode())

env = os.environ.copy()
env["JCC_ENDPOINT"] = "http://localhost:9901/v1"
env["OPENAI_API_KEY"] = "test-key"
env["JCC_DISABLE_STREAMING_FALLBACK"] = "1"

result = subprocess.run(
    ["dotnet", JCC, "--non-interactive", "--prompt", "Tell me a long story", "--format", "ndjson", "--trust"],
    capture_output=True, text=True, env=env, timeout=30
)

print("=== STDOUT ===")
print(result.stdout[:3000])
print("=== EXIT CODE ===", result.returncode)

# Parse and check
expected_text = "".join(parts)
messages = []
for line in result.stdout.strip().splitlines():
    try:
        messages.append(json.loads(line))
    except json.JSONDecodeError:
        pass

assistant_msgs = [m for m in messages if m.get("type") == "assistant"]
text_blocks = []
for msg in assistant_msgs:
    for block in msg.get("message", {}).get("content", []):
        if block.get("type") == "text":
            text_blocks.append(block)

print(f"\n=== Result ===")
print(f"assistant messages: {len(assistant_msgs)}")
print(f"text blocks: {len(text_blocks)} (expected 1)")
if text_blocks:
    actual_text = text_blocks[0].get("text", "")
    print(f"expected text: '{expected_text}'")
    print(f"actual text:   '{actual_text}'")
    if len(text_blocks) == 1 and actual_text == expected_text:
        print("PASS: 12 个 text delta chunk 合并为单个 text block，内容完整")
    else:
        print("FAIL: 合并逻辑有问题")
else:
    print("FAIL: 没有 text block")
