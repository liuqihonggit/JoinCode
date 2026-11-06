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
def build_error(status=500, msg="Internal Server Error"):
    return {"status_code": status, "content_type": "application/json",
        "body": json.dumps({"error": {"message": msg, "type": "server_error", "code": "internal_error"}}),
        "is_stream": False, "stream_chunks": [], "delay_ms": 0}
mock_clear()
mock_inject(build_error(404, "Not Found"))
env = os.environ.copy()
env.update({"OPENAI_API_KEY": "sk-test", "JCC_DEBUGLOG": "1",
            "JCC_ENDPOINT": "http://localhost:9901/v1", "JCC_VENDOR": "openai", "JCC_MODEL_ID": "gpt-4o"})
proc = subprocess.Popen([JCC_EXE, "--trust", "-p", "测试404", "--await", "15"],
    stdout=subprocess.PIPE, stderr=subprocess.PIPE, env=env, text=True, encoding="utf-8", errors="replace")
try:
    stdout, stderr = proc.communicate(timeout=20)
except subprocess.TimeoutExpired:
    proc.kill(); stdout, stderr = proc.communicate()
print("=== STDOUT ===")
print(stdout)
