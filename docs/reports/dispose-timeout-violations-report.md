# 释放函数超时违规报告

> 生成时间:2026-09-16
> 搜索方式:10 个子代理并行分区扫描 + 全项目超时模式反向扫描 + 同步阻塞专项扫描
> 规则:**所有释放函数(Dispose/DisposeAsync/Close/CloseAsync/ShutdownAsync/Shutdown)体内禁止任何超时/阻塞等待**,因为无论如何都要释放,加超时会导致资源泄漏;加阻塞会导致死锁

## 汇总

| 类别 | 违规数 | 涉及文件数 |
|------|--------|-----------|
| 生产代码 | 26 | 14 |
| 测试代码 | 11 | 7 |
| **合计** | **37** | **21** |

### 按违规模式分类(生产代码)

| 模式 | 数量 | 严重程度 |
|------|------|---------|
| `.Wait(TimeSpan)` 同步阻塞带超时 | 8 | 🔴 高(超时后资源泄漏 + 同步阻塞) |
| `.GetAwaiter().GetResult()` 同步阻塞无限等待 | 5 | 🔴 高(死锁风险) |
| `CancellationTokenSource(超时)` + `await task` 配合超时 token | 4 | 🟡 中(超时后抛 OperationCanceledException,释放中断) |
| `Thread.Join(TimeSpan)` 线程等待带超时 | 2 | 🟡 中(超时后线程泄漏) |
| `.WaitAsync(TimeSpan, ct)` 异步超时等待 | 2 | 🟡 中(超时后释放中断) |
| 间接调用(释放函数调用其他含超时的释放函数) | 2 | 🟠 低(传递违规) |
| `TryLock` 超时抛 `TimeoutException` | 1 | 🔴 高(释放路径抛异常,清理未完成) |
| `Task.WhenAll(...).WaitAsync(graceCts.Token)` 批量超时等待 | 1 | 🟡 中(部分子进程未释放) |
| `OnWindowClosed` 中 `.Wait(TimeSpan)` UI 线程阻塞 | 1 | 🔴 高(UI 卡顿 + 资源泄漏) |

---

## 一、生产代码违规(26 处)

### 1.1 同步 Dispose 委托异步 DisposeAsync + .Wait(TimeSpan) 模式(8 处)

> 共性:在同步 `Dispose()` 内调用 `DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5))`,5 秒超时后吞异常并记日志。**问题:超时后异步释放未完成就被放弃,资源泄漏**

| # | 文件 | 行号 | 代码片段 |
|---|------|------|----------|
| 1 | `lib/vault/memdir/sync/core/TeamMemorySyncService.cs` | 433 | `DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));` |
| 2 | `lib/infrastructure/shell/ShellProcessWatchdog.cs` | 149 | `DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));` |
| 3 | `lib/infrastructure/process/ProcessHealthMonitor.cs` | 153 | `DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));` |
| 4 | `lib/abstractions/abs_core/interfaces/services/RemoteCacheRefreshServiceBase.cs` | 144 | `.Wait(TimeSpan.FromSeconds(5));` |
| 5 | `kit/mcp_tool_dispatch/core/execution/ToolHealthMonitor.cs` | 391 | `DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));` |
| 6 | `kit/brain/summary/AwaySummaryService.cs` | 359 | `DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));` |
| 7 | `kit/brain/context/services/loop/LoopDiagnosticJournal.cs` | 131 | `DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));` |
| 8 | `app/gui/views/core/MainWindow.axaml.cs` | 112 | `_vm.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3));` (UI 线程阻塞) |

### 1.2 .GetAwaiter().GetResult() 同步阻塞无限等待(5 处)

> 共性:在同步释放函数内用 `.GetAwaiter().GetResult()` 同步阻塞等待异步释放,**无超时保护,可能永久阻塞,死锁风险极高**

| # | 文件 | 行号 | 函数 | 代码片段 |
|---|------|------|------|----------|
| 9 | `lib/plugins.infrastructure/services/PluginManager.cs` | 959 | Dispose | `DisposeAsync().GetAwaiter().GetResult();` |
| 10 | `lib/transport.impl/bridge/v1/V1ReplBridgeTransport.cs` | 218 | Close | `CloseAsync(_disposeCts.Token).GetAwaiter().GetResult();` |
| 11 | `kit/hands/skills/discovery/SkillDiscoveryService.cs` | 245 | Dispose | `DisposeAsync().AsTask().GetAwaiter().GetResult();` |
| 12 | `llm/agents/Services/Core/AgentServiceImpl.cs` | 885 | OnDispose | `sessionsTask.GetAwaiter().GetResult();` |
| 13 | `server/bridge/session/main/core/BridgeMain.Helpers.cs` | 481 | OnDispose | `_tokenRefresh?.DisposeAsync().GetAwaiter().GetResult();` |

