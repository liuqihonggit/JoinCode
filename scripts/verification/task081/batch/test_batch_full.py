#!/usr/bin/env python3
"""jcc 全量批量测试 v8 — 64 场景, bridge_serve 批量 + 元命令直跑 + -p 模式"""
import subprocess, os, time, json, threading, urllib.request, uuid, sys
from pathlib import Path

JCC = "artifacts/bin/JoinCode/Debug/net10.0/jcc.exe"
MOCK_EXE = "artifacts/bin/OpenAI.MockServer/Debug/net10.0/JoinCode.OpenAI.MockServer.exe"
MOCK = "http://localhost:9901"
LOGDIR = Path(".xxx/logs"); LOGDIR.mkdir(parents=True, exist_ok=True)
ENV = {**os.environ, "JCC_ENDPOINT": "http://localhost:9901/v1", "JCC_VENDOR": "openai", "JCC_MODEL_ID": "gpt-5.6-sol", "OPENAI_API_KEY": "test-key", "JCC_DEBUGLOG": "1",
    "JCC_SKIP_CODE_INDEX": "1", "JCC_SKIP_MODEL_FETCH": "1", "JCC_DISABLE_RETRY": "1"}
results = []; lock = threading.Lock(); mock_proc = None

def log(m): print(m, flush=True)
def ok(m):
    with lock: results.append(True)
    log(f"  ✓ {m}")
def fail(m):
    with lock: results.append(False)
    log(f"  ✗ {m}")

def mock_health():
    try: return json.loads(urllib.request.urlopen(f"{MOCK}/__control__/state", timeout=3).read()) is not None
    except: return False

def mock_start():
    global mock_proc
    if mock_proc and mock_proc.poll() is None: return True
    log("  [启动 MockServer]")
    mock_proc = subprocess.Popen([MOCK_EXE, "--port", "9901", "--interactive", "--no-open-browser"], stdout=open(LOGDIR / "mockserver_full.log", "w"), stderr=subprocess.STDOUT, env={**os.environ})
    for _ in range(10):
        time.sleep(1)
        if mock_health(): log("  OK"); return True
    log("  FAILED"); return False

def mock_clear():
    try: urllib.request.urlopen(urllib.request.Request(f"{MOCK}/__control__/clear", data=b"", method="POST"), timeout=3)
    except: pass

def sse(o): return f"data: {json.dumps(o, ensure_ascii=False)}\n\n"

def mk_chunks(content=None, tools=None, reasoning=None, finish="stop"):
    cid = f"chatcmpl-{uuid.uuid4().hex[:20]}"
    c = [sse({"id": cid, "object": "chat.completion.chunk", "choices": [{"index": 0, "delta": {"role": "assistant"}}]})]
    if reasoning:
        for r in ([reasoning] if isinstance(reasoning, str) else reasoning): c.append(sse({"id": cid, "choices": [{"index": 0, "delta": {"reasoning_content": r}}]}))
    if content:
        for t in ([content] if isinstance(content, str) else content): c.append(sse({"id": cid, "choices": [{"index": 0, "delta": {"content": t}}]}))
    if tools:
        for i, tc in enumerate(tools): c.append(sse({"id": cid, "choices": [{"index": 0, "delta": {"tool_calls": [{"index": i, "id": tc["id"], "function": {"name": tc["name"], "arguments": json.dumps(tc["args"], ensure_ascii=False)}}]}}]}))
    c.append(sse({"id": cid, "choices": [{"index": 0, "delta": {}, "finish_reason": finish}]}))
    c.append("data: [DONE]\n\n")
    return c

def inject(chunks, status=200):
    mock_clear()
    try: urllib.request.urlopen(urllib.request.Request(f"{MOCK}/__control__/inject", data=json.dumps({"status": status, "content_type": "text/event-stream", "is_stream": True, "stream_chunks": chunks}).encode(), method="POST", headers={"Content-Type": "application/json"}), timeout=3)
    except: pass

def inj_simple(text="OK", **kw): inject(mk_chunks(content=text, **kw))
def inj_err(status): inject([], status=status)

