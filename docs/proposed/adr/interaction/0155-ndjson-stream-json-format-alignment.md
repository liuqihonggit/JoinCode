# ADR 0155: NDJSON 输出格式对齐基准 ClaudeCode stream-json

## 状态

proposed

## 上下文

C# 侧 NDJSON 输出采用扁平事件流（13 种独立事件类型：text/thinking/tool_start/tool_end/...），基准 ClaudeCode stream-json 采用嵌套消息流（8+ 种顶层 type，工具调用嵌在 assistant/user 消息内）。

差异详见 [TASK080](../../task/TASK080-NDJSON-stream-json-格式对齐基准.md)。

### 核心差异

1. **事件结构**：C# `{ type, timestamp, data }` vs 基准 `{ type, uuid, session_id, ...fields }`
2. **工具调用**：C# 独立 tool_start/tool_end vs 基准 assistant 消息内 tool_use block + user 消息内 tool_result block
3. **缺少 system/init**：基准侧首条消息含 cwd/model/tools 等会话元数据
4. **缺少 uuid/session_id**：基准侧每条消息有唯一标识和会话关联
5. **result 结构不完整**：缺少 duration_ms/usage/cost/num_turns 等

## 决策

对齐基准侧 stream-json 格式：

1. **重写 CliEventConsumer**：文本/思考/工具调用嵌在 `assistant` 消息内，工具结果嵌在 `user` 消息内
2. **新增 system/init 消息**：每个会话开始时输出含会话元数据的 system/init
3. **补充 uuid/session_id**：每条消息有唯一标识和会话关联
4. **扩展 result 消息**：补充 duration_ms/usage/cost/num_turns/stop_reason 等字段

## 替代方案

### 方案 A：保留扁平事件流（放弃）

- 优点：改动小
- 缺点：SDK 消费者无法直接消费 jcc 输出；与基准侧格式不兼容；未来 SDK 集成困难

### 方案 B：双格式过渡（拒绝）

- 优点：渐进式迁移，不破坏现有消费者
- 缺点：维护两套格式成本高；过渡期长；项目无后向兼容要求（AGENTS.md 规则1）

### 方案 C：对齐基准侧（采纳）

- 优点：SDK 兼容性；与基准侧行为一致；未来集成容易
- 缺点：改动大，影响所有 NDJSON 消费方

## 影响

- `CliEventConsumer` 重写为 assistant/user 消息流
- `CliStreamEvent`/`CliStreamEventData` 替换为基准兼容的 DTO
- `NonInteractiveModeRunner` 的 result 事件扩展
- E2E 测试中 NDJSON 解析代码更新
- bridge `NdjsonParser` 已对齐基准侧（无需改动）
