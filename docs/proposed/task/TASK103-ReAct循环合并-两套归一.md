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
| 空响应/异常输出干预 | ✅（空白响应） | ✅（空响应 + XML 标签输出） |
| Stop hook 阻断回灌 | ✅ | ❌ |
| 结构化输出合成工具 | ✅ | ❌ |
| 迭代重试/退避 | ✅ | ❌（交给外层） |
| Token/USD 预算中间件 | ✅（query-pipeline） | ✅（chat-pipeline） |
| 内容替换/历史裁剪 | ✅ | ❌（chat 侧由 compact 管线承担） |
| 上下文预算守卫（ADR 0156） | ✅ | ❌ |
| 轮次闸门 | ✅ | ✅（TASK102 共享策略） |
| 事件协议 | `QueryStreamChunk` | `ChatStreamEvent` |

结论：两边各有独有能力，**不能直接删任何一边**。合并方向应为 **ChatService 收敛到 QueryEngine**（query2 是 TS 对齐目标），把 ChatService 循环的独有能力迁入 QueryEngine 后再下线旧循环。

## 迁移步骤（建议）

1. **规则单源冻结（已完成）**：闸门（`RoundGatePolicy`）、循环上限（`LoopLimits`：`JCC_MAX_TOOL_CALL_ITERATIONS` / `JCC_MAX_STOP_HOOK_REPLY_ROUNDS`）已抽为共享单源；新规则禁止写进任一循环。
   - 已完成证据：TASK102 第二轮，两套循环共同消费，单测覆盖默认/覆盖/非法回退。
2. **事件适配层（已完成）**：`QueryStreamChunkAdapter`（`modules/abstractions/abstractions.interfaces/interfaces/llm/QueryStreamChunkAdapter.cs`）覆盖全部 chunk 类型；`JCC_CHAT_LOOP_BACKEND=legacy|query` 灰度开关（`ChatLoopBackendPolicy`，默认 legacy）已落地。
   - 映射决策：`Error → SystemNotice`（失败原因上屏，不静默丢弃）；`ThinkingEnd` 无对应事件返回 null；`Complete → Done` 透传 usage/model/stopReason。
   - 单测：`QueryStreamChunkAdapterTests`（全类型映射）+ `ChatLoopBackendPolicyTests`（默认值/别名/非法回退）。
3. **能力迁移（部分完成）**：
   a. 流式工具执行器迁入 QueryEngine（或作为 QueryEngine 中间件）— ❌ 未迁（`JCC_ACTOR_TOOL_EXECUTOR` 仍仅 chat legacy 使用）。
   b. steer 邮箱与子代理事件通道迁入 QueryEngine — 🟡 子代理事件已由适配层合流；steer 仅步首注入（循环内 drain 待补）。
   c. 循环检测 + 三级干预（软提示/硬撤回重连/压缩）迁入 QueryEngine 中间件 — 🟡 检测已在适配层复现，三级干预仍由 chat 管线 `LoopInterventionMiddleware` 消费。
4. **灰度切换（已落地，默认 legacy）**：`ChatService.StreamWithEventsAsync` 内部在 `JCC_CHAT_LOOP_BACKEND=query` 时路由到 `IQueryEngine`；GUI 同源会话 E2E（流式 + 子代理事件）已在 query/legacy 双模式通过；slash/bridge 宿主与 native_gui 真机人工验收待补。
5. **下线旧循环**：全部宿主迁移且 E2E 绿后，`QueryLoopMiddleware` 与 `chat-pipeline` 中对应节点移入 `.xxx/` 归档，删除相关测试或改为 QueryEngine 等价测试。
6. **统一终止策略对象**：把闸门/空响应/循环检测/Stop hook 的优先级与计数收敛到一个 `TurnTerminationPolicy`，两阶段迁移期由新旧循环共同消费。

## S4 灰度接线（已实现，默认关闭）

