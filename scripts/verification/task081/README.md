# TASK081 C/S 闭环测试脚本

> **路径**：`scripts/verification/task081/`
> **文档**：`docs/proposed/task/TASK081-jcc-self-talk-testing-record.md`
> **总计**：71 个脚本，分 12 个子目录
> **来源**：第一轮（task081 原有）+ 第二轮（从 `.xxx/` 迁入）合并，重叠场景去重

## 设计原则

- **动态输入**：不固定 mock，通过 MockServer `__control__/inject` 动态注入响应
- **实际启动 exe**：启动真实 `jcc.exe` + `JoinCode.OpenAI.MockServer.exe`，非单元测试 mock
- **双端修复**：既操作服务器也操作客户端，遇问题两边代码都修
- **角色驱动**：客户端驱动服务端，客户端 mock 服务端数据实现收发验收

## 统一规范

所有脚本已统一为：
- **路径**：相对路径 `artifacts/bin/JoinCode/Debug/net10.0/jcc.exe`（无硬编码绝对路径）
- **端口**：MockServer 统一 `localhost:9901`
- **IP**：统一用 `localhost`（非 `127.0.0.1`）
- **可执行文件**：统一 `jcc.exe`（非 `jcc.dll`）

## 目录结构

| 子目录 | 脚本数 | 用途 |
|--------|--------|------|
| `format/` | 3 | D1-D4 格式修复（uuid/input/stop_reason/is_error） |
| `bug_fixes/` | 3 | Bug K/L/M 字段修复（model/modelUsage/permission_denials） |
| `protocol/` | 11 | 协议字段验证（S3 NDJSON/S8 usage/S9 continue/S17 init/S19 result/S20 stop_reason/S25 permission） |
| `basic/` | 4 | 基础框架测试（C/S 循环、bridge_serve、全面框架） |
| `batch/` | 6 | 批量/综合测试（64 场景全量、高级功能、扩展测试） |
| `edge/` | 7 | 边缘场景测试（空响应/空 tool_calls/无 DONE/部分流/错误恢复） |
| `tools/` | 7 | 工具调用测试（MCP 协议/多工具并行/混合内容/reasoning+tool） |
| `session/` | 8 | 会话/并发测试（长会话/多轮/并发/超大 reasoning/超大上下文） |
| `errors/` | 7 | 错误处理测试（401/429/500/503/超时/B 系列限流） |
| `helpers/` | 3 | 辅助脚本（启动分析/产物复制/协议分割） |
| `probes/` | 7 | Node.js 探针脚本（UI/feedback 探测） |
| `launch/` | 5 | 启动脚本（c7/c8/q host 启动 cmd/ps1） |

## 脚本清单

### format/ — 格式修复测试

| 脚本 | 验证内容 |
|------|----------|
| `test_d1_uuid.py` | D1: uuid 为标准 UUID 格式 |
| `test_d2_input.py` | D2: tool_use.input 为 JSON 对象 |
| `test_d4_d3.py` | D3+D4: stop_reason tool_calls→tool_use；is_error 非 nullable bool |

### bug_fixes/ — Bug 修复测试

| 脚本 | 验证内容 |
|------|----------|
| `test_bugK.py` | Bug K: model 优先从 WorkflowConfig.Provider.ModelId 获取 |
| `test_bugLM.py` | Bug L+M: result 含 modelUsage + permission_denials 字段 |
| `test_modelUsage.py` | modelUsage 从 Usage+TotalCostUsd 按模型构造用量字典 |

### protocol/ — 协议字段验证

| 脚本 | 验证内容 |
|------|----------|
| `test_s3_format.py` | S3: NDJSON 格式正确 |
| `test_s8_usage.py` | S8: usage 统计传递 |
| `test_s9_continue.py` | S9: --continue 会话恢复 |
| `test_s9_multiturn.py` | S9: 多轮对话 transcript 完整性 |
| `test_s9_transcript.py` | S9: transcript 文件格式验证 |
| `test_s9b_continue.py` | S9: --continue 恢复后第二轮对话 |
| `test_s17_init.py` | S17: system/init 事件字段完整性 |
| `test_s19_result_fields.py` | S19: result 事件字段完整性 |
| `test_s20_stop_reason.py` | S20: stop_reason 映射正确性 |
| `test_s21b_result.py` | S21b: 混合内容 result 验证 |
| `test_s25_permission.py` | S25: 权限拒绝 + permission_denials 填充 |

### basic/ — 基础框架测试

| 脚本 | 说明 |
|------|------|
| `cs_loop_test.py` | C/S 闭环动态测试 |
| `cs_loop_test_v3.py` | C/S 闭环动态测试 v3 — 扩展场景 |
| `test_v5.py` | jcc 全面测试框架 v5 — 带健康检查 |
| `test_bridge_quick.py` | 验证 bridge_serve 批量模式可用性 |

### batch/ — 批量/综合测试

| 脚本 | 说明 |
|------|------|
| `test_advanced_v4.py` | 高级功能测试 v4 — JSON/dry-run/会话/交互/压缩 |
| `test_batch_full.py` | 全量批量测试 v8 — 64 场景 |
| `test_batch_v7.py` | 批量测试框架 v7 |
| `test_comprehensive.py` | 全面功能测试 — 工具/斜杠命令/边缘/压缩 |
| `test_comprehensive_v2.py` | 全面功能测试 v2 |
| `test_extended_v3.py` | 扩展测试 v3 — 连接断开/超大参数/多轮/MCP |

