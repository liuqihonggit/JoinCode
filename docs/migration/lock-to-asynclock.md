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

- [ ] 批次1 foundation
- [ ] 批次2 infrastructure
- [ ] 批次3 libs
- [ ] 批次4 core
- [ ] 批次5 services
- [ ] 批次6 app
- [ ] 批次7 HotSpot 分片锁
- [ ] 全量测试

<!-- 🤖 Auto Decision: 2026-09-03 -->
<!-- 决策: 采用方案A（不可重入 + 逐处重构），沿用 ADR 0059 范式 -->
<!-- 原因: 项目已有 118 处 AsyncLock 成功经验，JCC4001 分析器已确立方向，仅 3 处真重入需重构，风险可控 -->
<!-- 替代方案: B(可重入 AsyncLock，AsyncLocal 跨 await 不可靠已否决) / C(混合，两套原语维护成本高) -->
