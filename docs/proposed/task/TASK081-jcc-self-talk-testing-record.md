# TASK081 — jcc 自己跟自己对话全链路测试记录

> **目标**：AI 同时控制客户端（jcc）和服务端（MockServer），深究整个链路每个细节，找 bug
> **原则**：抓大不放小 — 小细节往往是 bug 根源（如 ToolCallId 遗漏导致 id/input 空值）
> **创建**：2026-11-05

---

## 测试环境配置

### MockServer
- **csproj**：`test/mock/open_ai.mock_server/OpenAI.MockServer.csproj`
- **DLL**：`artifacts/bin/OpenAI.MockServer/Debug/net10.0/JoinCode.OpenAI.MockServer.dll`
- **启动**：`dotnet artifacts/bin/OpenAI.MockServer/Debug/net10.0/JoinCode.OpenAI.MockServer.dll --port 8091 --no-open-browser > .xxx/mockserver_new.log 2>&1 &`
- **control 端点**：GET `/__control__/state`，POST `/__control__/clear`，POST `/__control__/inject`，POST `/__control__/script`
- **注入 DTO**：`InjectedResponse`（`test/mock/mock_server.core/MockServerController.cs:19`）
  - `StatusCode` (int, 默认 200)
  - `ContentType` (string, 默认 "application/json")
  - `Body` (string, 非流式时使用)
  - `IsStream` (bool)
  - **`StreamChunks` (List<string>)** — 每片是 **SSE 格式字符串**（`"data: {json}\n\n"`），不是 JSON 对象
  - `DelayMs` (int)
- **关键**：`StreamChunks` 是 `List<string>` 不是 `List<object>`，每片必须是 `"data: {...}\n\n"` 格式

### jcc
- **csproj**：`modules/apps/cli/JoinCode.csproj`
- **DLL**：`artifacts/bin/JoinCode/Debug/net10.0/jcc.dll`
- **非交互模式**：`dotnet jcc.dll --non-interactive --prompt "xxx" --format ndjson --trust`
- **环境变量**：
  - `JCC_ENDPOINT=http://127.0.0.1:8091/v1`
  - `OPENAI_API_KEY=test-key`
  - `JCC_DEBUGLOG=1`（启用 Diag.WriteLine 输出到 stderr）
  - `JCC_DISABLE_STREAMING_FALLBACK=1`（禁用流式降级，简化测试）

### 编译注意
- 改 jcc 后：`dotnet build modules/apps/cli/JoinCode.csproj -c Debug`
- 改 MockServer 后：先杀旧进程再编译（`taskkill /F /IM VBCSCompiler.exe` + 杀 dotnet MockServer 进程）
- 改有 `[Register]` 的类后：`--no-incremental` 全量重建

---

## 已测试场景

### S1: 基本文本响应 ✅
- **注入**：流式 text chunk + finish_reason=stop + [DONE]
- **期望**：system/init → assistant(text block) → result
- **结果**：通过
- **日期**：2026-11-05

### S2: 工具调用（单工具）✅ → 🔴 → ✅
- **注入**：
  1. chunk 1: tool_calls delta (id="call_test123", function.name="bash")
  2. chunk 2: tool_calls delta (function.arguments='{"command":"echo hello"}')
  3. chunk 3: finish_reason="tool_calls"
  4. chunk 4: usage chunk (choices=[])
  5. [DONE]
  6. 第二轮：text "Done" + finish_reason=stop + [DONE]
- **期望**：system/init → assistant(tool_use block: id="call_test123", name="bash", input='{"command":"echo hello"}') → user(tool_result block: tool_use_id="call_test123") → assistant(text "Done") → result
- **初次结果**：🔴 tool_use 的 `id` 是空字符串，`input` 是 `"{}"`；tool_result 的 `tool_use_id` 也是空字符串
- **根因**：`QueryEngine.cs:287-291` 生成 `ToolCallStart` chunk 时遗漏 `ToolCallId` 和 `ToolArguments` 字段；`QueryEngine.cs:298-305` 生成 `ToolCallEnd` chunk 时遗漏 `ToolCallId`
- **修复**：commit 598b90683
- **修复后结果**：✅ 所有字段正确传递
- **日期**：2026-11-05

### S3: 多工具调用（并行工具）✅ 但有格式差异
- **注入**：两个 tool_calls（id="call_bash_001"/name="bash"/args='{"command":"echo hello"}' + id="call_read_002"/name="read"/args='{"file_path":"test.txt"}'）+ finish_reason="tool_calls" + 跟进文本
- **期望**：两个 tool_use block 都正确输出，id/input 各自正确
- **结果**：✅ id/input 都正确传递
  - tool_use 1: id='call_bash_001', name='bash', input='{"command":"echo hello"}' ✅
  - tool_result 1: tool_use_id='call_bash_001' ✅
  - tool_use 2: id='call_read_002', name='read', input='{"file_path":"test.txt"}' ✅
  - tool_result 2: tool_use_id='call_read_002' ✅
