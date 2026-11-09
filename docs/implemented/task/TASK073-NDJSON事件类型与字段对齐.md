# TASK073: NDJSON 事件类型与字段对齐 ✅ 已完成

## 状态：已完成

## 完成时间：2026-11-09

## 背景

C# 侧 `--format ndjson` 输出与基准侧 ClaudeCode stream-json 格式对齐。当前 C# 侧定义了 5 种事件类型，基准侧有 25 种，存在 19 种缺失事件类型 + 多处字段缺失。

## 基准侧来源

`D:\project\claude-code-rust\claude-code-rev-main\src\entrypoints\sdk\coreSchemas.ts` — `SDKMessageSchema` 联合类型（第 1854-1881 行）

## C# 侧来源

- `modules/apps/cli/core/output/SdkMessage.cs` — 事件类型定义
- `modules/apps/cli/adapters/CliEventConsumer.cs` — 事件消费与 NDJSON 输出

---

## 一、缺失的 19 种事件类型

### 高优先级（2种）

| # | type | subtype | Schema 名称 | 关键字段 | 说明 |
|---|------|---------|-------------|---------|------|
| 1 | `stream_event` | - | SDKPartialAssistantMessageSchema | `event: RawMessageStreamEvent`, `parent_tool_use_id`, `uuid`, `session_id` | 流式部分消息，用于 --include-partial-messages 功能 |
| 2 | `system` | `session_state_changed` | SDKSessionStateChangedMessageSchema | `state: idle\|running\|requires_action` | 会话状态变更 |

### 中优先级（10种）

| # | type | subtype | Schema 名称 | 关键字段 | 说明 |
|---|------|---------|-------------|---------|------|
| 3 | `system` | `api_retry` | SDKAPIRetryMessageSchema | `attempt`, `max_retries`, `retry_delay_ms`, `error_status`, `error` | API 重试通知 |
| 4 | `system` | `compact_boundary` | SDKCompactBoundaryMessageSchema | `compact_metadata: {trigger, pre_tokens, preserved_segment?}` | 压缩边界 |
| 5 | `system` | `status` | SDKStatusMessageSchema | `status: 'compacting'\|null`, `permissionMode?` | 状态消息 |
| 6 | `auth_status` | - | SDKAuthStatusMessageSchema | `isAuthenticating`, `output[]`, `error?` | 认证状态 |
| 7 | `system` | `hook_started` | SDKHookStartedMessageSchema | `hook_id`, `hook_name`, `hook_event` | Hook 开始 |
| 8 | `system` | `hook_progress` | SDKHookProgressMessageSchema | `hook_id`, `hook_name`, `hook_event`, `stdout`, `stderr`, `output` | Hook 进度 |
| 9 | `system` | `hook_response` | SDKHookResponseMessageSchema | `hook_id`, `hook_name`, `hook_event`, `output`, `stdout`, `stderr`, `exit_code?`, `outcome` | Hook 响应 |
| 10 | `system` | `task_started` | SDKTaskStartedMessageSchema | `task_id`, `tool_use_id?`, `description`, `task_type?`, `workflow_name?`, `prompt?` | 任务开始 |
| 11 | `system` | `task_progress` | SDKTaskProgressMessageSchema | `task_id`, `tool_use_id?`, `description`, `usage`, `last_tool_name?`, `summary?` | 任务进度 |
| 12 | `system` | `task_notification` | SDKTaskNotificationMessageSchema | `task_id`, `tool_use_id?`, `status`, `output_file`, `summary`, `usage?` | 任务通知 |
| 13 | `tool_use_summary` | - | SDKToolUseSummaryMessageSchema | `summary`, `preceding_tool_use_ids[]` | 工具使用摘要 |
| 14 | `rate_limit_event` | - | SDKRateLimitEventSchema | `rate_limit_info: {status, resetsAt?, rateLimitType?, ...}` | 速率限制事件 |

### 低优先级（5种）

| # | type | subtype | Schema 名称 | 关键字段 | 说明 |
|---|------|---------|-------------|---------|------|
| 15 | `system` | `local_command_output` | SDKLocalCommandOutputMessageSchema | `content: string` | 本地命令输出 |
| 16 | `system` | `files_persisted` | SDKFilesPersistedEventSchema | `files[]`, `failed[]`, `processed_at` | 文件持久化 |
| 17 | `system` | `elicitation_complete` | SDKElicitationCompleteMessageSchema | `mcp_server_name`, `elicitation_id` | 引导完成 |
| 18 | `prompt_suggestion` | - | SDKPromptSuggestionMessageSchema | `suggestion` | 提示建议 |
| 19 | `user` | - (isReplay) | SDKUserMessageReplaySchema | 同 user + `isReplay: true` | 用户消息回放 |

---

## 二、已有事件类型的字段缺失

