# 0158 全仓无锁化与业务级并发规约

- 状态: proposed
- 日期: 2026-11-19
- 关联: [TASK099](../task/TASK099-全仓无锁化与风格统一.md)
- 取代: [TASK079](../task/TASK079-锁改造为只读不可变CAS-杜绝死锁.md) 中“必须保留锁”的例外清单
- 参考: [DSG038](../../../modules/async_primitives/docs/design/DSG038-deadlock-elimination-overhaul-master-plan.md)、[REF006](../../../modules/async_primitives/docs/refactor/REF006-lock-to-asynclock.md)、[REF007](../../../modules/composition/docs/refactor/REF007-lock-to-channel-pipeline-plan.md)、[ADR 0059](../../archived/adr/security_concurrency/0059-asynclock-reentrancy-detection.md)

## 背景

项目已经积累了多层锁基础设施：`lock`、`System.Threading.Lock`、`SemaphoreSlim`、`ReaderWriterLockSlim`、`Mutex`、`AsyncLock`、`AsyncLockedDictionary`、`LockRef`、`TimeoutLock`、`OrderedLockManager`。它们并没有消除死锁，只是把死锁从“立即发生”变成“超时后发生”：

- 生产代码仍有 117 处 `lock` 语句，其中 `native_gui` 82 处、`native_tui` 13 处、`modules` 22 处。
- `AsyncLock` 仍被 47 个生产文件声明为字段，共 101 处获取点；`AsyncLockedDictionary`、`LockRef`、`TimeoutLock` 还在继续扩散这套模型。
- `SemaphoreSlim` 有 84 处生产引用、21 处 `new SemaphoreSlim`，同时承担互斥、限流、信号三种语义。
- `PithosDb`、`LeveledCompactor`、`HnswAnn` 仍使用 `ReaderWriterLockSlim`；跨进程路径仍使用命名 `Mutex`。
- `AsyncLock` 的超时/重试/降级策略把并发错误变成运行期随机行为，调用方必须同时理解锁顺序、超时、重试和补偿。

根因不是“锁的粒度不够细”，而是**共享可变状态没有唯一所有者**。只要“谁能改这份状态”藏在调用时序里，锁顺序、重入、超时和取消就会持续制造理解成本与死锁。用户要求以业务层规约并发：去掉所有用户态显式锁，用状态所有权和消息传递表达并发。

本 ADR 的“零锁”指**项目代码不直接持有互斥/阻塞同步原语**，不包含 BCL 内部实现（例如 `Channel<T>` 内部可能使用同步原语）。`Interlocked`、`Volatile`、`ImmutableInterlocked` 属于无锁原子操作，允许使用。

## 决策

### D1 零显式锁红线

生产代码、测试代码、工具代码一律禁止以下原语：

- `lock` 语句、`Monitor.Enter/Exit`、`System.Threading.Lock`
- `SemaphoreSlim`、`Semaphore`
- `Mutex`、`ReaderWriterLockSlim`、`SpinLock`
- `ManualResetEventSlim`、`AutoResetEvent`、`ManualResetEvent`、`CountdownEvent`、`Barrier`
- 项目自建锁封装：`AsyncLock`、`LockRef`、`AsyncLockedDictionary`、`TimeoutLock`、`OrderedLockManager`

不再新增“带超时的锁”“可重入锁”“分片锁”等替代品。超时不是无锁，重试也不是无锁。

### D2 允许的并发原语

| 场景 | 唯一推荐实现 |
|------|--------------|
| 一份可变状态有多个写者 | Actor 单写者邮箱（`Channel<T>` + 单 Consumer），状态由 Consumer 独占 |
| 小而热的状态读改写 | 不可变快照 + `Interlocked.CompareExchange` / `ImmutableInterlocked.Update` |
| 按键串行 | 分片 Actor 或 `ConcurrentDictionary<TKey, Actor>`，每个 key 一个所有者 |
| 有界并发 | `Channel` worker pool 或 `Parallel.ForEachAsync`，不再用信号量计数 |
| 只初始化一次 | `Interlocked.CompareExchange<Task>` 单飞，或 Actor 初始化命令 |
| 等待一个业务事件 | `TaskCompletionSource` / `Channel`，禁止阻塞事件对象 |
| 跨进程互斥 | 原子文件租约（`FileMode.CreateNew` + owner token + TTL + 心跳），禁止命名 `Mutex` |
| 读多写少的大状态 | 不可变快照发布；读者拿快照，写者串行产生新快照 |

### D3 状态所有权规则

1. 每个可变字段必须能回答“谁是唯一写者”。回答不了就不允许存在。
2. Actor 的命令处理函数是唯一写入口；外部只能发消息或读快照。
3. 读路径返回不可变快照或值拷贝，不返回内部可变容器。
4. 生命周期由所有者管理：先停止接收新命令，再排空邮箱，最后释放资源。
5. 禁止在 Actor Consumer 内同步等待另一个 Actor；需要组合时用 Tell + 后续命令，或由上层编排。

### D4 Ask/Tell 规则

- 副作用命令默认 `Tell`（发完即走），不等待回复。
- 只有只读查询允许 `Ask`，并且必须有超时和等待图诊断。
- `AskWithRetryAsync` 只允许用于实现 `IIdempotent` 的命令；非幂等命令禁止自动重试。
- `Dispose/DisposeAsync` 路径禁止 `Ask`，只允许 `Tell`/`TrySend`。
- 现有 25 处生产 `AskAwait` 调用逐项审计：能改 Tell 的改 Tell，必须 Ask 的补幂等标记与重试策略。

