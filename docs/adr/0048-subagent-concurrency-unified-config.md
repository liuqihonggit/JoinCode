# 0048. 子代理并发控制统一配置入口

- 状态：proposed
- 日期：2026-09-02
- 决策者：项目架构组
- 关联：[0049](docs/adr/0049-archive-maxconcurrentagents.md) | [0050](docs/adr/0050-spawn-stage-concurrency-limit.md) | [0051](docs/adr/0051-fork-concurrency-limit.md)

## 背景

子代理并发限制配置当前分散在 4 处，语义混乱，无统一入口：

| 配置点 | 文件 | 默认值 | 作用域 |
|--------|------|--------|--------|
| `AgentSettings.MaxConcurrentAgents` | `core/ai/Agents/src/Configuration/Settings/AgentSettings.cs:12` | 10 | Agent 并发（**死配置，见 ADR 0049**） |
| `ExecutionOptions.MaxConcurrentTasks` | `foundation/Abstractions/00-core/Configuration/Execution/ExecutionOptions.cs:6` | 12 | Scheduling 任务并发 |
| `CacheAndToolExecutionSettings.MaxParallelToolExecution` | `foundation/Abstractions/00-core/Configuration/Settings/CacheAndToolExecutionSettings.cs:52` | 5 | 工具并行执行 |
| `ClusterExecutionOptions.MaxConcurrency` | 运行时传入 | - | 集群执行并发 |

问题：
1. **配置分散** — 4 个不同类、不同命名空间、不同文件，新人无法找到"子代理并发上限"的唯一数据源
2. **语义重叠** — `MaxConcurrentAgents`、`MaxConcurrentTasks`、`MaxConcurrency` 三者都控制"并发数"，但作用域边界模糊
3. **违反规则7**（ADR 0005 文件驱动界面）— 配置不是单一数据源，改并发上限要改多个地方
4. **违反规则5**（ADR 0016 参数传接口不传属性）— 消费方各自从不同配置类读属性，没有统一的并发选项接口

## 决策

**引入 `SubAgentConcurrencyOptions` 作为子代理并发控制的唯一配置入口**，收口所有子代理并发相关配置。保留三阶段差异化字段（spawn/execute/fork 资源类型不同）。

### 1. 新增统一配置类

```csharp
// foundation/Abstractions/00-core/Configuration/Execution/SubAgentConcurrencyOptions.cs
public sealed class SubAgentConcurrencyOptions
{
    /// <summary>spawn 阶段最大并发数（同时创建子代理数，保护 worktree 磁盘资源）</summary>
    public int MaxConcurrentSpawns { get; set; } = 16;

    /// <summary>execute 阶段最大并发数（同时执行子代理数，保护 CPU/内存）</summary>
    public int MaxConcurrentExecutions { get; set; } = 24;

    /// <summary>Fork 最大并发数（同时 fork 子代理数，0=不限）</summary>
    public int MaxConcurrentForks { get; set; } = 12;

    /// <summary>校验</summary>
    public void Validate()
    {
        if (MaxConcurrentSpawns < 1) throw new ArgumentException("MaxConcurrentSpawns 必须 >= 1");
        if (MaxConcurrentExecutions < 1) throw new ArgumentException("MaxConcurrentExecutions 必须 >= 1");
        if (MaxConcurrentForks < 0) throw new ArgumentException("MaxConcurrentForks 必须 >= 0");
    }
}
```

### 2. 默认值依据

参考 Claude Code "3-5 起步可扩展"思路（见下方调研对比），本项目同进程需平衡资源与扩展性：

| 字段 | 默认值 | 依据 |
|------|--------|------|
| `MaxConcurrentSpawns` | 16 | spawn 主要是 worktree 创建（git 进程 fork + 磁盘 I/O），16 并发 git worktree 在 SSD 上可接受 |
| `MaxConcurrentExecutions` | 24 | execute 是 LLM 调用（I/O 密集，非 CPU 密集），24 并发 HTTP 请求在连接池范围内 |
| `MaxConcurrentForks` | 12 | fork 涉及 spawn + 缓存复制 + 后台任务，资源开销介于 spawn 和 execute 之间 |

### 3. 消费关系

| 消费方 | 读取字段 | 替代的旧配置 |
|--------|----------|-------------|
| `SpawnSubAgentsAsync`（ADR 0050） | `MaxConcurrentSpawns` | 无（原无限流） |
| `AgentExecutionEngine.ExecuteParallelAsync` | `MaxConcurrentExecutions` | `ClusterExecutionOptions.MaxConcurrency` / `ParallelOptions.MaxDegreeOfParallelism` |
| `ForkSubAgentManager`（ADR 0051） | `MaxConcurrentForks` | 无（原无限流） |
| `TaskExecutor.ExecuteAgentsParallelAsync` | `MaxConcurrentExecutions` | `ExecutionOptions.MaxConcurrentTasks` |
| **`GoalGraphEngine`** | **`MaxConcurrentExecutions`** | **`GoalGraph.MaxConcurrency`（废除，见下）** |

