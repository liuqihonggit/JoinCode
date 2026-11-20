# TASK103 — ReAct 循环合并：两套归一

- 日期：2026-11-21
- 来源：TASK102 用户决策"一套规则 + 合并立项（推荐）"
- 关联 ADR：[0159](../adr/0159-round-gate-turn-termination.md)

## 现状（证据）

| 循环 | 位置 | 消费方 |
|------|------|--------|
| `QueryEngine` | `modules/cognition/brain.query/query2/token_budget/core/QueryEngine.cs` | 主代理（`EngineAgentFactory`）、子代理、工作流、`-p` |
| `QueryLoopMiddleware` | `modules/cognition/brain.context_services/chat/core/QueryLoopMiddleware.cs` | `ChatService.StreamWithEventsAsync`：GUI/TUI 回退路径（`SessionController` 无主代理时）、slash 命令（如 `AskClarifyCommand`）、Bridge `MessageHandlers` |

两套循环都实现"无工具调用 → 收尾"，并各自维护：

- 空响应干预（QueryEngine `IEmptyResponseTracker` / QueryLoop `IEmptyResponseTracker`）
- 循环检测（QueryLoop `InformationEntropyGuardian`；QueryEngine 无）
- Stop hook 回灌（QueryEngine `StopHookBlockingMessages`；QueryLoop 无）
- 轮次闸门（TASK102 已抽为 `RoundGatePolicy` 单源）

## 能力矩阵

| 能力 | QueryEngine | QueryLoopMiddleware |
|------|-------------|---------------------|
| 流式工具执行（并发分类） | ❌ | ✅ `StreamingToolExecutor` / Actor 版 |
| 轮内 steer 注入 | ❌ | ✅ `ISteerInputMailbox` |
| 子代理事件通道 | ❌（回合级在 SessionController） | ✅ `SubAgentEventChannel` |
| LLM 输出循环检测 + 三级干预 | ❌ | ✅ `InformationEntropyGuardian` + `LoopInterventionMiddleware` |
| 空响应/异常输出干预 | ✅（空白响应 + XML fallback，S6） | ✅（空响应 + XML 标签输出） |
| Stop hook 阻断回灌 | ✅ | ❌ |
| 结构化输出合成工具 | ✅ | ❌ |
| 迭代重试/退避 | ✅ | ❌（交给外层） |
| Token/USD 预算中间件 | ✅（query-pipeline） | ✅（chat-pipeline） |
| 内容替换/历史裁剪 | ✅ | ❌（chat 侧由 compact 管线承担） |
| 上下文预算守卫（ADR 0156） | ✅（S8 请求前守卫） | ✅（GuardContextBudget） |
| 轮次闸门 | ✅ | ✅（TASK102 共享策略） |
| 事件协议 | `QueryStreamChunk` | `ChatStreamEvent` |

结论：两边各有独有能力，**不能直接删任何一边**。合并方向应为 **ChatService 收敛到 QueryEngine**（query2 是 TS 对齐目标），把 ChatService 循环的独有能力迁入 QueryEngine 后再下线旧循环。

## 迁移步骤（建议）

1. **规则单源冻结（已完成）**：闸门（`RoundGatePolicy`）、循环上限（`LoopLimits`：`JCC_MAX_TOOL_CALL_ITERATIONS` / `JCC_MAX_STOP_HOOK_REPLY_ROUNDS`）已抽为共享单源；新规则禁止写进任一循环。
   - 已完成证据：TASK102 第二轮，两套循环共同消费，单测覆盖默认/覆盖/非法回退。
2. **事件适配层（已完成）**：`QueryStreamChunkAdapter`（`modules/abstractions/abstractions.interfaces/interfaces/llm/QueryStreamChunkAdapter.cs`）覆盖全部 chunk 类型；`JCC_CHAT_LOOP_BACKEND=legacy|query` 开关（`ChatLoopBackendPolicy`，2026-11-21 已翻转默认 query，legacy 保留为逃生）已落地。
   - 映射决策：`Error → SystemNotice`（失败原因上屏，不静默丢弃）；`ThinkingEnd` 无对应事件返回 null；`Complete → Done` 透传 usage/model/stopReason。
   - 单测：`QueryStreamChunkAdapterTests`（全类型映射）+ `ChatLoopBackendPolicyTests`（默认值/别名/非法回退）。
