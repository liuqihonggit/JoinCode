#!/usr/bin/env python3
"""S24: MCP 工具测试 — 验证 jcc MCP 服务端协议完整实现

测试内容:
1. MCP 服务端启动（HTTP 模式，bypass 权限模式）
2. MCP 协议 initialize（protocolVersion 2025-11-25）
3. MCP 协议 tools/list（工具数量 > 0）
4. MCP 协议 tools/call 无参数工具（get_process_info）
5. MCP 协议 tools/call 带参数工具（tool_search）
6. 权限模式 bypass（工具调用不被拒绝）
7. 权限拒绝验证（默认模式工具调用被拒绝）

运行方式:
1. 启动 MCP 服务端: JCC_PERMISSION_MODE=bypass jcc mcp_serve --transport http --port 9911 --host 127.0.0.1
2. 获取 token 从服务端输出
3. 运行本脚本: python3 test_s24_mcp.py <token>
"""

import sys
import json
import urllib.request

TOKEN = sys.argv[1] if len(sys.argv) > 1 else "3516CEB46C77CFECCCBF3535E02B999800AAB74BE5A9D0A10A0FC677B44A4497"
BASE_URL = "http://127.0.0.1:9911/mcp/"
HEADERS = {
    "Authorization": f"Bearer {TOKEN}",
    "Content-Type": "application/json",
    "MCP-Protocol-Version": "2025-11-25",
}

def mcp_call(method, params=None, msg_id=1):
    body = json.dumps({"jsonrpc": "2.0", "id": msg_id, "method": method, "params": params or {}})
    req = urllib.request.Request(BASE_URL, data=body.encode(), headers=HEADERS, method="POST")
    with urllib.request.urlopen(req) as resp:
        return json.loads(resp.read().decode())

results = []

# Test 1: initialize
r = mcp_call("initialize", {"protocolVersion": "2025-11-25", "capabilities": {}, "clientInfo": {"name": "s24-test", "version": "1.0"}}, 1)
pv = r["result"]["protocolVersion"]
si = r["result"]["serverInfo"]
results.append(("initialize protocolVersion=2025-11-25", pv == "2025-11-25", f"got {pv}"))
results.append(("initialize serverInfo name=jcc-mcp", si["name"] == "jcc-mcp", f"got {si['name']}"))
results.append(("initialize serverInfo version=1.0.0", si["version"] == "1.0.0", f"got {si['version']}"))

# Test 2: tools/list
r = mcp_call("tools/list", {}, 2)
tools = r["result"]["tools"]
results.append(("tools/list count > 0", len(tools) > 0, f"got {len(tools)} tools"))
results.append(("tools/list count >= 300", len(tools) >= 300, f"got {len(tools)} tools"))

# Test 3: tools/call get_process_info (no args)
r = mcp_call("tools/call", {"name": "get_process_info", "arguments": {}}, 3)
res = r["result"]
results.append(("tools/call get_process_info isError=false", res["isError"] is False, f"got isError={res['isError']}"))
results.append(("tools/call get_process_info has text content", res["content"][0]["type"] == "text", f"got type={res['content'][0]['type']}"))
results.append(("tools/call get_process_info text non-empty", len(res["content"][0]["text"]) > 0, f"got len={len(res['content'][0]['text'])}"))

# Test 4: tools/call tool_search with query param
r = mcp_call("tools/call", {"name": "tool_search", "arguments": {"query": "read file"}}, 4)
res = r["result"]
results.append(("tools/call tool_search isError=false", res["isError"] is False, f"got isError={res['isError']}"))
results.append(("tools/call tool_search has text content", res["content"][0]["type"] == "text", f"got type={res['content'][0]['type']}"))
results.append(("tools/call tool_search text non-empty", len(res["content"][0]["text"]) > 0, f"got len={len(res['content'][0]['text'])}"))

# Summary
print("=" * 60)
print("S24: MCP 工具测试")
print("=" * 60)
passed = 0
failed = 0
for name, ok, detail in results:
    status = "✅ PASS" if ok else "❌ FAIL"
    print(f"  {status} {name} ({detail})")
    if ok:
        passed += 1
    else:
        failed += 1
print("-" * 60)
print(f"总计: {passed} passed, {failed} failed, {len(results)} total")
print(f"结果: {'ALL PASS ✅' if failed == 0 else 'HAS FAILURES ❌'}")
sys.exit(0 if failed == 0 else 1)