### 3.1 废除 GoalGraph.MaxConcurrency 图级别配置

**背景**：`GoalGraph.MaxConcurrency`（`composition/Clock/src/Goal/Models/GoalGraph.cs:23`）默认 `0`（无限），7 个预定义模板全部未设置，注释说"对齐 ClusterExecutionOptions.MaxConcurrency"但默认值不一致（0 vs 5）。`GoalGraphEngine` 已有 `SemaphoreSlim` 限流实现但默认关闭。

**决策**：废除 `GoalGraph.MaxConcurrency` 属性，`GoalGraphEngine` 改为从 `SubAgentConcurrencyOptions.MaxConcurrentExecutions` 取并发上限。

具体操作：
1. 删除 `GoalGraph.MaxConcurrency` 属性
2. `GoalGraphEngine` 构造函数新增 `SubAgentConcurrencyOptions` 依赖
3. `GoalGraphEngine.ExecuteAsync` 中 `SemaphoreSlim` 从 `SubAgentConcurrencyOptions.MaxConcurrentExecutions` 创建
4. 7 个预定义模板无需改（本来就没设 `MaxConcurrency`）
5. 测试中 `MaxConcurrency = 1` 的断言改为注入 `SubAgentConcurrencyOptions { MaxConcurrentExecutions = 1 }`

**理由**：
- 图级别配置默认无限是安全隐患（research 模板有并行节点，无限流压垮资源）
- 7 个模板都没设 `MaxConcurrency`，图级别覆盖能力从未使用
- 统一到全局配置，符合规则7文件驱动原则
- 消除"图级别 vs 全局"的优先级困惑

### 4. 配置文件挂载

`settings.json` 新增 `execution.subAgentConcurrency` 节点：
```json
{
  "execution": {
    "subAgentConcurrency": {
      "maxConcurrentSpawns": 16,
      "maxConcurrentExecutions": 24,
      "maxConcurrentForks": 12
    }
  }
}
```

### 5. 热重载

按 ADR 0015 双变量切换模式，`SubAgentConcurrencyOptions` 变更时通过 `IConfigChangeNotifier` 触发原子交换，spawn/execute/fork 三处的 SemaphoreSlim 动态重建。

## Claude Code 对比调研（2026-09-02 联网）

| 维度 | Claude Code 策略 | 本项目决策 |
|------|-----------------|-----------|
| **并发上限** | 无硬性上限（"no hard limit on teammates"），靠 token 成本 + 协调开销自然约束 | 同进程需硬性上限（内存/线程池共享），用 `SubAgentConcurrencyOptions` 三阶段字段 |
| **进程模型** | 独立进程（in-process / split-pane tmux/iTerm2），不共享内存 | 同进程 .NET，共享内存和线程池 |
| **配置位置** | 四源分散：frontmatter + settings.json + env + CLI flag | 收口到单一 `SubAgentConcurrencyOptions` 类，**比 Claude Code 更统一** |
| **隔离** | `isolation: worktree` 独立 git worktree | `WorktreeSpawnMiddleware`（已有） |
| **深度限制** | 子代理可 spawn 子代理（有 depth limit）；agent teams 禁止嵌套 | `CalculateForkDepth`（已有） |

**关键差异**：Claude Code 的"几百子代理"靠独立进程 + token 成本约束，本项目同进程必须硬性上限。但参考其扩展性思路，默认值提高到 16/24/12（原 8/12/6），支持更大规模。

## 替代方案

1. **保留 4 个分散配置**：放弃。新人无法找到唯一数据源，违反规则7文件驱动原则。
2. **用 `ExecutionOptions.MaxConcurrentTasks` 统管一切**：放弃。spawn/execute/fork 三阶段资源类型不同（磁盘/CPU/内存），单一数值无法表达差异化上限。
3. **用 `AgentSettings.MaxConcurrentAgents` 统管**：放弃。该配置是死配置（ADR 0049 归档），且 `AgentSettings` 混杂了超时、重试、模型名等非并发配置，语义不内聚。
4. **单一数值 `MaxConcurrentSubAgents` 统管三阶段**：放弃。三阶段资源类型不同，单一数值要么过度限制 spawn（磁盘 I/O 瓶颈）要么放任 execute（CPU/内存溢出）。

## 后果

- 正面：单一数据源，符合规则7；三阶段差异化上限，资源保护更精准；热重载统一入口；比 Claude Code 配置更统一
- 负面：新增一个配置类，需迁移 `ExecutionOptions.MaxConcurrentTasks` 和 `ClusterExecutionOptions.MaxConcurrency` 的消费方
- 中性：`CacheAndToolExecutionSettings.MaxParallelToolExecution`（工具并行）不在本 ADR 范围，工具并发与子代理并发语义不同，保持独立