- `QueryLoopMiddleware` 注入可选 `IQueryEngine`；`JCC_CHAT_LOOP_BACKEND=query` 时整轮循环委托 QueryEngine，默认 legacy 不变，dry-run 预览仍走旧循环。
- 历史去重：ChatPreprocessor 已落库当前用户消息 → 快照剥离末尾同内容 user 消息，QueryEngine 重新加入；结束后按角色把增量回写 `IChatContextManager`（跳过重复 user）。
- 事件：`QueryStreamChunkAdapter` 全类型映射；Complete 截留为最终 Done；Error → SystemNotice。
- 流式修复：QueryEngine 原先把整轮 Content 聚合后才入队（GUI 首末分片时间差≈0）；现改为收到即入 `OutputChunks`，并以 `HasEmittedStreamContent` 禁止"部分输出后整轮重试"造成重复文本。
- 子代理事件：回合级 `SubAgentEventChannel` 作用域 + 50ms 轮询合流（对齐旧循环）。
- 执行设置：`QueryOptions.ExecutionOverrides` 透传 ChatOptionsFactory 产物（温度/思考/延迟工具/PromptCacheKey 等）；`ApplyExecutionOverrides` / `CloneExecutionSettings` 保证 model/effort 覆盖不丢其余字段。
- 循环检测：工具调用序列 + 纯文本指纹在适配层复现，下游 `LoopInterventionMiddleware` 继续消费 LoopDetected。
- 单测：`QueryLoopQueryBackendTests`（3）+ `QueryExecutionOverridesTests`（2）。

### 四列验收表

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| `QueryStreamChunkAdapter`（abstractions.interfaces） | `QueryLoopMiddleware.RunQueryBackendAsync` + `AgentStreamChunkAdapter` 同级协议桥 | ✅ | ❌ 未走 ADR 0080 真机 exe；已有 MockServer E2E（query/legacy 双模式通过） |
| `ChatLoopBackendPolicy` + `JCC_CHAT_LOOP_BACKEND` | `QueryLoopMiddleware.InvokeAsync` 分支 | ✅ | ❌ 未走 ADR 0080 真机 exe；同源 GUI 会话 E2E 已验证 env=query 生效 |
| 历史去重与增量回写 | `BuildQueryHistoryAsync` / `PersistQueryDeltaQuietlyAsync` | ✅ | ❌ 待多轮真机会话核对历史条数与顺序 |
| 子代理事件合流 | `ConsumeQueryBackendAsync` + `SubAgentEventChannel` | ✅ | ❌ 未走 ADR 0080 真机 exe；`SubAgentEventStreamE2ETests` query 2/2 通过 |
| 闸门/执行设置透传 | `BuildQueryOptions` + `ApplyExecutionOverrides` | ✅ | ❌ 未走 ADR 0080 真机 exe；单测已覆盖设置透传/model 覆盖不丢字段 |
| 流式增量输出 | `QueryEngine.ExecuteIterationInternalAsync` 即收即入队 | ✅ | ❌ 未走 ADR 0080 真机 exe；`StreamingIncrementalE2ETests` query 跨度断言通过 + legacy 回归通过 |

### 验证记录（2026-11-21）

| 验证项 | 结果 |
|--------|------|
| `Brain.Context.Tests` 全量 | 976 通过 |
| `Brain.Other.Tests` 全量 | 499 通过（流式改造后 Query 子集 230 通过） |
| `StreamingIncrementalE2ETests`（MockServer + GUI 同源会话） | query 模式通过；legacy 模式通过 |
| `SubAgentEventStreamE2ETests` | query 模式 2/2 通过；legacy 模式通过 |
| `jcc slash_call ask` 真机尝试 | ❌ 工具元命令宿主 `SkipProviderConfig=true`，不加载供应商配置，无法连接 MockServer；不构成验收证据 |

### 已知差距（翻转默认前必须补）

1. 轮内 steer 仅步首注入；QueryEngine 循环内部无 drain 点（需 BeforeIteration 钩子）。
2. 可疑 XML 标签 fallback 解析仍为 chat-only。
3. 上下文预算守卫（ADR 0156 请求前裁剪）为 chat-only；query 后端依赖 QueryEngine 的 TokenBudget/HistorySnip。
4. 取消语义差异：legacy 取消时为未执行工具补写 `(aborted)` 结果；QueryEngine 取消按自身路径处理，待对齐。
5. 宿主 E2E（GUI/slash/bridge）尚未跑 query 后端，默认值保持 legacy；native_gui exe 人工验收待做。

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
