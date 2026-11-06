# TASK081 第二轮 C/S 闭环测试脚本

> **路径**：`scripts/verification/task081_round2/`
> **关联**：`docs/proposed/task/TASK081-jcc-self-talk-testing-record.md`
> **总计**：47 个脚本，分 9 个子目录
> **来源**：从 `.xxx/` 迁入（第二轮动态测试产物）

## 设计原则

- **动态输入**：不固定 mock，通过 MockServer `__control__/inject` 动态注入响应
- **实际启动 exe**：启动真实 `jcc.exe` + `JoinCode.OpenAI.MockServer.exe`，非单元测试 mock
- **双端修复**：既操作服务器也操作客户端，遇问题两边代码都修
- **角色驱动**：客户端驱动服务端，客户端 mock 服务端数据实现收发验收

## 目录结构

| 子目录 | 脚本数 | 用途 |
|--------|--------|------|
| `basic/` | 4 | 基础框架测试（C/S 循环、bridge_serve 验证、全面框架） |
| `batch/` | 6 | 批量/综合测试（64 场景全量、高级功能、扩展测试） |
| `edge/` | 6 | 边缘场景测试（空 tool_calls、无 [DONE]、错误恢复） |
| `tools/` | 6 | 工具调用测试（MCP 工具、多工具并行、混合响应） |
| `session/` | 5 | 会话/并发测试（长会话、多轮、并发、超大 reasoning） |
| `b_series/` | 5 | B 系列专项测试（B10/B13/B14 限流 429） |
| `helpers/` | 3 | 辅助脚本（启动分析、产物复制、协议分割） |
| `probes/` | 7 | Node.js 探针脚本（UI/feedback 探测） |
| `launch/` | 5 | 启动脚本（c7/c8/q host 启动 cmd/ps1） |

## 脚本清单

### basic/ — 基础框架测试

| 脚本 | 说明 |
|------|------|
| `cs_loop_test.py` | C/S 闭环动态测试 |
| `cs_loop_test_v3.py` | C/S 闭环动态测试 v3 — TASK081 扩展场景（工具调用/多轮/长文本/Unicode/思考链/异常边界） |
| `test_v5.py` | jcc 全面测试框架 v5 — 带健康检查 + 快速错误机制 |
| `test_bridge_quick.py` | 验证 bridge_serve 批量模式可用性 |

### batch/ — 批量/综合测试

| 脚本 | 说明 |
|------|------|
| `test_advanced_v4.py` | jcc 高级功能测试 v4 — JSON输出/dry-run/会话/交互模式/压缩 |
| `test_batch_full.py` | jcc 全量批量测试 v8 — 64 场景, bridge_serve 批量 + 元命令直跑 + -p 模式 |
| `test_batch_v7.py` | jcc 批量测试框架 v7 — bridge_serve 批量模式 + 元命令直跑 |
| `test_comprehensive.py` | jcc 全面功能测试 — 覆盖工具调用、斜杠命令、边缘条件、上下文压缩等 |
| `test_comprehensive_v2.py` | jcc 全面功能测试 v2 — 修正注入格式 |
| `test_extended_v3.py` | jcc 扩展测试 v3 — 连接断开/超大参数/多轮/MCP工具 |

### edge/ — 边缘场景测试

| 脚本 | 说明 |
|------|------|
| `test_e4.py` | E4 场景测试 |
| `test_edge.py` | 边缘条件测试 |
| `test_edge_v6.py` | jcc 边缘条件测试 v6 — 每个场景实时打印, 输出到 .xxx/logs/ |
| `test_empty_toolcalls.py` | 空 tool_calls 数组测试 — 检测 jcc 是否正确处理空工具调用 |
| `test_error_recovery.py` | 错误恢复测试 |
| `test_no_done.py` | 无 [DONE] 标记的流式响应测试 — 检测 jcc 是否正确处理流提前结束 |

### tools/ — 工具调用测试

