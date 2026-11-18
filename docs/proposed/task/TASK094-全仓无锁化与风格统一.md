# TASK094 全仓无锁化与风格统一

- 状态: proposed
- 日期: 2026-11-19
- 决策: [ADR 0158 全仓无锁化与业务级并发规约](../adr/0158-lock-free-business-concurrency.md)
- 验收规范: [ADR 0080 手动 exe 测试指南](../../implemented/adr/engineering_ci_test/0080-manual-exe-testing-guide.md)、[ADR 0077 MockServer + jcc 联合测试](../../implemented/adr/engineering_ci_test/0077-mockserver-jcc-joint-testing.md)
- 前置文档: [DSG038](../../../modules/async_primitives/docs/design/DSG038-deadlock-elimination-overhaul-master-plan.md)、[REF006](../../../modules/async_primitives/docs/refactor/REF006-lock-to-asynclock.md)、[REF007](../../../modules/composition/docs/refactor/REF007-lock-to-channel-pipeline-plan.md)

## 1. 用户诉求

1. 检查当前项目缺陷，制作可执行计划。
2. 统一整个项目的风格（语法、写法、代码味道等）。
3. 全仓无锁化：去掉所有锁，包括 Actor 内的锁、`SemaphoreSlim`、`AsyncLock`、`ReaderWriterLockSlim`、`Mutex` 等；以业务层规约并发，避免锁导致理解困难与死锁。
4. 验收必须启动真实 exe，不得用单元测试做挡箭牌。

本文把这四项拆成“零锁红线 + 业务级并发规约 + 风格门禁 + exe 验收”四部分。

## 2. 基线与缺陷

### 2.1 锁原语盘点（生产代码，排除 tests/tools/generators）

| 原语 | 规模 | 主要位置 | 结论 |
|------|------|----------|------|
| `lock` 语句 | **117 处 / 27 文件** | `native_gui` 82、`native_tui` 13、`modules` 22 | 全部要消除 |
| `AsyncLock` 字段 | **47 文件 / 101 处获取** | `modules/**` | 全部要消除 |
| `SemaphoreSlim` | **84 处引用 / 38 文件 / 21 处 `new`** | `modules/**`、`native_gui` | 互斥、限流、信号三种语义都要换 |
| `ReaderWriterLockSlim` | **3 个类型 / 24 处获取** | `PithosDb`（14）、`HnswAnn`（7）、`LeveledCompactor`（3） | 全部要消除 |
| `Mutex` | **2 处 `new Mutex`** | `CrossProcessLock` | 改为文件租约 |
| `System.Threading.Lock` | **10 个字段**（native_gui 另有 object 门闩，合计 82 处 `lock` 语句） | `native_gui/src/NativeGui.Core/web_host` | 全部要消除 |
| `ManualResetEventSlim` | **2 处** | `ReplShell:595`、`BridgeEventPump:15` | 改为 Channel/TCS |
| 自建锁封装 | `AsyncLockedDictionary`、`LockRef`、`TimeoutLock`、`OrderedLockManager` | 见 2.3 | 随锁生态移除 |

> 统计口径：`rg` 排除 `obj/bin/artifacts/tests/test/non_deliverables_tools`；`lock` 统计已排除 `ThinkingStoreCommands.cs:6` 的注释命中。数字随代码变化，B0 固化为基线文件后以基线为准。

#### `lock` 语句分布

| 区域 | 文件数 | 语句数 | 代表文件（计数） |
|------|-------:|-------:|------------------|
| `native_gui` | 16 | 82 | `DshSessionStreamHub.cs`（22）、`DshTerminalService.cs`（12）、`DshSettingsStore.cs`（8）、`DshApprovalBroker.cs`（7）、`DshMessageFeedbackStore.cs`（6）、`DshGoalStore.cs`（6） |
| `native_tui` | 3 | 13 | `RawStdinPump.cs`（5）、`LazyEngine.cs`（4）、`EngineTurnClient.cs`（4） |
| `modules` | 8 | 22 | `ErrorConsole.cs`（5）、`ConsoleSuppressionGate.cs`（5）、`LruBlockCache.cs`（4）、`S3FifoBlockCache.cs`（4）、`LspConnection.cs`（2）、`RgEngine.cs`（1）、`ConfigurationService.cs`（1） |

代表行：

- `modules/apps/cli/app/diagnostics/ErrorConsole.cs:22,47,55,73,83`
- `modules/abstractions/abstractions.primitives.utils/src/diagnostics/ConsoleSuppressionGate.cs:40,49,68,80,109`
- `modules/tools/cli_nodes/groups/code/private/LspConnection.cs:90,116` + `SemaphoreSlim _writeLock` 第 23 行
- `modules/tools/rg/RgEngine.cs:40`（PLINQ `ForAll` 内）
- `modules/guard/guard/configuration/services/ConfigurationService.cs:59`
- `modules/persistence/pithosdb/Core/LruBlockCache.cs:29,45,65,78`
- `modules/persistence/pithosdb/Core/S3FifoBlockCache.cs:63,78,100,108`
- `native_gui/src/NativeGui.Core/web_host/DshSessionStreamHub.cs:31,38,89,109,123,139,176,216`
- `native_gui/src/NativeGui.Core/web_host/DshTerminalService.cs:66,109,118,129,153,186`
- `native_tui/src/NativeTui.Core/repl/input/RawStdinPump.cs:93,121,145,208,222`
- `native_tui/src/NativeTui.Core/bridge/LazyEngine.cs:18,33,43,56`
- `native_tui/apps/NativeTui.Host/EngineTurnClient.cs:73,101,107,213`

#### `AsyncLock` 生态

| 资产 | 证据 | 说明 |
|------|------|------|
| `AsyncLock` 字段/获取 | 47 文件 / 101 处 | 互斥、限流、单飞、状态转换全部混用同一类型 |
| 显式 `new AsyncLock(...)` | 14 处 | 含 `ExecutionContext.cs:18`、`IOThrottleService.cs:44-46`、`GoalGraphEngine.cs:123,148`、`AgentCoordinator.cs:78,107`、`ForkSubAgentManagerActor.cs:93,555` |
| `AsyncLockedDictionary` | `FileHistoryService.cs:16` | 生产唯一消费方，内部仍用 `AsyncLock` |
| `LockRef` | `DeferredMailService.cs:102-109`、`IOThrottleService.cs:163` | 把“获取锁”伪装成返回值 |
| `TimeoutLock` | `CSharpSymbolExtractor.cs:86,102` | 内部用 `AsyncLock` 包一层超时 |
| `OrderedLockManager` | 仅定义文件，无外部消费 | 死代码，直接归档 |
| `LockRegistry` / `LockTimeoutPolicy` | `AsyncLock.cs` 诊断/策略耦合 | 锁生态移除后一并归档 |

典型用途分类：

