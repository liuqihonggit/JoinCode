# 0049. 归档 MaxConcurrentAgents 死配置

- 状态：proposed
- 日期：2026-09-02
- 决策者：项目架构组
- 关联：[0048](docs/adr/0048-subagent-concurrency-unified-config.md)
- 验证：待实现后补充

## 背景

`AgentSettings.MaxConcurrentAgents`（`core/ai/Agents/src/Configuration/Settings/AgentSettings.cs:12`）定义了"最大并发 Agent 数=10"，但代码调查发现：

1. **全项目仅 1 处引用** — 即定义处本身，没有任何消费方读取该属性
2. **实际并发控制由其他配置承担**：
   - `TaskExecutor.ExecuteAgentsParallelAsync` 用 `ExecutionOptions.MaxConcurrentTasks=12`
   - `AgentExecutionEngine.ExecuteParallelAsync` 用 `ClusterExecutionOptions.MaxConcurrency` 或 `ParallelOptions.MaxDegreeOfParallelism`
3. **配置项语义误导** — 新人看到 `MaxConcurrentAgents=10` 会以为子代理并发上限是 10，但实际是 12（`MaxConcurrentTasks`）或运行时传入值

死配置的危害：
- 维护负担（改了不生效，调试困惑）
- 违反减法思维（ADR 0023）— 加了配置项但没消费，是加法思维的产物
- 语义污染 — 与 `MaxConcurrentTasks` 语义重叠，新人不知该改哪个

## 决策

**归档 `AgentSettings.MaxConcurrentAgents` 属性**（按 ADR 0008 归档规范，移动到 `.xxx/`）。

具体操作：
1. 删除 `AgentSettings.MaxConcurrentAgents` 属性（`core/ai/Agents/src/Configuration/Settings/AgentSettings.cs:12`）
2. 归档 `AgentSettings.cs` 旧版本到 `.xxx/AgentSettings.cs.20260902.del`（保留审计追踪）
3. 子代理并发控制统一到 `SubAgentConcurrencyOptions`（ADR 0048）
4. 检查 `settings.json` 是否有 `maxConcurrentAgents` 配置项，如有则迁移到 `subAgentConcurrency.maxConcurrentExecutions` 并归档旧配置

归档后：
- `AgentSettings` 仅保留 `AgentTimeoutSeconds`、`MaxRetryCount`、`EnableWorktreeIsolation`、`DefaultModelName`、`MaxContextLength`（非并发配置）
- 子代理并发上限唯一数据源为 `SubAgentConcurrencyOptions`（ADR 0048）

## 替代方案

1. **激活 `MaxConcurrentAgents` 作为统一上限**：放弃。该配置位于 `AgentSettings`，与超时/重试/模型名等非并发配置混放，语义不内聚；且 ADR 0048 已决策用独立的 `SubAgentConcurrencyOptions` 收口。
2. **保留 `MaxConcurrentAgents` 但标记 `[Obsolete]`**：放弃。`TreatWarningsAsErrors` 已启用（ADR 0027），`[Obsolete]` 会编译失败；且保留死配置违反减法思维（ADR 0023）。
3. **重命名为 `MaxConcurrentSubAgents` 并激活**：放弃。重命名+激活=加法思维，且与 ADR 0048 的 `SubAgentConcurrencyOptions` 重复。

## 后果

- 正面：消除死配置；消除语义误导；减少 `AgentSettings` 维护面；符合减法思维
- 负面：需检查 `settings.json` 迁移配置项；归档操作需更新引用（本例无引用，影响为零）
- 中性：`AgentSettings` 其他配置保留；并发控制转移至 `SubAgentConcurrencyOptions`（ADR 0048）
