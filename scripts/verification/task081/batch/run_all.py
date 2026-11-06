#!/usr/bin/env python3
"""串行运行拆分后的小测试文件（MockServer 健康检查 + 自动重启 + 断点续跑）。

每次 bash 调用控制在 30s 内：
  python run_all.py --limit 5            # 跑前5个就退
  python run_all.py --limit 5 --resume   # 跳过已PASS的，再跑5个
  python run_all.py cv2 --limit 5        # 只跑 cv2_* 前5个

MockServer 自动守护：每个小文件跑之前检查 9901 端口，挂了自动重启。
"""
import subprocess, sys, os, time, json, urllib.request, socket
from pathlib import Path

HERE = Path(__file__).parent
SHARDS = HERE / "shards"
PROJECT_ROOT = HERE.parent.parent.parent.parent
MOCK_PORT = 9901
MOCK_URL = f"http://localhost:{MOCK_PORT}"
MOCK_EXE = PROJECT_ROOT / "artifacts/bin/OpenAI.MockServer/Debug/net10.0/JoinCode.OpenAI.MockServer.exe"
LOG_DIR = PROJECT_ROOT / ".xxx" / "logs"
RESULTS_FILE = LOG_DIR / "run_all_results.json"
SUBPROCESS_TIMEOUT = 25

def mock_health_check():
    """检查 MockServer 是否活着，返回 bool"""
    try:
        req = urllib.request.Request(f"{MOCK_URL}/__control__/state", method="GET")
        urllib.request.urlopen(req, timeout=3)
        return True
    except Exception:
        return False

def mock_kill():
    """杀掉 MockServer 进程"""
    try:
        subprocess.run(
            ["powershell", "-Command",
             "Get-Process -Name 'JoinCode.OpenAI.MockServer' -ErrorAction SilentlyContinue | Stop-Process -Force"],
            capture_output=True, timeout=5
        )
    except Exception:
        pass
    time.sleep(0.5)

def mock_start():
    """启动 MockServer（后台），返回 bool"""
    if not MOCK_EXE.exists():
        print(f"  !! MockServer exe 不存在: {MOCK_EXE}", flush=True)
        return False
    try:
        subprocess.Popen(
            [str(MOCK_EXE), "--port", str(MOCK_PORT), "--no-browser"],
            cwd=str(PROJECT_ROOT),
            stdout=open(LOG_DIR / "mockserver.log", "w"),
            stderr=subprocess.STDOUT,
            creationflags=0x00000008 if os.name == "nt" else 0,
        )
        for _ in range(10):
            time.sleep(0.5)
            if mock_health_check():
                return True
        return False
    except Exception as e:
        print(f"  !! 启动 MockServer 失败: {e}", flush=True)
        return False

def mock_ensure():
    """确保 MockServer 活着，挂了就重启，返回 bool"""
    if mock_health_check():
        return True
    print("  !! MockServer 挂了，重启中...", flush=True)
    mock_kill()
    return mock_start()

def mock_clear():
    """清空 MockServer 状态"""
    try:
        req = urllib.request.Request(f"{MOCK_URL}/__control__/clear", data=b"", method="POST")
        urllib.request.urlopen(req, timeout=3)
    except Exception:
        pass

def load_prev_results():
    """加载上次的结果（用于 --resume）"""
    if not RESULTS_FILE.exists():
        return {}
    try:
        data = json.loads(RESULTS_FILE.read_text(encoding="utf-8"))
        return {d["name"]: d for d in data}
    except Exception:
        return {}

def run_one(shard_path):
    """运行单个小测试文件，返回 dict"""
    name = Path(shard_path).stem
    t0 = time.time()
    try:
        proc = subprocess.run(
            [sys.executable, shard_path],
            cwd=str(PROJECT_ROOT),
            capture_output=True, text=True,
            timeout=SUBPROCESS_TIMEOUT,
            encoding="utf-8", errors="replace",
        )
        elapsed = time.time() - t0
        return {"name": name, "exit": proc.returncode, "elapsed": round(elapsed, 1),
                "stdout_tail": (proc.stdout or "")[-400:], "stderr_tail": (proc.stderr or "")[-400:]}
    except subprocess.TimeoutExpired as e:
        elapsed = time.time() - t0
        out = e.stdout or b""
        err = e.stderr or b""
        if isinstance(out, bytes): out = out.decode(errors="replace")
        if isinstance(err, bytes): err = err.decode(errors="replace")
        return {"name": name, "exit": -999, "elapsed": round(elapsed, 1),
                "stdout_tail": out[-400:], "stderr_tail": f"TIMEOUT({SUBPROCESS_TIMEOUT}s)\n{err[-300:]}"}
    except Exception as e:
        return {"name": name, "exit": -1, "elapsed": round(time.time()-t0, 1),
                "stdout_tail": "", "stderr_tail": str(e)[:300]}

