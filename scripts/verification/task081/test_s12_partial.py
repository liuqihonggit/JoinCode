import json, subprocess, os, urllib.request

JCC = r"D:\project\w1\artifacts\bin\JoinCode\Debug\net10.0\jcc.dll"

def sse(obj):
    return f"data: {json.dumps(obj)}\n\n"

# Clear state
req = urllib.request.Request("http://127.0.0.1:8091/__control__/clear", data=b"", method="POST")
urllib.request.urlopen(req)

# Inject: partial SSE stream (text chunks but NO [DONE] terminator)
stream_chunks = [
    sse({"choices": [{"index": 0, "delta": {"role": "assistant", "content": "Hello"}, "finish_reason": None}]}),
    sse({"choices": [{"index": 0, "delta": {"content": " partial"}, "finish_reason": None}]}),
    # NO finish_reason chunk, NO [DONE]
]

inject = {"delay_ms": 0, "status_code": 200, "is_stream": True, "stream_chunks": stream_chunks}
req = urllib.request.Request("http://127.0.0.1:8091/__control__/inject", data=json.dumps(inject).encode(), headers={"Content-Type": "application/json"}, method="POST")
print("Inject:", urllib.request.urlopen(req).read().decode())

env = os.environ.copy()
env["JCC_ENDPOINT"] = "http://127.0.0.1:8091/v1"
env["OPENAI_API_KEY"] = "test-key"
env["JCC_DISABLE_STREAMING_FALLBACK"] = "1"

result = subprocess.run(
    ["dotnet", JCC, "--non-interactive", "--prompt", "test", "--format", "ndjson", "--trust"],
    capture_output=True, text=True, env=env, timeout=30
)

print("=== STDOUT ===")
print(result.stdout[:2000])
print("=== EXIT CODE ===", result.returncode)

# Check if partial content was output
has_assistant = False
has_text = False
text_content = ""
for line in result.stdout.strip().splitlines():
    try:
        msg = json.loads(line)
        if msg.get("type") == "assistant":
            has_assistant = True
            for block in msg.get("message", {}).get("content", []):
                if block.get("type") == "text":
                    has_text = True
                    text_content = block.get("text", "")
        if msg.get("type") == "result":
            is_error = msg.get("is_error")
            print(f"\n=== Result ===")
            print(f"  is_error: {is_error}")
            print(f"  result: '{msg.get('result', '')[:100]}'")
    except json.JSONDecodeError:
        pass

print(f"\n  assistant msg: {has_assistant}")
print(f"  text block: {has_text}, content: '{text_content}'")
if has_text and "Hello" in text_content:
    print("PASS: 部分 SSE 流已接收内容正确输出")
else:
    print("FAIL: 部分 SSE 流内容丢失")
