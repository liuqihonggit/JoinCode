# 0129. 构造函数安全模式 — 禁止半构造化，强制 private ctor + static 工厂

> 📍 **导航**: [docs/](../README.md) › [adr/](README.md)
> 🔗 **上游索引**: [adr/README.md](README.md) — 修改本文档后须同步更新此索引

- 状态：accepted
- 日期：2026-10-02
- 决策者：用户 + AI

## 背景

构造函数在执行过程中若抛异常，会导致**部分字段已赋值、部分字段未赋值**的"半构造化"状态。此时对象已分配但不可用，且：

1. **多 IDisposable 资源泄漏**：构造函数内顺序 `new A()` → `new B()` → `new C()`，若 `C` 构造抛异常，`A`、`B` 已分配但无人持有引用，无法 Dispose，直接泄漏。
2. **后台线程访问未完全构造对象**：构造函数内 `new Thread(...).Start()` 后，后台线程可能在构造函数后续赋值完成前就读取字段，读到 null/默认值。
3. **fire-and-forget 异步初始化**：构造函数内 `_ = InitializeAsync()` 启动的异步任务访问未完全构造对象，且异常被吞、无法感知失败。
4. **GCHandle/指针悬挂**：`GCHandle.Alloc` 后若后续解析抛异常，GCHandle 未 Free，内存泄漏且指针悬挂。

代码库扫描发现 28 个高风险构造函数，集中在 `lib/pithosdb/`（多资源+后台线程+WAL 恢复）、fire-and-forget 异步初始化（14 处）、多 Timer/HttpClient 资源。项目已有 106 个 `static Create` 工厂方法，团队熟悉该模式。

## 决策

### 决策1：涉及 IO/资源分配/可能抛异常的构造函数改为 private + static 工厂

凡构造函数体内存在以下任一操作，**必须**改为 `private` 构造函数 + `public static` 工厂方法（`Create`/`Open`/`OpenAsync`）：

- 文件/网络 IO（`FileStream`、`MemoryMappedFile`、`HttpClient`、`Directory.CreateDirectory`、`File.Exists`）
- 多个 IDisposable 资源分配（第 N 个失败则前 N-1 个泄漏）
- `GCHandle.Alloc` / unsafe 指针获取
- 后台线程/Timer 启动（`Thread.Start`、`new Timer(立即启动)`）
- fire-and-forget 异步（`_ = XxxAsync()`）
- 阻塞异步（`.GetAwaiter().GetResult()`）
- 调用可能抛异常的实例方法做初始化（`Initialize()`、`RecoverFromWal()`）

**工厂方法职责**：在工厂内逐步获取资源，任一步骤失败时用 `try-catch` 或 `using` 释放已分配资源后重新抛出；全部成功后用 `private` 构造函数做纯字段赋值构造对象并返回。

**构造函数职责**：仅做字段赋值（`this._x = x`），禁止任何可能抛异常的操作。参数验证（`ArgumentNullException.ThrowIfNull` 等）允许留在构造函数，因其不分配资源。

### 决策2：工厂方法命名约定

| 场景 | 方法签名 | 说明 |
|------|---------|------|
| 同步资源获取 | `public static T Create(args)` | 资源获取不涉及 async |
| 异步资源获取 | `public static async Task<T> OpenAsync(args)` | 涉及 async IO（文件读、网络） |
| 仅打开文件 | `public static T Open(args)` | 语义为"打开"，如 `SSTableReader.Open(path)` |
| 内存/无 IO | `public static T Create(args)` | 语义为"创建"，如 `StreamTokenDetector.Create(...)` |

### 决策3：无后向兼容

`public` 构造函数直接改为 `private`，所有调用方改用工厂方法。不保留旧构造函数、不加 `[Obsolete]` 过渡。符合项目"无后向兼容"原则。

## 替代方案