- **单飞初始化**：`LspService`、`PuppeteerBrowserAutomationService`、`ConfigPersistentServiceBase`、`FileBasedTaskService`、`ChromeIntegrationService`、`SandboxIpcClient`。
- **状态转换**：`StateMachine`、`Fsm`、`AgentStateMachine`、`AgentBase`、`SshSession`、`WebSocketTransport`。
- **并发限流**：`ExecutionContext`、`IOThrottleService`、`GoalGraphEngine`、`AgentCoordinator`、`ForkSubAgentManagerActor`、`StreamingToolExecutor`、`TreeSitterParserPool`。
- **单写者传输**：`TransportBase`、`BufferedChannel`、`SerialBatchEventUploader`、`BoundedUUIDSet`、`ResilientChannel`、`BridgeMain`。
- **鉴权刷新/状态**：`McpAuthProviders`、`McpPkceAuthProvider`、`McpOAuthService`、`McpAuthToolHandlers`、`InteractiveElicitationHandler`。
- **索引/注册**：`SkillSearchService`、`HookConfigurationManager`、`HotSpotSpawnIntegration`。

#### `SemaphoreSlim` 语义分布

| 语义 | 代表位置 | 目标实现 |
|------|----------|----------|
| 互斥（1,1） | `DshWorkspaceStore.cs:12`、`DshSessionJournal.cs:13`、`DshProviderSettingsStore.cs:34`、`ConcurrentDag.cs:8`、`HighWaterMarkManager.cs:14`、`LeasedKvStoreAdapter.cs:14` | Actor 单写者 / 不可变 CAS |
| 跨进程/租约互斥 | `NamedMutexMailboxLock.cs:15,44`、`PithosKvStoreLease.cs:13`、`PithosKvStoreLeaseFactory.cs:29` | 原子文件租约 |
| 限流（n,n） | `GatewayActor.cs:67,94`、`DshWebHost.cs:440`、`GitHubRunLogFilterRunner.cs:250,343,625`、`GitHubToolHandlers.Run.cs:472,1246`、`DownloadSession.cs:222`、`BatchDownloader.cs:32`、`AgentExecutionEngine.cs:34`、`TarballDownloader.cs:58` | `Channel` worker pool / `Parallel.ForEachAsync` |
| 信号（0,n） | `PithosDb.cs:28`、`GraphExecutionContext.cs:51`、`PriorityMailbox.cs:49`、`CommandQueue.cs:57` | `Channel` / `TaskCompletionSource` |
| LSP 写入串行 | `LspConnection.cs:23` | 单写者 Channel |

#### `ReaderWriterLockSlim` 与跨进程 `Mutex`

- `modules/perception/code_index/vector/HnswAnn.cs:23`：读写锁保护 HNSW 图。
- `modules/persistence/pithosdb/PithosDb.cs:25,62,74`：14 处获取；`Compaction/LeveledCompactor.cs:21,53`：3 处获取。
- `modules/abstractions/infrastructure/async_file_lock/CrossProcessLock.cs:48,50`：`Global\`/`Local\` 命名 `Mutex`。
- `NamedMutexMailboxLock`、`PithosKvStoreLease`、`PithosKvStoreLeaseFactory`、`LeasedKvStoreAdapter` 构成跨进程租约锁链。

### 2.2 并发正确性缺陷

| 缺陷 | 证据 | 影响 |
|------|------|------|
| `AskAwait` 单次 10s 无重试 | `ActorBase.cs:621,635`；生产 25 处调用；`AskWithRetryAsync`（`ActorBase.cs:691`）**0 处生产调用** | 线程池饥饿/Consumer 阻塞时直接抛死锁异常，没有幂等重试兜底 |
| 无界邮箱 | 8 处 `Channel.CreateUnbounded`（`AgentRegistry.cs:37`、`Inbox.cs:10`、`GoalEngine.cs:18`、`Win32ConsoleInputPump.cs:14`、`GitHubRunLogFilterRunner.cs:341,626`、`DshWorkspaceFilesService.cs:196` 等） | 生产速度快于消费时内存无界增长，最终 OOM |
| `async void` | `InMemoryFileSystemWatcher.cs:76,107`、`PhysicalFileSystemWatcher.cs:90,99,108,117` | 文件事件异常无法被调用方捕获，直接炸进程 |
| 锁内做 I/O/事件/Console | `ErrorConsole`、`ConsoleSuppressionGate`、`DshSessionStreamHub`（await 在锁外但状态操作多） | stderr 管道满/回调重入时持锁阻塞 |
| 单写者语义缺失 | `LspConnection` 用信号量串行 stdin；`DshConPty` 用 `Lock _writeGate`；`RgEngine` 用 `lock` 包 PLINQ 回调 | 写入交错、回调重入、吞吐下降 |
| LSM 核心读写锁 | `PithosDb` 14 处、`LeveledCompactor` 3 处 | 压缩与写入路径一旦阻塞，整个存储不可用 |
| HNSW 读写锁 | `HnswAnn.cs:23` 及 7 处获取 | 索引构建与查询互相阻塞，锁顺序难验证 |

### 2.3 风格缺陷

| 项目 | 规模（生产，排除 tests/tools/generators） | 目标 |
|------|------------------------------------------|------|
| `#pragma warning disable` | **112 行 / 103 文件**（JCC11005 48、JCC1018 33、JCC1017 28、JCC9203 6） | 归零；修根因或修分析器，禁止抑制 |
| 文件内 `using` | **188 行 / 75 文件**（含生成器为 216 行 / 83 文件） | 全部收敛到 `GlobalUsings.cs` |
| null-forgiving `!` | grep 粗测 **272 处 / 145 文件**（含误报） | Roslyn 精确清零；禁止 `!` 绕可空分析 |
| `async void` | **6 个生产方法**（2 个文件监听器） | UI 事件处理器以外全部改 `async Task` |
| 无界 Channel | **8 处** | 有界或写明背压理由 |
| 目录命名 | **127 个生产路径**不符合 `lower_snake`（多为与 csproj 同名的项目目录，如 `guard.security`、`*.generator`；嵌套源码目录有 `DependencyInjection`、`Serialization`、`Adapters`、`Core`、`Compaction`、`Storage`） | 项目目录按 csproj 名登记白名单；嵌套源码目录改 `lower_snake` |
| 重复命名空间 | `namespace PithosDB.Core.Core` 共 8 个文件（`WriteAheadLog.cs`、`MemTable.cs`、`LruBlockCache.cs` 等） | 改为 `PithosDB.Core`，目录同步小写 |
| 过时注释 | `ThinkingStoreCommands.cs:6` 仍写“读操作走 lock(entries)”，实际无锁 | 注释与实现同步 |
| 同步阻塞异步 | grep 粗测 418 处（含 `.Result` 属性误报） | Roslyn 分类后清零异步链路阻塞 |

### 2.4 现有文档/工具冲突

| 文件 | 冲突 |
|------|------|
| `docs/proposed/task/TASK079-锁改造为只读不可变CAS-杜绝死锁.md:68-82` | “必须保留锁”清单与新零锁目标冲突，B0 标注被 ADR 0158 取代 |
| `modules/cognition/prompts/sections/domain/DeadlockAuditSection.cs:23,52` | 推荐 `TimeoutLock`、分片 `TimeoutLock`，与新目标相反 |
| `modules/generators/aot_safety.generator/rules/concurrency/LockInAsyncMethodRule.cs:10` | JCC4001 建议“改用 SemaphoreSlim(1,1)”，与新目标相反 |
| JCC4001-JCC4007 | 全部围绕“锁内风险”，没有“禁止锁原语”；JCC4008 编号未占用 |
| `REF006` / `REF007` / `DSG038` | 记录的是“lock → AsyncLock / Channel”中间态，需要在 B6 标注终态 |

