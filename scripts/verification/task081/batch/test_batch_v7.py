#!/usr/bin/env python3
"""jcc 批量测试框架 v7 — bridge_serve 批量模式 + 元命令直跑
LLM 响应场景用 bridge_serve(1次启动), 元命令场景直跑(0.18s/个)
每个场景实时打印进度, 输出到 .xxx/logs/
"""
import subprocess, os, time, json, threading, urllib.request, uuid, sys
from pathlib import Path

JCC = "artifacts/bin/JoinCode/Debug/net10.0/jcc.exe"
MOCK_EXE = "artifacts/bin/OpenAI.MockServer/Debug/net10.0/JoinCode.OpenAI.MockServer.exe"
MOCK = "http://localhost:9901"
MODEL = "gpt-5.6-sol"
LOGDIR = Path(".xxx/logs")
LOGDIR.mkdir(parents=True, exist_ok=True)
ENV_BASE = {
    "JCC_ENDPOINT": "http://localhost:9901/v1",
    "JCC_VENDOR": "openai",
    "JCC_MODEL_ID": MODEL,
    "OPENAI_API_KEY": "test-key",
    "JCC_DEBUGLOG": "1",
    "JCC_SKIP_MODEL_FETCH": "1",
    "JCC_DISABLE_RETRY": "1",
}
ENV = {**os.environ, **ENV_BASE}

results = []
lock = threading.Lock()

def log(msg):
    print(msg, flush=True)

def ok(msg):
    with lock: results.append(True)
    log(f"  ✓ {msg}")

def fail(msg):
    with lock: results.append(False)
    log(f"  ✗ {msg}")

# ============================================================
# MockServer 管理
# ============================================================
mock_proc = None

def mock_health():
    try:
        resp = urllib.request.urlopen(f"{MOCK}/__control__/state", timeout=3)
        return json.loads(resp.read()) is not None
    except: return False

def mock_start():
    global mock_proc
    if mock_proc and mock_proc.poll() is None: return True
    log("  [启动 MockServer]")
    mock_proc = subprocess.Popen([MOCK_EXE, "--port", "9901", "--interactive", "--no-open-browser"], stdout=open(LOGDIR / "mockserver_batch.log", "w"), stderr=subprocess.STDOUT, env={**os.environ})
    for _ in range(10):
        time.sleep(1)
        if mock_health(): log("  OK"); return True
    log("  FAILED"); return False

def mock_clear():
    try: urllib.request.urlopen(urllib.request.Request(f"{MOCK}/__control__/clear", data=b"", method="POST"), timeout=3)
    except: pass

def sse_line(obj):
    return f"data: {json.dumps(obj, ensure_ascii=False)}\n\n"

def make_stream_chunks(content_text=None, tool_calls=None, reasoning=None, finish_reason="stop"):
    chunks = []
    cid = f"chatcmpl-{uuid.uuid4().hex[:20]}"
    chunks.append(sse_line({"id": cid, "object": "chat.completion.chunk", "choices": [{"index": 0, "delta": {"role": "assistant"}}]}))
    if reasoning:
        if isinstance(reasoning, str):
            chunks.append(sse_line({"id": cid, "choices": [{"index": 0, "delta": {"reasoning_content": reasoning}}]}))
        else:
            for r in reasoning: chunks.append(sse_line({"id": cid, "choices": [{"index": 0, "delta": {"reasoning_content": r}}]}))
    if content_text is not None:
        if isinstance(content_text, str):
            chunks.append(sse_line({"id": cid, "choices": [{"index": 0, "delta": {"content": content_text}}]}))
        else:
            for c in content_text: chunks.append(sse_line({"id": cid, "choices": [{"index": 0, "delta": {"content": c}}]}))
    if tool_calls:
        for i, tc in enumerate(tool_calls):
            chunks.append(sse_line({"id": cid, "choices": [{"index": 0, "delta": {"tool_calls": [{"index": i, "id": tc["id"], "function": {"name": tc["name"], "arguments": json.dumps(tc["args"], ensure_ascii=False)}}]}}]}))
    chunks.append(sse_line({"id": cid, "choices": [{"index": 0, "delta": {}, "finish_reason": finish_reason}]}))
    chunks.append("data: [DONE]\n\n")
    return chunks

