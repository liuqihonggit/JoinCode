# 0106. 子代理卡死防护纵深防御体系

> 📍 **导航**: [docs/](../README.md) › [adr/](README.md)
> 🔗 **上游索引**: [adr/README.md](README.md) — 修改本文档后须同步更新此索引

> ⚠️ **L1预防设计已废弃** (2026-09-15) — 子代理不能加超时限制（AgentTimeoutSeconds接线），应通过L2检测+L3干预+L4恢复实现卡死防护（均已实现）

- 状态：proposed
- 日期：2026-09-15
- 决策者：用户 + AI

> 关联：[subagent-stall-defense.md](../design/DSG028-subagent-stall-defense.md)（技术设计文档）、[ADR 0040](0040-shannon-entropy-state-machine.md)（Shannon熵状态机）、[ADR 0048](0048-subagent-concurrency-options.md)（子代理并发控制）、[ADR 0054](0054-llm-output-loop-detection-intervention.md)（LLM输出循环检测与干预）、[ADR 0086](0086-core-tech-selection-lock-design.md)（锁设计与死锁防护）

## 背景

子代理（SubAgent）在并行执行中可能卡死，根因包括：

1. **AI 死循环输出**：上下文在变但内容循环重复（熵减）— 已有 `InformationEntropyGuardian` 四层漏斗检测
2. **完全无输出**：上下文 30s 无变化（欠费/网络问题/内部死锁）— `Entity.LastActivityAt` 字段存在但**无后台扫描器检查**
3. **工具调用无限挂起** — L1 工具超时已实现，但 `AgentSettings.AgentTimeoutSeconds=300` **配置存在却未接线**
4. **等待孙代理但孙代理已卡死** — `ForkSubAgentManager.CalculateForkDepth` 可回溯链，但**无链路级卡死聚合判定**

当前防护机制散落且不完整：

| 层级 | 现状 | 缺口 |
|------|------|------|
| L1 预防 | 工具超时已实现（`AbsoluteTimeoutMiddleware`+`TimeoutRecoveryToolHandlers`） | `AgentTimeoutSeconds=300` 未接线到子代理执行管道 |
| L2 检测 | `InformationEntropyGuardian` 检测死循环；`Entity.LastActivityAt`+`Touch()` 已在 AgentBase | **无子代理级活性检测器**、**无后台扫描器**、**无链路级聚合** |
| L3 干预 | `ResumeAgentAsync`/`PauseAgentAsync` 协作式原语已有 | **无抢占式调度**、子代理完成后**直接 Dispose 无代理池** |
| L4 恢复 | `LoopInterventionMiddleware.CompactAsync`+`IChatContextManager.FoldIfNeededAsync(agentId)` 已支持 agentId | **未与卡死检测联动** |

## 决策

采用 **四层纵深防御（Defense in Depth）** 体系，任何一层失效下一层接管：

### L1 预防：工具超时（已有，补全接线）

接线 `AgentSettings.AgentTimeoutSeconds=300` 到子代理执行管道。在子代理执行中间件链中加超时检测，复用 `TimeoutHelper.CreateLinkedTimeout`。

### L2 检测：双路检测 + 里程碑巡查 + 链路聚合

**双路检测**（按卡死根因分）：
- **死循环输出**（上下文在变但内容循环）→ 直接复用 `InformationEntropyGuardian`（已有，零改造）
- **完全无输出**（上下文 N 秒无变化）→ 新建 `SubAgentIdleDetector`，基于 `Entity.LastActivityAt`，状态机 `Monitoring→Suspected→Confirmed` + 时间窗口二次确认（复用 `ShannonEntropyDetector` 的 FSM 模式）

**里程碑巡查**（需求1）→ 新建 `SubAgentLivenessScanner`：
- 订阅 `AgentStateMachine.StateChanged` 事件
- 完成率 ≥ 阈值（默认 0.8）时触发一次全量巡查
- 巡查内容：状态、`LastActivityAt`、是否有孙代理、上下文时间戳

**链路聚合**（需求3）→ 新建 `SubAgentChainStallDetector`：
- 沿 `ParentSessionId` 链遍历（复用 `ForkSubAgentManager.CalculateForkDepth` 的回溯逻辑）
- 链上每个节点都满足卡死条件 → 整链告警 → 触发 L4