### 2.5 可复用的无锁基建

不重复造轮子，优先复用：

- `ActorBase` + `Channel<T>` 邮箱 + 等待图诊断（`modules/async_primitives/async_lock/actor/`）。
- `ImmutableHamT` + `ImmutableInterlocked` + `Interlocked.CompareExchange`（参考 `AgentServiceImpl.cs:52,96-104`）。
- `MapRegistry`、`CasRetry`、`ConcurrentDictionary`、`PriorityMailbox`（先把内部信号量换成 Channel）。
- 现有 `ConsoleActor`、`AsyncStderrWriter`、`ResilientChannel`、`BoundedUUIDSet`（改造其内部锁后复用）。
- ADR 0137 的 16 域结构；跨域变更走全仓编译。

## 3. 目标规约

| 编号 | 规约 | 判定 |
|------|------|------|
| R1 | 生产/测试/工具代码零显式锁原语 | `JCC4008` + CI 扫描为 0 |
| R2 | 每份可变状态有唯一所有者 | 代码审查：字段写点只能出现在 Actor Consumer 或 owner 方法 |
| R3 | 跨线程只传消息或不可变快照 | 禁止共享可变集合；读返回快照 |
| R4 | 副作用命令默认 `Tell`，只读查询才 `Ask` | Ask 必须有超时；重试只允许 `IIdempotent` |
| R5 | 有界并发用 Channel worker pool / `Parallel.ForEachAsync` | 禁止 `SemaphoreSlim` 计数 |
| R6 | 单飞用 CAS Task 或 Actor 初始化命令 | 禁止 `AsyncLock`/双检查锁 |
| R7 | UI 线程是 UI 状态唯一写者 | 禁止锁跨 UI/后台线程 |
| R8 | 跨进程用原子文件租约 + TTL | 禁止命名 `Mutex`；崩溃可恢复 |
| R9 | 生命周期先停写、再排空、后释放 | Dispose 路径禁止 `Ask` 和阻塞等待 |
| R10 | 风格零新增债务 | 新代码不得引入 `#pragma`、local `using`、`!`、`async void`、无界 Channel |
| R11 | 验收只认真实 exe | 单元测试只做回归，不替代 exe 证据 |

## 4. 批次计划

| 批次 | 主题 | 范围 | 依赖 | 退出标准 |
|------|------|------|------|----------|
| B0 | 门禁与验收入口 | JCC4008、CI 扫描、提示词/分析器修正、TUI/GUI/跨进程 exe 挂钩 | 无 | 注入违规构建失败；exe 挂钩可运行 |
| B1 | 低风险模块锁 | ErrorConsole、ConsoleSuppressionGate、LspConnection、RgEngine、ConfigurationService、AsyncLazy、FileHistoryService | B0 | 模块锁清零；jcc 真机无卡死 |
| B2 | UI 宿主锁 | native_tui 3 文件 + 2 事件；native_gui 16 文件 / 82 锁 | B0 | UI 锁清零；TUI/GUI smoke 通过 |
| B3 | AsyncLock 与限流 | 47 文件 / 101 获取；SemaphoreSlim 限流/信号 | B1、B2 | AsyncLock 生产引用为 0；并发回合通过 |
| B4 | 存储与索引核心 | PithosDb、LeveledCompactor、HnswAnn、Lru/S3Fifo、HighWaterMark、ConcurrentDag | B3 | RWLock 为 0；多进程存储验收通过 |
| B5 | 跨进程租约 | CrossProcessLock、NamedMutexMailboxLock、PithosKvStoreLease/Factory、LeasedKvStoreAdapter | B4 | `Mutex` 为 0；崩溃接管验收通过 |
| B6 | 移除锁生态 | AsyncLock/LockRegistry/LockRef/AsyncLockedDictionary/TimeoutLock/OrderedLockManager/LockTimeoutPolicy + 测试 + JCC4001-4007 | B1-B5 | 全仓编译；锁生态退出编译；exe 回归 |
| B7 | 风格统一 | pragma、local using、null-forgiving、async void、无界 Channel、命名空间、目录、过时注释、文档收敛 | B6 | 风格门禁归零；exe 回归 |

### B0 门禁与验收入口

1. 新增 `ForbiddenLockPrimitiveRule`（JCC4008），检测：
   - `lock` 语句、`Monitor.Enter/Exit`、`System.Threading.Lock`
   - `SemaphoreSlim/Semaphore`、`Mutex`、`ReaderWriterLockSlim`、`SpinLock`
   - `ManualResetEventSlim/AutoResetEvent/ManualResetEvent/CountdownEvent/Barrier`
   - `AsyncLock`、`LockRef`、`AsyncLockedDictionary`、`TimeoutLock`、`OrderedLockManager`
2. 新增 CI 脚本 `non_deliverables_tools/scripts/Verify-NoLockPrimitives.py`，与 JCC4008 双保险；基线 `non_deliverables_tools/lock_gate/lock-primitive-baseline.txt` 只减不增（`|0` 行为分析器专用间接消费豁免，B6 清零）。
3. 修正提示词与分析器文案：
   - `DeadlockAuditSection` 改为“发现锁 → 改 Actor/不可变+CAS/Channel/文件租约”，删除 `TimeoutLock` 和分片锁建议。
   - JCC4001 的修复建议删除“改用 SemaphoreSlim”，改为指向 JCC4008 与 Actor 范式。
   - `TASK079` 的“必须保留锁”清单标注被 ADR 0158 取代。
4. 固化 exe 验收入口：
   - TUI：`jcc.exe --selftest-tui` / `--selftest-input` 透传到 `NativeTui.Host` 现有 selftest（当前为 private，需要公开一个入口）；保留 `NativeTui.Demo.exe --selftest`、`--selftest-bash`、`--bridge-selftest`。
   - GUI：保留 `NativeGui.Host.exe --webui-smoke`；新增 `--dsh-smoke`，真实启动 DSH WebHost，完成一次会话/流式事件往返后退出。
   - 跨进程：新增 `jcc.exe --selftest-crossproc` 或独立 selftest 模式，用两个真实进程竞争同一租约/存储目录（依赖 B5 原子文件租约，挂在 B5 验收）。
5. 退出标准：故意新增一个 `lock`/`SemaphoreSlim`，JCC4008 与 CI 均失败；三个 exe 挂钩正常退出。

### B1 低风险模块锁（22 处）