- **发现的细节差异（待确认是否 bug）**：
  1. **串行 vs 并行**：jcc 把两个 tool_use 放在不同 assistant 消息中（串行执行），基准 ClaudeCode 可能在同一 assistant 消息中（并行执行）。需确认基准侧行为。
  2. **--trust 工具差异**：bash 被拒绝（"用户拒绝工具执行"），read 实际执行了（"File does not exist"）。说明 --trust 对不同工具有不同权限行为，read 可能是自动允许的工具。
  3. **num_turns=0** → ✅ Bug D 已修复（commit 85cbc9bd0）— 现在正确返回 num_turns=2
- **日期**：2026-11-05

---

## 已发现并修复的 Bug

### Bug A: QueryEngine ToolCallStart/End chunk 遗漏 ToolCallId/ToolArguments
- **位置**：`modules/cognition/brain/query/query2/token_budget/core/QueryEngine.cs:287-305`
- **根因**：jcc 非交互模式走 `QueryEngine` 路径（不走 `LLMInvocationHandler`/`QueryLoopMiddleware`）。`QueryEngine` 生成 `AgentStreamChunk` 时只设了 `ToolName`/`ToolCallNumber`，遗漏 `ToolCallId`/`ToolArguments`
- **对比**：`QueryLoopMiddleware.cs:301` 正确传递了 `toolCall.Id, toolCall.Arguments`，但非交互模式不走该路径
- **修复**：添加 `ToolCallId = toolCall.ToolCallId, ToolArguments = toolCall.RawArguments`（ToolCallStart）+ `ToolCallId = toolCall.ToolCallId`（ToolCallEnd）
- **commit**：598b90683
- **教训**：两条并行链路（QueryEngine vs QueryLoopMiddleware）各自生成事件，修改一条需检查另一条是否同步

### Bug B: QueryEngine 未区分 thinking/reasoning_content 与普通 text
- **位置**：`modules/cognition/brain/query/query2/token_budget/core/QueryEngine.cs:493-500`
- **根因**：`QueryEngine` 不检查 `reasoning_content` metadata，所有 content 都生成 `AgentStreamChunkType.Content`，导致 thinking 内容混入 text block
- **修复**：检查 `streamChunk.Metadata["reasoning_content"]` 或 `["thinking_content"]`，thinking 内容生成 `AgentStreamChunkType.Thinking` 且不追加到 `responseBuilder`
- **commit**：975fe604b

### Bug C: QueryEngine 空工具参数输出空字符串而非 {}
- **位置**：`modules/cognition/brain/query/query2/token_budget/core/QueryEngine.cs:477`
- **根因**：解析 AllToolCalls 时 `arguments = argsProp.GetString() ?? "{}"` 只处理 null，不处理空字符串
- **修复**：添加 `if (string.IsNullOrWhiteSpace(arguments)) arguments = "{}";`
- **commit**：8e1db496c

### Bug D: num_turns 始终为 0 — NumTurns 从未赋值
- **位置**：跨层 — `QueryEngine` → `AgentBase` → `SessionController` → `CliSession` → `StartupContext` → `NonInteractiveModeRunner` → `SdkResultMessage`
- **根因**：`SdkResultMessage.NumTurns`（`SdkMessage.cs:147`）从未被赋值。`NonInteractiveModeRunner` 构造 `SdkResultMessage` 时未设置 `NumTurns`，使用默认值 0。`QueryEngine` 核心循环的 `while` 迭代次数（= LLM 调用轮次）从未传递出来
- **修复**：跨层传递 `TotalTurns`：
  1. `QueryStreamChunk` 添加 `TotalTurns` 字段
  2. `AgentStreamChunk` 添加 `TotalTurns` 字段
  3. `QueryEngine.ExecuteCoreLoopAsync` 中 `totalTurns++` 计数 `while` 迭代
  4. `QueryEngine.BuildCompletionChunk` 接收并设置 `totalTurns`
  5. `AgentBase.ExecuteStreamAsync` 转换中传递 `TotalTurns = chunk.TotalTurns`
  6. `SessionController` chunk 循环中读取 `chunk.TotalTurns` 设置 `_totalTurns`
  7. `CliSession.TotalTurns` → `StartupContext.NumTurns` → `NonInteractiveModeRunner` 构造 `SdkResultMessage` 时设置 `NumTurns = context.NumTurns`
- **验证**：单工具调用场景 `num_turns=2`（工具调用轮 + 最终文本轮），`BuildCompletionChunk` 5 个单元测试通过
- **commit**：85cbc9bd0