| 脚本 | 说明 |
|------|------|
| `test_mcp_tools.py` | jcc MCP 工具直接调用测试 — 测试各种工具是否正常工作 |
| `test_mixed_response.py` | content+tool_calls 混合响应测试 — 先文本后工具调用（OpenAI 合法行为） |
| `test_multi_tool.py` | 多工具调用测试 |
| `test_multi_tool_parallel.py` | 多工具同时调用测试 — 一次响应包含多个 tool_calls |
| `test_reasoning_tool.py` | reasoning + tool 调用测试 |
| `test_tool_call.py` | 工具调用基础测试 |

### session/ — 会话/并发测试

| 脚本 | 说明 |
|------|------|
| `test_big_reasoning.py` | 超大思考链测试 — 10KB reasoning_content |
| `test_concurrent.py` | 并发请求测试 — 同时启动 3 个 jcc 进程 |
| `test_long_session.py` | 长会话测试 |
| `test_long_session2.py` | 长会话测试 2 |
| `test_multi_turn.py` | 多轮对话测试 |

### b_series/ — B 系列专项测试

| 脚本 | 说明 |
|------|------|
| `test_b10.py` | B10 场景测试 |
| `test_b13.py` | B13 场景测试 |
| `test_b14_429.py` | 测试 429 限流行为 — 检查 Retry-After 头处理 |
| `test_b14_full.py` | B14 全量测试 |
| `test_b14_long.py` | B14 长测试 |

### helpers/ — 辅助脚本

| 脚本 | 说明 |
|------|------|
| `analyze_startup.py` | 分析 jcc 启动耗时 — 用 debuglog 时间戳定位瓶颈 |
| `copy_dev_artifacts.py` | 复制 jcc-gh.exe 和 jcc-rg.exe 及其依赖到 jcc 输出目录 |
| `split_protocol.py` | 协议分割工具 |

### probes/ — Node.js 探针脚本

| 脚本 | 说明 |
|------|------|
| `probe-c5-ui.mjs` | C5 UI 探针 |
| `probe-feedback-cmd.mjs` | feedback 命令探针 |
| `probe-feedback-corner.mjs` | feedback 边角场景探针 |
| `probe-feedback-more.mjs` | feedback 更多场景探针 |
| `probe-feedback-open.mjs` | feedback 打开探针 |
| `probe-feedback-shot.mjs` | feedback 截图探针 |
| `probe-feedback-ui.mjs` | feedback UI 探针 |

### launch/ — 启动脚本

| 脚本 | 说明 |
|------|------|
| `launch-host-c7.cmd` | 启动 c7 host（cmd） |
| `launch-host-c8.cmd` | 启动 c8 host（cmd） |
| `launch-host-c8.ps1` | 启动 c8 host（PowerShell） |
| `launch-host-q.cmd` | 启动 q host（cmd） |
| `launch-host-q.ps1` | 启动 q host（PowerShell） |

## 运行方式

### 前置条件

1. MockServer 已构建：`artifacts/bin/OpenAI.MockServer/Debug/net10.0/JoinCode.OpenAI.MockServer.exe`
2. jcc 已构建：`artifacts/bin/JoinCode/Debug/net10.0/jcc.exe`
3. 环境变量：`JCC_ENDPOINT=http://localhost:9901/v1`、`OPENAI_API_KEY=test-key`、`JCC_SKIP_MODEL_FETCH=1`、`JCC_DISABLE_RETRY=1`

### 运行单个脚本

```bash
python3 scripts/verification/task081_round2/basic/cs_loop_test_v3.py
```

### 运行某类全部脚本

```bash
for f in scripts/verification/task081_round2/edge/test_*.py; do
    echo "=== $(basename $f) ==="
    python3 "$f" 2>&1 | tail -5
    echo ""
done
```

## 注意事项

- 测试脚本不参与编译（纯 Python/Node.js），不影响 CI
- 脚本会启动真实 exe 进程，需确保构建产物存在
- MockServer 端口默认 9901，部分脚本用 8093，按脚本内常量为准
- 日志输出到 `.xxx/logs/`