| 文件 | 现状 | 改造 |
|------|------|------|
| `ErrorConsole.cs` | 5 处 `lock` 包 Console 渲染 | 全部投递到 `ConsoleActor`/非阻塞 stderr 队列，单写者消费 |
| `ConsoleSuppressionGate.cs` | 5 处 `lock` 保护静态深度/环形缓冲/writer 替换 | 不可变状态 + `Interlocked.Exchange`；环形缓冲单写者；writer 替换一次 CAS 发布 |
| `LspConnection.cs` | `SemaphoreSlim _writeLock` + 2 处 `lock(_stderrTail)` | stdin 写入改为单写者 Channel；stderr 环形缓冲改无锁快照 |
| `RgEngine.cs` | PLINQ `ForAll` 内 `lock(streamLock)` 包回调 | 结果先写 `ConcurrentQueue`/Channel，统一 drain 回调；或 PLINQ 改 `Parallel.ForEachAsync` |
| `ConfigurationService.cs` | `lock(_migrationLock)` 单飞迁移 | `Interlocked.CompareExchange<Task>` 单飞；失败可重试 |
| `AsyncLazy.cs` | `AsyncLock _gate` 单飞 | `Interlocked.CompareExchange<Task<T>>` 单飞；失败清理 |
| `FileHistoryService.cs` | `AsyncLockedDictionary` 版本计数 | 每路径 Actor 或不可变 `ConcurrentDictionary` + CAS |
| `ThinkingStoreCommands.cs:6` | 注释声称 `lock(entries)`，实际已无锁 | 更新注释；核对其 Actor 读路径是否返回不可变快照 |

验收：`jcc.exe --selftest-tui` + `JCC_DEBUGLOG=1` 高日志输出；jcc + MockServer 完成一次真实回合；LSP/搜索/索引命令并发触发；无超时、无写交错、无 `ActorAskDeadlockException`。

### B2 UI 宿主锁（native_tui 13 + native_gui 82）

| 范围 | 现状 | 改造 |
|------|------|------|
| `RawStdinPump.cs` | 5 处 `lock(_gate)` | 控制台输入线程只写 `Channel<KeyPress>`；泵单读者；状态归泵线程 |
| `LazyEngine.cs` | 4 处 `lock(_gate)` | CAS 单飞初始化；引擎句柄 volatile |
| `EngineTurnClient.cs` | 4 处 `lock(_turnLock)` | 回合 Actor：命令化 start/cancel/finish；状态只由 Consumer 写 |
| `ReplShell.cs:595` | `ManualResetEventSlim _inputWake` | `Channel<bool>` 或 `TaskCompletionSource` 唤醒 |
| `BridgeEventPump.cs:15` | `ManualResetEventSlim _completed` | `TaskCompletionSource` |
| `DshSessionStreamHub.cs` 22 锁 | 订阅表/流状态 | 订阅 Actor；订阅表不可变快照；事件入 Channel 单写者 |
| `DshTerminalService.cs` 12 锁 | 终端会话/缓冲 | 终端 Actor 独占会话；输出 Channel；读取快照 |
| `DshSettingsStore.cs` 8 锁 | 设置读写 | 不可变设置快照 + 串行写队列 |
| `DshGoalStore.cs` 6、`DshMessageFeedbackStore.cs` 6 | 持久化状态 | 存储 Actor + 原子文件替换 |
| `DshApprovalBroker.cs` 7、`DshUserQuestionBroker.cs` 4 | 待审批/待问答表 | 请求 Actor + TCS；移除时 CAS 摘除 |
| `DshWebHost.cs:440` 发送信号量 | WebSocket 发送互斥 | 每连接单写者 Channel |
| `DshConPty.cs:31` `Lock _writeGate` | ConPTY 写入 | 单写者 Channel |
| `DshSessionJournal.cs:13`、`DshWorkspaceStore.cs:12`、`DshProviderSettingsStore.cs:34` | 追加/工作区/设置互斥 | Actor/不可变快照 + 串行追加 |
| 其余 `DshSessionControlHub`、`DshDirectoryPicker`、`DshFileReferenceService`、`DshChangesProtocol`、`DshHostServices`、`DshWebAssets` | 2-3 处锁 | CAS/不可变快照；无状态服务去掉锁 |
| `native_gui` 10 个 `Lock` 字段 | `System.Threading.Lock` | 全部随上述改造移除 |

UI 规则：UI 线程只消费消息、渲染快照；后台 Actor 不得直接改 UI 集合。

验收：
- TUI：`jcc.exe --selftest-tui`、`jcc.exe --selftest-input`、`NativeTui.Demo.exe --selftest`、`--selftest-bash`、`--bridge-selftest`。
- GUI：`NativeGui.Host.exe --webui-smoke`（回归）+ `--dsh-smoke`（DSH 主路径）；真实打开窗口完成一次会话/流式事件后退出。
- 多会话并发：同时开两个会话、并发终端输出、审批/问答交错；日志无锁等待/超时。

#### B2 执行记录（native_tui + native_gui）

- native_tui 3 文件 + 2 事件已清零（提交 02d6dc2d8），selftest 全部 EXIT=0。
- native_gui 16 文件 / 82 锁 → 0：新增 `DshSerialMailbox` 作为 DSH 宿主状态单写者原语（有界 512 邮箱）；设置/目标/反馈/供应商设置/工作区/日志等存储改 Actor，事件回调（`PermissionPresetChanged`/`GoalChanged`）出 Actor 触发防重入死锁；审批/问答桥改 `Volatile` 通道快照；PTY 写入改单写者 Channel；WebSocket 每连接单写者发送器；静态资产解包 CAS 单飞；回合队列/终端/控制流/目录选择/文件引用按 Actor 或 `ConcurrentDictionary` CAS 重写。
- 真机 bug（顺手修复）：`Program.Main` 为 `async Task<int>` 时 `[STAThread]` 不生效（实测主线程 `apartment=MTA`），WebView2 环境创建报 `RPC_E_CHANGED_MODE`（0x80010106），`--webui-smoke` EXIT=3。改为同步 `int Main` 后真实窗口冒烟 EXIT=0（`WEBVIEW2-READY` + `BRIDGE-SMOKE ok`）；新增入口契约测试 `WebViewEntryPointTests`（async Main 红 → 同步 Main 绿），日志 `.xxx/logs/TASK094/gui_host_entry_red2.log` / `gui_host_entry_green.log`。
- `--dsh-smoke` 升级为真实主路径：首页注入 → `session/create` → `session/list` → `$events`/`session/follow` 双流 → `goals/create`+`goals/pause` → `$events` 收到 `goal/activation-changed` → 32 路 `goals/get` + 16 路 `settings/describe` 并发，全部走真实 HttpListener/WS/文件系统，数据目录隔离到系统临时目录（`DshHostServices` 的目标文件路径改由 `AppDataPaths` 注入）。

### B3 AsyncLock 与限流（47 文件 / 101 获取 + SemaphoreSlim）

按用途分批：