### L3 干预：激活 + 抢塞新任务

**激活**（对疑似卡死）：
- 死循环输出 → 直接触发 L4 渐进式压缩（复用 `InformationEntropyGuardian` 检测结果）
- 完全无输出 → 注入催促提示消息 + `ResumeAgentAsync`

**抢塞新任务**（需求5，对已完成）→ 架构级变更：
- 新建 `SubAgentPool` 代理池：改造 `AgentLifecycleManager.DisposeAgentAsync` 为回池而非销毁
- 新建 `PreemptiveScheduler` 抢占式调度器：基于 `PriorityMailbox` 三通道严格优先级
- 抢塞时新任务接在旧上下文之后（**保前缀缓存**：前缀不变，KV cache 命中）
- 窗口上限保护：上下文长度逼近窗口时拒绝抢塞，走 L4 压缩

### L4 恢复：渐进式压缩

三级 escalation（复用已有基础设施）：
1. **Light**：`CompressionOptions.Light`（0.8 比率）— 仅剪裁过期大工具结果
2. **Aggressive**：`CompressionOptions.Aggressive`（0.3 比率）— 头部消息摘要化，保留最近 N 轮 + 关键决策点
3. **ExitWithSummary**：退出并输出任务摘要

调用 `IChatContextManager.FoldIfNeededAsync(decision, agentId)` — 已支持 agentId 参数，子代理隔离折叠。

### 配置统一

新建 `SubAgentLivenessOptions` 配置类（支持热重载，复用 `ISubAgentConcurrencyUpdater` 模式）：
- `IdleThresholdSeconds`（默认 30）— 完全无输出超时阈值
- `CompletionCheckThreshold`（默认 0.8）— 80% 巡查触发阈值
- `ConfirmationWindowSeconds`（默认 5）— 二次确认时间窗口
- `ScanIntervalSeconds`（默认 10）— 定时扫描间隔
- `ChainStallThreshold`（默认 3）— 链路卡死节点数阈值

## 替代方案

### 方案 B：仅复用 InformationEntropyGuardian，不建独立 IdleDetector

**放弃原因**：`InformationEntropyGuardian` 检测的是"输出在变化但内容循环"，无法检测"完全无输出"（欠费/网络中断）。两种卡死模式需要不同检测器。

### 方案 C：用 Timer 定时轮询每个子代理

**放弃原因**：N 个子代理 × M 秒轮询 = N/M 次/秒回调，高频唤醒影响性能。改用事件驱动（`StateChanged`）+ 里程碑巡查（80%触发）+ 低频定时扫描（10s）组合，减少不必要唤醒。

### 方案 D：抢塞新任务用全新子代理（不复用上下文）

**放弃原因**：用户明确要求"利用已有上下文 + 前缀缓存"。新建子代理需重新加载项目信息，且前缀缓存失效。复用已完成子代理的上下文接续新任务，前缀不变 KV cache 命中，显著降低延迟和成本。

## 验收标准

| 编号 | 标准 |
|------|------|
| AC-01 | 子代理执行超过 `AgentTimeoutSeconds` 被超时取消 |
| AC-02 | 子代理 30s 无活动且无孙代理 → `SubAgentIdleDetector` 进入 Suspected → 5s 内仍无活动 → Confirmed |
| AC-03 | 完成率 ≥ 80% 时触发一次全量巡查，日志记录巡查结果 |
| AC-04 | 子孙链所有节点都 Confirmed → 触发链路告警 → L4 压缩 |
| AC-05 | 子代理完成后回池而非 Dispose，新任务可抢塞复用上下文 |
| AC-06 | 抢塞新任务后前缀缓存命中（CacheReadInputTokens > 0） |
| AC-07 | 卡死确认后渐进式压缩：Light 失败 → Aggressive 失败 → ExitWithSummary |
| AC-08 | 所有阈值可通过 `SubAgentLivenessOptions` 配置，支持热重载 |

## 实现顺序

1. L1 接线（最小改动，立即收益）
2. L2 配置类 + IdleDetector + Scanner + ChainDetector
3. L3 激活 + 代理池 + 抢占调度器
4. L4 渐进式压缩编排
5. 集成到 AgentCoordinator/AgentLifecycleManager