def main():
    args = sys.argv[1:]
    limit = 0
    resume = False
    max_batch_time = 25.0
    prefixes = []
    i = 0
    while i < len(args):
        a = args[i]
        if a == "--limit": limit = int(args[i+1]); i += 1
        elif a == "--resume": resume = True
        elif a == "--max-batch-time": max_batch_time = float(args[i+1]); i += 1
        elif a == "--timeout": global SUBPROCESS_TIMEOUT; SUBPROCESS_TIMEOUT = int(args[i+1]); i += 1
        else: prefixes.append(a)
        i += 1

    LOG_DIR.mkdir(parents=True, exist_ok=True)

    shards = sorted(SHARDS.glob("*.py"))
    if prefixes:
        shards = [s for s in shards if any(s.name.startswith(p) for p in prefixes)]
    if not shards:
        print(f"没有找到匹配的测试文件 (prefixes={prefixes})")
        sys.exit(1)

    prev = load_prev_results() if resume else {}
    if resume:
        before = len(shards)
        shards = [s for s in shards if s.stem not in prev]
        skipped = before - len(shards)
        if not shards:
            print(f"--resume: 全部 {before} 个已跑过，无待跑文件", flush=True)
            sys.exit(0)
        print(f"--resume: 跳过 {skipped} 个已跑过，剩余 {len(shards)} 个", flush=True)
    else:
        prev = {}  # 不 resume 时也加载用于合并，但不跳过
    prev = load_prev_results()  # 总是加载，用于合并保存

    if limit > 0 and len(shards) > limit:
        shards = shards[:limit]

    print(f"本轮跑 {len(shards)} 个文件，单文件超时 {SUBPROCESS_TIMEOUT}s", flush=True)
    print("=" * 70, flush=True)

    if not mock_ensure():
        print("!! MockServer 无法启动，退出", flush=True)
        sys.exit(2)

    results = []
    t_start = time.time()
    for s in shards:
        if not mock_ensure():
            print(f"  !! MockServer 重启失败，跳过 {s.name}", flush=True)
            results.append({"name": s.stem, "exit": -2, "elapsed": 0, "stdout_tail": "", "stderr_tail": "mock_start_failed"})
            continue
        mock_clear()
        r = run_one(str(s))
        results.append(r)
        status = "PASS" if r["exit"] == 0 else "FAIL" if r["exit"] > 0 else "ERROR"
        print(f"  {status} {r['name']} ({r['elapsed']}s) exit={r['exit']}", flush=True)
        if r["exit"] != 0 and r["stderr_tail"]:
            print(f"    {r['stderr_tail'][:200]}", flush=True)
        # 每跑完一个就保存（被 bash timeout 杀也不丢）
        all_results = prev.copy() if resume else {}
        for rr in results:
            all_results[rr["name"]] = rr
        RESULTS_FILE.write_text(
            json.dumps(list(all_results.values()), ensure_ascii=False, indent=2),
            encoding="utf-8")

    total_elapsed = time.time() - t_start

    all_results = prev.copy() if resume else {}
    for r in results:
        all_results[r["name"]] = r
    RESULTS_FILE.write_text(
        json.dumps(list(all_results.values()), ensure_ascii=False, indent=2),
        encoding="utf-8")

    passed = sum(1 for r in results if r["exit"] == 0)
    failed = sum(1 for r in results if r["exit"] > 0)
    errors = sum(1 for r in results if r["exit"] < 0)
    print(f"\n本轮: {len(results)}个 | PASS:{passed} FAIL:{failed} ERR:{errors} | {total_elapsed:.1f}s", flush=True)

    total_pass = sum(1 for d in all_results.values() if d.get("exit") == 0)
    total_fail = sum(1 for d in all_results.values() if d.get("exit", 0) > 0)
    total_err = sum(1 for d in all_results.values() if d.get("exit", 0) < 0)
    print(f"累计: {len(all_results)}个 | PASS:{total_pass} FAIL:{total_fail} ERR:{total_err}", flush=True)

    if total_fail > 0 or total_err > 0:
        print("\n失败/错误:", flush=True)
        for d in sorted(all_results.values(), key=lambda x: x["name"]):
            if d.get("exit", 0) != 0:
                icon = "✗" if d["exit"] > 0 else "!!"
                print(f"  {icon} {d['name']} exit={d['exit']}", flush=True)

    sys.exit(0 if failed == 0 and errors == 0 else 1)

if __name__ == "__main__":
    main()