| 集群 | 代表文件 | 方案 |
|------|----------|------|
| 单飞初始化 | `LspService.cs`、`PuppeteerBrowserAutomationService.cs`、`ConfigPersistentServiceBase.cs`、`FileBasedTaskService.cs`、`ChromeIntegrationService.cs`、`SandboxIpcClient.cs` | `Interlocked.CompareExchange<Task>` 单飞；失败重试由调用方决定 |
| 状态机/暂停 | `StateMachine.cs`、`Fsm.cs`、`AgentStateMachine.cs`、`AgentBase.cs`、`SshSession.cs`、`WebSocketTransport.cs` | Actor 命令化状态转换；属性提供 volatile 快照 |
| 并发限流 | `ExecutionContext.cs`、`IOThrottleService.cs`、`GoalGraphEngine.cs`、`AgentCoordinator.cs`、`ForkSubAgentManagerActor.cs`、`StreamingToolExecutor.cs`、`TreeSitterParserPool.cs` | Channel permit/worker pool 或 `Parallel.ForEachAsync`；去掉 `AsyncLock(n,n)` |
| 单写者传输 | `TransportBase.cs`、`BufferedChannel.cs`、`SerialBatchEventUploader.cs`、`BoundedUUIDSet.cs`、`ResilientChannel.cs`、`BridgeMain.cs` | 单写者 Channel；序号/集合用 CAS |
| 鉴权刷新 | `McpAuthProviders.cs`、`McpPkceAuthProvider.cs`、`McpOAuthService.cs`、`McpAuthToolHandlers.cs`、`InteractiveElicitationHandler.cs` | CAS 单飞 + actor 刷新状态；过期 token 只产生一个刷新任务 |
| 索引/钩子 | `SkillSearchService.cs`、`HookConfigurationManager.cs`、`HotSpotSpawnIntegration.cs` | 索引 Actor；配置不可变+CAS |
| `AsyncLockedDictionary` / `LockRef` | `FileHistoryService.cs`、`DeferredMailService.cs`、`IOThrottleService.cs` | 每 key Actor / 单写者 |
| Semaphore 限流 | `GatewayActor.cs`、`GitHubRunLogFilterRunner.cs`、`GitHubToolHandlers.Run.cs`、`DownloadSession.cs`、`BatchDownloader.cs`、`AgentExecutionEngine.cs`、`TarballDownloader.cs` | Channel worker pool / `Parallel.ForEachAsync` |
| Semaphore 信号 | `PriorityMailbox.cs`、`CommandQueue.cs`、`GraphExecutionContext.cs`、`PithosDb.cs` | Channel/TCS；优先级邮箱用优先队列 + Channel 唤醒 |
| `AskAwait` 审计 | 25 处生产调用 | 能 Tell 的改 Tell；必须 Ask 的补 `IIdempotent` 与重试策略 |

验收：
- jcc + MockServer 并发 4 个真实回合；检查无死锁、无重复刷新、无丢失事件。
- jcc 对测试仓库执行真实代码索引/解析命令；观察 TreeSitter 并发无交错。
- TUI/GUI smoke 回归。

#### B3 执行记录

- 新增无锁原语：`AsyncGate`（任务链门闩，`modules/abstractions/abstractions.utils/state/AsyncGate.cs`）与 `PermitPool`（Channel 令牌池，支持热调整容量），统一承载互斥与 (N,N) 限流。
- 单飞初始化：`PuppeteerBrowserAutomationService`、`ConfigPersistentServiceBase`、`FileBasedTaskService` 改 `TaskCompletionSource` 单飞信号（CAS 创建、初始化完成统一 `TrySetResult`），消除 `AsyncLock` + double-check 模式。
- 状态机/暂停：`AgentStateMachine` 内部 `AsyncGate`；`AgentBase` 暂停语义重写为 TCS 门闩（Pause 创建、Resume/5s 超时清除），删除死代码 `_pauseLock`；`BridgeDeviceTokenService` 存储互斥改 `AsyncGate`。
- 并发限流：`ExecutionContext.ConcurrencyLock` → `PermitPool`；`GoalGraphEngine` 并发限流 → `PermitPool`；`ForkSubAgentManagerActor` 并发槽 → 热替换 `PermitPool`；`IOThrottleService`、`DeferredMailService`、`HotSpotSpawnIntegration` 去 `AsyncLock`/`LockRef`。
- 单写者传输：`BridgeMain` 待清理任务表改 `ConcurrentDictionary` + `ContinueWith` 摘除；`BridgePermissionCallbacks` 处理器表改 `ConcurrentDictionary` + `ImmutableList` CAS。
- 工作者池：`BatchDownloader`、`DownloadSession`、`AgentExecutionEngine` 改 `Interlocked.Increment` 领号 + 结果下标数组，去掉信号量。
- 信号/限流：`GatewayActor` 限流改预填具有界 `Channel<bool>`；`GraphExecutionContext.NodeCompletedSignal` 改无界唤醒 Channel（`WaitToReadAsync` + 1s 超时 + 排空），`PollingGraphScheduler`/`EventDrivenGraphScheduler` 写端 `TryWrite`。
- `PlanModeManager` 去掉 `LockRegistry.EnsureFlowRegistered`，改进程内单调序号（slug 仅作会话缓存键）。
- 基线：82 文件/372 处 → 54 文件/320 处；AsyncLock 模块外生产消费者为 0，剩余为 B4/B5/锁生态自身。
- exe 验收（ADR 0080/0077）：MockServer(Debug) + jcc(Debug) 并发 4 个真实回合，4 进程 EXIT=0，MockServer 记录 4 次 `chat/completions`，日志无异常/超时/死锁（`.xxx/logs/mockserver_concurrent.log`、`.xxx/logs/jcc_turn_1..4.log`，脚本 `.xxx/acceptance/run_concurrent_turns.sh`）；`jcc --selftest-tui` EXIT=0（HOST SELFTEST OK）。
- 工具环境问题（非本批回归）：本 worktree Debug 布局缺 `artifacts/bin/JoinCode/Debug/net10.0/jcc-rg.exe`（实际产物在 `artifacts/bin/rg/Debug/net10.0/`），`jcc rg` 子命令不可用；`jcc --selftest-input` 在非交互 bash 下收 0 键（需真实 TTY）。已记录，待构建布局/入口补齐。

### B4 存储与索引核心

| 文件 | 现状 | 方案 |
|------|------|------|
| `PithosDb.cs` | `ReaderWriterLockSlim` 14 处 + `_compactionSignal` | 写路径单写者 Actor；manifest/metadata 不可变快照；读路径拿快照；压缩信号改 Channel |
| `LeveledCompactor.cs` | 外部传入同一把 RWLock，3 处获取 | 压缩 Actor 独占；只接收不可变清单 |
| `HnswAnn.cs` | 7 处读写锁 | 写入 Actor；搜索读不可变图快照（双缓冲发布）；构建完成原子换快照 |
| `LruBlockCache.cs` / `S3FifoBlockCache.cs` | 各 4 处 `lock(_sync)` | 单写者 Actor 或近似淘汰 + CAS；读命中统计用 Interlocked |
| `HighWaterMarkManager.cs` | `SemaphoreSlim(1,1)` + `#pragma JCC9203` | CAS 单调推进；去掉 pragma |
| `ConcurrentDag.cs` | `SemaphoreSlim _lock` | 调度 Actor/不可变图快照 |

验收：
- 两个真实 jcc 进程对同一 `JCC_APP_DATA_FOLDER` 存储目录并发写入/读取，校验 JSON/文件完整性。
- 进程重启后恢复；压缩与读写并发时无超时、无损坏。
- HNSW 查询结果与改造前召回对比（固定样本集），确保不退化。

### B5 跨进程租约

| 文件 | 改造 |
|------|------|
| `CrossProcessLock.cs` | 删除 `Mutex`；改为原子 owner 文件租约（`CreateNew` + token + TTL + 心跳） |
| `NamedMutexMailboxLock.cs` | 租约 + 进程内单写者 Actor；删除 `SemaphoreSlim` |
| `PithosKvStoreLease.cs` / `PithosKvStoreLeaseFactory.cs` | 租约获取/续期/释放全部走新租约；降级语义保持“立即返回” |
| `LeasedKvStoreAdapter.cs` | 去掉 `_gate`；委托租约 Actor |
| `IKvStoreLease.cs`、`ServiceRegistration.CoreServices.cs`、`PhysicalFileSystem.cs` 注释 | 同步更新“跨进程 Mutex”描述 |

