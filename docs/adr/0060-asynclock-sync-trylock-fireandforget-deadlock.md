# 0060. AsyncLock 同步 TryLock + StreamingToolExecutor 死锁排查

- 状态：accepted
- 日期：2026-09-04
- 决策者：项目架构组
- 关联：[0052](0052-asynclock-unified-mutex-file-access.md)、[0059](0059-asynclock-reentrancy-detection.md)

## 背景

AsyncLock 从异步 API（LockAsync/TryLockAsync）收缩为同步 `TryLock(CancellationToken)`（SemaphoreSlim(1,1) 薄封装）。迁移后发现 `Brain.Context.Tests` 中 `StreamingToolExecutorTests` 死锁，根因排查进行中。

## 决策（已落地部分）

### 1. AsyncLock 仅提供同步 TryLock + 异步 TryLockAsync

```csharp
public IDisposable? TryLock(CancellationToken ct = default)      // 同步,非async上下文用
public async ValueTask<IDisposable?> TryLockAsync(CancellationToken ct = default)  // async上下文用,避免线程池饥饿
```

- `ct == default` → 内部用 `DefaultTimeout`（5s）超时
- 超时返回 null + `LockRegistry.OnLockTimeout` 记录日志
- 取消抛 `OperationCanceledException`
- **移除 CheckReentrancy**（ThreadId 在 async/await 下因线程池复用不可靠）

### 2. fire-and-forget 用 Task.Run 避免自等自（已应用）

在 `using var guard = _lock.TryLock()` 作用域内启动 fire-and-forget async 方法时，用 `Task.Run` 隔离线程。`StreamingToolExecutor.RunFireAndForget` 已改为 `_ = Task.Run(SafeRunAsync)`。

### 3. TaskCompletionSource.TrySetResult 必须在锁外调用（核心根因修复）

**核心避坑规则**：在 `using var guard = _lock.TryLock()` 锁作用域内，**禁止调用 `TaskCompletionSource.TrySetResult`**。`TrySetResult` 唤醒的等待者续体可能在**当前线程同步执行**，若续体尝试获取同一把锁 → 锁未释放 → 自等自死锁。

```csharp
// ❌ 错误 — TrySetResult 在锁内,续体同线程同步重入锁 → 自等自
using var guard = _semaphore.TryLock() ?? throw ...;
tool.Status = Completed;
_buffer.Add(result);
tool.CompletionSource.TrySetResult(result);  // 唤醒 Task.WhenAll 续体,续体 TryLock 自等自
// guard 释放(但续体已卡死)

// ✅ 正确 — TrySetResult 移到锁外,续体执行时锁已释放
using (var guard = _semaphore.TryLock() ?? throw ...)
{
    tool.Status = Completed;
    _buffer.Add(result);
}
tool.CompletionSource.TrySetResult(result);  // 锁外唤醒,续体可正常获取锁
```

## 排错方法论（本次确立）

### 方法：修改抛错包含 DumpAll + 脚本带超时运行

死锁测试用 `dotnet test` 直接运行会永久卡死。采用两步排错法：

**步骤1：修改 TryLock 超时抛错处，嵌入 `LockRegistry.DumpAll()` 诊断**

```csharp
// 原始
using var guard = _semaphore.TryLock() ?? throw new System.TimeoutException("锁等待超时");

// 排错时改为（包含锁状态快照）
using var guard = _semaphore.TryLock()
    ?? throw new System.TimeoutException("GetCompletedResults 锁等待超时\n" + Core.Utils.LockRegistry.DumpAll());
```

**步骤2：用 `.xxx/run_test.ps1` 脚本带超时运行，避免永久卡死**

```powershell
$psi = [System.Diagnostics.ProcessStartInfo]::new()
$psi.FileName = "dotnet"
$psi.Arguments = "test <csproj> -c Debug --no-build --filter `"FullyQualifiedName~Xxx`""
$psi.UseShellExecute = $false
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$p = [System.Diagnostics.Process]::Start($psi)
$taskOut = $p.StandardOutput.ReadToEndAsync()
if (-not $p.WaitForExit(30000)) { $p.Kill(); "KILLED" } else { "EXIT $($p.ExitCode)" }
$taskOut.Result  # 输出包含 DumpAll 锁状态
```

**关键点**：
- `Process.WaitForExit(timeout)` 硬超时 + `Kill()` 兜底，永不卡死
- `ReadToEndAsync()` 异步读 stdout 避免管道死锁
- 抛错信息包含 `DumpAll()` → 测试失败时直接在错误消息中看到"哪把锁被哪个线程持有、持有多久、获取调用栈"

### 判断 DefaultTimeout 超时是时序竞争 vs 真死锁

- `DefaultTimeout = 5s` 仍超时 → **真死锁**（锁被永久持有），非时序竞争
- `DefaultTimeout = 1s` 超时但 5s 通过 → 时序竞争（锁持有 1-5s），需优化而非修死锁

## 排查记录（StreamingToolExecutor）

### 现象

`StreamingToolExecutorTests.AddTool_SingleSafeTool_ExecutesImmediately` 失败：
```
System.TimeoutException : GetCompletedResults 锁等待超时
  at StreamingToolExecutor.GetCompletedResultsAsync() line 101
  at StreamingToolExecutor.GetRemainingResultsAsync() line 133
