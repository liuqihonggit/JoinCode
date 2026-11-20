# 0160. QueryEngine 并行工具执行 — 并发安全合批与有序回写

- 状态：accepted（已实现，TASK103 S13）
- 日期：2026-11-21
- 决策者：项目架构组

## 背景

TASK103 合并两套 ReAct 循环后，`QueryEngine` 成为唯一循环，工具调用按数组顺序逐条 `await` 执行（`ExecuteCoreLoopAsync`）。合并前 ChatService 侧由 `StreamingToolExecutor(Actor)` + `ToolConcurrencyClassifier` 提供的能力随之丢失：

| 能力 | 合并前（Actor 执行器） | 合并后（QueryEngine） |
|------|------------------------|------------------------|
| 并发安全分类 | `ToolConcurrencyClassifier`（`SafeToolNames` + Bash 只读探测） | 无 |
| 并行执行 | 多个并发安全工具同时运行 | 严格串行 |
| 结果回写顺序 | 按原调用顺序 | 按原调用顺序 |

源码生成器仍在为 `[McpTool(ConcurrencySafe = true)]` 生成 `SafeToolNames` 并注册到 DI（`ServiceRegistration.cs`），但已无消费者。并行能力丢失导致多只读工具（grep/read/搜索）串行等待，长链条工具调用延迟成倍增加。

## 决策

1. **单一分类器**：在 `brain.query` 新增 `IToolConcurrencyClassifier` + `ToolConcurrencyClassifier`（`[Register]` 自动注册），消费 DI 中的 `FrozenSet<string> SafeToolNames`，Bash/PowerShell 通过 `ReadOnlyCommandProbe` 委托做只读探测（由 composition 注册，委托 `IReadOnlyCommandDetector.CheckReadOnlyConstraints`）。
2. **合批规则**：把本轮 `ToolCalls` 按分类结果切分为批次 —
   连续且并发安全的调用合成一个并行批次；任一非并发安全调用独占一个批次。
   批次之间严格串行，保持调用原顺序。
3. **有序回写**：并行批次内先启动全部任务并发出全部 `ToolCallStart`，随后按原调用顺序逐条 `await` 结果，逐条发出 `ToolCallEnd`、写 history、跑 `AfterToolCallHooks`。LLM 看到的 assistant/tool 消息顺序与串行执行完全一致。
4. **取消语义不变**：仍取消令牌触发时，为当前及后续未完成的调用补写 `(aborted)` 结果（`WriteAbortedToolResults`）后向上抛 `OperationCanceledException`；并行批次中已完成但排在取消点之后的调用同样按 aborted 处理，保证历史合法。
5. **无分类器即串行**：`QueryEngine` 通过 `IServiceProvider` 延迟解析分类器；解析不到（测试/裁剪宿主）时全部调用按非并发安全处理，行为与现状一致。
6. **上限语义不变**：`MaxToolCallIterations` 在调度前按剩余预算截断，编号 `TotalToolCalls` 与事件顺序保持现有语义。

## 考虑过的替代方案

| 方案 | 放弃原因 |
|------|----------|
| 恢复 Actor 执行器（`StreamingToolExecutor`） | 与合并目标相悖：循环内多一层消息传递，取消/进度/历史回写需要跨 Actor 协调，复杂度显著高于"批次 + 有序 await" |
| 全部工具并行 | 写操作/交互工具并发会破坏状态一致性，交互工具并行会卡死 |
| 只按 `SafeToolNames` 静态分类，不做 Bash 只读探测 | 丢失 TS 对齐语义（只读 bash 是最常见的并行场景） |
| 批次内按完成顺序回写 | assistant 消息的时间局部性丢失，模型会看到乱序工具结果，推理质量下降 |

## 后果

- 正面：多个并发安全工具（只读搜索/读取）并行执行，长工具链延迟下降；分类器与生成器重新接线，`ConcurrencySafe` 标记重新生效；结果顺序与串行一致，无行为回归。
- 代价：并行批次内工具共享取消令牌，首个失败（非取消）不打断其他任务（各任务内部已把异常转为错误结果）；`AfterToolCallHooks` 在批次后按序执行，预算/递减回报检查粒度从"每工具即时"变为"批次完成即"。
- 风险控制：默认分类集合为空时退化为串行；交互工具（`ask_user_question`/`confirm_action`）由分类器防御性排除。

## 关联

- 任务文档：`docs/proposed/task/TASK103-ReAct循环合并-两套归一.md`
- 源码生成器：`modules/generators/mcp_tool_dispatch.generator/McpToolDispatchGenerator.cs`（`SafeToolNames`）
- 只读探测：`modules/guard/guard.security/services/readonly_detector/ReadOnlyCommandDetector.cs`
- 手动验收：[ADR 0080](../../implemented/adr/engineering_ci_test/0080-manual-exe-testing-guide.md)