### 2.1 system/init — 缺失 7 个字段

基准侧字段定义（coreSchemas.ts:1457-1494）：

| 基准侧字段 | 类型 | C# 侧 | 差异 |
|-----------|------|-------|------|
| `agents: string[]` | optional | - | **缺失** |
| `apiKeySource: enum(user/project/org/temporary/oauth)` | 必填 | - | **缺失** |
| `betas: string[]` | optional | - | **缺失** |
| `claude_code_version: string` | 必填 | `ClaudeCodeVersion: string` | 一致 |
| `cwd: string` | 必填 | `Cwd: string` | 一致 |
| `tools: string[]` | 必填 | `Tools: List<string>` | 一致 |
| `mcp_servers: [{name, status}]` | 必填 | `McpServers: List<SdkMcpServer>` | 一致 |
| `model: string` | 必填 | `Model: string` | 一致 |
| `permissionMode: enum(default/acceptEdits/bypassPermissions/plan/dontAsk)` | 必填 | `PermissionMode: string` | **类型差异**：基准是枚举，C# 是自由 string |
| `slash_commands: string[]` | 必填 | - | **缺失** |
| `output_style: string` | 必填 | - | **缺失** |
| `skills: string[]` | 必填 | `Skills: List<string>` | 一致 |
| `plugins: [{name, path, source?}]` | 必填 | - | **缺失** |
| `fast_mode_state: enum(off/cooldown/on)` | optional | - | **缺失** |
| `uuid: string` | 必填 | `Uuid: string?` | **可选性差异** |
| `session_id: string` | 必填 | `SessionId: string?` | **可选性差异** |

**C# 侧文件**: `SdkMessage.cs:92-116` (`SdkSystemInitMessage`)
**消费侧**: `CliEventConsumer.cs:239-252` (`EnsureSystemInit`)

### 2.2 assistant — 缺失 1 个字段

基准侧字段定义（coreSchemas.ts:1347-1356）：

| 基准侧字段 | 类型 | C# 侧 | 差异 |
|-----------|------|-------|------|
| `type: "assistant"` | 必填 | `Type => "assistant"` | 一致 |
| `message: APIAssistantMessage` | 必填 | `Message: SdkMessageBody` | 一致 |
| `parent_tool_use_id: string\|null` | 必填 | `ParentToolUseId: string?` | 一致 |
| `error: enum(authentication_failed/billing_error/rate_limit/invalid_request/server_error/unknown/max_output_tokens)` | optional | - | **缺失** |
| `uuid: string` | 必填 | `Uuid: string?` | **可选性差异** |
| `session_id: string` | 必填 | `SessionId: string?` | **可选性差异** |

**C# 侧文件**: `SdkMessage.cs:58-68` (`SdkAssistantMessage`)

### 2.3 user — 缺失 3 个字段 + 1 个类型差异

基准侧字段定义（coreSchemas.ts:1273-1295）：

| 基准侧字段 | 类型 | C# 侧 | 差异 |
|-----------|------|-------|------|
| `type: "user"` | 必填 | `Type => "user"` | 一致 |
| `message: APIUserMessage` | 必填 | `Message: SdkMessageBody` | 一致 |
| `parent_tool_use_id: string\|null` | 必填 | `ParentToolUseId: string?` | 一致 |
| `isSynthetic: boolean` | optional | - | **缺失** |
| `tool_use_result: unknown` | optional | `ToolUseResult: string?` | **类型差异**：基准是 object，C# 是 string |
| `priority: enum(now/next/later)` | optional | - | **缺失** |
| `timestamp: string` | optional | - | **缺失** |
| `uuid: string?` | optional | `Uuid: string?` | 一致 |
| `session_id: string?` | optional | `SessionId: string?` | 一致 |

**C# 侧文件**: `SdkMessage.cs:70-83` (`SdkUserMessage`)

### 2.4 result — 缺失 2 个字段 + 1 个语义差异

基准侧 success result（coreSchemas.ts:1407-1426）：

