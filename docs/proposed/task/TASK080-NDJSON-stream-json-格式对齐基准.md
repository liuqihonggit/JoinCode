# TASK080 — NDJSON stream-json 格式对齐基准 ClaudeCode

> **状态：已实现** — 代码已演进为嵌套消息流模式，与基准 ClaudeCode stream-json 格式对齐。本文档差异项已过时，保留作历史参考。

## 差异概述（历史记录 — 代码已对齐）

C# 侧 NDJSON 输出格式曾采用扁平事件流（13 种独立事件类型），现已改为嵌套消息流（assistant/user 消息内嵌 content blocks），与基准侧对齐。

## 两侧源码位置

### C# 侧（JoinCode）

| 文件 | 行号 | 说明 |
|------|------|------|
| `modules/apps/cli/adapters/CliEventConsumer.cs` | 63 | `text` 事件 |
| 同上 | 78 | `thinking` 事件 |
| 同上 | 86 | `tool_start` 事件 |
| 同上 | 102 | `tool_end` 事件 |
| 同上 | 127 | `tool_progress` 事件 |
| 同上 | 136 | `loop_detected` 事件 |
| 同上 | 145 | `timing` 事件 |
| 同上 | 155 | `done` 事件 |
| 同上 | 165 | `agent_started` 事件 |
| 同上 | 172 | `subagent_tool_use` 事件 |
| 同上 | 179 | `agent_finished` 事件 |
| 同上 | 186 | `context_usage` 事件 |
| `modules/apps/cli/entry/runners/NonInteractiveModeRunner.cs` | 63 | `result` 事件 |
| `modules/apps/cli/core/output/CliStreamEvent.cs` | 6-22 | `CliStreamEvent` 结构（type + timestamp + data） |
| 同上 | 27-105 | `CliStreamEventData` 负载（扁平字段集合） |

### 基准侧（ClaudeCode TS）

| 文件 | 行号 | 说明 |
|------|------|------|
| `src/entrypoints/sdk/coreSchemas.ts` | 1854-1881 | `SDKMessageSchema` union（23 项） |
| 同上 | 1347-1356 | `SDKAssistantMessageSchema`（assistant 消息） |
| 同上 | 1273-1295 | `SDKUserMessageSchema`（user 消息） |
| 同上 | 1407-1426 | `SDKResultSuccessSchema`（result 成功） |
| 同上 | 1428-1451 | `SDKResultErrorSchema`（result 错误） |
| 同上 | 1457-1494 | `SDKSystemMessageSchema`（system/init） |
| `src/cli/print.ts` | 884-886 | stream-json 输出点 |
| `src/utils/queryHelpers.ts` | 102-119 | assistant 消息生成 |
| 同上 | 203-217 | user 消息生成（含 tool_result） |
| `src/utils/messages/systemInit.ts` | 53-96 | system/init 消息生成 |

## 差异项清单（逐项决策）

### 差异 1：事件结构 — 扁平 vs 嵌套

| 维度 | C# 侧 | 基准侧 |
|------|--------|--------|
| 结构 | `{ type, timestamp, data: { ... } }` | `{ type, uuid, session_id, ...fields }` |
| 工具调用 | 独立 `tool_start`/`tool_end` 事件 | `assistant` 消息内 `tool_use` block + `user` 消息内 `tool_result` block |
| 文本内容 | 独立 `text` 事件 | `assistant` 消息内 `text` block |
| 思考内容 | 独立 `thinking` 事件 | `assistant` 消息内 `thinking` block |

**影响面**：所有消费 jcc NDJSON 输出的下游（bridge、SDK 消费者、E2E 测试解析器）

### 差异 2：缺少 system/init 消息

基准侧每个 query turn 第一条消息是 `system/init`，含 `cwd`/`model`/`tools`/`mcp_servers`/`permissionMode`/`slash_commands`/`skills`/`plugins` 等会话元数据。C# 侧无此消息。

**影响面**：SDK 消费者依赖 system/init 获取会话上下文

### 差异 3：缺少 uuid/session_id

基准侧每条消息有 `uuid` + `session_id`。C# 侧无。

**影响面**：消息追踪、会话关联、重放

### 差异 4：result 消息结构不同

