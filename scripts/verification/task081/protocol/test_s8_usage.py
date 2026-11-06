import json, subprocess, os, urllib.request

JCC = "artifacts/bin/JoinCode/Debug/net10.0/jcc.exe"

def sse(obj):
    return f"data: {json.dumps(obj)}\n\n"

# Clear state
req = urllib.request.Request("http://localhost:9901/__control__/clear", data=b"", method="POST")
urllib.request.urlopen(req)

# Inject: text response with usage stats
stream_chunks = [
    sse({"choices": [{"index": 0, "delta": {"role": "assistant", "content": "Hello"}, "finish_reason": None}]}),
    sse({"choices": [{"index": 0, "delta": {"content": " world"}, "finish_reason": None}]}),
    sse({"choices": [{"index": 0, "delta": {}, "finish_reason": "stop"}]}),
    sse({"choices": [], "usage": {"prompt_tokens": 42, "completion_tokens": 7, "total_tokens": 49}}),
    "data: [DONE]\n\n"
]

inject = {"delay_ms": 0, "status_code": 200, "is_stream": True, "stream_chunks": stream_chunks}
req = urllib.request.Request("http://localhost:9901/__control__/inject", data=json.dumps(inject).encode(), headers={"Content-Type": "application/json"}, method="POST")
print("Inject:", urllib.request.urlopen(req).read().decode())

env = os.environ.copy()
env["JCC_ENDPOINT"] = "http://localhost:9901/v1"
env["OPENAI_API_KEY"] = "test-key"
env["JCC_DISABLE_STREAMING_FALLBACK"] = "1"

result = subprocess.run(
    ["dotnet", JCC, "--non-interactive", "--prompt", "Hi", "--format", "ndjson", "--trust"],
    capture_output=True, text=True, env=env, timeout=30
)

print("=== STDOUT ===")
print(result.stdout[:2000])

# Parse and check result event
for line in result.stdout.strip().splitlines():
    try:
        msg = json.loads(line)
        if msg.get("type") == "result":
            usage = msg.get("usage")
            print(f"\n=== Result ===")
            print(f"usage field: {usage}")
            if usage is not None:
                print(f"  input_tokens: {usage.get('input_tokens')} (expected 42)")
                print(f"  output_tokens: {usage.get('output_tokens')} (expected 7)")
                if usage.get('input_tokens') == 42 and usage.get('output_tokens') == 7:
                    print("PASS: usage 统计正确传递到 result 事件")
                else:
                    print("FAIL: usage 值不正确")
            else:
                print("FAIL: result 事件没有 usage 字段（SdkResultMessage.Usage 未赋值）")
    except json.JSONDecodeError:
        pass
