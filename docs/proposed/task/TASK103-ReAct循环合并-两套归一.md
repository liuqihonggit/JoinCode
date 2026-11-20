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

1. **规则单源冻结（已完成）**：闸门、空响应等"何时允许结束"的规则抽到共享策略；新规则禁止写进任一循环。
2. **事件适配层**：定义 `QueryStreamChunk → ChatStreamEvent` 的稳定适配器（`AgentStreamChunkAdapter` 已有类似实现），确保 GUI/slash 消费方无感。
3. **能力迁移（按依赖顺序）**：
   a. 流式工具执行器迁入 QueryEngine（或作为 QueryEngine 中间件）。
   b. steer 邮箱与子代理事件通道迁入 QueryEngine。
   c. 循环检测 + 三级干预（软提示/硬撤回重连/压缩）迁入 QueryEngine 中间件。
4. **灰度切换**：`ChatService.StreamWithEventsAsync` 内部改为路由到 `IQueryEngine`，用 profile 开关（如 `chat-loop-backend=query|legacy`）灰度；E2E 覆盖 GUI 回退、slash、bridge 三类宿主。
5. **下线旧循环**：全部宿主迁移且 E2E 绿后，`QueryLoopMiddleware` 与 `chat-pipeline` 中对应节点移入 `.xxx/` 归档，删除相关测试或改为 QueryEngine 等价测试。
6. **统一终止策略对象**：把闸门/空响应/循环检测/Stop hook 的优先级与计数收敛到一个 `TurnTerminationPolicy`，两阶段迁移期由新旧循环共同消费。

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