| 字段 | C# 侧 | 基准侧 |
|------|--------|--------|
| exitCode | ✅ `data.exitCode` | ❌（基准侧用 `subtype` 区分成功/错误） |
| content | ✅ `data.content` | ✅ `result`（仅 success） |
| error | ✅ `data.error` | ✅ `errors` 数组（仅 error） |
| duration_ms | ❌ | ✅ |
| duration_api_ms | ❌ | ✅ |
| num_turns | ❌ | ✅ |
| total_cost_usd | ❌ | ✅ |
| usage | ❌ | ✅（input_tokens/output_tokens/cache_*） |
| modelUsage | ❌ | ✅ |
| stop_reason | ❌（在 done 事件中） | ✅ |
| is_error | ❌ | ✅ |
| permission_denials | ❌ | ✅ |

**影响面**：SDK 消费者依赖 result 消息获取用量/成本/轮次等统计

### 差异 5：缺少多种 system subtype

基准侧 13 个 system subtype：`init`/`compact_boundary`/`status`/`api_retry`/`local_command_output`/`hook_started`/`hook_progress`/`hook_response`/`task_notification`/`task_started`/`task_progress`/`session_state_changed`/`files_persisted`/`elicitation_complete`/`post_turn_summary`

C# 侧对应：`context_usage`（部分对应 `status`）、`loop_detected`（C# 独有）、`timing`（C# 独有）

**影响面**：上下文压缩、API 重试、hook 事件、任务通知等场景的 SDK 可见性

### 差异 6：缺少 stream_event（流式增量）

基准侧有 `stream_event` 类型（含 Anthropic SDK 的 `RawMessageStreamEvent`），用于流式增量输出。C# 侧无此概念，文本/思考/工具调用都是已组装完毕的完整事件。

**影响面**：需要流式增量的 SDK 消费者

### 差异 7：缺少 parent_tool_use_id

基准侧 assistant/user/stream_event/tool_progress 都有 `parent_tool_use_id` 字段，用于子代理嵌套场景的消息归属。C# 侧用 `agent_started`/`agent_finished` 事件 + `subagent_id` 字段表达。

**影响面**：子代理嵌套消息归属

### 差异 8：C# 独有事件

C# 侧有基准侧无的事件：`loop_detected`、`timing`、`context_usage`、`agent_started`、`subagent_tool_use`、`agent_finished`

**影响面**：C# 侧独有功能在基准侧无对应

## 四问回答

### 1. 专家隐性实践
领域专家会：① 编写 SDK 消费者测试验证两种格式兼容性 ② 检查现有 SDK 消费者（bridge/VSCode 扩展等）的格式依赖 ③ 评估格式变更对版本契约的影响 ④ 考虑双格式输出过渡期方案

### 2. 原始素材清单
- 基准侧 Zod schema：`src/entrypoints/sdk/coreSchemas.ts`（23 项 union 的字段约束）
- 基准侧输出代码：`src/cli/print.ts:884-886` + `src/utils/queryHelpers.ts`
- Anthropic SDK 类型：`@anthropic-ai/sdk` 的 `APIAssistantMessage`/`APIUserMessage`
- C# 侧 `CliStreamEvent`/`CliStreamEventData` 定义

### 3. 具体方法与工具
- 逐项对比 Zod schema 字段 vs C# DTO 字段
- 编写兼容性测试：用基准侧 SDK 消费者解析 C# 侧 NDJSON 输出
- 评估过渡方案：双格式输出 or 一步切换

### 4. 独立验证来源
- 基准侧 SDK 消费者（bridge sessionRunner）解析 C# 侧 NDJSON
- Anthropic SDK 类型定义验证 message 结构
- E2E 测试：jcc --format ndjson 输出 vs 基准侧 stream-json 输出对比

## 决策矩阵

| 差异项 | 对齐方案 | 改动量 | 影响面 | 建议 |
|--------|---------|--------|--------|------|
| 1 事件结构 | 重写 CliEventConsumer 为 assistant/user 消息流 | 大 | 所有下游 | 需 ADR |
| 2 system/init | 新增 system/init 事件 | 中 | SDK 消费者 | 推荐 |
| 3 uuid/session_id | 给每条消息加 uuid/session_id | 小 | 消息追踪 | 推荐 |
| 4 result 结构 | 扩展 result 事件字段 | 中 | SDK 消费者 | 推荐 |
| 5 system subtypes | 按需新增 | 大 | 多场景 | 按需 |
| 6 stream_event | 新增流式增量模式 | 大 | 流式消费者 | 按需 |
| 7 parent_tool_use_id | 给消息加此字段 | 小 | 子代理 | 推荐 |
| 8 C# 独有事件 | 保留 or 映射到 system subtype | 小 | 兼容性 | 保留 |