### 1.3 CancellationTokenSource(超时) + await task 配合超时 token(4 处)

> 共性:在释放函数内 `new CancellationTokenSource(TimeSpan.FromSeconds(N))` 构造带超时的 token,然后 `await task.ConfigureAwait(false)`。**问题:超时后 task 被 cancel,释放中断,资源泄漏**

| # | 文件 | 行号 | 函数 | 代码片段 |
|---|------|------|------|----------|
| 14 | `lib/transport.impl/bridge/v1/V1ReplBridgeTransport.cs` | 185-186 | CloseAsync | `using var cts = new CancellationTokenSource(CloseGraceMs);` + `await uploader.FlushAsync(cts.Token)` |
| 15 | `llm/agents/Services/Core/AgentServiceImpl.cs` | 883 | OnDispose | `sessionsTask.Wait(TimeSpan.FromSeconds(5))` (Wait 返回 false 时跳过,但 890 行再次 Wait) |
| 16 | `llm/agents/Services/Core/AgentServiceImpl.cs` | 890 | OnDispose | `CleanupWorktreeAsync(...).Wait(TimeSpan.FromSeconds(5))` |
| 17 | `server/bridge/session/core/BridgeSubprocessManager.cs` | 473-476 | DisposeAsync | `new CancellationTokenSource(TimeSpan.FromSeconds(5))` + `await _process.WaitForExitAsync(cts.Token)` |

### 1.4 Thread.Join(TimeSpan) 线程等待带超时(2 处)

> 共性:在 `Dispose()` 内用 `Thread.Join(TimeSpan)` 等待后台线程退出,**超时后线程仍在运行,线程泄漏**

| # | 文件 | 行号 | 代码片段 |
|---|------|------|----------|
| 18 | `lib/infrastructure/reaper_scheduler/ReaperScheduler.cs` | 103 | `Thread.Join(TimeSpan.FromSeconds(5));` |
| 19 | `kit/brain/context/services/loop/StreamTokenDetector.cs` | 213 | `_detectThread.Join(TimeSpan.FromSeconds(1));` |

### 1.5 .WaitAsync(TimeSpan, ct) 异步超时等待(2 处)

> 共性:在释放函数内 `await task.WaitAsync(TimeSpan, ct)`,**超时后抛 TimeoutException,释放中断**

| # | 文件 | 行号 | 函数 | 代码片段 |
|---|------|------|------|----------|
| 20 | `lib/guard/security/sandbox/ipc/SandboxIpcClient.cs` | 171 | ShutdownAsync | `await _writeConsumerTask.WaitAsync(TimeSpan.FromSeconds(3), ct)` |
| 21 | `server/bridge/session/core/BridgeSubprocessManager.cs` | 877 | ShutdownAllAsync | `await Task.WhenAll(doneTasks).WaitAsync(graceCts.Token)` (graceCts 带超时) |

### 1.6 间接调用违规(释放函数调用其他含超时的释放函数)(2 处)

| # | 文件 | 行号 | 函数 | 间接调用 |
|---|------|------|------|----------|
| 22 | `lib/transport.impl/bridge/v1/V1ReplBridgeTransport.cs` | 276 | DisposeAsync | 调用 `Close()`(含 .GetAwaiter().GetResult() 阻塞) |
| 23 | `lib/guard/security/sandbox/ipc/SandboxIpcClient.cs` | 314 | DisposeAsync | 调用 `await ShutdownAsync()`(含 .WaitAsync 超时) |

### 1.7 TryLock 超时抛 TimeoutException(1 处)

> 释放路径中获取锁失败抛 `TimeoutException`,**导致清理未完成**

| # | 文件 | 行号 | 函数 | 代码片段 |
|---|------|------|------|----------|
| 24 | `llm/agents/Coordinator/Fork/ForkSubAgentManager.cs` | 602 | DisposeAsync→CleanupForkEntriesAsync | `_lock.TryLock() ?? throw new System.TimeoutException($"锁 '{_lock.Name}' 等待超时")` |

### 1.8 其他(2 处)