1. **构造函数内 try-catch 释放已分配资源**：在每个 `new` 后包 try-catch，catch 里 Dispose 前面的资源。放弃原因：样板冗长易漏，资源数量增加时 try-catch 嵌套爆炸，且无法解决"后台线程已启动"问题（Thread 无法撤销）。
2. **工厂方法返回 `Result<T, Error>`**：不抛异常，返回错误码。放弃原因：不符合 C# 惯例，调用方代码膨胀，且这些失败（文件不存在、磁盘满）本就该抛异常终止。
3. **对象池 + Rent/Return**：所有资源类走对象池。放弃原因：过度设计，PithosDb/SSTableReader 生命周期与池模型不匹配，且不解决"首次构造半构造化"问题。
4. **构造函数链 + 资源容器**：构造函数接收预构造的资源容器。放弃原因：调用方仍需构造容器，半构造化风险转移而非消除。

## 后果

- 正面：
  - 构造函数抛异常时对象不可达，GC 可回收已分配的托管资源；工厂方法内已显式释放非托管资源（GCHandle/FileStream），无泄漏。
  - 后台线程/Timer 在工厂方法全部资源就绪后才启动，不会访问半构造对象。
  - 构造函数体一目了然仅赋值，可读性提升。
  - 与项目已有 106 个 `static Create` 工厂模式统一。
- 负面：
  - 调用方全部需改为 `await Xxx.OpenAsync(...)` 或 `Xxx.Create(...)`，改动面波及适配器层和测试。
  - DI 容器若通过反射激活 `public ctor`，需改为工厂激活（`AddSingleton<T>(sp => T.Create(...))`）。
- 中性：
  - `private` 构造函数使单元测试无法直接 `new`，需通过工厂方法构造，测试代码同步调整。

## 完整风险清单（按优先级分级，P0 先行落地）

> 本清单为半构造化重构的完整待办。上下文压缩后从此 ADR 恢复，按状态列继续推进。每完成一个改为 ✅ 并补 commit hash。

### P0 — 极高风险：多资源泄漏 + 后台线程（5 个）

| 状态 | 类 | 文件:行 | 风险 | 工厂方法 |
|------|----|---------|------|---------|
| ✅ 73a544d | `PithosDb` | `lib/pithosdb/PithosDb.cs:44` | 多资源+后台线程+WAL恢复 | `static Open(directory, options)` |
| ✅ 6db3a44 | `WriteAheadLog` | `lib/pithosdb/Core/WriteAheadLog.cs:49` | FileStream+BinaryWriter+Timer | `static Open(path, syncMode, interval)` |
| ✅ b5bbae1 | `SSTableReader` | `lib/pithosdb/Storage/SSTableReader.cs:41` | FileStream+GCHandle+ReadMetadata（GCHandle 泄漏 bug 已修） | `static Open(path, blockCache)` |
| ✅ b13a729 | `PhysicalMemoryMappedRead` | `lib/infrastructure/io/file_system/PhysicalMemoryMappedRead.cs:15` | mmf+accessor+AcquirePointer | `static Open(path)` |
| ✅ 21da9e5 | `StreamTokenDetector` | `kit/brain/context/services/loop/StreamTokenDetector.cs:30` | RingBuffer+CTS+Thread.Start | `static Create(...)` |

### P1 — fire-and-forget 异步初始化（11 个）

> 模式：构造函数内 `_ = XxxAsync()` 访问未完全构造对象。统一改为显式 `InitializeAsync()` 方法，调用方在启动阶段 await；或改为 `static Create` 工厂内 await 完成初始化。

