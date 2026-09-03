# lock → AsyncLock 全量迁移计划

> ADR: [0059](../adr/0059-asynclock-reentrancy-detection.md)（不可重入 + 重入检测）
> 方案：A — 坚持不可重入 + 逐处重构（锁内只操作字段、副作用移锁外）

## 调查结论（2026-09-03）

- **AsyncLock**：`foundation/AsyncLock/src/AsyncLock.cs`，基于 `SemaphoreSlim(1,1)`，不可重入
- **JCC4001 分析器**只检测 async 方法中的 lock，189 处 lock 全在同步方法里，未拦截
- **待迁移**：29 个生产文件，122 个含 lock 方法（已排除 tests/bin/obj/artifacts）
- **SemaphoreSlim 35 处**：多为限流/信号门闩（n,n n>1 或 0,1），**不迁移**

### 风险分类

| 类别 | 文件数 | 处理 |
|------|--------|------|
| 真重入需重构 | 3 | SimpleModeService / FastModeService / SubAgentCardManager |
| 误报可机械迁移 | 2 | StateMachine（调无锁重载）/ LspDiagnosticRegistry（锁外调用） |
| 无重入机械迁移 | 24 | `lock(_lock)` → `using (_lock.Lock())` |
| 分片锁特殊 | 2 | DeferredMailService / IntentCollector（`lock(GetLock(key))` → `ConcurrentDictionary<string,AsyncLock>`） |

### 迁移规则

1. 字段：`private readonly object _lock = new();` → `private readonly AsyncLock _lock = new("ClassName");`
2. 同步获取：`lock (_lock) { ... }` → `using (_lock.Lock()) { ... }`
3. 异步获取：`lock (_lock) { ... }` → `await using (await _lock.LockAsync(ct)) { ... }`（本批均为同步方法）
4. 高风险重构：锁内只操作字段，副作用（事件触发、调其他服务、计时器）移锁外
5. 分片锁：`Dictionary<string,object>` + `GetLock` → `ConcurrentDictionary<string,AsyncLock>` + `GetOrAdd`

### 执行顺序（按层从底到上）

| 批次 | 层 | 文件数 | 文件 |
|------|----|--------|------|
| 1 | foundation | 5 | SessionScope, Fsm, StateMachine(误报), SubAgentEventChannel, WorkflowPluginBase |
| 2 | infrastructure | 5 | NetworkConnectivityService, PluginManager, ResourceReferenceGraph, UnifiedCircuitBreaker, FixedWindowRateLimiter |
| 3 | libs | 3 | TextDocument, HighlightingManager, RopeNode |
| 4 | core | 10 | DiagnosticEngine, DiminishingReturnsDetector, BuildQueueService, DesktopSafetyChecker, MacroRecorder, UndoStack, FastModeService(重构), SimpleModeService(重构), MemorySearchHistory, StoreSelector |
| 5 | services | 2 | BridgeFaultInjection, LspDiagnosticRegistry(误报) |
| 6 | app | 4 | SubAgentRun, SubAgentCardManager(重构), TerminalPainter, TerminalResizeMonitor |
| 7 | HotSpot 分片锁 | 2 | DeferredMailService, IntentCollector, HotSpotSpawnIntegration |

## 进度

- [x] 批次1 foundation（6 文件：StateMachine, SessionScope, Fsm, SubAgentEventChannel, WorkflowPluginBase, L.cs）
- [x] 批次2 infrastructure（7 文件：NetworkConnectivityService, FixedWindowRateLimiter, ResourceReferenceGraph, UnifiedCircuitBreaker, PluginManager, InMemoryFileSystem, HotSpotSpawnIntegration）
- [~] 批次3 libs（保留：AvaloniaEdit 移植库未引用 AsyncLock 项目）
- [x] 批次4 core（12 文件：7 低风险机械迁移 + SimpleModeService/FastModeService 重构 + MemorySearchHistory/ProgressTracker/PsAstParser）
- [x] 批次5 services（2 文件：BridgeFaultInjection, LspDiagnosticRegistry）
- [x] 批次6 app（5 文件：TerminalPainter, TerminalResizeMonitor, SubAgentRun, SubAgentRunTracker, SubAgentCardManager 重构）
- [x] 批次7 HotSpot 分片锁（2 文件：DeferredMailService, IntentCollector）
- [x] 全量测试（dotnet test App.slnx -c Debug --filter "Category!=Integration"，全部通过）

## API 变更（2026-09-04）

用户中途收缩了 AsyncLock API：
- **移除** `Lock()` / `LockAsync()` / `TryLockAsync()`
- **保留** `TryLock(CancellationToken)` 返回 `IDisposable?`（超时返回 null）
- **迁移模式**：`using (_lock.TryLock() ?? throw new System.TimeoutException("锁等待超时"))`
- **DefaultTimeout**：5s → 1s（卡死快速失败）
- **命名空间冲突**：`Infrastructure.Utils.System` 存在，需用 `global::System.TimeoutException`

## 修复记录

- **L.cs EnsureInitialized 重入死锁**（扫描漏掉，跨方法跨类）：
  - 根因：`EnsureInitialized` 持锁调 `LazyInitializer?.Invoke()` → `L.Initialize` 获取同一 `s_lock` → 重入死锁
  - 修复：`EnsureInitialized` 不持锁，直接调 `LazyInitializer?.Invoke()`（`L.Initialize` 幂等，自己获取锁串行化）

## 保留未迁移（决策记录）

| 范围 | 原因 |
|------|------|
| `libs/Editor/`（AvaloniaEdit 移植库） | 未引用 AsyncLock 项目，保持独立 |
| `ObjectIdManager.cs` | 锁 List 对象本身（分片锁），无重入风险 |
| core 层 15 个分片锁文件 | 锁局部变量/参数/字典value（`lock(existing)`, `lock(records)`, `lock(kvp.Value)` 等），无重入风险，改 AsyncLock 需重构数据结构，成本不抵收益 |

<!-- 🤖 Auto Decision: 2026-09-03 -->
<!-- 决策: 采用方案A（不可重入 + 逐处重构），沿用 ADR 0059 范式 -->
<!-- 原因: 项目已有 118 处 AsyncLock 成功经验，JCC4001 分析器已确立方向，仅 3 处真重入需重构，风险可控 -->
<!-- 替代方案: B(可重入 AsyncLock，AsyncLocal 跨 await 不可靠已否决) / C(混合，两套原语维护成本高) -->

<!-- 🤖 Auto Decision: 2026-09-04 -->
<!-- 决策: 保留 core 层 15 个分片锁文件不迁移 -->
<!-- 原因: 锁局部变量/参数/字典value 无重入风险，改 AsyncLock 需重构数据结构（如 List→ConcurrentQueue），成本不抵收益 -->
<!-- 验证: 全量测试通过 ✅ -->
