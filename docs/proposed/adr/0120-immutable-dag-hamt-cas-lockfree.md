# 0120. ImmutableDag 不可变无锁 DAG — HAMT + CAS 原子更新

> 📍 **导航**: [docs/](../README.md) › [adr/](README.md)
> 🔗 **上游索引**: [adr/README.md](README.md) — 修改本文档后须同步更新此索引

- 状态：proposed
- 日期：2026-09-29
- 决策者：用户（liuqihonggit）+ AI

## 背景

项目已有两种 DAG 实现：

- **`Dag<T>`**（`lib/structura/dag/Dag.cs`）— 非线程安全，纯可变 `Dictionary` + `HashSet`，551 处调用
- **`ConcurrentDag<T>`**（`lib/structura/dag/ConcurrentDag.cs`）— `SemaphoreSlim` 锁保护，3 处调用（TaskService、TodoService、TaskRuntime）

### 问题

1. **`ConcurrentDag<T>` 用 `SemaphoreSlim` 锁**，不符合项目并发方向（只读类型 + 不可变容器 + CAS 原子更新 + 无锁化暴露，见记忆 `concurrency-design-direction`）
2. **`Dag<T>` 非线程安全**，消费方需自行加锁或保证单线程访问，易遗漏
3. **两套 API 不完全对齐** — `ConcurrentDag<T>` 额外有 async API 和 `Clear()`，`Dag<T>` 有静态 `WouldCreateCycle`，替换时需分别处理
4. **`DagNode<T>` 内部 `List<string>` 可变** — `InEdgeIds`/`OutEdgeIds` 是可变 List，边增删时直接修改，无法做不可变快照

### 项目已有 CAS 模式

项目已有 30+ 处 `ImmutableHamT` + `Interlocked.CompareExchange` CAS 模式（LspServerRegistry、PeerSessions、AgentServiceImpl、SubAgentPool、SessionCache、MapRegistry 等），是新 DAG 的参考模式。

## 决策

### 决策1：新建 `ImmutableDag<T>`，不修改现有 `Dag<T>`/`ConcurrentDag<T>`

- 新建 `lib/structura/dag/ImmutableDag.cs`
- 现有 `Dag<T>`/`ConcurrentDag<T>` 保持不动，后续渐进式替换消费方后归档
- **理由**：渐进式迁移策略（AGENTS.md），禁止一次性大规模重构

### 决策2：内部存储用 `ImmutableHamT` + `ImmutableHamTSet`

- 节点表：`ImmutableHamT<string, ImmutableDagNode<T>>` — 不可变 HAMT，O(log₃₂ N) 查找
- 边表：`ImmutableHamT<string, DagEdge>` — 边 ID → 边
- 边端点索引：`ImmutableHamT<(string From, string To), string>` — 端点对 → 边 ID，O(1) 查找
- 邻接表：`ImmutableHamT<string, ImmutableHamTSet<string>>` — 节点 → 后继集合
- 逆邻接表：`ImmutableHamT<string, ImmutableHamTSet<string>>` — 节点 → 前驱集合
- **理由**：复用项目已有不可变容器，与全局 CAS 模式统一

### 决策3：节点改为不可变 `ImmutableDagNode<T>`

- `InEdgeIds`/`OutEdgeIds` 从 `List<string>` 改为 `ImmutableHamTSet<string>`
- 边增删时构造新节点实例（路径复制）
- **理由**：可变 List 无法做不可变快照，必须改为不可变集合

### 决策4：CAS 循环做原子更新

- 字段：`volatile ImmutableDagState _state` — 不可变快照
- 写操作（AddNode/AddEdge/RemoveNode/RemoveEdge）：读旧状态 → 构造新状态 → `Interlocked.CompareExchange` → 失败重试
- 读操作（Nodes/Edges/TopologicalSort 等）：`Volatile.Read` 读快照，无锁
- **理由**：对齐项目 CAS 循环模式（见记忆 `feedback-cas-loop-pattern`），禁止 `Volatile.Write`/直接赋值/`Interlocked.Exchange`

### 决策5：API 完全对齐 `Dag<T>` + `ConcurrentDag<T>`

同步 API（对齐 `Dag<T>`）：
- `AddNode` / `AddEdge` / `TryAddEdge` / `RemoveNode` / `RemoveEdge`
- `WouldCreateCycle` / `HasCycle` / `FindAllCycles`
- `TopologicalSort` / `TopologicalSortByLevels`
- `GetAncestors` / `GetDescendants` / `GetAffectedSubgraph`
- `TryGetEdge` / `Nodes` / `Edges` / `Version`
- 静态 `WouldCreateCycle(adjacency, fromId, toId)`