### Bug E: result 事件缺少 usage 字段 — SdkResultMessage.Usage 从未赋值
- **位置**：跨层 — `SessionController` → `CliSession` → `StartupContext` → `NonInteractiveModeRunner` → `SdkResultMessage`
- **根因**：`NonInteractiveModeRunner` 构造 `SdkResultMessage` 时未设置 `Usage`，始终为 null。`SessionController._lastDoneUsage` 存储了 Done 事件的 usage 但未传递到输出
- **修复**：`SessionController.LastUsage` → `CliSession.LastUsage` → `StartupContext.Usage` → `SdkResultMessage.Usage`，`TokenUsage2` → `SdkUsage` 字段映射（PromptTokens→InputTokens, CompletionTokens→OutputTokens）
- **验证**：S8 测试 input_tokens=42 output_tokens=7 正确传递
- **commit**：ff685a218

### S3 格式修复: 多 tool_use 合并到同一 assistant 消息
- **位置**：`modules/apps/cli/adapters/CliEventConsumer.cs`
- **根因**：`OnToolEnd` 的 `FlushAssistant()` 提前 flush，导致每个 tool_use 拆成独立 assistant 消息。基准侧所有 tool_use 在同一 assistant 消息（`toolOrchestration.ts`）
- **修复**：引入 `_pendingToolResults` 列表，`OnToolStart`/`OnToolEnd` 不再提前 flush，在非工具事件时统一 `FlushPendingToolCalls` 输出 `assistant[tool_use...]` + `user[tool_result...]`
- **验证**：S3 双工具调用 — 2 tool_use 在同一 assistant 消息，2 tool_result 在同一 user 消息
- **commit**：d51643ce1

### Bug F: NDJSON session_id 与 transcript 目录名不一致 — CliEventConsumer 自生成 UUID
- **位置**：`modules/apps/cli/adapters/CliEventConsumer.cs:204`
- **根因**：`CliEventConsumer.EnsureSystemInit()` 行 204 `_sessionId ??= Guid.NewGuid().ToString("N")` 自生成 UUID（如 `02c5959db375433992e26ca52a6e4ed2`），与 `SessionIdGenerator.Generate()` 格式（如 `20261105-1327-w1-w1-parent-2`，transcript 目录名）不一致。`-c <session_id>` 恢复会话时用 NDJSON 输出的 UUID，找不到 transcript 目录
- **修复**：`CliEventConsumer(bool agentMode, string? sessionId = null)` 构造函数添加 `sessionId` 参数，`CliSession` 构造 `CliEventConsumer` 时传入 `_sessionId`
- **验证**：S9 端到端 — NDJSON 输出 `session_id=20261105-1327-w1-w1-parent-2`，transcript 目录名 `20261105-1327-w1-w1-parent-2`，一致 ✅
- **commit**：d3c769e3f

---

## 已发现但未修复的问题

### P1: NDJSON 格式对齐（已实现，PR #592）
- **内容**：CliEventConsumer 重写为 assistant/user 消息流 + system/init + uuid/session_id + result 扩展
- **commits**：ea739cff2, 27fb3e197
- **PR**：#592（已创建并启用 auto-merge）
- **状态**：等待 CI + 合并

### P2: CI FrameworkReference 传递（已修复）
- **commit**：50dffc502
- **内容**：E2E 测试移除 MockServer.Core 引用，消除 FrameworkReference 传递

### P3: -c 恢复会话后新 session transcript 为空（待调查）
- **现象**：`-c <session_id>` 恢复会话成功（`SessionResume: restored ... with 2 messages`），assistant 响应正确，但新 session 目录的 `transcript.jsonl` 为 0 字节
- **可能根因**：`PostProcessMainAgentAsync` 中 `snapshot.Count <= startCount` 提前返回（恢复的消息已在 contextManager 中，`startCount` 包含恢复的消息，新消息可能未通过 contextManager 添加）
- **优先级**：P2 — 不影响功能（会话恢复成功，响应正确），仅 transcript 持久化缺失

---

## 待测试场景清单

### 高优先级（小细节易出 bug）

- [x] **S3: 多工具调用** ✅ — id/input 都正确，S3 格式差异已修复（commit d51643ce1）— 多 tool_use 合并到同一 assistant 消息，多 tool_result 合并到同一 user 消息

- [x] **S4: thinking/reasoning content** ✅ Bug B 已修复（commit 975fe604b）— thinking 现在正确分离为 thinking block，不混入 text

- [x] **S5: 工具调用参数为空** ✅ Bug C 已修复（commit 8e1db496c）— 空参数现在输出 `{}` 而非空字符串
  - **深入研究发现**：
    1. **`--trust` 语义** — 只是"自动信任工作目录"，**不跳过权限检查**！要跳过权限需 `--permission-mode bypass`。之前所有测试用 `--trust` 导致 bash 被拒绝。
    2. **参数校验先于权限检查** — 空参数 bash 在参数校验阶段失败（"缺少必需参数"），未到达权限检查。有参数 bash 通过参数校验，在 default mode 被权限拒绝（"用户拒绝"），在 bypass mode 被允许执行。
    3. **Bug D: `num_turns: 0`** → ✅ 已修复（commit 85cbc9bd0）— 跨层传递 LLM 调用轮次数从 QueryEngine 到 SdkResultMessage，现在正确返回 num_turns=2