| 状态 | 类 | 文件:行 | 风险 |
|------|----|---------|------|
| ✅ 29a8061 | `ToolHealthMonitor` | `kit/mcp_tool_dispatch/core/execution/ToolHealthMonitor.cs:75` | fire-and-forget LoadFromDiskAsync + Timer → _loadTask 持有 + DisposeAsync await（DI Singleton 保留 public ctor） |
| ✅ 1be0975 | `ToolTemplateService` | `kit/mcp_tool_dispatch/core/execution/ToolTemplateService.cs:22` | EnsureTemplatesDir 实例方法 + fire-and-forget → _loadTask 持有 |
| ✅ 0bd72cf | `ToolInterventionManager` | `kit/mcp/core/management/ToolInterventionManager.cs:19` | fire-and-forget LoadFromDiskAsync → _loadTask 持有 |
| ✅ 3d1d9f0 | `McpAuthToolHandlers` | `kit/mcp/core/handlers/McpAuthToolHandlers.cs:24` | fire-and-forget LoadAuthStateAsync → _loadTask 持有 + DisposeAsync await |
| ✅ 7736844 | `DynamicKeywordConfigService` | `kit/prompts/utils/DynamicKeywordConfigService.cs:26` | ReloadActor + fire-and-forget + FileSystemWatcher → _loadTask 持有 + DisposeAsync await |
| ✅ 84488a3 | `WindowShakeCoordinator` | `kit/hands/desktop/services/WindowShakeCoordinator.cs:42` | fire-and-forget + 事件订阅悬挂 → _loadTask 持有 |
| ✅ 6c848a9 | `TeamManager` | `llm/agents/Coordinator/Team/core/TeamManager.cs:43` | 5 对象 + fire-and-forget LoadStateAsync → _loadTask 持有 + DisposeAsync await |
| ⏭️ 已评估 | `AgentMemoryService` | `llm/agents/Services/Support/AgentMemoryService.cs:35` | fs.GetCurrentDirectory 同步调用，抛异常对象不可达，无半构造化风险 |
| ✅ 030f343 | `PermissionChecker` | `lib/guard/permission/permission2/tool_handlers/core/PermissionChecker.cs:25` | fire-and-forget InitializeModeAsync → _loadTask 持有 |
| ✅ bfa494a | `PermissionManager` | `lib/guard/permission/permission2/tool_handlers/core/PermissionManager.cs:32` | fire-and-forget InitializeModeAsync → _loadTask 持有 + DisposeAsync await |
| ✅ edd439f | `BriefModeService` | `lib/guard/configuration/services/BriefModeService.cs:18` | fire-and-forget LoadFromFileAsync → _loadTask 持有 |

### P2 — 多 IDisposable 资源（9 个）

> 模式：构造函数内顺序 new 多个 IDisposable，第 N 个失败前 N-1 个泄漏。改为 `static Create` 工厂，工厂内逐步创建并 try-catch 释放已分配资源。

