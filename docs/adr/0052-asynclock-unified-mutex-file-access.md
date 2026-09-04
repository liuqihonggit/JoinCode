# 0052. AsyncLock 统一互斥锁 + 文件读写可剥离架构

- 状态：accepted
- 日期：2026-09-02
- 决策者：项目架构组

## 背景

### 现状

1. **AsyncLock 已存在但仅是 SemaphoreSlim 包装**：
   - `foundation/AsyncLock/src/AsyncLock.cs` 内部 `private readonly SemaphoreSlim _semaphore = new(1, 1)`
   - API：`LockAsync() → ValueTask<AsyncLockGuard>`、`Lock() → AsyncLockGuard`（同步）
   - `AsyncLockGuard` 是 `readonly struct IDisposable`，Dispose 时 Release
   - 已有约 20 处使用（TranscriptFileWriter、SandboxManager、TokenBudgetManager 等）

2. **SemaphoreSlim 散落 264 处，语义混杂**：

   | 用法 | 数量 | 示例 | 能否替换为 AsyncLock |
   |------|------|------|----------------------|
   | 互斥锁 `new SemaphoreSlim(1, 1)` | ~50+ | `_lock = new SemaphoreSlim(1, 1)` | ✅ 可替换 |
   | 信号量 `new SemaphoreSlim(0, int.MaxValue)` | 少量 | `CommandQueue._signal` | ❌ 不可替换（非互斥语义） |
   | 信号 `new SemaphoreSlim(0, 1)` | 测试中 | `StreamIdleWatchdogTests` | ❌ 不可替换（信号语义） |
   | 并发限流 `new SemaphoreSlim(n, n)` | 少量 | `ExecutionContext.ConcurrencyLock`、`DownloadSession`、`GoalGraphEngine` | ❌ 不可替换（n>1） |
   | 公开属性暴露 `SemaphoreSlim` | ~3 | `ExecutionContext.ConcurrencyLock`、`GraphExecutionContext.StateLock`、`TeammateExecutionContext.TeammateLock` | ⚠️ 需评估（破坏 ABI） |

3. **文件读写锁分散**：TranscriptFileWriter、AssistantDailyLog、ThinkingStore、SessionTagService、FileBasedTaskService 等各自维护独立锁，无统一协调。

4. **git worktree 隔离基础设施已完备**：`IAgentWorktreeManager`、`AgentWorktreeManager`、`BootstrapWorktreeManager` 已落地，Fork/Doctor/Teammate 均支持 worktree 隔离。

### 问题

- **锁不统一**：AsyncLock 与 SemaphoreSlim 并存，开发者不知道何时用哪个
- **无法剥离锁**：文件读写锁硬编码在各个服务中，无法切换到 worktree 隔离模式
- **替换成本高**：`SemaphoreSlim` → `AsyncLock` 不是纯文本替换（API 不同：`WaitAsync/Release` vs `LockAsync/using guard`），需逐个改调用方式
- **性能声明待验证**：用户声称 AsyncLock 性能比 SemaphoreSlim 高，但当前实现就是包装 SemaphoreSlim，性能实际相同

## 决策

### 决策1：AsyncLock 提供 SemaphoreSlim 兼容构造（参数作假）

新增构造函数 `AsyncLock(int initialCount, int maxCount)`，签名与 `SemaphoreSlim` 完全一致：

```csharp
public sealed class AsyncLock : IDisposable
{
    // 现有无参构造保留
    public AsyncLock() { ... }

    // 新增：参数兼容构造（参数作假，仅支持互斥语义）
    public AsyncLock(int initialCount, int maxCount)
    {
        if (initialCount != 1 || maxCount != 1)
            throw new ArgumentOutOfRangeException(
                nameof((initialCount, maxCount)),
                "AsyncLock 仅支持互斥语义 (1,1)。信号量/并发限流请继续使用 SemaphoreSlim。");
        ...
    }

    public ValueTask<AsyncLockGuard> LockAsync(CancellationToken ct = default);
    public AsyncLockGuard Lock();
    public void Dispose();
}
```

**参数作假含义**：
- 接受 `(1, 1)` → 正常互斥锁
- 接受其他值 → 抛异常（编译期不报错，运行期 fail-fast，防止误用）
- 目的：批量替换时构造签名兼容，减少改动量

**不可替换的 SemaphoreSlim 保留**：
- 信号量 `(0, int.MaxValue)`、信号 `(0, 1)`、并发限流 `(n, n)` — 这些是 SemaphoreSlim 的正当用途，不强行替换

### 决策2：AsyncLock 回退为 SemaphoreSlim 包装（API 统一，非高性能自实现）

**原方案 B（SpinLock + TCS 队列）已废弃**：基准测试证明自实现比 SemaphoreSlim 慢 1.3-3.5x。

