import json, subprocess, os, urllib.request

JCC = "artifacts/bin/JoinCode/Debug/net10.0/jcc.exe"

def sse(obj):
    return f"data: {json.dumps(obj)}\n\n"

# Clear state
req = urllib.request.Request("http://localhost:9901/__control__/clear", data=b"", method="POST")
urllib.request.urlopen(req)

# Inject: empty response body (200 OK but zero bytes)
inject = {"delay_ms": 0, "status_code": 200, "is_stream": False, "body": ""}
req = urllib.request.Request("http://localhost:9901/__control__/inject", data=json.dumps(inject).encode(), headers={"Content-Type": "application/json"}, method="POST")
print("Inject:", urllib.request.urlopen(req).read().decode())

env = os.environ.copy()
env["JCC_ENDPOINT"] = "http://localhost:9901/v1"
env["OPENAI_API_KEY"] = "test-key"
env["JCC_DISABLE_STREAMING_FALLBACK"] = "1"

result = subprocess.run(
    ["dotnet", JCC, "--non-interactive", "--prompt", "test", "--format", "ndjson", "--trust"],
    capture_output=True, text=True, env=env, timeout=30
)

print("=== STDOUT ===")
print(result.stdout[:2000])
print("=== STDERR ===")
print(result.stderr[-500:] if result.stderr else "(empty)")
print("=== EXIT CODE ===", result.returncode)

# Check result
for line in result.stdout.strip().splitlines():
    try:
        msg = json.loads(line)
        if msg.get("type") == "result":
            is_error = msg.get("is_error")
            errors = msg.get("errors", [])
            subtype = msg.get("subtype")
            result_text = msg.get("result", "")
            print(f"\n=== Result ===")
            print(f"  subtype: {subtype}")
            print(f"  is_error: {is_error}")
            print(f"  errors: {errors}")
            print(f"  result: '{result_text[:100]}'")
            if is_error:
                print("PASS: 空响应体正确识别为错误")
            else:
                print("INFO: 空响应体未识别为错误（可能被重试后拿到默认响应）")
    except json.JSONDecodeError:
        pass
