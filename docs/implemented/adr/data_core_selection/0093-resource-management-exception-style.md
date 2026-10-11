# 0093. 资源管理与异常控制风格规范

> 📍 **导航**: [docs/](../README.md) › [adr/](README.md)
> 🔗 **上游索引**: [adr/README.md](README.md) — 修改本文档后须同步更新此索引

- 状态：accepted
- 日期：2026-09-09（最后更新 2026-09-17：归纳决策4-9 + 避坑清单，引用 0108/0112）
- 决策者：用户（liuqihonggit）+ AI

## 背景

项目代码库中存在两类高频不良风格，影响可读性、维护性，且容易引发资源泄漏：

1. **裸 `new` + 手动 `Dispose`**：创建 `IDisposable` 对象后用 `try-finally` 或显式 `Dispose()` 释放，而非 `using var`。典型反例：`Mcp.MockServer/Program.cs` 中 `await new StreamReader(ctx.Request.Body).ReadToEndAsync()` 未用 using，`StreamReader` 自身 buffer 不释放。
2. **一个方法多个 `try-catch`**：尤其 `Dispose()` 方法里用 3 个独立 `try-catch` 包裹每个资源的 `Cancel`/`Dispose`（如 `FileToolHandlers.cs:1246-1251`），以及业务方法内嵌套 `try-catch`（如 `QueryLoopMiddleware.cs` 占位结果写入）。

用户明确偏好：
- `using var xx` 手法释放内存（确定性释放 + 作用域清晰 + 0 样板）
- 一个方法尽量一个 `try-catch`，多个通常可化解为"一个函数一个 using 释放"

## 决策

### 决策1：资源释放强制 `using var` / `await using var`

任何 `IDisposable`/`IAsyncDisposable` 对象，在当前作用域内创建且不逃逸出该方法，**必须**用 `using var` / `await using var` 声明。禁止裸 `new` 后手动 `Dispose` 或 `try-finally` 释放。

**例外**（允许手动释放，需注释说明）：
- 字段持有的长生命周期资源 → 在 `Dispose(bool disposing)` 中释放
- 工厂方法返回可释放对象（如 `OpenRead()` 返回 `Stream`）→ 调用方负责
- 故意不释放底层流（`leaveOpen: true`）→ 注释说明为何不释放
- **容错删除**（吞异常 + 日志，不传播）的目录/文件清理 → 如 `try { _fs.DeleteFile(path); } catch (Exception ex) { _logger?.LogWarning(ex, "清理失败"); }`，常见于更新/升级流程的备份清理（`UpgradeService.ApplyUpdateAsync`）
- **需特殊文件处理的目录清理**（先并行删只读文件再删目录）→ 如 `CodeSandboxService.DeleteDirectoryAsync`，dotnet build 产物含只读 `.dll`/`.pdb`，直接 `DeleteDirectory(recursive: true)` 会失败，需先 `_fileOperationService.DeleteFileAsync` 逐个处理只读属性
- **资源逃逸给调用方的方法返回值** → 如 `UpgradeService.DownloadAsync` 返回 `downloadedPath`（位于 tempDir 内），tempDir 不能在方法结束时删除，需由调用方在 `ApplyUpdateAsync` 移动文件后清理

### 决策2：一个方法一个 `try-catch`（尽量）

一个方法内尽量只保留一个 `try-catch` 块。化解手段（按优先级）：

1. **用 `using var` 消除 `finally`**：资源释放交给 using，去掉 `try-finally`
2. **提取辅助方法**：每个资源操作独立成方法，各自 `using`，主方法只编排
3. **合并相邻 `try-catch`**：异常处理逻辑相同时合并，用 `when` 子句区分类型
4. **`Dispose` 合并**：`Dispose()` 方法里多资源释放，用 `DisposeSafe()` 扩展方法统一吞 `ObjectDisposedException`，禁止每个资源一个 `try-catch`

**嵌套 `try-catch`**（try 内套 try）：内层通常是"补偿/回退"逻辑（如写占位结果），应提取为独立方法（如 `WritePlaceholderResultAsync`），外层 `catch` 调用它。

### 决策3：提供 `DisposeSafe` 扩展方法（消除 Dispose 样板）

在 `Abstractions/00-core` 新增 `DisposeSafeExtensions`，提供：

```csharp
public static void DisposeSafe(this IDisposable? obj, ILogger? logger = null, [CallerMemberName] string? caller = null)
{
    try { obj?.Dispose(); }
    catch (ObjectDisposedException) { /* 已释放，幂等忽略 */ }
    catch (Exception ex) { logger?.LogWarning(ex, "[{Caller}] Dispose 失败", caller); }
}

public static void CancelAndDisposeSafe(this CancellationTokenSource? cts, ILogger? logger = null)
{
    try { cts?.Cancel(); } catch (ObjectDisposedException) { }
    cts.DisposeSafe(logger);
}
```