**基准测试结果**（BenchmarkDotNet 0.15.8, ShortRunJob, i9-9900KF, .NET 10.0.9）：

| 场景 | AsyncLock(SpinLock+TCS) | SemaphoreSlim(1,1) | 比率 |
|------|:-----------------------:|:-------------------:|:----:|
| 无竞争 async | 114.98 ns | 33.27 ns | 慢 3.5x |
| 无竞争 sync | 108.24 ns | 33.10 ns | 慢 3.3x |
| 竞争 4线程 | 2.367 us | 1.780 us | 慢 1.3x |
| 竞争 8线程 | 7.322 us | 3.099 us | 慢 2.4x |

**根因**：SemaphoreSlim 是 CLR 高度优化的内置类型，无竞争路径用 Interlocked 快速检查；自实现 SpinLock(线程跟踪模式) + TCS 队列分配难以超越。

**新方案：SemaphoreSlim 包装**：
- AsyncLock 内部包装 `SemaphoreSlim(1,1)`，API 保持 `LockAsync() → ValueTask<AsyncLockGuard>`
- **价值在 API 统一**（`using var guard = await lock.LockAsync()`），非性能提升
- 参数兼容构造 `AsyncLock(1,1)` 保留（降低迁移成本）
- 单独 csproj 项目保留（`foundation/AsyncLock/`，AllowPack=true 支持未来独立打包），基准测试项目 `tests/Benchmarks/AsyncLock.Benchmarks/`

### 决策3：复用现有 AsyncFileLock 跨进程互斥锁（不实现进程内读写锁）

**调查发现**：项目已有 `infrastructure/Infrastructure/AsyncFileLock/` 模块，提供 per-file 跨进程互斥锁：

| 组件 | 职责 |
|------|------|
| `FileLock` | per-file 跨进程互斥锁（基于 `AsyncCrossProcessMutex` + SHA256 哈希路径 → `Global\AsyncFileLock_{hash}` Mutex） |
| `BatchLock` | 批量文件锁（按路径排序防死锁） |
| `FileLockService` | 静态服务入口：`AcquireAsync(path, timeout)` / `AcquireBatchAsync(paths, timeout)` |

**已是 per-file 分段架构**：每个文件路径生成独立 Mutex，不同文件天然并行，同文件串行互斥。

**5 个消费方**：`FileWriter`、`FileEditor`、`ThrottledFileService`、`HighWaterMarkManager` + 测试。

**决策：不实现进程内读写锁，复用现有 AsyncFileLock**（用户确认"原本就挺好用的"）：

| 因素 | 结论 |
|------|------|
| 现有锁能力 | per-file 分段 + 跨进程互斥 + 批量防死锁 — 已满足文件访问安全 |
| 读写分离收益 | 文件场景以写为主（FileWriter/FileEditor/HighWaterMarkManager），读多写少场景少，读写锁并发收益小 |
| 实现成本 | 自实现 AsyncReaderWriterLock + PerFileFileAccessCoordinator + IFileAccessCoordinator 接口 + 全量迁移 — 成本高，收益小 |
| 跨进程 vs 进程内 | 现有锁跨进程（Mutex），比进程内（SemaphoreSlim）更强，安全冗余但无害 |

**已归档**：先前试写的 `AsyncReaderWriterLock.cs`（SemaphoreSlim 组合自实现）已归档到 `.xxx/AsyncReaderWriterLock.cs.20260902.del`。

**可剥离架构（worktree 隔离）**：与锁独立。worktree 是 subAgent 活动时创建的隔离环境（`AgentWorktreeManager`），非锁替代。锁模式与 worktree 模式通过全局配置切换，与本次 AsyncLock 统一工作解耦 — 不在本次 ADR 范围内实现。

### 决策4：公开属性暴露的 SemaphoreSlim 处理

`ExecutionContext.ConcurrencyLock`、`GraphExecutionContext.StateLock`、`TeammateExecutionContext.TeammateLock` 是并发限流锁（n>1）或跨模块共享锁：

**已确认：按语义区分，同步改消费方**：
- **并发限流锁（n>1）**：保留 `SemaphoreSlim`，不替换（AsyncLock 仅互斥语义）。如 `ExecutionContext.ConcurrencyLock`（`MaxConcurrentTasks` 可能 >1）、`GoalGraphEngine` 的 `MaxConcurrency` 限流器
- **互斥锁（1,1）**：替换为 `AsyncLock`，**同步修改所有消费方**（字段类型 `SemaphoreSlim` → `AsyncLock`，`WaitAsync/Release` → `LockAsync/using guard`）。如 `GraphExecutionContext.StateLock`、`TeammateExecutionContext.TeammateLock` 若为 (1,1) 则替换
- **ABI 影响处理**：公开属性类型变更需同步改所有外部消费方，编译期发现（非运行时），可借助 Roslyn CodeFix 批量转换消费方