def inject_stream(chunks, status=200):
    mock_clear()
    body = {"status": status, "content_type": "text/event-stream", "is_stream": True, "stream_chunks": chunks}
    try: urllib.request.urlopen(urllib.request.Request(f"{MOCK}/__control__/inject", data=json.dumps(body).encode(), method="POST", headers={"Content-Type": "application/json"}), timeout=3)
    except: pass

def inject_simple(text="完成", **kwargs):
    inject_stream(make_stream_chunks(content_text=text, **kwargs))

# ============================================================
# bridge_serve 批量模式
# ============================================================
class BridgeSession:
    def __init__(self):
        self.proc = None
        self.events = []
        self.reader_thread = None
        self.seq = 0
        self.started = False

    def start(self):
        self.proc = subprocess.Popen([JCC, "bridge_serve", "--trust", "--permission-mode", "bypass"], env=ENV, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, bufsize=0)
        self.events = []
        def reader():
            for line in self.proc.stdout:
                line = line.decode('utf-8', errors='replace').strip()
                if line:
                    try: self.events.append(json.loads(line))
                    except: pass
        self.reader_thread = threading.Thread(target=reader, daemon=True)
        self.reader_thread.start()
        deadline = time.time() + 10
        while time.time() < deadline:
            if any(e.get("type") == "session_info" for e in self.events):
                self.started = True
                return True
            time.sleep(0.2)
        return False

    def submit(self, text, timeout=15):
        self.seq += 1
        seq = self.seq
        before = len(self.events)
        self.proc.stdin.write((json.dumps({'type': 'submit', 'text': text, 'seq': seq}) + '\n').encode())
        self.proc.stdin.flush()
        deadline = time.time() + timeout
        while time.time() < deadline:
            for e in self.events[before:]:
                if e.get("type") == "turn_completed" and e.get("ackSeq") == seq:
                    return True
                if e.get("type") == "error":
                    return False
            time.sleep(0.1)
        return False

    def get_assistant_messages(self):
        return [e.get("text", "") for e in self.events if e.get("type") == "assistant_message"]

    def get_events_by_type(self, etype):
        return [e for e in self.events if e.get("type") == etype]

    def quit(self):
        try:
            self.proc.stdin.write((json.dumps({'type': 'quit'}) + '\n').encode())
            self.proc.stdin.flush()
            self.proc.wait(timeout=5)
        except: pass

# ============================================================
# 元命令直跑
# ============================================================
def run_cmd(args, timeout=15):
    cmd = [JCC, "--trust", "--non-interactive"] + args
    try:
        proc = subprocess.run(cmd, env=ENV, capture_output=True, text=True, timeout=timeout+5)
        return proc.returncode, proc.stdout, proc.stderr
    except subprocess.TimeoutExpired:
        return -999, "", "TIMEOUT"

# ============================================================
print("=" * 60)
print("jcc 批量测试框架 v7 (bridge_serve)")
print("=" * 60)

if not mock_start():
    print("MockServer 启动失败, 退出")
    sys.exit(1)

# === A. bridge_serve 批量 LLM 场景 ===
print("\n=== A. bridge_serve 批量 LLM 场景 ===")

bs = BridgeSession()
t0 = time.time()
if not bs.start():
    fail("bridge_serve 启动失败")
else:
    log(f"  [bridge_serve 启动 {time.time()-t0:.2f}s]")

    # A1. 基础流式
    print("\n--- A1. 基础流式 ---")
    inject_simple("你好，我是AI")
    if bs.submit("打招呼"): ok("基础流式")
    else: fail("基础流式")

    # A2. 多语言
    print("--- A2. 多语言 ---")
    inject_simple("中文 English 日本語")
    if bs.submit("多语言测试"): ok("多语言")
    else: fail("多语言")

    # A3. emoji
    print("--- A3. emoji ---")
    inject_simple("测试 🎉🚀✨")
    if bs.submit("emoji测试"): ok("emoji")
    else: fail("emoji")

    # A4. 逐字符流式
    print("--- A4. 逐字符流式 ---")
    inject_simple(list("逐字符输出"))
    if bs.submit("逐字符测试"): ok("逐字符流式")
    else: fail("逐字符流式")

    # A5. 思考链
    print("--- A5. 思考链 ---")
    inject_simple("回答", reasoning="让我想想...")
    if bs.submit("思考链测试"): ok("思考链")
    else: fail("思考链")

    # A6. 1KB 响应(bridge_serve 批量)
    print("--- A6. 1KB 响应 ---")
    inject_simple("X" * 1000)
    if bs.submit("生成1KB", timeout=15): ok("1KB 响应")
    else: fail("1KB 响应")

    # A7. 换行符
    print("--- A7. 换行符 ---")
    inject_simple("第一行\n第二行\n\r第三行\r\n")
    if bs.submit("换行测试"): ok("各种换行符")
    else: fail("各种换行符")

    # A8. 5轮对话
    print("--- A10. 5轮对话 ---")
    for i in range(5):
        inject_simple(f"第{i+1}轮回复")
        if bs.submit(f"第{i+1}轮问题"): ok(f"5轮对话 第{i+1}轮")
        else: fail(f"5轮对话 第{i+1}轮")

    log(f"  [bridge_serve {len(bs.events)} 事件]")
    bs.quit()