验收：
- 3 个 `jcc.exe` 同时竞争同一存储目录：只有一个 owner，其余按业务策略降级/重试，不阻塞。
- 强制结束 owner 进程，等待 TTL 后另一个进程可接管；数据无损坏。
- 日志记录 owner token、获取/续期/接管时间线。

### B6 移除锁生态

1. 按 AGENTS.md 禁删规则，把以下文件移动到仓库根 `.xxx/`（带时间戳），不直接删除：
   - `AsyncLock.cs`、`LockRegistry.cs`、`LockTimeoutPolicy.cs`、`LockRef.cs`、`AsyncLockedDictionary.cs`
   - `OrderedLockManager.cs`
   - `TimeoutLock.cs`
   - JCC4001-JCC4007 的锁/信号量分析器及测试
   - 上述类型的单元测试
2. 清理所有引用、`GlobalUsings`、注册、诊断、提示词；禁止保留兼容重载。
3. 更新文档状态：`TASK079` 标注被 ADR 0158 取代；`DSG038`/`REF006`/`REF007` 改为“已被本任务收敛为终态”的历史记录。
4. 全仓编译 `pwsh non_deliverables_tools/scripts/Build-AllDomains.ps1`；跑完整 exe 回归矩阵。

### B7 风格统一

| 子项 | 目标 | 方法 |
|------|------|------|
| `#pragma` | 112 行 / 103 文件归零 | JCC11005/JCC1018/JCC1017/JCC9203 逐类修根因；分析器误报修分析器；生成器源码单独治理 |
| 文件内 `using` | 188 行 / 75 文件（含生成器 216/83）归零 | 每项目补 `GlobalUsings.cs`；卫星项目也统一 |
| null-forgiving | 粗测 272 处 / 145 文件归零 | Roslyn 精确审计；改为 `if (x is null) return/throw` 或可空建模 |
| `async void` | 6 个 watcher 方法 | 改 `async Task` + 队列；事件处理器仅保留 UI 事件 |
| 无界 Channel | 8 处 | 设容量/背压；Actor 邮箱有明确无界理由并登记 |
| 同步阻塞异步 | Roslyn 分类 418 粗测命中 | 异步链路清零；同步入口由框架边界处理 |
| 命名空间 | `PithosDB.Core.Core` 8 文件 | 改 `PithosDB.Core` |
| 目录命名 | 127 个生产路径冲突 | 项目目录镜像 csproj 名登记白名单；嵌套源码目录改 `lower_snake`（`DependencyInjection`→`dependency_injection`、`Serialization`→`serialization`、`Adapters`→`adapters`、`Core`→`core` 等） |
| 注释 | 过时锁描述 | 全仓 grep `lock(`/`AsyncLock`/`SemaphoreSlim` 注释，逐条核对 |
| 文档 | TASK079/DSG038/REF006/REF007/ADR 0059 | 标注终态与取代关系；ADR 0059 标 superseded |

验收：风格门禁脚本零命中；全仓编译；exe 回归矩阵全部通过。

## 5. 四列验收表

> `已验收` 只填真实 exe 证据，必须附 ADR 0080 记录；单元测试不能替代。B0/B1/B2/B3 已验收，B4-B7 待实现/待验收。