### 决策5：迁移策略 — 按模块渐进式（辅助 Roslyn CodeFix）

**已确认：按模块渐进式迁移**，不一次性全量替换。

**渐进式步骤**：
1. 先完成 AsyncLock SemaphoreSlim 包装回退 + AsyncReaderWriterLock 原语选型（决策2/6）
2. 选首个模块（候选 Vault，因其文件写入最密集）试点替换
3. 每替换一个服务：编译 → 单元测试 → git 提交
4. 验证无误后推广到下一模块

**辅助工具 — Roslyn 分析器 + CodeFix**（可选，加速渐进式迁移）：

编写分析器检测 `SemaphoreSlim(1,1)` 用法并提供 CodeFix 自动转换：

| 检测模式 | 自动转换 |
|----------|----------|
| `new SemaphoreSlim(1, 1)` | `new AsyncLock(1, 1)` |
| `await _x.WaitAsync(ct)` | `using var guard = await _x.LockAsync(ct)` |
| `_x.Release()` | 删除（using guard 自动释放） |
| `SemaphoreSlim` 字段类型 | `AsyncLock` |

**非 (1,1) 的 SemaphoreSlim 不转换**，分析器报告诊断但不提供 CodeFix。

**工具定位**：辅助而非主导。渐进式逐模块迁移时，每模块可用 CodeFix 批量转换该模块内的 (1,1) 用法，但仍需人工审查 + 编译 + 测试。

### 决策6：锁分类归宿

**锁分类归宿**（基于 264 处 SemaphoreSlim 调查 + 现有 AsyncFileLock 发现）：

| 锁类型 | 归宿 | 原因 |
|--------|------|------|
| 文件 IO 锁（FileWriter、FileEditor、ThrottledFileService、HighWaterMarkManager 等） | 现有 `AsyncFileLock`（FileLockService） | per-file 跨进程互斥锁已存在且够用，不重复实现 |
| 内存状态互斥锁（TokenBudgetManager、SandboxManager 等） | `AsyncLock`（SemaphoreSlim 包装） | 全局互斥，无 key 分段 |
| 并发限流（n>1） | 保留 `SemaphoreSlim(n,n)` | 限流非互斥/读写 |
| 信号量/信号（0,N） | 保留 `SemaphoreSlim(0,N)` | 信号语义非锁 |

### 决策7：LockRegistry 诊断层 — 全局注册表 + 自动死锁检测

扩展 AsyncLock 接入 `LockRegistry` 静态诊断层，卡死时精确定位"哪个锁被哪个线程持有、等了多久、从哪获取的"。

**诊断能力**：

| 能力 | 实现 |
|------|------|
| 全局注册表 | `LockRegistry` 记录所有锁实例的实时持有/等待状态 |
| 调用栈捕获 | 获取/等待时用 `Environment.StackTrace` 记录（AOT 兼容，不用 StackFrame.GetMethod()） |
| 后台扫描 | 5s 间隔定时检测持有/等待超时并告警 |
| 超时告警 | 等待 30s / 持有 5s 阈值输出诊断（锁名+线程+调用栈） |
| DumpAll() | 卡死时一键输出所有锁状态 + 双方调用栈 |
| 自动死锁检测 | wait-for graph DFS，在 `OnWaitStart` 和后台扫描时自动运行，检测到等待环时输出 `DEADLOCK-DETECTED` 报告 |

**死锁检测身份选型**：

| 方案 | 结论 |
|------|------|
| `AsyncLocal<int?>` 异步流身份 | ❌ 实测不跨 await 传播（每次续体生成新 ID） |
| `Thread.ManagedThreadId` | ✅ 同步+async 均可靠，线程池复用产生的自环也是死锁（AsyncLock 不支持重入） |
| 调用栈哈希 | ❌ 开销大，async 续体调用栈不同 |

死锁检测用线程ID构建等待图，FlowId（AsyncLocal）仅用于诊断显示。

**替换范围**：

| 替换 | 文件数 | 说明 |
|------|--------|------|
| `lock` → `using (asyncLock.Lock())` | 36 | 专用锁字段 + 集合锁字段 |
| `SemaphoreSlim(1,1)` → `AsyncLock` | 5 | WaitAsync/Release → LockAsync/guard.Dispose |
| **保留** `ReaderWriterLockSlim` | 10 | 高性能读写锁，位置少 |
| **保留** 限流 `SemaphoreSlim(n>1)` | — | 非互斥 |
| **保留** per-key 局部变量锁 | 5 | 持有极短，诊断价值低 |

**向后兼容**：AsyncLock 保留全部原有 API，173 处现有使用零改动自动获得诊断能力。新增 `AsyncLock(string name)` 具名构造。