### D5 UI 线程亲和

`native_tui` 与 `native_gui` 的 UI 状态遵循“UI 线程是唯一写者”的业务规约，不使用锁保护 UI 状态：

- TUI 输入由输入 Channel 单读者消费，渲染状态只在渲染循环线程修改。
- GUI 的 DSH 服务状态由各自 Actor 持有，UI 通过消息/快照读取。
- 禁止用锁跨 UI 线程和后台线程共享可变集合；跨线程只传消息或不可变快照。

### D6 跨进程互斥

命名 `Mutex` 不再作为白名单。跨进程所有权改为**带租期的原子文件租约**：

- 获取：在租约目录用 `FileMode.CreateNew` 创建 owner 文件，写入进程 ID、进程启动时间、随机 token、到期时间。
- 心跳：持有者周期性原子刷新到期时间。
- 释放：仅当 owner token 匹配时删除自己的 owner 文件。
- 接管：只有租约过期才允许把旧文件原子重命名到归档名后重新竞争；不做阻塞等待。
- 业务语义：获取失败立即返回“降级/稍后重试/只读”，由上层决定，不在锁上等待。

该方案仍需在 B5 用真实多进程验收证明：无重复 owner、崩溃后 TTL 可恢复、数据不损坏。

2026-11-19 用户确认：采用原子文件租约，不保留命名 `Mutex` 白名单。

### D7 门禁

新增分析器规则 `JCC4008`（编号当前未占用）：禁止显式锁原语，Severity = Error。同时增加 CI 源码扫描作为第二道门禁，防止分析器漏检或新项目未接入。

- 现有 JCC4001-JCC4007 是“锁内风险”规则，保留到锁生态移除；其修复建议不得再推荐 `SemaphoreSlim`/`AsyncLock`。
- `DeadlockAuditSection` 提示词改为“发现锁即要求改为 Actor/不可变+CAS/Channel”，不再推荐 `TimeoutLock`。
- 门禁基线只减不增；每一批必须让基线数字下降，最终归零。

### D8 移除锁生态

`AsyncLock`、`LockRegistry`、`LockRef`、`AsyncLockedDictionary`、`TimeoutLock`、`OrderedLockManager`、`LockTimeoutPolicy` 及其测试全部退出编译。

按 AGENTS.md 的禁删规则，源文件移动到仓库根 `.xxx/` 归档，不直接删除；引用、`GlobalUsings`、分析器、提示词同步清理，禁止保留“兼容层”。

### D9 风格统一

无锁化不是孤立改造。与并发直接相关的风格债务同步清理：

- `async void` 仅允许 UI 事件处理器；文件监听器改为 `async Task` + 队列。
- 禁止无界 Channel 作为默认选择，必须有容量或明确背压理由。
- 禁止同步阻塞异步（`.Result`、`.Wait()`、`.GetAwaiter().GetResult()`）出现在异步链路。
- 锁相关过时注释（如“读操作走 lock(entries)”）必须随实现同步更新。
- 命名、目录、`GlobalUsings`、`#pragma`、null-forgiving 按 AGENTS.md 统一治理，详见 TASK099 的 B7。

## 替代方案

| 方案 | 结论 | 原因 |
|------|------|------|
| A. 保留 AsyncLock，继续加超时/重试 | 否决 | 仍是锁，仍会死锁；超时把错误推迟到运行期，调用方仍需理解锁顺序 |
| B. `lock` 全部换 `SemaphoreSlim` | 否决 | 现有 JCC4001 的建议；语义相同，还引入异步等待和资源释放问题 |
| C. Actor + 不可变快照 + CAS + Channel + 文件租约 | 采用 | 状态所有权显式，读路径无阻塞，跨进程也不依赖 OS Mutex |
| D. PithosDb 独立进程隔离 | 暂缓 | IPC 成本高，且不能解决其余 100+ 处锁；作为 PithosDb 的备选回退 |

## 后果

正面：

- 死锁类问题在架构层消失，而不是靠超时兜底。
- 并发行为可解释：谁拥有状态、消息顺序、快照版本都可见。
- 可观测性从“锁等待图”转为“邮箱深度、命令延迟、快照版本”。
- 测试可以直接驱动 Actor 消息，不依赖时序碰运气。

负面：

- 需要把分散的字段收敛到 Actor，前期改动面大。
- Actor 带来额外分配和调度开销；极热的小状态需要改用不可变 CAS。
- 跨进程文件租约引入 TTL、时钟偏差、崩溃恢复等新业务规则，需要真实多进程验收。
- 移除锁生态会破坏现有内部 API，按项目规则不做向后兼容。

## 验收

- 每个批次跨域变更执行全仓编译 `pwsh non_deliverables_tools/scripts/Build-AllDomains.ps1`。
- 每个批次必须启动真实 exe 验收：`jcc.exe`、`NativeGui.Host.exe`、必要时多进程 `jcc.exe`；单元测试只作为回归，不作为验收证据。
- 验收记录写入 TASK099 的四列验收表，并附 `.xxx/logs/` 下的命令、退出码和关键日志。
- `JCC4008` 基线最终为 0；任何新增锁原语使 CI 失败。