| 基建实现 | 消费点 | 已实现 | 已验收 |
|----------|--------|:------:|:------:|
| JCC4008 `ForbiddenLockPrimitiveRule` + `Verify-NoLockPrimitives.py` + 逐文件基线 | 16 域 + native_gui/native_tui + CI | ✅ | ✅ 注入 `LockGateProbe.cs` 实测构建失败（`error JCC4008`，`.xxx/logs/TASK094/build-lock-probe-red.txt`），移入 `.xxx/` 后构建恢复 0 错误；基线门禁 B0 150 文件/570 处 → B1 收紧为 143 文件/553 处 → B2 收紧为 138 文件/536 处 → **B2 终态 116 文件/425 处**（`lock-gate-b2-gui2.txt`）通过；全仓 `Build-AllDomains.ps1` 通过（`.xxx/logs/TASK094/build-all-b1.txt`）；工具侧 7 处 `SemaphoreSlim` 限流改为 `BoundedWorkQueue`/`AsCpuParallel`，`JccAuditCli.csproj` 0 警告 0 错误 |
| TUI/GUI/跨进程 exe 挂钩 | `jcc.exe --selftest-tui`、`--selftest-input`、`NativeGui.Host.exe --webui-smoke`/`--dsh-smoke`；`--selftest-crossproc` 挂 B5 | ✅（跨进程待 B5） | ✅ `--selftest-tui` EXIT=0（HOST SELFTEST OK，`.xxx/logs/TASK094/b1-selftest-tui2.txt`）、`--selftest-input` 管道喂入 9 键 EXIT=0（INPUT SELFTEST OK，`.xxx/logs/TASK094/b1-selftest-input.txt`）、**`--dsh-smoke` EXIT=0 ×4**（`.xxx/logs/TASK094/gui_dsh_smoke_loop.log`、`gui_dsh_smoke_after_fix.log`：首页 → session/create → 双流 → goals 暂停事件 → 48 路并发 RPC）、**`--webui-smoke` EXIT=0 ×3**（`.xxx/logs/TASK094/gui_webui_smoke3.log`、`gui_webui_smoke_loop1/2.log`：`WEBVIEW2-READY` + `BRIDGE-SMOKE ok`） |
| 非阻塞 Console 单写者 | `ErrorConsole`、`ConsoleSuppressionGate` | ✅ | ✅ `jcc.EXE -p hi` 经无 key 路径渲染 `✖ 致命错误` EXIT=1（`.xxx/logs/TASK094/exe-errorconsole-endpoint.txt`）；`--selftest-tui` 在 `JCC_DEBUGLOG=1` 高日志下 EXIT=0；单元测试 5/5（`ConsoleSuppressionGateTests`，`test-gate.txt`） |
| 单写者 Channel / 快照 | `LspConnection`、`RgEngine`、`ConfigurationService`、`AsyncLazy` | ✅ | ✅ `jcc-tool-code.exe lsp_hover` 经 csharp-ls 真实 stdio JSON-RPC 返回 `RgOutcome? outcome` EXIT=0（`.xxx/logs/TASK094/lsp-hover.txt`）；`jcc-rg.exe --stream` 输出 4 个命中文件 EXIT=0（`.xxx/logs/TASK094/exe-rg-stream2.txt`）；AsyncLazy 单飞 3/3、FileHistory 多版本 3/3、Rg 154/154 全绿 |
| TUI 输入与回合 Actor | `RawStdinPump`、`LazyEngine`、`EngineTurnClient`、`ReplShell`、`BridgeEventPump`、`TerminalQuerier` | ✅ | ✅ `jcc.exe --selftest-tui` EXIT=0（HOST SELFTEST OK，`.xxx/logs/TASK094/b2-selftest-tui.txt`）、`--selftest-input` 管道喂入 9 键 EXIT=0（INPUT SELFTEST OK，`.xxx/logs/TASK094/b2-selftest-input.txt`）、`NativeTui.Demo.exe --selftest` / `--selftest-bash` / `--bridge-selftest` 三个 EXIT=0（`b2-demo-selftest.txt` / `b2-demo-bash.txt` / `b2-demo-bridge.txt`，跨进程 bridge 协议走真实管道）；单元测试 NativeTui.Tests 3024/3024、NativeTui.Host.Tests 140/140 |
| DSH 状态 Actor/不可变快照 | `native_gui` 16 文件 / 82 锁 → 0；事件出 Actor 触发；WS 每连接单写者 | ✅ | ✅ `--dsh-smoke` EXIT=0 ×4（真实 HttpListener/WS：session/create+list、$events/session/follow 双流、goals 暂停事件、32+16 并发 RPC；`.xxx/logs/TASK094/gui_dsh_smoke_loop.log` / `gui_dsh_smoke_after_fix.log`）；`--webui-smoke` EXIT=0 ×3（真实 WebView2 窗口桥接，修复 async Main 导致 STA 失效的 `RPC_E_CHANGED_MODE`，`.xxx/logs/TASK094/gui_webui_smoke3.log` / `gui_webui_smoke_loop1/2.log`）；NativeGui.Core.Tests 409/409（`gui_tests_full3.log`）、NativeGui.Host.Tests 47/47（`gui_host_tests_full.log`） |
| AsyncLock/限流替换 | 47 文件 / 101 获取 + Semaphore 限流 | ✅ 钩子配置/沙箱 IPC/技能索引/MCP 认证与降级链/流式执行器/GH 并行下载/感知域已去锁；B3 完成单飞初始化（TCS）、状态机/暂停（AsyncGate/TCS 门闩）、并发限流（PermitPool）、单写者传输（ConcurrentDictionary+ImmutableList CAS）、工作者池、GatewayActor 令牌 Channel、GraphExecutionContext 唤醒 Channel、PlanModeManager 去 LockRegistry；**模块外 AsyncLock 生产引用为 0**；基线 138 文件/536 处 → **54 文件/320 处**（剩余仅 B4/B5/锁生态自身与测试） | ✅ jcc + MockServer 并发 4 个真实回合 EXIT=0 ×4（4 次 `chat/completions`，无异常/超时/死锁，`.xxx/logs/mockserver_concurrent.log`、`jcc_turn_1..4.log`）；`jcc --selftest-tui` EXIT=0（`HOST SELFTEST OK`）；相关域测试全绿（AsyncLock 631、Agents 626、Clock 519、Scheduling 287、Vault 867、Bridge 633、Infra 1399、Host 1332、Composition 1135、Downloader 100、Browser 2、Brain 426） |
| PithosDb 单写者 + 快照 | `PithosDb`、`LeveledCompactor`、Lru/S3Fifo | ❌ | ❌（双进程存储完整性） |
| HNSW 快照 | `HnswAnn` | ✅ 单 AsyncGate 串行化替换 `ReaderWriterLockSlim`（CodeIndex 792 测试全绿） | ❌（真实索引构建 + 固定样本召回） |
| 原子文件租约 | `CrossProcessLock`、`NamedMutexMailboxLock`、Pithos 租约 | ❌ | ❌（3 进程竞争 + 崩溃接管） |
| 锁生态归档 | AsyncLock/LockRef/AsyncLockedDictionary/TimeoutLock/OrderedLockManager | 🔄 `TimeoutLock` 及其测试已移入 `.xxx/`，`CodeIndex.csproj` 去掉 `AsyncLock.csproj` 引用 | ❌（全仓编译 + exe 回归） |
| 风格门禁与清理 | pragma/using/null-forgiving/async void/命名/目录/文档 | ❌ | ❌（门禁零命中 + exe 回归） |

## 6. Exe 验收协议

### 6.1 通用规则

1. 构建：跨域改动执行 `pwsh non_deliverables_tools/scripts/Build-AllDomains.ps1`；快速迭代用 Debug 单项目编译，不做 AOT 发布。
2. 隔离：所有验收使用临时 `JCC_APP_DATA_FOLDER`，避免污染真实用户目录。
3. 日志：命令、退出码、关键日志写入 `.xxx/logs/TASK094/Bx-<name>/`，禁止散落仓库根目录。
4. MockServer 只替换外部 LLM；被测的 jcc/TUI/GUI 必须是真实 exe，不是单元测试或 in-process mock。
5. 并发/时序批次重复 16 次；出现 flaky 按“确定性验证 + 时序验证”拆分，不得用单次通过掩盖。

### 6.2 jcc + MockServer（外部模型替换）

```bash
# 终端 1：MockServer
./artifacts/bin/OpenAI.MockServer/Debug/net10.0/JoinCode.OpenAI.MockServer.exe --port 9901

# 终端 2：真实 jcc
export JCC_ENDPOINT=http://localhost:9901
export OPENAI_API_KEY=sk-test-1234567890
export JCC_VENDOR=openai
export JCC_MODEL_ID=gpt-4o
export JCC_ALLOW_DIRECT_FALLBACK=1
export JCC_DEBUGLOG=1
export JCC_APP_DATA_FOLDER="$PWD/.xxx/tmp/jcc-accept-$(date +%s)"
./artifacts/bin/JoinCode/Debug/net10.0/jcc.exe --trust --await 20 -p "echo hello"
```

通过条件：退出码 0；`[READY]`/回合完成日志出现；无锁等待超时、无 `ActorAskDeadlockException`、无挂起。

### 6.3 TUI

```bash
./artifacts/bin/JoinCode/Debug/net10.0/jcc.exe --selftest-tui
printf 'ab\025\033[D\033[1;5D\033[200~中文粘贴内容\033[201~\037你\r' | ./artifacts/bin/JoinCode/Debug/net10.0/jcc.exe --selftest-input
./artifacts/bin/NativeTui.Demo/Debug/net10.0/NativeTui.Demo.exe --selftest
./artifacts/bin/NativeTui.Demo/Debug/net10.0/NativeTui.Demo.exe --selftest-bash
./artifacts/bin/NativeTui.Demo/Debug/net10.0/NativeTui.Demo.exe --bridge-selftest
```

`--selftest-tui`/`--selftest-input` 是 B0 新增的 jcc 透传入口；B0 之前先用 `NativeTui.Demo` 的现有 selftest，B0 完成后以 jcc 入口为准。通过条件：退出码 0；输入泵无丢键、回合状态无竞态、进程正常退出。

### 6.4 GUI / DSH

```bash
./artifacts/bin/NativeGui.Host/Debug/net10.0-windows10.0.26100.0/NativeGui.Host.exe --webui-smoke
./artifacts/bin/NativeGui.Host/Debug/net10.0-windows10.0.26100.0/NativeGui.Host.exe --dsh-smoke
```

`--webui-smoke` 已存在（WebUI 握手/桥接往返）；`--dsh-smoke` 由 B0 新增，真实启动 DSH WebHost 并完成会话/流式事件往返。通过条件：退出码 0；日志出现 smoke ok；无窗口卡死、无未处理异常。