**验证**：18 个单元测试覆盖互斥/具名/TryLock/超时/Dispose/诊断sink/后台扫描/同步死锁/async死锁，全通过。

## 替代方案

### 替代1：不引入参数作假构造，直接用 `new AsyncLock()`

- 优点：API 干净，不引入"作假"概念
- 缺点：批量替换时每个构造点都要改参数，无法用脚本/分析器自动转换
- 放弃原因：用户明确要求参数兼容以降低替换成本

### 替代2：AsyncLock 直接继承或实现 SemaphoreSlim 接口

- 优点：完全 API 兼容，零改动替换
- 缺点：SemaphoreSlim 不是接口，是 sealed class，无法继承；且 AsyncLock 的 `LockAsync → ValueTask<Guard>` 语义与 `WaitAsync → Task` 不同
- 放弃原因：技术上不可行

### 替代3：不引入 IFileAccessCoordinator，直接在每个服务里 if-else 切换

- 优点：改动小
- 缺点：锁逻辑散落，无法统一切换，违反"可剥离"诉求
- 放弃原因：不符合用户需求2

### 替代4：用 ReaderWriterLockSlim 替代 AsyncLock 做文件读写锁

- 优点：读写分离，读多写少场景性能更好
- 缺点：非异步友好（有 Async 版本但分配多），且用户明确要求 AsyncLock
- 放弃原因：与用户诉求冲突

## 后果

- **正面**：
  - 互斥锁统一为 AsyncLock，消除 AsyncLock/SemaphoreSlim 二元选择
  - 复用现有 AsyncFileLock，零新增抽象，文件锁已有 5 个消费方稳定运行
  - 参数兼容构造降低迁移成本
  - Roslyn CodeFix 自动化转换，减少人工错误
- **负面**：
  - "参数作假"构造引入隐式约束（非 (1,1) 运行时抛异常），需文档说明
  - 公开属性 SemaphoreSlim 的 ABI 变更需协调消费方
- **中性**：
  - 信号量/并发限流场景继续用 SemaphoreSlim，两者并存（各司其职）
  - 文件锁跨进程（Mutex）比进程内更强，安全冗余但无害

## 待确认问题

1. ~~**AsyncLock 高性能实现方案**~~：✅ 已确认 — 回退为 SemaphoreSlim 包装（决策2，基准测试证明自实现慢 1.3-3.5x）
2. ~~**IFileAccessCoordinator 范围**~~：✅ 已确认 — 不实现 IFileAccessCoordinator，复用现有 AsyncFileLock 跨进程互斥锁（决策3，用户确认"原本就挺好用的"）
3. ~~**worktree 隔离模式触发方式**~~：✅ 已确认 — 与本次 AsyncLock 统一工作解耦，不在本 ADR 范围内
4. ~~**迁移节奏**~~：✅ 已确认 — 按模块渐进式（辅助 Roslyn CodeFix），不一次性全量替换
5. ~~**公开属性 SemaphoreSlim**~~：✅ 已确认 — 按语义区分：n>1 限流锁保留 SemaphoreSlim；(1,1) 互斥锁替换为 AsyncLock 并同步改消费方

## 确认状态

所有 5 个待确认问题已全部确认。AsyncLock 替换迁移已完成（Vault → Infrastructure → Core → Services → Composition → App 全层编译通过，Host.Tests 862 通过 / Infra.IO 132 通过 / Infra.Utils 560 通过）。ADR 状态改为 `accepted`。

## 后续变更（由 ADR-0060 引入，本 ADR 正文保持不变）

> 以下变更由 [0060](0060-asynclock-sync-trylock-fireandforget-deadlock.md) 决策引入，本 ADR 正文（决策2 的 `LockAsync/Lock` API 描述、决策7 的重入检测）已部分被取代。

| 本 ADR 描述 | 实际现状（0060 后） | 取代决策 |
|------------|---------------------|----------|
| `LockAsync() → ValueTask<AsyncLockGuard>`、`Lock() → AsyncLockGuard` | `TryLock() → IDisposable?`、`TryLockAsync() → ValueTask<IDisposable?>`（超时返回 null） | 0060 决策1 |
| `CheckReentrancy` + `LockReentrancyException` 抛异常检测同步重入（决策7） | 已移除 `CheckReentrancy`（ThreadId 在 async/await 下因线程池复用不可靠）；改为 `TryLock` 超时返回 null + `TrySetResult` 移到锁外 | 0060 决策1/3，详见 [0059](0059-asynclock-reentrancy-detection.md)（已 superseded） |

本 ADR 仍有效的部分：决策1（参数兼容构造）、决策3（复用 AsyncFileLock）、决策4（公开属性 SemaphoreSlim 按语义区分）、决策6（锁分类归宿）、决策7 的 LockRegistry 诊断层（DumpAll/后台扫描/死锁检测 wait-for graph DFS）。
