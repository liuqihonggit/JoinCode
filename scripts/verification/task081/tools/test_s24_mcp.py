#!/usr/bin/env python3
"""S24: MCP 工具测试 — 验证 jcc MCP 服务端协议完整实现

自动启动 MCP 服务端，测试完成后自动关闭。

测试内容:
1. MCP 服务端启动（HTTP 模式，bypass 权限模式）
2. MCP 协议 initialize（protocolVersion 2025-11-25）
3. MCP 协议 tools/list（工具数量 > 0）
4. MCP 协议 tools/call 无参数工具（get_process_info）
5. MCP 协议 tools/call 带参数工具（tool_search）
"""

import sys, os, json, time, re, subprocess, urllib.request, signal

JCC = "artifacts/bin/JoinCode/Debug/net10.0/jcc.exe"
MCP_PORT = 9917
BASE_URL = f"http://localhost:{MCP_PORT}/mcp/"

def start_mcp_server():
    env = os.environ.copy()
    env["JCC_PERMISSION_MODE"] = "bypass"
    env["JCC_SKIP_CODE_INDEX"] = "1"
    proc = subprocess.Popen(
        [JCC, "mcp_serve", "--transport", "http", "--port", str(MCP_PORT), "--host", "localhost"],
        stdout=subprocess.PIPE, stderr=subprocess.PIPE, env=env, text=True, encoding="utf-8", errors="replace"
    )
    token = None
    deadline = time.time() + 10
    while time.time() < deadline:
        line = proc.stdout.readline()
        if not line:
            if proc.poll() is not None:
                break
            continue
        clean = re.sub(r"\x1b\[[0-9;]*m", "", line)
        m = re.search(r"Mcp-Token:\s*([A-F0-9]+)", clean)
        if m:
            token = m.group(1)
            break
    if not token:
        proc.kill()
        raise RuntimeError("MCP 服务端启动失败，未获取到 token")
    time.sleep(3)
    return proc, token

def mcp_call(token, method, params=None, msg_id=1):
    headers = {
        "Authorization": f"Bearer {token}",
        "Content-Type": "application/json",
        "MCP-Protocol-Version": "2025-11-25",
    }
    body = json.dumps({"jsonrpc": "2.0", "id": msg_id, "method": method, "params": params or {}})
    req = urllib.request.Request(BASE_URL, data=body.encode(), headers=headers, method="POST")
    with urllib.request.urlopen(req, timeout=10) as resp:
        return json.loads(resp.read().decode())

mcp_proc, token = start_mcp_server()
print(f"MCP 服务端已启动，token={token[:16]}...")

results = []
try:
    r = mcp_call(token, "initialize", {"protocolVersion": "2025-11-25", "capabilities": {}, "clientInfo": {"name": "s24-test", "version": "1.0"}}, 1)
    pv = r["result"]["protocolVersion"]
    si = r["result"]["serverInfo"]
    results.append(("initialize protocolVersion=2025-11-25", pv == "2025-11-25", f"got {pv}"))
    results.append(("initialize serverInfo name=jcc-mcp", si["name"] == "jcc-mcp", f"got {si['name']}"))
    results.append(("initialize serverInfo version=1.0.0", si["version"] == "1.0.0", f"got {si['version']}"))

    r = mcp_call(token, "tools/list", {}, 2)
    tools = r["result"]["tools"]
    results.append(("tools/list count > 0", len(tools) > 0, f"got {len(tools)} tools"))
    results.append(("tools/list count >= 300", len(tools) >= 300, f"got {len(tools)} tools"))

    r = mcp_call(token, "tools/call", {"name": "get_process_info", "arguments": {}}, 3)
    res = r["result"]
    results.append(("tools/call get_process_info isError=false", res["isError"] is False, f"got isError={res['isError']}"))
    results.append(("tools/call get_process_info has text content", res["content"][0]["type"] == "text", f"got type={res['content'][0]['type']}"))
    results.append(("tools/call get_process_info text non-empty", len(res["content"][0]["text"]) > 0, f"got len={len(res['content'][0]['text'])}"))

    r = mcp_call(token, "tools/call", {"name": "tool_search", "arguments": {"query": "read file"}}, 4)
    res = r["result"]
    results.append(("tools/call tool_search isError=false", res["isError"] is False, f"got isError={res['isError']}"))
    results.append(("tools/call tool_search has text content", res["content"][0]["type"] == "text", f"got type={res['content'][0]['type']}"))
    results.append(("tools/call tool_search text non-empty", len(res["content"][0]["text"]) > 0, f"got len={len(res['content'][0]['text'])}"))
finally:
    mcp_proc.kill()
    mcp_proc.wait()

print("=" * 60)
print("S24: MCP 工具测试")
print("=" * 60)
passed = sum(1 for _, ok, _ in results if ok)
failed = sum(1 for _, ok, _ in results if not ok)
for name, ok, detail in results:
    status = "PASS" if ok else "FAIL"
    print(f"  [{status}] {name} ({detail})")
print("-" * 60)
print(f"总计: {passed} passed, {failed} failed, {len(results)} total")
sys.exit(0 if failed == 0 else 1)