### 6.5 跨进程租约与存储

1. 启动 3 个真实 `jcc.exe`，同一 `JCC_APP_DATA_FOLDER`，并发写同一批记忆/任务条目。
2. 检查每条记录只有一个 owner 写入；最终文件完整、可反序列化。
3. 强制结束持有租约的进程，等待 TTL；另一个进程接管，旧租约文件被归档，数据不丢。
4. 重复 16 次；记录 owner token/获取/续期/接管日志。

### 6.6 失败处理

- 挂起：先查 `jcc` 日志中的 Actor 邮箱深度、Ask 等待图、最后命令；禁止用超时参数掩盖。
- 数据损坏：保留现场到 `.xxx/logs/`，回退该批次提交（`git revert`，禁止 `reset --hard`）。
- 门禁误报：修分析器或加精确例外（需 ADR + 到期时间），禁止 `#pragma` 抑制。

## 7. 风险与回滚

| 风险 | 缓解 |
|------|------|
| PithosDb 改造引入数据损坏 | 单写者 Actor + 不可变快照；双进程验收；每批独立提交可 revert |
| HNSW 召回/延迟退化 | 固定样本集对比；快照双缓冲；性能基线 |
| GUI UI 线程违规 | UI 线程唯一写者；`--dsh-smoke` + 多会话手工/自动验收 |
| 租约时钟偏差/崩溃残留 | TTL + 心跳 + token + 原子接管；多进程故障注入；不做阻塞等待 |
| 一次性改动过大 | B0-B7 分批；每批编译+提交；B6 才移除锁生态 |
| 分析器基线噪声 | 基线只减不增；每批记录下降数字；最终归零 |
| 目录重命名牵连命名空间 | 先建映射表，单项目重命名+编译+提交；禁止一次性全局 sed |

## 8. 自主决策记录

<!-- 🤖 Auto Decision: 2026-11-19 -->
<!-- 决策: 采用 Actor 单写者 + 不可变快照/CAS + Channel + 原子文件租约，拒绝继续强化 AsyncLock/SemaphoreSlim -->
<!-- 原因: 用户要求全仓零锁；锁超时/重试只推迟死锁，状态所有权仍隐式；项目已有 Actor/Immutable/CAS 基建可复用 -->
<!-- 替代方案: 保留 AsyncLock 加超时（否决）；lock 全换 SemaphoreSlim（否决）；PithosDb 进程隔离（暂缓） -->
<!-- 验证: 计划阶段，未编译未验收；实施后按四列验收表逐批补 exe 证据 -->

<!-- 🤖 Auto Decision: 2026-11-19 -->
<!-- 决策: 跨进程 Mutex 改为原子文件租约，不保留白名单（2026-11-19 用户已确认） -->
<!-- 原因: 用户明确要求去掉 Mutex；租约用 CreateNew/rename 的原子性表达业务所有权，避免阻塞锁和锁顺序 -->
<!-- 替代方案: 保留命名 Mutex（已否决）；数据库事务（PithosDb 自研，暂不引入外部依赖） -->
<!-- 验证: 待 B5 真实多进程验收；若失败先修租约设计，不自动改回 Mutex -->

<!-- 🤖 Auto Decision: 2026-11-19 -->
<!-- 决策: HnswAnn 的 ReaderWriterLockSlim 用单个 AsyncGate 串行化全部读写，不做不可变图快照双缓冲 -->
<!-- 原因: HNSW 插入会改动多层邻接表，整图不可变复制的成本随 N 线性增长；索引构建/搜索并发度低，串行化按业务可接受 -->
<!-- 替代方案: 写入 Actor + 图快照双缓冲（待 B4 与 PithosDb 一起做性能评估后再定）；保留 RWLock（用户已否决） -->
<!-- 验证: CodeIndex.Tests 792/792 通过；HNSW 召回对比留到 B4 exe 验收 -->

<!-- 🤖 Auto Decision: 2026-11-19 -->
<!-- 决策: LspDiagnosticRegistry 用不可变快照（pending/delivered/DeliveredOrder）CAS 发布，CheckPending 一次 CAS 原子取走全部待处理批次 -->
<!-- 原因: 原实现标记 AttachmentSent 与删除分两个锁窗口，并发 CheckPending 可能重复投递；快照 take 消除双投递且无锁 -->
<!-- 替代方案: 每 key Actor 邮箱（过度设计，注册表操作都是短临界区）；保留 AsyncLock（用户已否决） -->
<!-- 验证: Eyes.Tests 78/78 通过 -->

<!-- 🤖 Auto Decision: 2026-11-19 -->
<!-- 决策: 项目目录名与 csproj/程序集同名的（如 guard.security、*.generator）登记命名白名单；嵌套源码目录仍强制 lower_snake -->
<!-- 原因: 项目目录名与构建/包名绑定，改名会破坏 slnx/CI/包引用；嵌套目录是纯源码组织，可安全统一 -->
<!-- 替代方案: 全量重命名所有目录（成本高、收益低，且与现有 16 域结构冲突） -->
<!-- 验证: B7 门禁脚本按“项目目录白名单+嵌套目录零违规”判定 -->


<!-- 🤖 Auto Decision: 2026-11-19 B3 -->
<!-- 决策: AgentBase 暂停语义用 TaskCompletionSource 门闩（Pause 创建 gate、Resume/5s 超时清除），删除 _pauseLock -->
<!-- 原因: 原 AsyncLock(1,1) 从未被持有/释放，暂停实际靠轮询标志；TCS 直接表达“挂起直到恢复或超时”，无锁可死锁 -->
<!-- 替代方案: 保留 AsyncLock 供 Pause/Resume（语义错位）；Channel 信号（需排空且无超时语义） -->
<!-- 验证: Agents.Tests 626/626；暂停超时日志保留“暂停等待超时（5秒），自动恢复执行” -->

<!-- 🤖 Auto Decision: 2026-11-19 B3 -->
<!-- 决策: GatewayActor 限流改预填具有界 Channel<bool> 令牌；GraphExecutionContext 完成信号改无界 Channel 唤醒 -->
<!-- 原因: SemaphoreSlim 仅作计数令牌/信号，Channel 读写端天然支持背压与取消，无 WaitAsync 超时/Dispose 竞态 -->
<!-- 替代方案: PermitPool（支持热调整，但 GatewayActor 容量固定且无需 Resize）；TCS 广播（多等待者需重建） -->
<!-- 验证: AsyncLock.Tests 631/631；Clock.Tests 519/519（含 GoalEngine 管线） -->

<!-- 🤖 Auto Decision: 2026-11-19 B3 -->
<!-- 决策: 单飞初始化统一用 TaskCompletionSource 信号字段 + CAS 创建，而非缓存 Task 字段 -->
<!-- 原因: JCC9307 要求 Task 字段在 Dispose 中 await，初始化任务天然不随宿主释放；TCS 只暴露完成信号，失败/成功都统一传播 -->
<!-- 替代方案: Lazy<Task>（异常缓存语义不同）；AsyncGate + double-check（仍是一把互斥锁） -->
<!-- 验证: Puppeteer/Browser、ConfigPersistentServiceBase/Vault、FileBasedTaskService/Scheduling 全绿 -->