3. **能力迁移（部分完成）**：
   a. 流式工具执行器迁入 QueryEngine（或作为 QueryEngine 中间件）— ❌ 未迁（`JCC_ACTOR_TOOL_EXECUTOR` 仍仅 chat legacy 使用）。
   b. steer 邮箱与子代理事件通道迁入 QueryEngine — 🟡 子代理事件已由适配层合流；steer 仅步首注入（循环内 drain 待补）。
   c. 循环检测 + 三级干预（软提示/硬撤回重连/压缩）迁入 QueryEngine 中间件 — 🟡 检测已在适配层复现，三级干预仍由 chat 管线 `LoopInterventionMiddleware` 消费。
4. **默认切换（已落地，默认 query）**：`ChatService.StreamWithEventsAsync` 默认路由到 `IQueryEngine`，`JCC_CHAT_LOOP_BACKEND=legacy` 显式回旧循环；GUI 同源会话 E2E（流式 + 子代理事件）在默认/legacy 双模式通过；native_gui 真机人工验收 ○ 待补。
5. **下线旧循环**：全部宿主迁移且 E2E 绿后，`QueryLoopMiddleware` 与 `chat-pipeline` 中对应节点移入 `.xxx/` 归档，删除相关测试或改为 QueryEngine 等价测试。
6. **统一终止策略对象**：把闸门/空响应/循环检测/Stop hook 的优先级与计数收敛到一个 `TurnTerminationPolicy`，两阶段迁移期由新旧循环共同消费。

## S4 灰度接线（已实现，默认关闭）

- `QueryLoopMiddleware` 注入可选 `IQueryEngine`；默认整轮循环委托 QueryEngine（`JCC_CHAT_LOOP_BACKEND=legacy` 回旧循环），dry-run 预览仍走旧循环。
- 历史去重：ChatPreprocessor 已落库当前用户消息 → 快照剥离末尾同内容 user 消息，QueryEngine 重新加入；结束后按角色把增量回写 `IChatContextManager`（跳过重复 user）。
- 事件：`QueryStreamChunkAdapter` 全类型映射；Complete 截留为最终 Done；Error → SystemNotice。
- 流式修复：QueryEngine 原先把整轮 Content 聚合后才入队（GUI 首末分片时间差≈0）；现改为收到即入 `OutputChunks`，并以 `HasEmittedStreamContent` 禁止"部分输出后整轮重试"造成重复文本。
- 子代理事件：回合级 `SubAgentEventChannel` 作用域 + 50ms 轮询合流（对齐旧循环）。
- 执行设置：`QueryOptions.ExecutionOverrides` 透传 ChatOptionsFactory 产物（温度/思考/延迟工具/PromptCacheKey 等）；`ApplyExecutionOverrides` / `CloneExecutionSettings` 保证 model/effort 覆盖不丢其余字段。
- 循环检测：工具调用序列 + 纯文本指纹在适配层复现，下游 `LoopInterventionMiddleware` 继续消费 LoopDetected。
- 轮内 steer（S5）：`QueryOptions.OnIterationStart` 在每次 LLM 请求前把邮箱内输入写入实时历史并排队 `SteerInput` 事件；`HasPendingIterationInput` 在收尾前发现待注入时继续一轮。
- 单测：`QueryLoopQueryBackendTests`（4）+ `QueryExecutionOverridesTests`（2）+ `QueryIterationInputTests`（2）。

## S6 XML fallback 迁移（已实现）

