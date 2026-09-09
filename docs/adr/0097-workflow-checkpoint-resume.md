# 0097. Workflow 级断点续跑持久化策略

- 状态：accepted
- 日期：2026-09-09
- 决策者：AI + 用户确认

## 背景

规格要求"并行工具调用 + DAG依赖 + 断点续跑"（来源：`D:\Users\54076\Desktop\1并行工具调用设计 - 副本.txt`）。探索发现 w3 已有：

- **任务级断点续跑**：`TaskRuntime.PersistAsync`/`RecoverTasksAsync`（`core/execution/Scheduling/src/Runtime/TaskRuntime.cs:281`）持久化 `runtime-tasks.json`，崩溃后恢复 Running→Pending
- **DAG 算法**：`Dag<T>` 分层拓扑+环检测+增量重算（`foundation/Structura/Dag/Dag.cs`）
- **并行执行**：Actor 模型 + Channel<T>（ADR 0074/0091），非裸 `Parallel.ForEachAsync`

**硬缺口**：`WorkflowTaskExecutor._activeWorkflows`（`WorkflowTask.cs:84`）是内存 `ConcurrentDictionary`，进程崩溃全丢。`WorkflowDefinition` 不持久化执行进度（CompletedSteps/FailedSteps/SkipReasons）。规格要求的 `workflow_{id}.state.json` + 断点续跑不存在。

阶段 0 已修复 `TaskRuntime.PersistAsync` 非原子写问题（commit `15d39867a`），为 Workflow 快照原子写奠定基础。

## 决策

新增 **`WorkflowStateStore` + `IWorkflowStateStore`**，每 workflow 独立文件 `workflow_{id}.state.json`，实现 Workflow 级断点续跑。

### 架构

```
WorkflowTaskExecutor                    磁盘
┌──────────────────┐                   ┌────────────────────────┐
│ ExecuteDagAsync   │──每层完成──→     │ workflow_{id}.state.json │
│  ├ LoadSnapshot   │←启动恢复──       │ (原子写 temp+move)       │
│  ├ 执行一层       │                   │ (损坏隔离 .corrupt)      │
│  └ SaveSnapshot   │──每层完成──→     └────────────────────────┘
└──────────────────┘
        ↑
  IWorkflowStateStore（可选注入，null 时不持久化）
```

### 核心设计

1. **每 workflow 独立文件** — `workflow_{id}.state.json`，多 workflow 并发持久化无冲突，清理按 workflow 粒度

2. **原子写 temp+move** — 复用阶段 0 的模式（先写 `.tmp` 再 `MoveFileAsync(overwrite:true)`），崩溃中途不损坏目标文件

3. **损坏文件隔离** — 复用 `TaskRuntime.QuarantineCorruptFileAsync` 模式（移动到 `.corrupt` 后缀，ADR 0008 归档不删除），Load 返回空 snapshot 从头执行

4. **DAG 模式每层完成后保存** — `ExecuteDagAsync` 每完成一层拓扑层后调 `SaveSnapshotAsync`，非每步保存（层�数远少于步数，降低 IO 频率）

5. **启动时加载快照恢复** — `ExecuteWorkflowAsync` 开头调 `LoadSnapshotAsync`，跳过 CompletedSteps，FailedSteps 下游标记 Skipped + 记录 SkipReasons

6. **IWorkflowStateStore 可选注入** — 构造函数参数 `= null`，不破坏现有测试（现有 WorkflowTaskExecutorTests 不传则不持久化）

7. **快照一致性校验** — 恢复时校验 `definition.Steps` 包含快照中的 StepId，不一致则丢弃快照从头执行（防 definition 变更后旧快照错配）

### 数据结构

```csharp
public sealed partial class WorkflowSnapshot
{
    public required string WorkflowId { get; init; }
    public required Dictionary<string, StepState> StepStates { get; init; }
    public Dictionary<string, string> SkipReasons { get; init; } = new();
    public DateTimeOffset LastUpdated { get; init; }
}
```

### 接口

```csharp
public interface IWorkflowStateStore
{
    Task SaveSnapshotAsync(string workflowId, WorkflowSnapshot snapshot, CancellationToken ct = default);
    Task<WorkflowSnapshot?> LoadSnapshotAsync(string workflowId, CancellationToken ct = default);
}
```

## 替代方案

### 方案 A：复用 TaskRuntime 的 runtime-tasks.json（全量单文件）

**放弃原因**：
- 全量单文件无法多 workflow 并发持久化（文件锁竞争）
- 混淆任务级状态（RuntimeTask）和 workflow 级状态（WorkflowStep 执行进度），职责不清
- TaskRuntime 持久化所有 durable 任务，不区分 workflow 归属

### 方案 B：用 Redis 分布式存储

**放弃原因**：
- 当前无分布式/多进程需求，w3 是单进程 CLI（ADR 0074 Actor 模型进程内并发）
- 引入 Redis 增加运维复杂度（部署、连接池、故障转移），违反"最小可用"原则
- ADR 0068 已用 Actor 模型统一持久化管道，进程内持久化已足够
- 若未来有分布式需求，可新增 `RedisWorkflowStateStore : IWorkflowStateStore`，接口已抽象

### 方案 C：用 IPersistencePipeline（ADR 0068）异步持久化

**放弃原因**：
- Workflow 快照需要**强一致**（崩溃前必须落盘），ADR 0068 的异步管道用 `BoundedChannelFullMode.DropOldest` 有丢消息风险
- 异步管道是"最终一致"，断点续跑要求"立即一致"
- Workflow 快照写入频率低（每层一次），无需异步管道的背压优化

### 方案 D：复用 TaskFileWriter.WriteAtomicAsync

**放弃原因**：
- `TaskFileWriter.WriteAtomicAsync` 只接受 `FileTaskMetadata`，不接受任意 JSON 字符串
- 改 `ITaskFileWriter` 接口加 string 重载影响面大
- 阶段 0 已在 TaskRuntime 内联原子写模式，WorkflowStateStore 同样内联，后续多处需要时再归纳提取扩展方法（渐进式重构，ADR 0017 归纳性重构不放弃）

## 后果

- **正面**：
  - Workflow 级断点续跑实现，进程崩溃可恢复（规格核心要求）
  - 每 workflow 独立文件，多 workflow 并发无冲突
  - 原子写 + 损坏隔离，崩溃不损坏数据
  - IWorkflowStateStore 可选注入，不破坏现有测试
  - 接口抽象，未来可扩展 Redis/Web API 实现

- **负面**：
  - 每 workflow 一文件，文件数量随 workflow 增多，需清理策略（保留最近 N 个，超期归档）
  - 快照写入有 IO 开销（每层一次，DAG 层数通常远少于步数，可接受）
  - 恢复时需校验快照与 definition 一致性，增加少量开销

- **中性**：
  - 仅 DAG 模式接入断点续跑，Sequential/Parallel 模式后续复用
  - 持久化目录与 TaskRuntime 共用 `PersistenceDirectory`，需子目录隔离（`workflow-states/`）

## 关联

- [ADR 0013](0013-hypergraph-vs-dag-separation.md) — DAG 管执行顺序，超图管评分共享
- [ADR 0068](0068-unified-persistence-pipeline-actor.md) — Actor 模型统一持久化管道（异步，不适用于强一致快照）
- [ADR 0074](0074-actor-supervisor-tree.md) — Actor 监督树（并行执行架构）
- [ADR 0093](0093-resource-management-exception-style.md) — 资源管理（using var + DisposeSafe）
- [工作计划](../plans/Workflow断点续跑工作计划.md) — 分阶段实现计划
