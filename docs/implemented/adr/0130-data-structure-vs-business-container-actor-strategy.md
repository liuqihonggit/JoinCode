# 数据结构 vs 业务容器的 Actor 改造策略

## 状态

accepted

## 背景

DSG034 Actor 改造候选 6 个（P1-P6），其中 3 个在底层项目（structura/pithosdb）不能引用 ActorBase（async_lock 引用 structura 形成循环依赖）。需要明确哪些改 Actor、哪些保持锁。

## 决策

区分**数据结构**和**业务容器**，采用不同并发策略：

| 类型 | 特征 | 策略 | 示例 |
|------|------|------|------|
| 数据结构 | 同步容器、短临界区（纳秒级）、无 IO、不嵌套锁 | 保持锁（SemaphoreSlim/lock） | ConcurrentDag、S3FifoBlockCache、LruBlockCache |
| 业务容器 | 异步 IO、生命周期管理、监督需求、长临界区 | Actor 改造（ActorBase） | ContextHierarchy、HighWaterMarkManager、SubAgentPool |

### 判断标准

1. **临界区长度** — 纳秒级（纯内存操作）→ 数据结构；毫秒级+（IO 等待）→ 业务容器
2. **是否嵌套锁** — 单锁不嵌套 → 数据结构（不会死锁）；多锁嵌套/锁环 → 业务容器
3. **是否有生命周期** — 无状态/纯数据 → 数据结构；PreStart/PostStop/Dispose/监督 → 业务容器
4. **是否需要背压/重试** — 不需要 → 数据结构；需要 → 业务容器

### 数据结构不会死锁的证明

- 临界区极短（只操作内存 Dictionary/LinkedList，无 IO）
- 不嵌套锁（临界区内不获取其他锁，不构成锁环）
- 同步 API 用 `Wait(0)` 非阻塞（锁忙返回默认值，不无限等待）
- 单锁对象（每实例一把锁，无锁环可能）

## 替代方案

| 方案 | 优点 | 缺点 | 结论 |
|------|------|------|------|
| A. 全部 Actor 改造 | 统一并发模型 | 数据结构性能退化数个数量级（跨线程通信 vs 纳程级锁）；底层项目不能引用 ActorBase | ❌ |
| B. 全部不改 | 零改动 | 业务容器死锁/生命周期问题未解决 | ❌ |
| C. 区分改（本决策） | 数据结构保持高性能，业务容器获得 Actor 监督/背压/生命周期 | 需要维护两套并发模型 | ✅ |

## 影响

- P1 ConcurrentDag → 保持 SemaphoreSlim（回退裸 Channel 改造）
- P4 S3FifoBlockCache → 保持 lock（回退裸 Channel 改造）
- P5 LruBlockCache → 保持 lock（不改造）
- P2 ContextHierarchy → ActorBase ✅
- P3 HighWaterMarkManager → ActorBase ✅
- P6 SubAgentPool → ActorBase ✅

## 关联

- DSG034: Actor 改造无锁化 + 内存泄露修复
- ADR 0086: 核心技术选型锁设计