所有 `Dispose()` 方法禁止再写 `try { x.Dispose(); } catch (ObjectDisposedException) ...` 样板，统一调 `x.DisposeSafe(_logger)`。

### 决策4：释放方法统一命名 — 只认 Dispose() / DisposeAsync()

> 编译期强制：[ADR 0108](0108-dispose-consistency-analyzer-rules.md) — JCC9105/9106/9107

只认 `Dispose()` 和 `DisposeAsync()` 两种。禁止 `OnDispose`/`Close`/`Stop`/`Shutdown` 等变体（`StopAsync` 易与 `IHostedService` 语义混淆）。Entity 基类 `OnDispose` 间接层已删除，102 个子类改为 `override Dispose()` + `base.Dispose()`。

### 决策5：同步/异步选型 — 按资源性质分流（否定全面异步化）

类持有资源的性质决定释放方法，**不强制全面异步化**：

| 类持有的资源 | 释放方法 | 接口 |
|-------------|---------|------|
| 仅纯同步资源（内存 / Span / 数组 / Mutex / SemaphoreSlim） | `Dispose()` | `IDisposable` |
| 仅异步资源（网络 / 文件 / 管道 / 锁 / Actor） | `DisposeAsync()` | `IAsyncDisposable` |
| 混合资源 | `Dispose()` + `DisposeAsync()` 并存 | `IDisposable` + `IAsyncDisposable` |

**并存规则**（混合类）：`Dispose()` 只释放同步资源，`DisposeAsync()` 只释放异步资源，**禁止同一资源在两个方法里重复释放**。`Dispose()` 不得调用 `DisposeAsync().AsTask().GetAwaiter().GetResult()`（同步阻塞异步释放，违反决策7）。

### 决策6：释放体内禁止 fire-and-forget

> 编译期强制：[ADR 0112](0112-dispose-fire-and-forget-ban-continuewith-to-await.md) — JCC9200

`DisposeAsync` 必须 `async`，所有异步操作 `await`，禁止 `_ = xxxAsync()` discard 和裸 `ContinueWith`。同步 `Dispose` 体内不能有任何异步操作。释放禁止阻塞：禁止 `.Wait()`/`.GetAwaiter().GetResult()`。

### 决策7：释放函数禁止超时阻塞

释放是必须完成的操作，超时无意义——无论如何都要释放。禁止在释放函数（`Dispose`/`DisposeAsync`/`Close`/`StopAsync`/`ShutdownAsync`）体内使用：`.GetAwaiter().GetResult()` / `.Wait(TimeSpan)` / `.WaitAsync(timeout)` / `new CancellationTokenSource(timeout)` / `Task.WhenAny(task, Task.Delay(timeout))` / `Thread.Join(timeout)`。直接 `await task.ConfigureAwait(false)` 或用 `CancellationToken.None`。

### 决策8：禁止 `new` 隐藏父类释放方法

```csharp
// ❌ new 隐藏：父类引用调用走父类实现，子类释放被跳过，资源泄露
public new ValueTask DisposeAsync() => DisposeAsyncCore();

// ✅ override（父类是 IAsyncDisposable）
public override async ValueTask DisposeAsync()
{
    await DisposeAsyncCore().ConfigureAwait(false);
    await base.DisposeAsync().ConfigureAwait(false);
}

// ✅ 直接实现接口（父类无释放方法）
public async ValueTask DisposeAsync() => await DisposeAsyncCore().ConfigureAwait(false);
```

### 决策9：原子守卫防重复释放

```csharp
private int _disposed;

public async ValueTask DisposeAsync()
{
    if (Interlocked.Exchange(ref _disposed, 1) == 1) return; // 已释放直接跳过
    // ...释放逻辑
}
```

禁止 `ref bool` 非原子重载，用 `int` + `Interlocked.Exchange`。`IsDisposed` 检查用 `Volatile.Read(ref _disposed) != 0`。

## 避坑清单