# === B. 元命令直跑(0.18s/个) ===
print("\n=== B. 元命令直跑 ===")

def test_cmd(name, args, expected_code=0, timeout=10):
    code, out, err = run_cmd(args, timeout=timeout)
    if code == expected_code: ok(name)
    else: fail(f"{name}: expected {expected_code}, got {code}")

test_cmd("slash_list", ["slash_list"])
test_cmd("mcp_list", ["mcp_list"])
test_cmd("schema", ["schema"])
test_cmd("config-print", ["config-print"])
test_cmd("rg 基本", ["rg", "class", "modules/", "--type", "cs", "--max-count", "3"])
test_cmd("rg 无匹配", ["rg", "ZZZNOTEXIST", "modules/"], expected_code=1)
test_cmd("rg 正则", ["rg", "internal.*class", "modules/tools/rg/", "--type", "cs", "--max-count", "3"])
test_cmd("gh --help", ["gh", "--help"])
test_cmd("--help", ["--help"])
test_cmd("--version", ["--version"])

# === C. -p 模式(少量, 无法批量化的) ===
print("\n=== C. -p 模式 ===")

def run_p(prompt, await_sec=15, extra_args=None, env_override=None):
    args = [JCC, "--trust", "--non-interactive", "-p", prompt, "--await", str(await_sec)]
    if extra_args: args.extend(extra_args)
    e = {**ENV}
    if env_override: e.update(env_override)
    try:
        proc = subprocess.run(args, env=e, capture_output=True, text=True, timeout=await_sec+10)
        return proc.returncode, proc.stdout, proc.stderr
    except subprocess.TimeoutExpired:
        return -999, "", "TIMEOUT"

def test_p(name, prompt, inject_text="OK", await_sec=15, extra_args=None, env_override=None):
    inject_simple(inject_text)
    code, out, err = run_p(prompt, await_sec=await_sec, extra_args=extra_args, env_override=env_override)
    if code == 0: ok(name)
    else: fail(f"{name}: exit={code}")

test_p("--json", "json测试", extra_args=["--json"])
test_p("--quiet", "quiet测试", extra_args=["--quiet"])
test_p("-m 切换", "模型测试", extra_args=["-m", "gpt-5.6-sol"])
test_p("小上下文", "小上下文", env_override={"JCC_MAX_CONTEXT_CHARS": "100"})
test_p("10KB 响应", "生成10KB", inject_text="X" * 10000, await_sec=20)
test_p("10KB 提示词", "A" * 10000, inject_text="收到", await_sec=20)
# 工具调用在 test_v5.py 已验证(64/64通过), 此处不重复
# anthropic/deepseek: GRD016 模型未注册是设计行为, 跳过

# === D. --force-interactive ===
print("\n=== D. --force-interactive ===")
try:
    proc = subprocess.run([JCC, "--trust", "--force-interactive", "--await", "5"], env=ENV, capture_output=True, text=True, timeout=15, input="/exit\n")
    if "交互模式需要真实终端" not in proc.stderr: ok("--force-interactive TTY")
    else: fail("--force-interactive TTY")
except Exception as e:
    fail(f"--force-interactive: {e}")

# ============================================================
elapsed = time.time() - t0
print("\n" + "=" * 60)
print(f"测试汇总 ({elapsed:.1f}s)")
print("=" * 60)
total = len(results)
passed = sum(results)
failed = total - passed
print(f"总计: {total} | 通过: {passed} | 失败: {failed}")

with open(LOGDIR / "test_batch_v7_result.txt", "w", encoding="utf-8") as f:
    f.write(f"总计: {total} | 通过: {passed} | 失败: {failed} | 耗时: {elapsed:.1f}s\n")

if mock_proc: mock_proc.kill()
sys.exit(0 if failed == 0 else 1)
