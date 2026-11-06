import json, subprocess, os, urllib.request

JCC = "artifacts/bin/JoinCode/Debug/net10.0/jcc.exe"

def sse(obj):
    return f"data: {json.dumps(obj)}\n\n"

env = os.environ.copy()
env["JCC_ENDPOINT"] = "http://localhost:9901/v1"
env["OPENAI_API_KEY"] = "test-key"
env["JCC_DISABLE_STREAMING_FALLBACK"] = "1"
env["JCC_DISABLE_RETRY"] = "1"

def test_error(status_code, error_body, description):
    # Clear state
    req = urllib.request.Request("http://localhost:9901/__control__/clear", data=b"", method="POST")
    urllib.request.urlopen(req)

    # Inject error response
    inject = {"delay_ms": 0, "status_code": status_code, "is_stream": False, "body": error_body}
    req = urllib.request.Request("http://localhost:9901/__control__/inject", data=json.dumps(inject).encode(), headers={"Content-Type": "application/json"}, method="POST")
    urllib.request.urlopen(req)

    result = subprocess.run(
        [JCC, "--non-interactive", "--prompt", "test", "--format", "ndjson", "--trust"],
        capture_output=True, text=True, env=env, timeout=30
    )

    print(f"\n=== {description} (HTTP {status_code}) ===")
    # Parse result event
    for line in result.stdout.strip().splitlines():
        try:
            msg = json.loads(line)
            if msg.get("type") == "result":
                is_error = msg.get("is_error")
                errors = msg.get("errors", [])
                subtype = msg.get("subtype")
                print(f"  subtype: {subtype}")
                print(f"  is_error: {is_error}")
                print(f"  errors: {errors}")
                if is_error and len(errors) > 0:
                    print(f"  PASS: 错误正确传递")
                else:
                    print(f"  FAIL: 错误未正确传递")
        except json.JSONDecodeError:
            pass
    if result.returncode != 0:
        print(f"  exit code: {result.returncode}")

# Test different error codes
test_error(401, '{"error": {"message": "Invalid API key", "type": "invalid_request_error"}}', "401 Unauthorized")
test_error(429, '{"error": {"message": "Rate limit exceeded", "type": "rate_limit_error"}}', "429 Rate Limited")
test_error(500, '{"error": {"message": "Internal server error", "type": "server_error"}}', "500 Server Error")
test_error(503, '{"error": {"message": "Service unavailable", "type": "server_error"}}', "503 Service Unavailable")