- 抽象层新增 `TextToolCallFallback.cs`（`ITextToolCallFallbackParser` + 调用/结果 DTO）；`TextToolCallParserChain`（chat 域）双 `[Register]` 暴露接口实现，query 域不反向引用 chat。
- 共享干预文案 `OutputInterventionPrompts`（abstractions.utils）：可疑 XML 标签检测 + 纠正提示词 + 上限 3，chat 循环（`QueryLoopMiddleware` / `MalformedOutputTracker`）与 QueryEngine 同源消费。
- QueryEngine 无工具调用分支新增 `ApplyTextToolCallFallback`：协议层 0 调用 → 文本解析（成功则替换为剩余纯文本并执行工具）；解析失败且含可疑标签 → 注入 `<system-reminder>` 纠正提示并 `RequestsAnotherTurn` 再来一轮；超 3 次上屏警告放行收尾。
- 工具路径 assistant 消息保留工具块外纯文本（`AddToolResultToHistory(..., assistantContent)`），对齐 chat 循环。
- 单测：`QueryEngineTextFallbackTests`（3）覆盖解析成功执行/纠正回灌/超限强制结束。

## S7 取消语义对齐（已实现）

- `ExecuteToolAsync` 不再把取消吞成"工具调用失败"错误结果：`OperationCanceledException` 且令牌已取消 → 原样上抛。
- 工具执行循环改为下标遍历，`ExecuteToolAsync` / ToolCallEnd / `AddToolResultToHistory` 包在 `when (cancellationToken.IsCancellationRequested)` 捕获中：取消时 `WriteAbortedToolResults` 为当前及剩余调用补写 `assistant + "(aborted)"` 结果后重抛（对齐 chat 循环 `ToolExecutionHandler.WriteAbortedToolResultsAsync`）。
- 单测：`QueryEngineCancellationTests`（1）断言取消后历史含 2 条 aborted 结果且每条前有 assistant 工具调用消息。

## S8 请求前上下文预算守卫（已实现）

- 新增 `PreRequestBudgetGuardMiddleware`（query 域 `IQueryMiddleware`）：`BeforeIterationHooks` 每轮 LLM 请求前用 ADR 0156 内容类型感知估算（CJK 1.5 字/token、非 CJK 4 字/token、每条消息 +4）对比 `IChatContextManager.GetContextMaxTokens()`。
- 分级处理：≥0.7 预防性裁剪到 65% 窗口；≥1.0 紧急裁剪到 50%；依赖缺失（无 `IHistorySnipService`/`IChatContextManager`）时不注册钩子。
- 补位关系：`HistorySnipMiddleware` 只在工具调用后触发，单轮内工具输出突增会漏检；本中间件在请求前兜底（对齐 chat 循环 `GuardContextBudget`）。
- 单测：`PreRequestBudgetGuardTests`（4）覆盖阈值以下不裁剪/预防性/紧急/依赖缺失。

## 默认翻转（2026-11-21，已落地）

- `ChatLoopBackendPolicy.Read()` 未设置/非法值 → `Query`；`JCC_CHAT_LOOP_BACKEND=legacy` 为逃生开关，dry-run 预览仍走 legacy。
- 翻转依据：S6/S7/S8 已关闭 query 后端能力差距；GUI 回退与 slash 共用 `ChatService.StreamWithEventsAsync` 路径且默认模式 E2E 通过；bridge `mainAgent` 路径不经 ChatService，不受开关影响。
- 未覆盖项：slash 独立宿主 E2E（宿主 `SkipProviderConfig=true`）与 native_gui 真机人工验收 ○ 待用户。