| # | 文件 | 行号 | 函数 | 说明 |
|---|------|------|------|------|
| 25 | `lib/transport.impl/bridge/v1/V1ReplBridgeTransport.cs` | 186 | CloseAsync | `await uploader.FlushAsync(cts.Token)` 配合 185 行的 CancelAfter 超时 |
| 26 | `server/bridge/session/core/BridgeSubprocessManager.cs` | 476 | DisposeAsync | `await _process.WaitForExitAsync(cts.Token)` 配合 473 行的 CancelAfter 超时 |

---

## 二、测试代码违规(11 处)

> 测试代码中的超时通常是**测试清理保护**(防止 DisposeAsync 卡死导致测试挂起),属于测试最佳实践。但严格按规则也算违规,列出供参考

| # | 文件 | 行号 | 函数 | 模式 | 代码片段 |
|---|------|------|------|------|----------|
| T1 | `lib/guard.config.tests/plugins/EffectScopeTests.cs` | 6 | DisposeSync | .GetAwaiter().GetResult() | `scope.DisposeAsync().GetAwaiter().GetResult()` |
| T2 | `lib/scheduling.tests/scheduling/tasks/MonitorMcpTaskExecutorTests.cs` | 20 | DisposeAsync | .WaitAsync(TimeSpan) | `.WaitAsync(TimeSpan.FromSeconds(10))` |
| T3 | `server/bridge.tests/bridge/core/FlushGateTests.cs` | 20 | DisposeAsync | .WaitAsync(TimeSpan) | `.WaitAsync(TimeSpan.FromSeconds(10))` |
| T4 | `server/bridge.tests/bridge/core/PeerSessionManagerTests.cs` | 19 | DisposeAsync | .WaitAsync(TimeSpan) | `.WaitAsync(TimeSpan.FromSeconds(10))` |
| T5 | `server/bridge.tests/bridge/core/BridgeSessionRunnerTests.cs` | 29 | DisposeAsync | .WaitAsync(TimeSpan) | `.WaitAsync(TimeSpan.FromSeconds(10))` |
| T6 | `server/bridge.tests/bridge/core/BridgeServerWebSocketTests.cs` | 34 | DisposeAsync | CancellationTokenSource(TimeSpan) | `new CancellationTokenSource(TimeSpan.FromSeconds(5))` |
| T7 | `server/bridge.tests/bridge/core/BridgeServerWebSocketTests.cs` | 35 | DisposeAsync | await task 配合超时 token | `await _server.StopAsync(stopCts.Token)` |
| T8 | `test/unit/testing.common/process/StdioProcessManager.cs` | 250 | StopAsync(由DisposeAsync调用) | TimeSpan timeout 参数 | `public async Task StopAsync(TimeSpan? timeout = null)` |
| T9 | `test/unit/testing.common/process/StdioProcessManager.cs` | 266 | StopAsync | await task 配合超时 token | `await _process.WaitForExitAsync(killCts.Token)` |
| T10 | `test/unit/testing.common/process/StdioProcessManager.cs` | 280 | StopAsync | Task.WhenAny + Task.Delay | `await Task.WhenAny(stdoutTask, Task.Delay(timeout.Value))` |
| T11 | `test/unit/testing.common/process/StdioProcessManager.cs` | 282 | StopAsync | Task.WhenAny + Task.Delay | `await Task.WhenAny(stderrTask, Task.Delay(timeout.Value))` |

---

## 三、无违规目录

| 目录 | 说明 |
|------|------|
| `gen/` | 仅含源码生成器/分析器,无真正的释放函数定义 |
| `build/` | 仅含 .ps1 脚本,无 .cs 文件 |
| `tool/` | 仅含 .py 脚本,无 .cs 文件 |
| `libs/` | 空目录(主代码库在 `lib/` 单数) |
| `non_deliverables_tools/` | 无真正的释放函数定义(仅审计工具逻辑) |
| `tests/` | 仅含 MockServers 子目录,无 .cs 文件 |

---

## 四、修复建议

### 4.1 核心原则

**释放函数必须无条件、无超时、无阻塞地完成所有资源释放。** 如果某个资源释放可能卡住,说明该资源的释放逻辑本身有 bug,应该修复底层释放逻辑,而不是在上层加超时掩盖问题。

### 4.2 修复优先级