| 基准侧字段 | 类型 | C# 侧 | 差异 |
|-----------|------|-------|------|
| `type: "result"` | 必填 | `Type => "result"` | 一致 |
| `subtype: "success"` | 必填(literal) | `Subtype: string` (默认 "success") | **类型差异**：基准是 literal type，C# 是自由 string |
| `duration_ms: number` | 必填 | `DurationMs: long` | 一致 |
| `duration_api_ms: number` | 必填 | `DurationApiMs: long` | 一致 |
| `is_error: boolean` | 必填 | `IsError: bool` | 一致 |
| `num_turns: number` | 必填 | `NumTurns: int` | 一致 |
| `result: string` | 必填(仅success) | `Result: string` (默认空) | **语义差异**：error result 基准无此字段，C# 总是有 |
| `stop_reason: string\|null` | 必填 | `StopReason: string?` | 一致 |
| `total_cost_usd: number` | 必填 | `TotalCostUsd: decimal` | 一致 |
| `usage: NonNullableUsage` | 必填 | `Usage: SdkUsage?` | 一致 |
| `modelUsage: Record<string, ModelUsageSchema>` | 必填 | `ModelUsage: Dictionary<string, SdkModelUsage>?` | 一致 |
| `permission_denials: [{tool_name, tool_use_id, tool_input}]` | 必填 | `PermissionDenials: List<SdkPermissionDenial>` | 一致 |
| `structured_output: unknown` | optional(仅success) | - | **缺失** |
| `fast_mode_state: enum(off/cooldown/on)` | optional | - | **缺失** |
| `uuid: string` | 必填 | `Uuid: string?` | **可选性差异** |
| `session_id: string` | 必填 | `SessionId: string?` | **可选性差异** |

基准侧 error result（coreSchemas.ts:1428-1451）：

| 基准侧字段 | 类型 | C# 侧 | 差异 |
|-----------|------|-------|------|
| `subtype: enum(error_during_execution/error_max_turns/error_max_budget_usd/error_max_structured_output_retries)` | 必填 | `Subtype: string` | **类型差异** |
| `errors: string[]` | 必填 | `Errors: List<string>` | 一致 |
| **无 `result` 字段** | - | `Result: string` (默认空) | **语义差异**：error result 不应有 result 字段 |

**C# 侧文件**: `SdkMessage.cs:172-209` (`SdkResultMessage`)

### 2.5 tool_progress — 缺失 1 个字段

基准侧字段定义（coreSchemas.ts:1648-1659）：

| 基准侧字段 | 类型 | C# 侧 | 差异 |
|-----------|------|-------|------|
| `type: "tool_progress"` | 必填 | `Type => "tool_progress"` | 一致 |
| `tool_use_id: string` | 必填 | `ToolUseId: string` | 一致 |
| `tool_name: string` | 必填 | `ToolName: string` | 一致 |
| `parent_tool_use_id: string\|null` | 必填 | `ParentToolUseId: string?` | 一致 |
| `elapsed_time_seconds: number` | 必填 | `ElapsedTimeSeconds: double` | 一致 |
| `task_id: string` | optional | - | **缺失** |
| `uuid: string` | 必填 | `Uuid: string?` | **可选性差异** |
| `session_id: string` | 必填 | `SessionId: string?` | **可选性差异** |

**C# 侧文件**: `SdkMessage.cs:211-228` (`SdkToolProgressMessage`)

---

## 三、Content Block 差异

| 基准侧 block type | C# 侧对应 | 差异 |
|-------------------|-----------|------|
| `text` | `SdkTextBlock` (Text) | 一致 |
| `thinking` | `SdkThinkingBlock` (Thinking) | 一致 |
| `tool_use` | `SdkToolUseBlock` (Id, Name, Input: JsonElement) | 一致 |
| `tool_result` | `SdkToolResultBlock` (ToolUseId, Content, IsError) | 一致 |
| `advisor` | - | **缺失**（内部类型，可能不需要） |
| `advisor_tool_result` | - | **缺失**（内部类型，可能不需要） |

---

## 四、实施计划

### Phase 1: 补齐已有事件缺失字段（低风险）

改动集中在 `SdkMessage.cs` + `CliEventConsumer.cs`，不涉及新事件发射逻辑。

| 步骤 | 内容 | 涉及文件 |
|------|------|---------|
| 1.1 | system/init 补 7 字段 (`agents`, `apiKeySource`, `betas`, `slash_commands`, `output_style`, `plugins`, `fast_mode_state`) | `SdkMessage.cs`, `CliEventConsumer.cs` |
| 1.2 | assistant 补 `error` 字段（7种错误类型枚举） | `SdkMessage.cs`, `CliEventConsumer.cs` |
| 1.3 | user 补 3 字段 (`isSynthetic`, `priority`, `timestamp`) + `tool_use_result` 类型修正 | `SdkMessage.cs`, `CliEventConsumer.cs` |
| 1.4 | result 补 2 字段 (`structured_output`, `fast_mode_state`) + error result 语义修正（不应有 `result` 字段） | `SdkMessage.cs`, `CliEventConsumer.cs` |
| 1.5 | tool_progress 补 `task_id` 字段 | `SdkMessage.cs`, `CliEventConsumer.cs` |

### Phase 2: 新增高优先级事件类型（需设计）

| 步骤 | 内容 | 涉及文件 |
|------|------|---------|
| 2.1 | `stream_event` — 流式部分消息 | 新增 `SdkStreamEventMessage`，需 `--include-partial-messages` 开关 |
| 2.2 | `session_state_changed` — 会话状态变更 | 新增 `SdkSessionStateChangedMessage`，需状态机对接 |

