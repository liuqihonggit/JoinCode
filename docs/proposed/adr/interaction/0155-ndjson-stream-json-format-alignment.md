# ADR 0155: NDJSON 输出格式对齐基准 ClaudeCode stream-json

## 状态

accepted — 已实现

## 上下文

C# 侧 NDJSON 输出曾采用扁平事件流（13 种独立事件类型：text/thinking/tool_start/tool_end/...），现已改为嵌套消息流（assistant/user 消息内嵌 content blocks），与基准 ClaudeCode stream-json 格式对齐。

差异详见 [TASK080](../../task/TASK080-NDJSON-stream-json-格式对齐基准.md)（历史记录）。

### 已对齐的核心差异

1. **事件结构**：已改为 `{ type, uuid, session_id, ...fields }` 嵌套消息流 — `SdkMessageBase` + `SdkContentBlock`（text/thinking/tool_use/tool_result）
2. **工具调用**：已嵌在 `assistant` 消息内 `tool_use` block + `user` 消息内 `tool_result` block — `_pendingBlocks` 累积后 `FlushAssistant()` 整体输出
3. **system/init 消息**：已实现 — `SdkSystemInitMessage` 含 cwd/model/tools/mcp_servers/permissionMode/slash_commands/skills/plugins/agents/apiKeySource/betas/output_style/fast_mode_state
4. **uuid/session_id**：已实现 — `SdkMessageBase.Uuid` + `SessionId`
5. **result 结构**：已扩展 — `SdkResultMessage` 含 structured_output/duration_ms/usage/cost/num_turns/stop_reason/permission_denials/modelUsage

## 决策

对齐基准侧 stream-json 格式（方案 C）：

1. **CliEventConsumer 重写为 assistant/user 消息流** — 已实现
2. **新增 system/init 消息** — 已实现
3. **补充 uuid/session_id** — 已实现
4. **扩展 result 消息** — 已实现

## 实现位置

| 组件 | 文件 |
|------|------|
| SDK 消息类型定义 | `modules/apps/cli/core/output/SdkMessage.cs` |
| NDJSON 事件流输出器 | `modules/apps/cli/adapters/CliEventConsumer.cs` |
| --format ndjson 参数 | `modules/apps/engine/app/builder/CommandLineOptions.cs` |
| 非交互模式启动器 | `modules/apps/cli/entry/runners/NonInteractiveModeRunner.cs` |
| NDJSON 行解析器 | `modules/async_primitives/transport.impl/bridge/shared/messaging/NdjsonParser.cs` |