### 四列验收表

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| `QueryStreamChunkAdapter`（abstractions.interfaces） | `QueryLoopMiddleware.RunQueryBackendAsync` + `AgentStreamChunkAdapter` 同级协议桥 | ✅ | ❌ 未走 ADR 0080 真机 exe；已有 MockServer E2E（query/legacy 双模式通过） |
| `ChatLoopBackendPolicy` + `JCC_CHAT_LOOP_BACKEND` | `QueryLoopMiddleware.InvokeAsync` 分支（默认 query，legacy 逃生） | ✅ | ❌ 未走 ADR 0080 真机 exe；同源 GUI 会话 E2E 已验证默认模式与 legacy 模式均生效 |
| 历史去重与增量回写 | `BuildQueryHistoryAsync` / `PersistQueryDeltaQuietlyAsync` | ✅ | ❌ 待多轮真机会话核对历史条数与顺序 |
| 轮内 steer 注入 | `QueryOptions.OnIterationStart` / `HasPendingIterationInput` + `DrainSteerIntoQueryHistoryAsync` | ✅ | ❌ 未走 ADR 0080 真机 exe；单测覆盖步首注入/事件/落库与收尾继续 |
| 子代理事件合流 | `ConsumeQueryBackendAsync` + `SubAgentEventChannel` | ✅ | ❌ 未走 ADR 0080 真机 exe；`SubAgentEventStreamE2ETests` query 2/2 通过 |
| 闸门/执行设置透传 | `BuildQueryOptions` + `ApplyExecutionOverrides` | ✅ | ❌ 未走 ADR 0080 真机 exe；单测已覆盖设置透传/model 覆盖不丢字段 |
| 流式增量输出 | `QueryEngine.ExecuteIterationInternalAsync` 即收即入队 | ✅ | ❌ 未走 ADR 0080 真机 exe；`StreamingIncrementalE2ETests` query 跨度断言通过 + legacy 回归通过 |
| 文本工具调用 fallback | `ITextToolCallFallbackParser`（abstractions.interfaces）+ `TextToolCallParserChain` 实现 + `QueryEngine.ApplyTextToolCallFallback` | ✅ | ❌ 未走 ADR 0080 真机 exe；`QueryEngineTextFallbackTests` 3 通过 + 两后端 MockServer E2E 回归通过 |
| 异常输出干预同源 | `OutputInterventionPrompts` 被 `MalformedOutputTracker` / `QueryLoopMiddleware` / QueryEngine 共同消费 | ✅ | ❌ 未走 ADR 0080 真机 exe；单测覆盖纠正回灌与超限放行 |
| 取消语义对齐 | `QueryEngine.ExecuteToolAsync` 取消上抛 + `WriteAbortedToolResults` | ✅ | ❌ 未走 ADR 0080 真机 exe；`QueryEngineCancellationTests` 通过 + query 模式 E2E 回归通过 |
| 请求前预算守卫 | `PreRequestBudgetGuardMiddleware`（BeforeIterationHooks + IHistorySnipService + IChatContextManager） | ✅ | ❌ 未走 ADR 0080 真机 exe；`PreRequestBudgetGuardTests` 4 通过 + query 模式 E2E 回归通过 |

### 验证记录（2026-11-21）

| 验证项 | 结果 |
|--------|------|
| `Brain.Context.Tests` 全量 | 976 通过（含 S5 后新增用例，最新全量见提交记录） |
| `Brain.Other.Tests` 全量 | 499 通过（流式改造后 Query 子集 230 通过） |
| steer 专项 | `QueryLoopQueryBackendTests` 4 通过 + `QueryIterationInputTests` 2 通过 |
| `StreamingIncrementalE2ETests`（MockServer + GUI 同源会话） | query 模式通过；legacy 模式通过 |
| `SubAgentEventStreamE2ETests` | query 模式 2/2 通过；legacy 模式通过 |
| `jcc slash_call ask` 真机尝试 | ❌ 工具元命令宿主 `SkipProviderConfig=true`，不加载供应商配置，无法连接 MockServer；不构成验收证据 |
| S6 后 `Brain.Other.Tests` 全量 | 504 通过（含 `QueryEngineTextFallbackTests` 3） |
| S6 后 `Brain.Context.Tests` 全量 | 977 通过 |
| S6 后 MockServer E2E（query + legacy 双模式） | `StreamingIncrementalE2ETests` + `SubAgentEventStreamE2ETests` 3/3 通过 ×2 |
| S6 后全仓编译 | `Build-AllDomains.ps1` 全部通过（solution/native_tui/native_gui） |
| S7 后 `Brain.Other.Tests` 全量 | 505 通过（含 `QueryEngineCancellationTests`） |
| S7 后 MockServer E2E（query 模式） | `StreamingIncrementalE2ETests` + `SubAgentEventStreamE2ETests` 3/3 通过（取消路径未破坏泵收尾） |
| S8 后 `Brain.Other.Tests` 全量 | 509 通过（含 `PreRequestBudgetGuardTests` 4） |
| S8 后 MockServer E2E（query 模式） | 3/3 通过（守卫钩子默认不触发，聊天行为无回归） |
| S8 后全仓编译 | `Build-AllDomains.ps1` 全部通过 |
| 默认翻转后 `Brain.Other.Tests` 全量 | 509 通过（`ChatLoopBackendPolicyTests` 默认断言改为 query） |
| 默认翻转后 `Brain.Context.Tests` 全量 | 977 通过 |
| 默认翻转后 MockServer E2E（默认模式） | 3/3 通过（未设置环境变量即 query 后端） |
| 默认翻转后 MockServer E2E（legacy 逃生） | 3/3 通过（`JCC_CHAT_LOOP_BACKEND=legacy`） |