| 状态 | 类 | 文件:行 | 风险 |
|------|----|---------|------|
| ✅ d555222 | `V2ReplBridgeTransport` | `lib/transport.impl/bridge/v2/V2ReplBridgeTransport.cs:48` | CTS + HttpClient×2 + SerialBatchEventUploader×2 → private ctor + static Create |
| ✅ cf0c9b2 | `V1ReplBridgeTransport` | `lib/transport.impl/bridge/v1/V1ReplBridgeTransport.cs:66` | HttpClient + WebSocketTransport + CTS + 事件订阅×2 → private ctor + static Create |
| ⏭️ 已评估 | `PriorityMailbox` | `lib/async_lock/mailbox/PriorityMailbox.cs:61` | Channel×3 + fire-and-forget StartConsumingAsync → 已评估：_consumerTask 字段持有 Task，DisposeAsync await，非真正 fire-and-forget |
| ⏭️ 已评估 | `CodeIndexer` | `server/code_index/indexing/CodeIndexer.cs:42` | 11 协作对象 + TryInitEmbeddingIndex + BuildIndexStoreList → 已评估：DI Singleton 约束，大部分对象非 IDisposable，TryInitEmbeddingIndex 有 try-catch，无 fire-and-forget |
| ⏭️ 已评估 | `V1BridgeHandle` | `server/bridge/transport/v1/core/V1BridgeHandle.cs:50` | Timer×2 立即启动 → 已评估：字段全先赋值，dueTime=120s/1h 非立即，回调有 try-catch，Dispose 释放 Timer |
| ⏭️ 已评估 | `BridgeRequestScope` | `server/bridge/client/BridgeClient.cs:217` | CTS×2 + TaskCompletionSource + 事件订阅 → 已评估：CTS 创建仅 OOM 可能抛异常，GC finalize 兜底，Dispose 释放全部 |
| ✅ 850d0e0 | `BridgeSubprocessHandle` | `server/bridge/session/core/BridgeSubprocessManager.cs:64` | 已部分合规，private ctor 内 fire-and-forget MonitorExitAsync → _monitorExitTask 持有 + DisposeAsync await |
| ⏭️ 已评估 | `CostTracker` | `kit/brain/cost_tracking/services/core/CostTracker.cs:27` | 5 对象 + ValidateOrThrow + 后台 LoadCostHistory → 已评估：DI Singleton 约束，Tell 是 Actor 邮箱投递非 fire-and-forget，ValidateOrThrow 是配置验证，DisposeAsync await Actor |
| ✅ ef8a3b6 | `QueryServiceBase` | `llm/core/Adapters/LLM/core/QueryServiceBase.cs:30` | CreateHttpClient 内 new Uri（UriFormatException）handler 泄漏 → try-catch 释放 handler |

### P3 — 阻塞异步 + 网络 IO（2 个）

| 状态 | 类 | 文件:行 | 风险 | 工厂方法 |
|------|----|---------|------|---------|
| ✅ a43c0a7 | `ProviderDefinitionRegistry` | `lib/guard/configuration/configuration2/core/providers/shared/ProviderDefinitionRegistry.cs:17` | InitializeAsync().GetAwaiter().GetResult() 阻塞（死锁风险） | `static CreateAsync` + `static Create` |
| ✅ b68a209 | `UpdateServer` | `server/update/UpdateServer.cs:24` | 构造函数内 GetAvailablePort（TcpListener.Start） | `static Create` |

### P4 — Avalonia 框架惯例（1 个）

| 状态 | 类 | 文件:行 | 风险 |
|------|----|---------|------|
| ⏳ | `MainWindow` | `app/gui/views/core/MainWindow.axaml.cs:36` | InitializeComponent IO + 事件订阅×4 + Timer.Start。Avalonia 惯例，确保 InitializeComponent 失败时事件订阅不悬挂 |

### 中风险（14 个，非平凡方法调用但不易抛异常）

> 优先级低于高风险，P0-P4 完成后再评估。清单：`PithosKvStore.cs:18`、`SshSession.cs:35`、`CronScheduler.cs:124`、`ShellProcessWatchdog.cs:39`、`ReaperScheduler.cs:27`、`TranscriptFileWriter.cs:26`、`ThinkingStore.cs:21`、`SessionTagService.cs:21`、`LspServerInstance.cs:187`、`EmbeddingIndex.cs:32`、`ToolHypergraphScorer.cs:21`、`McpSecureTokenStorage.cs:20`、`MonitorMcpTask.cs:382`、`SubAgentOutputTruncator.cs:24`

## 推进顺序

1. **P0（5 个）** — 本轮先行，逐个红测试→重构→编译→绿测试→提交
2. **P1（11 个）** — P0 验证通过后，fire-and-forget 模式统一改 InitializeAsync
3. **P2（9 个）** — 多资源改 static Create 工厂
4. **P3（2 个）** — 阻塞异步改 CreateAsync
5. **P4（1 个）** — Avalonia 惯例评估
6. **中风险（14 个）** — 高风险全完后评估

状态标记：⏳ 待推进 / 🔧 进行中 / ✅ 已完成（补 commit hash）/ ⏭️ 已评估无需改