- [x] **S6: 工具调用 id 为空** ✅ — jcc 正确生成 fallback id（call_{Guid:N}[:20]），tool_use_id 匹配

- [x] **S7: 长文本流式合并** ✅ — 12 个 text delta chunk 合并为单个 text block，内容完整（MergeAdjacentBlocks 逻辑正确）

- [x] **S8: usage 统计传递** ✅ Bug E 已修复（commit ff685a218）— result 事件现在含 usage 字段（input_tokens/output_tokens/cache_creation_input_tokens/cache_read_input_tokens）

### 中优先级

- [ ] **S9: 多轮对话（-c）** — 会话恢复 + 历史消息
  - 需要真实 transcript 文件
  - 检查：恢复后 messages 数组含历史 assistant 响应
  - ✅ **已修复** — Bug F: NDJSON session_id 与 transcript 目录名不一致（commit d3c769e3f）

- [x] **S10: 错误处理** ✅ — 401 正确传递（is_error=true, errors 含 HTTP 401 消息）；429/500/503 因 MockServer 注入响应被消费后重试拿到默认 200 空响应显示 success（测试环境限制非 jcc bug）

- [x] **S11: 空响应体** ✅ — 正确识别为错误，显示"模型返回空回复（无文本/思考/工具产出）。可能为请求过大超时或供应商响应异常，请重试或切换模型/供应商。"

- [x] **S12: 部分 SSE 流** ✅ — 无 [DONE] 终止符的 SSE 流，已接收内容 "Hello partial" 正确输出到 assistant 消息

### 低优先级

- [ ] **S13: 子代理事件** — agent_started/agent_ended
  - 检查：role/agentType 字段
  - 已在之前会话修复（commit c5d1f1560）

- [ ] **S14: 工具进度事件** — tool_progress
  - 检查：进度消息正确传递

---

## 关键链路图（非交互模式）

```
jcc --non-interactive --prompt "xxx"
  → NonInteractiveModeRunner
    → SessionController
      → QueryEngine.ExecuteCoreLoopAsync
        → chatCompletionService.GetStreamEventContentsAsync  [OpenAIQueryService]
          → 解析 SSE chunks → 累积 tool_calls → yield StreamEvent (含 Metadata["AllToolCalls"])
        → QueryEngine 解析 AllToolCalls → pendingToolCalls
        → 生成 AgentStreamChunk (ToolCallStart/End)  ← Bug A 修复点
        → AgentStreamChunkAdapter.ToChatStreamEvent → ChatStreamEvent
      → SessionController.ProcessEvent → IEventConsumer.OnToolStart/OnToolEnd
        → CliEventConsumer.OnToolStart → NDJSON 输出
```

**注意**：非交互模式**不走** `LLMInvocationHandler`/`ChatStreamChunkProcessor`/`QueryLoopMiddleware`！
交互模式走 `QueryLoopMiddleware` → `LLMInvocationHandler` → `ChatStreamChunkProcessor`。

---

## 调试日志标记速查

| 标记 | 位置 | 用途 |
|------|------|------|
| `[WIRE]` | OpenAIQueryService.cs | AllToolCalls 生成确认 |
| `[CHUNK]` | ChatStreamChunkProcessor.cs | ParseAllToolCalls 调用确认（仅交互模式） |
| `[CLI]` | CliEventConsumer.cs | OnToolStart 收到的参数 |
| `[LLM-HANDLER]` | LLMInvocationHandler.cs | chunk Metadata 键列表（仅交互模式） |
| `[LLM {callId}]` | LLMInvocationHandler.cs:103 | LLM 调用结果摘要 |
| `[LOOP {callId}]` | QueryLoopMiddleware.cs | 纯文本/工具调用判定 |
| `[TOOL {callId}]` | QueryLoopMiddleware.cs:321 | 工具执行结果 |

---

## 教训记录

1. **两条并行链路**：QueryEngine（非交互）vs QueryLoopMiddleware（交互），修改一条必须检查另一条
2. **小细节是 bug 源源**：ToolCallId/ToolArguments 遗漏是"小"问题但导致 id/input 空值
3. **StreamChunks 是 List<string>**：MockServer 注入的 stream_chunks 必须是 SSE 格式字符串（`"data: {json}\n\n"`），不是 JSON 对象
4. **[LLM-HANDLER] 不出现是关键线索**：说明非交互模式不走 LLMInvocationHandler，走 QueryEngine