### 已知差距（翻转默认前必须补）

1. ~~可疑 XML 标签 fallback 解析仍为 chat-only~~ → 已解决（S6：接口下沉 abstractions，QueryEngine 经 DI 消费同一解析链）。
2. ~~上下文预算守卫（ADR 0156 请求前裁剪）为 chat-only~~ → 已解决（S8：query 中间件每轮请求前守卫，按 0.7/1.0 分级裁剪）。
3. 流式工具执行（`UseStreamingToolExecution`，默认 false）为 chat-only — 接受为 opt-in 差异：query 后端按非流式执行（与 chat 默认配置一致），启用该设置且走 query 后端时输出 `[LOOP-QUERY]` 诊断说明；迁移并发调度器风险高于收益，不阻塞默认切换。
4. ~~取消语义差异~~ → 已解决（S7：取消上抛 + `(aborted)` 补写，历史合法性对齐 legacy）。
5. 宿主验收：GUI 同源会话已由 MockServer E2E 在默认（query）后端通过；slash 与 GUI 回退共用同一 `ChatService.StreamWithEventsAsync` 路径（slash 元命令宿主 `SkipProviderConfig=true` 无法连 MockServer，缺独立 E2E）；bridge `mainAgent` 路径本就绕过 ChatService（`SessionController.cs:210`），不受开关影响；native_gui exe 人工验收 ○ 待用户执行。

## 验收标准

- GUI 回退路径、slash 命令、Bridge 三类宿主的 E2E 场景全部通过，事件序列与旧循环一致（工具开始/结束、文本、Complete）。
- `QueryLoopMiddleware` 删除后，`chat-pipeline` 仍保留非循环类节点（审计/转录/保存上下文），或这些节点迁入 QueryEngine 管线。
- 全仓编译 + 相关域测试全绿；真机 `jcc` 交互与 `-p` 行为一致。

## 风险

- 事件协议差异导致 GUI 渲染回归（需事件适配层 + 快照对比）。
- 流式工具执行迁移影响并发调度与取消语义（需保留现有并发测试）。
- slash 命令对延迟敏感（合并后需实测首 token 延迟）。

## 排除项

- 不在合并任务中引入新功能；闸门等新能力已按共享策略落地。
- 不改变 TS 对齐方向（query2 为目标架构）。

## native_gui 真机验收清单（ADR 0080，默认 query 后端）

> 目的：默认后端已切到 QueryEngine（`JCC_CHAT_LOOP_BACKEND` 未设置 = query），验证真实 exe 下 GUI 回退路径的流式、工具、子代理、取消与逃生开关。
> 全部为真实进程运行，禁止用单元测试替代；每项验收后在下表勾选并记录异常。

### 前置准备

1. 编译（Debug，不做 AOT 发布）：
   ```
   dotnet build native_gui/apps/NativeGui.Host/NativeGui.Host.csproj -c Debug
   dotnet build test/mock/mock_server.e2e.tests/OpenAI.MockServer 2>/dev/null || dotnet build -t:OpenAI.MockServer
   ```
   MockServer exe 定位：`artifacts/bin/OpenAI.MockServer/**/JoinCode.OpenAI.MockServer.exe`（优先 Debug）。