### edge/ — 边缘场景测试

| 脚本 | 说明 |
|------|------|
| `test_s11_empty.py` | S11: 空响应体识别为错误 |
| `test_s12_partial.py` | S12: 部分 SSE 流（无 [DONE]）正确处理 |
| `test_e4.py` | E4 场景测试 |
| `test_edge.py` | 边缘条件测试 |
| `test_edge_v6.py` | 边缘条件测试 v6 — 实时打印 |
| `test_empty_toolcalls.py` | 空 tool_calls 数组处理 |
| `test_error_recovery.py` | 错误后恢复 — 先 500 错误重试后成功 |

### tools/ — 工具调用测试

| 脚本 | 说明 |
|------|------|
| `test_s21_mixed.py` | S21: thinking + tool_use 混合 + content block 顺序 |
| `test_s24_mcp.py` | S24: MCP 协议完整验证（initialize/tools/list/tools/call） |
| `test_mcp_tools.py` | MCP 工具直接调用测试 |
| `test_multi_tool.py` | 多工具调用测试 |
| `test_multi_tool_parallel.py` | 多工具同时调用 — 一次响应多个 tool_calls |
| `test_reasoning_tool.py` | reasoning + tool 调用测试 |
| `test_tool_call.py` | 工具调用基础测试 |

### session/ — 会话/并发测试

| 脚本 | 说明 |
|------|------|
| `test_s7_long_text.py` | S7: 长文本流式合并（12 chunk → 单 text block） |
| `test_s18_multi_turn.py` | S18: 多轮工具调用 + num_turns 递增 |
| `test_s23_large.py` | S23: 5000 字符超大上下文 |
| `test_big_reasoning.py` | 超大思考链 — 10KB reasoning_content |
| `test_concurrent.py` | 并发请求 — 同时 3 个 jcc 进程 |
| `test_long_session.py` | 长会话测试 |
| `test_long_session2.py` | 长会话测试 2 |
| `test_multi_turn.py` | 多轮工具调用闭环 |

### errors/ — 错误处理测试

| 脚本 | 说明 |
|------|------|
| `test_s10_errors.py` | S10: 401/429/500/503 错误处理 |
| `test_s15_timeout.py` | S15: 超时处理 |
| `test_b10.py` | B10 场景测试 |
| `test_b13.py` | B13 场景测试 |
| `test_b14_429.py` | 429 限流 — Retry-After 头处理 |
| `test_b14_full.py` | B14 全量测试 |
| `test_b14_long.py` | B14 长测试 |

### helpers/ — 辅助脚本

| 脚本 | 说明 |
|------|------|
| `analyze_startup.py` | 分析 jcc 启动耗时 |
| `copy_dev_artifacts.py` | 复制 jcc-gh.exe/jcc-rg.exe 到输出目录 |
| `split_protocol.py` | 协议分割工具 |

### probes/ — Node.js 探针脚本

| 脚本 | 说明 |
|------|------|
| `probe-c5-ui.mjs` | C5 UI 探针 |
| `probe-feedback-*.mjs` (6 个) | feedback 场景探针（cmd/corner/more/open/shot/ui） |

### launch/ — 启动脚本

| 脚本 | 说明 |
|------|------|
| `launch-host-c7.cmd` | 启动 c7 host |
| `launch-host-c8.cmd` / `.ps1` | 启动 c8 host |
| `launch-host-q.cmd` / `.ps1` | 启动 q host |

## 运行方式

### 前置条件

1. MockServer 已构建：`artifacts/bin/OpenAI.MockServer/Debug/net10.0/JoinCode.OpenAI.MockServer.exe`
2. jcc 已构建：`artifacts/bin/JoinCode/Debug/net10.0/jcc.exe`
3. 环境变量：`JCC_ENDPOINT=http://localhost:9901/v1`、`OPENAI_API_KEY=test-key`、`JCC_SKIP_MODEL_FETCH=1`、`JCC_DISABLE_RETRY=1`

### 运行单个脚本

```bash
python3 scripts/verification/task081/edge/test_s11_empty.py
```

### 运行某类全部脚本

```bash
for f in scripts/verification/task081/edge/test_*.py; do
    echo "=== $(basename $f) ==="
    python3 "$f" 2>&1 | tail -5
    echo ""
done
```

## 合并去重记录

合并两轮测试脚本时，以下 4 个重叠脚本被归档到 `.xxx/`：

| 归档脚本 | 替代者 | 原因 |
|---------|--------|------|
| `test_s22_concurrent.py` | `test_concurrent.py` | 场景相同，保留 round2 版本（路径已统一） |
| `test_s16_edge_cases.py` | `test_edge.py` + `test_edge_v6.py` | round2 更全面（618 行 vs 300 行） |
| `test_no_done.py` | `test_s12_partial.py` | 场景相同，保留 task081 版本（更完善） |
| `test_mixed_response.py` | `test_s21_mixed.py` | 场景相同，保留 task081 版本（含 thinking + 顺序验证） |

## 注意事项

- 测试脚本不参与编译（纯 Python/Node.js），不影响 CI
- 脚本会启动真实 exe 进程，需确保构建产物存在
- MockServer 端口统一 9901
- 日志输出到 `.xxx/logs/`