```

### DumpAll 诊断结果（2026-09-04）

```
[LOCK-DUMP] 共 1 把锁，时间 20:13:13.923
  #1 'AsyncLock#1' — 持有中(线程 18, 已持有 5.0s)
    获取调用栈:
    at AsyncLock.TryLock(CancellationToken ct) line 74
    at StreamingToolExecutor.ExecuteToolAsync(QueuedTool tool) line 330
```

**根因确认**：锁被 `ExecuteToolAsync:330` 持有 5s 不释放。`ExecuteToolAsync:339` 在锁内调用 `TrySetResult`，唤醒 `Task.WhenAll` 续体。续体因 `ConfigureAwait(false)` + 已完成 Task 在**同一线程同步执行** `GetCompletedResultsAsync` TryLock → 锁未释放 → 自等自死锁。

### 修复

将 `ExecuteToolAsync` 的 `TrySetResult` 移到锁作用域外：

```csharp
using (var guard = _semaphore.TryLock() ?? throw ...)
{
    tool.Status = ToolStatus.Completed;
    _completedBuffer.Add(result);
    _executingCount--;
    if (!tool.IsConcurrencySafe) _nonSafeExecutingCount--;
}
tool.CompletionSource.TrySetResult(result);  // 锁外
RunFireAndForget(ProcessQueueAsync);
```

### 验证

- `StreamingToolExecutorTests` 12/12 通过（含新增 `Discard_DuringGetRemaining_DoesNotDeadlock` 回归测试）
- `Brain.Context.Tests` 778/778 通过（3s）
- `GoalEngineTests` 33/33 通过

## 全局排查与防御修复（锁内 TrySetResult 模式）

对全项目 78 处 `TrySetResult` 调用排查，确认在 AsyncLock 锁作用域内的 7 处，全部防御修复：

| 文件 | 行号 | 风险 | 修复方式 |
|------|------|------|----------|
| `StreamingToolExecutor.cs` | 339 | 高（已复现死锁） | TrySetResult 移到 `using` 锁块外 |
| `StreamingToolExecutor.cs` | 181 | 高（Discard 同模式） | TrySetResult 移到 `using` 锁块外 |
| `McpClientBase.cs` | 144 | 中（异常路径获同锁） | TrySetResult 移到 `guard.Dispose()` 后 |
| `GoalEngine.cs` | 350,395,519,548 | 低（TCS 续体选项隐患） | TCS 创建加 `RunContinuationsAsynchronously` |
| `SerialBatchEventUploader.cs` | 216 | 低（已安全） | 已用 `RunContinuationsAsynchronously`，无需修改 |

### 两种防御策略

1. **TrySetResult 移到锁外**（首选，彻底消除锁-续体耦合）：先在锁内更新状态/读取需要的数据，释放锁后 TrySetResult
2. **TCS 加 `RunContinuationsAsynchronously`**（次选，续体异步执行）：适用于 TrySetResult 难以移到锁外（如 TCS 在多处被设置）的场景

## 已修复的 fire-and-forget 场景（其他文件，已验证）

| 文件 | 方法 | 修复 |
|------|------|------|
| `InProcessTeammateTask.cs` | `RunTeammateLoopBackground` | `_ = Task.Run(SafeRunLoopAsync)` |
| `VoiceService.cs` | `StartRecordingAsync` | `_ = Task.Run(() => RecordLoopAsync(...))` |
| `MonitorMcpTask.cs` | `StartMonitoringAsync` | `_ = Task.Run(() => RunMonitorLoopAsync(...))` |

## 判断是否需要 Task.Run 的检查清单

1. fire-and-forget 调用是否在 `using var guard = _lock.TryLock()` 作用域内？→ 是则继续
2. 被调用的 async 方法内部是否也获取同一把锁？→ 是则**必须用 Task.Run**
3. 如果不在锁作用域内（如 `MailboxPoller.StartPolling`）→ 不需要 Task.Run

## 替代方案

1. **保留异步 LockAsync** — 被否决：用户明确要求"所有加锁改为同步"
2. **CheckReentrancy 用 ThreadId 检测** — 被否决：async/await 线程池复用导致误判
3. **CheckReentrancy 用 AsyncLocal FlowId** — 被否决：FlowId 跨 await 丢失不可靠
4. **StreamingToolExecutor 改用 Channel 消费者模型** — 未采用：根因是 TrySetResult 在锁内，移到锁外即解决，无需架构重构