2. 准备 MockServer 脚本配置（`.xxx/mockserver_gui.json`，长文本按 word 分片≈20ms/片）：
   ```json
   {
     "port": 0,
     "default_response": "(script exhausted)",
     "scripted_turns": [
       { "thinking_content": null, "tool_calls": null, "text_response": "word1 word2 ... word50", "follow_up_text": null, "http_status_code": null }
     ]
   }
   ```
3. 启动 MockServer 并记录就绪行端口：
   ```
   artifacts/bin/OpenAI.MockServer/<config>/JoinCode.OpenAI.MockServer.exe --config .xxx/mockserver_gui.json --port 0 --no-open-browser
   ```
   就绪标志：`[OpenAI]   URL: http://localhost:<port>/`
4. 以环境变量启动 GUI（cmd/PowerShell 均可；**不要**设置 `JCC_CHAT_LOOP_BACKEND`，让默认值生效）：
   ```
   set JCC_ENDPOINT=http://localhost:<port>
   set OPENAI_API_KEY=sk-test-gui-accept
   set JCC_VENDOR=openai
   set JCC_MODEL_ID=gpt-4o
   set JCC_PERMISSION_MODE=bypass
   set JCC_DEBUGLOG=1
   artifacts/bin/NativeGui.Host/Debug/net10.0-windows10.0.26100.0/NativeGui.Host.exe
   ```

### 验收场景

| # | 场景 | 操作 | 期望 | 观察点 |
|---|------|------|------|--------|
| 1 | 流式增量 | 发送"给我讲一个很长的故事" | 文本逐段上屏（非整块一次性出现），结束后有 Done | 首 token 延迟、末 token 时间；日志 `[LOOP-QUERY]` |
| 2 | 工具调用 | 发送"用 Bash 执行 echo hello" | 工具开始/结束事件、结果回填、模型继续收尾 | `ToolCallStart/ToolCallEnd`、无重复文本 |
| 3 | 子代理事件 | 发送需要 fork 子代理的任务 | 子代理进度事件实时显现 | `SubAgentEventChannel` 50ms 合并无丢失 |
| 4 | 取消（Esc/中断） | 长工具执行中打断 | 回合停止，无崩溃；历史保持合法（可继续下一轮对话） | 日志无未捕获异常；下一轮请求不报 tool_calls 无结果 |
| 5 | XML fallback 干预 | MockServer 脚本返回含 `<antml:invoke ...>` 的文本 | 自动解析执行或注入纠正提示后继续；连续 3 次后警告结束 | 日志 `[XML-FALLBACK]` |
| 6 | 预算守卫 | 构造接近窗口的历史（重复长对话/大工具输出） | 请求前自动裁剪，不出现 `prompt_too_long` 崩溃 | 日志 `[BUDGET-GUARD]`（阈值以下不出现属正常） |
| 7 | legacy 逃生 | 关闭 GUI，设置 `JCC_CHAT_LOOP_BACKEND=legacy` 重启 | 与场景 1/2 行为一致（旧循环），验证逃生可用 | 日志无 `[LOOP-QUERY]` |

### 回退与报告

- 任一场景失败：记录场景号 + 日志片段（grep `[LOOP-QUERY]` / `[XML-FALLBACK]` / `[BUDGET-GUARD]` / 异常栈），先用 `JCC_CHAT_LOOP_BACKEND=legacy` 恢复使用并反馈。
- 通过后：本表勾选，作为“已验收”证据回填四列验收表对应行的最后一列。

### 验收记录（○ 待用户填写）

| # | 场景 | 结果（✅/❌） | 备注（日志/耗时/截图路径） |
|---|------|--------------|------------------------------|
| 1 | 流式增量 | ○ | |
| 2 | 工具调用 | ○ | |
| 3 | 子代理事件 | ○ | |
| 4 | 取消 | ○ | |
| 5 | XML fallback 干预 | ○ | |
| 6 | 预算守卫 | ○ | |
| 7 | legacy 逃生 | ○ | |