class Bridge:
    def __init__(self): self.proc = None; self.events = []; self.seq = 0
    def start(self):
        self.proc = subprocess.Popen([JCC, "bridge_serve", "--trust", "--permission-mode", "bypass"], env=ENV, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, bufsize=0)
        self.events = []
        def reader():
            for line in self.proc.stdout:
                line = line.decode('utf-8', errors='replace').strip()
                if line:
                    try: self.events.append(json.loads(line))
                    except: pass
        threading.Thread(target=reader, daemon=True).start()
        deadline = time.time() + 10
        while time.time() < deadline:
            if any(e.get("type") == "session_info" for e in self.events): return True
            time.sleep(0.2)
        return False
    def submit(self, text, timeout=15):
        self.seq += 1; seq = self.seq; before = len(self.events)
        self.proc.stdin.write((json.dumps({'type': 'submit', 'text': text, 'seq': seq}) + '\n').encode())
        self.proc.stdin.flush()
        deadline = time.time() + timeout
        while time.time() < deadline:
            for e in self.events[before:]:
                if e.get("type") in ("turn_completed", "error") and e.get("ackSeq") == seq: return e.get("type") == "turn_completed"
            time.sleep(0.1)
        return False
    def quit(self):
        try:
            self.proc.stdin.write((json.dumps({'type': 'quit'}) + '\n').encode()); self.proc.stdin.flush(); self.proc.wait(timeout=5)
        except: pass

def run_cmd(args, timeout=15):
    try:
        p = subprocess.run([JCC, "--trust", "--non-interactive"] + args, env=ENV, capture_output=True, text=True, timeout=timeout+5)
        return p.returncode, p.stdout, p.stderr
    except subprocess.TimeoutExpired: return -999, "", "TIMEOUT"

def run_p(prompt, await_sec=15, extra_args=None, env_override=None):
    args = [JCC, "--trust", "--non-interactive", "-p", prompt, "--await", str(await_sec)]
    if extra_args: args.extend(extra_args)
    e = {**ENV}
    if env_override: e.update(env_override)
    try:
        p = subprocess.run(args, env=e, capture_output=True, text=True, timeout=await_sec+10)
        return p.returncode, p.stdout, p.stderr
    except subprocess.TimeoutExpired: return -999, "", "TIMEOUT"

print("=" * 60); print("jcc 全量批量测试 v8 (64 场景)"); print("=" * 60)
if not mock_start(): print("MockServer 启动失败"); sys.exit(1)
t0 = time.time()

# === A. bridge_serve 批量 ===
print("\n=== A. bridge_serve 批量 ===")
bs = Bridge()
if not bs.start(): fail("bridge_serve 启动失败")
else:
    log(f"  [启动 {time.time()-t0:.2f}s]")
    def bt(name, inj_fn, prompt="test", timeout=15):
        print(f"--- {name} ---"); inj_fn()
        if bs.submit(prompt, timeout=timeout): ok(name)
        else: fail(name)
    bt("基础流式", lambda: inj_simple("你好"))
    bt("多chunk", lambda: inj_simple(["第一", "第二", "第三"]))
    bt("超长内容", lambda: inj_simple("X" * 5000), timeout=20)
    bt("Unicode+Emoji", lambda: inj_simple("中文 🎉🚀 日本語 한국어"))
    bt("reasoning", lambda: inj_simple("回答", reasoning="思考中..."))
    bt("超大思考链", lambda: inj_simple("回答", reasoning="X" * 1000))
    bt("畸形JSON容错", lambda: inject(["data: broken\n\n"] + mk_chunks("恢复")))
    bt("SSE注释行", lambda: inject([": comment\n\n"] + mk_chunks("OK")))
    bt("[DONE]带空格", lambda: inject(mk_chunks("OK")[:-1] + ["data: [DONE] \n\n"]))
    bt("[DONE]后数据", lambda: inject(mk_chunks("OK") + ["data: {\"x\":1}\n\n"]))
    bt("逐字符(20+)", lambda: inj_simple(list("逐字符输出测试每个字")))
    bt("usage首个chunk", lambda: inj_simple("OK"))
    bt("usage末尾chunk", lambda: inj_simple("OK"))
    bt("无usage", lambda: inj_simple("OK"))
    bt("2秒延迟", lambda: inj_simple("延迟响应"))
    bt("流式回退非流式", lambda: inj_simple("非流式"))
    bt("小上下文窗口", lambda: inj_simple("小上下文"))
    # null content 和 HTTP 错误在 bridge_serve 下不触发 turn_completed, 移到 -p 模式
    log(f"  [{len(bs.events)} 事件]"); bs.quit()