### Phase 3: 新增中优先级事件类型

| 步骤 | 内容 |
|------|------|
| 3.1 | `system/api_retry` — API 重试通知 |
| 3.2 | `system/compact_boundary` — 压缩边界 |
| 3.3 | `system/status` — 状态消息 |
| 3.4 | `auth_status` — 认证状态 |
| 3.5 | `system/hook_started/progress/response` — Hook 生命周期（3种） |
| 3.6 | `system/task_started/progress/notification` — 任务生命周期（3种） |
| 3.7 | `tool_use_summary` — 工具使用摘要 |
| 3.8 | `rate_limit_event` — 速率限制事件 |

### Phase 4: 新增低优先级事件类型

| 步骤 | 内容 |
|------|------|
| 4.1 | `system/local_command_output` |
| 4.2 | `system/files_persisted` |
| 4.3 | `system/elicitation_complete` |
| 4.4 | `prompt_suggestion` |
| 4.5 | `user` (replay) — 用户消息回放 |

---

## 五、验收标准

每个步骤完成后需验证：
1. 编译通过（`dotnet build`）
2. NDJSON 输出格式与基准侧字段名/类型一致
3. 现有 E2E 测试不受影响（137/137 通过）
4. 新增字段/事件有对应单元测试

---

## 六、完成总结

### 25 种事件类型全部对齐

| 事件 | 类型定义 | NDJSON 输出 | 触发逻辑 | 验证 |
|------|---------|-------------|---------|------|
| system/init | ✅ | ✅ | ✅ | ✅ 真实运行验证 |
| system/session_state_changed | ✅ | ✅ | ✅ | ✅ 真实运行验证 |
| system/compact_boundary | ✅ | ✅ | ✅ | ✅ |
| system/status | ✅ | ✅ | ✅ | ✅ 真实运行验证 |
| system/local_command_output | ✅ | ✅ | ✅ | ✅ |
| system/files_persisted | ✅ | ✅ | ✅ | ✅ 真实运行验证 |
| system/elicitation_complete | ✅ | ✅ | ✅ | ✅ |
| assistant (含 error) | ✅ | ✅ | ✅ | ✅ 真实运行验证 |
| user (含 tool_result, IsReplay) | ✅ | ✅ | ✅ | ✅ 真实运行验证 |
| result (含 permission_denials, errors) | ✅ | ✅ | ✅ | ✅ 真实运行验证 |
| stream_event | ✅ | ✅ | ✅ | ⚠️ 需真实 API 验证（MockServer 不产生 SSE 流） |
| api_retry | ✅ | ✅ | ✅ | ✅ |
| rate_limit_event | ✅ | ✅ | ✅ | ✅ |
| hook_started | ✅ | ✅ | ✅ | ✅ |
| hook_progress | ✅ | ✅ | ✅ | ✅ |
| hook_response | ✅ | ✅ | ✅ | ✅ |
| task_started | ✅ | ✅ | ✅ | ✅ |
| task_progress | ✅ | ✅ | ✅ | ✅ |
| task_notification | ✅ | ✅ | ✅ | ✅ |
| tool_use_summary | ✅ | ✅ | ✅ | ✅ 真实运行验证 |
| tool_progress | ✅ | ✅ | ✅ | ✅ |
| auth_status | ✅ | ✅ | ✅ | ✅ 真实运行验证 |
| prompt_suggestion | ✅ | ✅ | ✅ | ✅ |

### Git 提交历史

```
2d92c303d — stream_event 原始事件捕获
54ccad63f — stream_event 基础框架
8599b9a08 — system/init 重复输出修复
a8e7a9cda — tool_use_summary 和 prompt_suggestion 触发逻辑
640eda83f — 5种剩余事件触发逻辑
c5eb40bd4 — 7种剩余事件回调定义和NDJSON输出
966b10176 — Task 事件 NDJSON 桥接
75e977cf3 — RateLimit 事件 NDJSON 桥接
13ca9e6ca — Hook 事件 NDJSON 桥接
eed6a129a — Phase 4 新增 5 种低优先级事件类型定义
91fd12ff9 — Phase 3 新增 10 种中优先级事件类型定义
7aa2fcbd2 — Phase 2.2 session_state_changed
4436f5671 — Phase 1 补齐已有事件缺失字段
```

### 已知限制

- `stream_event` 的 `event` 字段为 `JsonElement`（原始 API 事件 JSON 透传），需真实 OpenAI/Anthropic API 环境验证
- `tool_use_summary` 的 `summary` 字段为工具名拼接（如 "bash, bash"），基准侧用 Haiku 异步生成更详细的摘要
- `files_persisted` 的 `file_id` 字段为空字符串（C# 侧无 Files API 的 file_id 概念）