Async API（对齐 `ConcurrentDag<T>`）：
- `AddNodeAsync` / `AddEdgeAsync` / `TryAddEdgeAsync` / `RemoveNodeAsync` / `RemoveEdgeAsync` / `WouldCreateCycleAsync`
- **实现**：CAS 无锁，async 直接包同步操作（`Task.FromResult`），不阻塞

额外 API：
- `Clear()` — 对齐 `ConcurrentDag<T>.Clear()`
- **理由**：替换时只改类型声明，不改 API 调用（用户明确要求"API对齐的话,不就是直接替换了吗"）

### 决策6：放在 `lib/structura/dag/`，命名空间 `Structura.Dag`

- 与现有 `Dag<T>`/`ConcurrentDag<T>` 同目录同命名空间
- **理由**：方便替换时只改类型名，不改 `using`

### 决策7：不实现 `IDisposable`

- `ConcurrentDag<T>` 实现了 `IDisposable`（释放 `SemaphoreSlim`），`ImmutableDag<T>` 无锁无资源，不需要
- **理由**：消除替换时 `using var dag = new ConcurrentDag<T>()` 的 `using` 声明（调用方可保留 `using`，无副作用）

## 后果

### 正面

- **无锁并发** — 读操作无锁无阻塞，写操作 CAS 无锁重试
- **统一并发模型** — 与项目 30+ 处 CAS 模式一致
- **API 对齐** — 替换时只改类型声明，降低迁移风险
- **不可变快照** — 读操作始终看到一致快照，无撕裂

### 负面

- **CAS 重试开销** — 高并发写时 CAS 可能重试多次（但 DAG 写少读多，影响小）
- **新类型 `ImmutableDagNode<T>`** — 消费方引用 `DagNode<T>.InEdgeIds`（`List<string>`）需改为 `ImmutableHamTSet<string>`，但 `IEnumerable<string>` 兼容
- **async API 是假 async** — 内部同步，`Task.FromResult` 包装，但 API 对齐 `ConcurrentDag<T>` 消费方无需改

## 压测结果（2026-09-29 BenchmarkDotNet ShortRunJob）

压测代码：`test/benchmarks/structura.benchmarks/DagBench.cs`，规模 1K/5K 节点线性链。

### Build（AddNode + AddEdge 构建图）

| Size | ImmutableDag (HAMT+CAS) | Dag (Dictionary) | ConcurrentDag (SemaphoreSlim) | 慢倍数 (vs Dag) |
|------|------------------------|-------------------|-------------------------------|----------------|
| 1K | 3,765 us / 6.74 MB | 663 us / 1.58 MB | 829 us / 1.84 MB | **5.7x** |
| 5K | 38,419 us / 38.51 MB | 13,768 us / 7.62 MB | 16,644 us / 8.91 MB | **2.8x** |

### TopologicalSort（Kahn 算法拓扑排序）

| Size | ImmutableDag | Dag | 慢倍数 |
|------|-------------|-----|--------|
| 1K | 361 us / 525 KB | 196 us / 336 KB | **1.8x** |
| 5K | 2,778 us / 2.6 MB | 1,443 us / 1.7 MB | **1.9x** |

### GetDescendants（BFS 下游查询）

| Size | ImmutableDag | Dag | 慢倍数 |
|------|-------------|-----|--------|
| 1K | 149 us / 251 KB | 50 us / 72 KB | **3.0x** |
| 5K | 1,005 us / 1.2 MB | 488 us / 315 KB | **2.1x** |

### 结论

- **Build 最慢**：HAMT 路径复制 + CAS 循环，比 Dictionary O(1) 慢 3-6x，内存 4-5x
- **查询次之**：HAMT O(log₃₂N) vs Dictionary O(1)，慢 2-3x，内存 1.5-3.5x
- **ImmutableDag 的价值在死锁消除 + 无锁一致快照，不在性能**
- 用户于 2026-09-29 确认：目标是消除死锁 + 统一数据结构，非性能优化，接受此性能代价
- **后续全量替换计划暂停**：用户于 2026-09-29 决定不替换现有 Dag/ConcurrentDag，ImmutableDag 作为独立实现保留

### 替代方案（考虑过但放弃）

1. **改 `ConcurrentDag<T>` 内部用 CAS** — 不改 API，但 `Dag<T>` 内部可变 `DagNode<T>` 无法做不可变快照，需大改 `DagNode<T>`，影响 551 处
2. **用 `ConcurrentDictionary` + 细粒度锁** — 仍是锁，不符合项目无锁化方向
3. **用 `FrozenDictionary` 快照** — `FrozenDictionary` 不支持 `SetItem`/`Remove`，每次写都全量重建，性能差