1. **删方法前先迁移全部调用方** — 删 `Dispose()` 时先删方法定义会产生 78 个调用方编译错误（调用方仍在 `using`/显式调 `Dispose()`）。正确顺序 = 先批量迁移所有调用方到 `DisposeAsync()`/`await using` → 再删方法定义。删除任何方法前必须先迁移全部调用方。
2. **方法体内末尾显式 `Dispose()` 也属同步释放** — 如 `BridgeSubprocessManager` 末尾 `Dispose()` 触发 Entity 生命周期注销，必须改为 `await base.DisposeAsync()`。删除方法定义 ≠ 调用方已迁移，必须 grep 全部调用点逐一改。
3. **后台周期任务用 `volatile bool` 替代 CTS 字段** — `PeriodicTimer` 循环用 `volatile bool _stopping` + `_timer?.Dispose()`，避免 CTS Dispose 后 `ObjectDisposedException` 竞态。循环条件 `while (!_stopping && await _timer.WaitForNextTickAsync(CancellationToken.None))`。
4. **Dispose 体内禁止堆分配** — 禁止 `new List`/`ToArray` 等堆分配，释放是收尾不应制造垃圾。少量 `Task` 逐个 `await`，遍历字典直接 `foreach` 不拷贝。
5. **ValueTask ↔ Task 转换** — `ValueTask`→`Task` 用 `.AsTask()`；`Task`→`ValueTask` 用 `new ValueTask(task)`（`Task` 无 `AsTask` 方法）。`ValueTask` 禁止 `ContinueWith`。
6. **非 `async ValueTask DisposeAsync()` 是 fire-and-forget 根因** — 无法用 `await` 只能用 `ContinueWith`/fire-and-forget。改为 `async ValueTask DisposeAsync()` 后可 `await`（详见 [ADR 0112](0112-dispose-fire-and-forget-ban-continuewith-to-await.md)）。

## 替代方案

### 替代方案A：全靠文档约定，不提供辅助方法

- 放弃原因：用户记忆"已知坑应在代码中直接抛异常作防御"表明文档约定不可靠，后来者不看文档照样写多 try-catch。提供 `DisposeSafe` 扩展方法让"正确写法"比"错误写法"更短（`x.DisposeSafe()` vs `try { x.Dispose(); } catch ...`），用便利性引导合规。

### 替代方案B：Roslyn 分析器编译期强制 `using var`

- 放弃原因：过度工程。`using var` 强制需要分析器识别"创建后未 using 且未逃逸"的复杂数据流分析，实现成本高、误报风险大。先用规范 + 代码审查 + `DisposeSafe` 便利性引导，待反例反复出现再考虑分析器。符合用户"抽象方法足够时不写分析器，避免过度工程"的偏好。

### 替代方案C：用 `SafeHandle` 封装所有资源

- 放弃原因：`SafeHandle` 适合非托管资源互操作，对托管 `IDisposable` 是杀鸡用牛刀，增加抽象层无收益。

### 替代方案D：全面异步化，只保留 DisposeAsync()，删除全部同步 Dispose/IDisposable

- 优点：统一单一入口，无选型纠结
- 缺点：纯内存类（Span/数组/Mutex）被迫背 `ValueTask` 状态机，异步零收益负成本；`using var` 全改 `await using var`，异步传染无收益；字段持有 `IDisposable`（非 `IAsyncDisposable`）的类无法适配
- **放弃原因**：对无 I/O 资源异步化违反"简单事情简单做"，采用决策5按资源性质分流

## 后果

- 正面：
  - 资源泄漏风险降低（`using var` 编译期保证释放）
  - `Dispose()` 方法从 3-5 行 try-catch 压缩到 1-2 行 `DisposeSafe` 调用
  - 异常控制流扁平化，可读性提升
  - 统一 `ObjectDisposedException` 处理策略（幂等忽略 + 可选日志）
- 负面：
  - 现有大量反例需渐进式重构（禁止一次性大规模重构，每次一个文件/方法）
  - `DisposeSafe` 扩展方法新增一处公共 API（但粒度极小，可接受）
- 中性：
  - 规范依赖代码审查执行，无编译期强制（待反例反复出现再加分析器）

## 验证清单

- [x] `DisposeSafeExtensions` 实现并通过单元测试（11 用例全绿）
- [x] `FileToolHandlers.Dispose()` 改用 `DisposeSafe`（3 try-catch → 2 行）
- [x] `Mcp.MockServer/Program.cs` 改用 `using var reader`
- [x] `McpServer.RunAsync` 的 `reader`/`writer` 加注释说明为何不 using（Console 流）
- [x] `AgentCoordinator`/`ForkSubAgentManager` 信号量释放改用 `DisposeSafe`
- [x] `DoctorSseClient`/`StreamIdleWatchdog` 无防御 Dispose 加固
- [x] AGENTS.md 插入规范章节并引用本 ADR
- [x] 受影响项目单元测试全绿：Abs(11) + Hands.FileTool(25) + Agents(552) + Llm(386) + Mcp(211) = 1185

## 关联

- 下游：[ADR 0108](0108-dispose-consistency-analyzer-rules.md) — Dispose 一致性分析器规则（JCC9105/9106/9107，编译期强制决策4）
- 下游：[ADR 0112](0112-dispose-fire-and-forget-ban-continuewith-to-await.md) — Dispose 体内禁止 fire-and-forget（JCC9200，编译期强制决策6）
- 本 ADR：释放总纲（运行时规范 + 同步/异步选型 + 避坑清单），0108/0112 为编译期强制手段