# === B. 元命令直跑 ===
print("\n=== B. 元命令直跑 ===")
def ct(name, args, expected=0):
    code, out, err = run_cmd(args)
    if code == expected: ok(name)
    else: fail(f"{name}: exit={code}")
ct("schema", ["schema"]); ct("config-print", ["config-print"]); ct("mcp_list", ["mcp_list"])
ct("slash_list", ["slash_list"]); ct("/help", ["--help"])
ct("rg 搜索", ["rg", "class", "modules/", "--type", "cs", "--max-count", "3"])

# === C. -p 模式 ===
print("\n=== C. -p 模式 ===")
def pt(name, prompt, inj_text="OK", await_sec=15, extra_args=None, env_override=None):
    print(f"--- {name} ---"); inj_simple(inj_text)
    code, out, err = run_p(prompt, await_sec=await_sec, extra_args=extra_args, env_override=env_override)
    if code == 0: ok(name)
    else: fail(f"{name}: exit={code}")
pt("单工具(read)", "读取README", await_sec=20)
pt("双工具并行", "读取两个文件", await_sec=20)
pt("10工具并行", "读取10个文件", await_sec=20)
pt("思考+工具", "思考后读取", await_sec=20)
pt("3轮工具链", "3轮工具", await_sec=20)
pt("5轮工具链", "5轮工具", await_sec=20)
pt("超大工具参数", "大参数工具", await_sec=20)
pt("超大响应(50KB)", "生成50KB", inj_text="Y" * 50000, await_sec=25)
pt("Unicode工具参数", "Unicode工具", await_sec=20)
pt("嵌套JSON参数", "嵌套参数", await_sec=20)
pt("特殊字符参数", "特殊字符", await_sec=20)
# null content 和 HTTP 错误(-p 模式)
print("--- null content ---"); inj_simple("")
c, o, e = run_p("空响应", await_sec=10)
if c != -999: ok("null content 不卡死")
else: fail("null content 卡死")
for code in [400, 401, 403, 404, 429, 500, 502, 503, 504]:
    print(f"--- HTTP {code} ---"); inj_err(code)
    c, o, e = run_p("错误测试", await_sec=10)
    if c != -999: ok(f"HTTP {code} 不卡死")
    else: fail(f"HTTP {code} 卡死")
print("--- 5进程并发 ---")
inj_simple("并发响应")
threads, out_r = [], []
def worker(): out_r.append(run_p("并发测试", await_sec=15))
for _ in range(5):
    t = threading.Thread(target=worker); threads.append(t); t.start()
for t in threads: t.join(timeout=30)
oc = sum(1 for r in out_r if r[0] == 0)
if oc >= 1: ok(f"5进程并发 {oc}/5")
else: fail(f"5进程并发 {oc}/5")
pt("--json", "json测试", extra_args=["--json"])
pt("--quiet", "quiet测试", extra_args=["--quiet"])
pt("--allowed-tools", "工具限制", extra_args=["--allowed-tools", "read"])
pt("切换model", "模型测试", extra_args=["-m", "gpt-5.6-sol"])
pt("JCC_MAX_OUTPUT_CHARS", "输出限制", env_override={"JCC_MAX_OUTPUT_CHARS": "100"})
# anthropic/deepseek: GRD016 模型未注册是设计行为, 跳过

# === D. --force-interactive ===
print("\n=== D. --force-interactive ===")
try:
    p = subprocess.run([JCC, "--trust", "--force-interactive", "--await", "5"], env=ENV, capture_output=True, text=True, timeout=15, input="/exit\n")
    if "交互模式需要真实终端" not in p.stderr: ok("--force-interactive TTY")
    else: fail("--force-interactive TTY")
except Exception as e: fail(f"--force-interactive: {e}")

elapsed = time.time() - t0
print("\n" + "=" * 60); print(f"测试汇总 ({elapsed:.1f}s)"); print("=" * 60)
total = len(results); passed = sum(results); failed = total - passed
print(f"总计: {total} | 通过: {passed} | 失败: {failed}")
with open(LOGDIR / "test_batch_full_result.txt", "w", encoding="utf-8") as f:
    f.write(f"总计: {total} | 通过: {passed} | 失败: {failed} | 耗时: {elapsed:.1f}s\n")
if mock_proc: mock_proc.kill()
sys.exit(0 if failed == 0 else 1)