| 优先级 | 违规类型 | 修复方式 |
|--------|---------|---------|
| P0 | `.GetAwaiter().GetResult()` 无限阻塞(5 处) | 改为 `await DisposeAsync()` 或用 `DisposeSafe(_logger)` 扩展方法 |
| P0 | `TryLock` 抛 TimeoutException(1 处) | 释放路径用无锁设计(Actor 邮箱模型)或 `TryLock()` 返回 null 时跳过而非抛异常 |
| P0 | UI 线程 `.Wait(TimeSpan)`(1 处) | 改为 fire-and-forget + 日志,或 `await using` 语法 |
| P1 | `.Wait(TimeSpan)` 同步阻塞带超时(8 处) | 改为 `await DisposeAsync()` 或 `DisposeSafe(_logger)` |
| P1 | `CancellationTokenSource(超时)` + await(4 处) | 移除超时,用 `CancellationToken.None` 或 `_disposeCts.Token`(无超时) |
| P1 | `.WaitAsync(TimeSpan, ct)`(2 处) | 移除超时,直接 `await task` |
| P2 | `Thread.Join(TimeSpan)`(2 处) | 改为 `await` 异步等待,或用 `volatile bool _stopping` + `PeriodicTimer` 模式 |
| P2 | 间接调用违规(2 处) | 修复被调用的释放函数后自动消除 |

### 4.3 推荐修复模式

**模式 A:同步 Dispose 委托异步 DisposeAsync(推荐 DisposeSafe 扩展方法)**

```csharp
// ❌ 违规
public void Dispose() {
    DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
}

// ✅ 正确(使用 DisposeSafe 扩展方法,见 AGENTS.md 代码风格规范)
public void Dispose() {
    this.DisposeAsyncSafe(_logger);  // 内部 fire-and-forget + 日志,不阻塞
}
```

**模式 B:异步 DisposeAsync 无超时**

```csharp
// ❌ 违规
public async ValueTask DisposeAsync() {
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    await _process.WaitForExitAsync(cts.Token);
}

// ✅ 正确(无超时,用内部取消令牌)
public async ValueTask DisposeAsync() {
    _disposeCts.Cancel();
    await _process.WaitForExitAsync(_disposeCts.Token).ConfigureAwait(false);
}
```

**模式 C:释放路径锁(用 Actor 邮箱模型替代)**

```csharp
// ❌ 违规
private async Task CleanupForkEntriesAsync() {
    using var guard = _lock.TryLock() ?? throw new TimeoutException("锁超时");
    // ...
}

// ✅ 正确(Actor 邮箱模型,无锁,见 AGENTS.md "Actor邮箱管道是无锁模型")
private async Task CleanupForkEntriesAsync() {
    await _mailbox.PostAsync(CleanupMessage.Instance).ConfigureAwait(false);
}
```

---

## 五、子代理搜索覆盖说明

| 子代理 | 范围 | 违规数 |
|--------|------|--------|
| 1 | lib/ 释放函数 | 12(含测试) |
| 2 | server/ 释放函数 | 8(含测试) |
| 3 | kit/ 释放函数 | 5 |
| 4 | llm/ 释放函数 | 4 |
| 5 | app/ 释放函数 | 1 |
| 6 | test/+tests/ 释放函数 | 4(间接) |
| 7 | gen/+build/+tool/ 释放函数 | 0 |
| 8 | libs/+non_deliverables_tools/ 释放函数 | 0 |
| 9 | 全项目超时模式反向扫描 | 2(1 新增 + 1 重复) |
| 10 | 全项目同步阻塞专项扫描 | 4(2 新增 + 2 重复) |

> ⚠️ 子代理 10 额外发现 2 处之前未报告的违规:`RemoteCacheRefreshServiceBase.cs:144` 和 `ReaperScheduler.cs:103`,已补入本报告
> ⚠️ 子代理 9 额外发现 1 处:`BridgeSubprocessManager.cs:877` 的 `ShutdownAllAsync`,已补入本报告

---

## 六、验证建议

本报告基于 10 个子代理并行搜索结果。建议用 Python 脚本对照验证完整性:

```python
import re, pathlib
roots = [pathlib.Path("D:/project/w2")]
patterns = [r"\.Wait\s*\(\s*TimeSpan", r"\.GetAwaiter\(\)\.GetResult\(\)", r"\.WaitAsync\s*\(\s*TimeSpan",
            r"CancelAfter\s*\(", r"Thread\.Join\s*\(\s*TimeSpan", r"Task\.WhenAny\s*\("]
# 对每个匹配,读取上下文判断是否在释放函数体内
# ...
```
