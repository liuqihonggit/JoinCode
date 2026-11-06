"""测试 429 限流行为 — 检查 Retry-After 头处理"""
import subprocess, urllib.request, json, time, os
JCC_EXE = r"D:\project\w3\artifacts\bin\JoinCode\Debug\net10.0\jcc.exe"
MOCK_BASE = "http://localhost:9901"
def mock_clear():
    req = urllib.request.Request(f"{MOCK_BASE}/__control__/clear", data=b"", method="POST")
    urllib.request.urlopen(req, timeout=3).read()
def mock_inject(injection):
    req = urllib.request.Request(f"{MOCK_BASE}/__control__/inject",
        data=json.dumps(injection).encode("utf-8"),
        headers={"Content-Type": "application/json"}, method="POST")
    return urllib.request.urlopen(req, timeout=5).read().decode()
def build_error(status=429, msg="Rate Limit Exceeded", headers=None):
    inj = {"status_code": status, "content_type": "application/json",
        "body": json.dumps({"error": {"message": msg, "type": "rate_limit_error", "code": "rate_limit_exceeded"}}),
        "is_stream": False, "stream_chunks": [], "delay_ms": 0}
    if headers: inj["headers"] = headers
    return inj
mock_clear()
# 带 Retry-After: 1 头
mock_inject(build_error(429, "Rate Limit Exceeded", {"Retry-After": "1"}))
env = os.environ.copy()
env.update({"OPENAI_API_KEY": "sk-test", "JCC_DEBUGLOG": "1",
            "JCC_ENDPOINT": "http://localhost:9901/v1", "JCC_VENDOR": "openai", "JCC_MODEL_ID": "gpt-4o"})
proc = subprocess.Popen([JCC_EXE, "--trust", "-p", "测试429", "--await", "20"],
    stdout=subprocess.PIPE, stderr=subprocess.PIPE, env=env, text=True, encoding="utf-8", errors="replace")
try:
    stdout, stderr = proc.communicate(timeout=25)
except subprocess.TimeoutExpired:
    proc.kill(); stdout, stderr = proc.communicate()
print("=== STDOUT ===")
print(stdout)
print("=== STDERR (RETRY 相关) ===")
for l in stderr.splitlines():
    if any(k in l for k in ["RETRY","retry","Retry","429","Retry-After","retry_after","delay","Delay"]):
        print(f"  {l[:200]}")
