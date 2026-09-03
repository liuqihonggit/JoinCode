# 0060. AsyncLock 同步 TryLock + fire-and-forget 自等自死锁避坑

- 状态：accepted
- 日期：2026-09-04
- 决策者：项目架构组
- 关联：[0052](0052-asynclock-unified-mutex-file-access.md)、[0059](0059-asynclock-reentrancy-detection.md)

## 背景

AsyncLock 从异步 API（LockAsync/TryLockAsync）收缩为唯一的同步 `TryLock(CancellationToken)`（SemaphoreSlim(1,1) 薄封装）。迁移后发现 CI 测试失败，根因是一个隐蔽的"自等自"死锁模式。

## 决策

### 1. AsyncLock 仅提供同步 TryLock(CancellationToken)

```csharp
public IDisposable? TryLock(CancellationToken ct = default)
```

- `ct == default` → 内部用 `DefaultTimeout`（1s）超时
- `ct` 可取消 → 同时受内部 1s 超时和 ct 取消约束
- 超时返回 null + `LockRegistry.OnLockTimeout` 记录日志
- 取消抛 `OperationCanceledException`
- **移除全部异步方法**（LockAsync/TryLockAsync/Lock/TryLock(TimeSpan)）
- **移除 CheckReentrancy**（ThreadId 在 async/await 下因线程池复用不可靠）

### 2. fire-and-forget 必须用 Task.Run 避免自等自

**核心避坑规则**：在 `using var guard = _lock.TryLock()` 作用域内启动 fire-and-forget async 方法时，**必须用 `Task.Run`**，不能直接 `_ = MethodAsync()`。

## 避坑指南

### 问题模式：async 方法同步前缀自等自

```csharp
// ❌ 错误 — 自等自死锁
using var guard = _lock.TryLock() ?? throw ...;
_ = BackgroundLoopAsync(ct);  // fire-and-forget
return result;
// BackgroundLoopAsync 的 async 同步前缀在当前线程执行
// 如果 BackgroundLoopAsync 内部也 TryLock 同一把锁 → 自等自 → 超时

// ✅ 正确 — Task.Run 隔离线程
using var guard = _lock.TryLock() ?? throw ...;
_ = Task.Run(() => BackgroundLoopAsync(ct));  // 在另一线程执行
return result;
```

### 根因详解

C# async 方法在**第一个 await 之前**的代码是**同步执行**的。`_ = MethodAsync()` 不会立即跳到线程池，而是在当前线程同步执行 MethodAsync 的同步前缀。如果 MethodAsync 内部第一个操作是 `TryLock`，而当前线程已持有同一把锁，`SemaphoreSlim.Wait` 会自等自 → 阻塞 → 超时返回 null → throw TimeoutException。

### 已修复的 fire-and-forget 场景

| 文件 | 方法 | 修复 |
|------|------|------|
| `InProcessTeammateTask.cs` | `RunTeammateLoopBackground` | `_ = Task.Run(SafeRunLoopAsync)` |
| `VoiceService.cs` | `StartRecordingAsync` | `_ = Task.Run(() => RecordLoopAsync(...))` |
| `MonitorMcpTask.cs` | `StartMonitoringAsync` | `_ = Task.Run(() => RunMonitorLoopAsync(...))` |

### 判断是否需要 Task.Run 的检查清单

1. fire-and-forget 调用是否在 `using var guard = _lock.TryLock()` 作用域内？→ 是则继续
2. 被调用的 async 方法内部是否也获取同一把锁？→ 是则**必须用 Task.Run**
3. 如果不在锁作用域内（如 `MailboxPoller.StartPolling`）→ 不需要 Task.Run

### DefaultTimeout = 1s 的理由

- 5s 太长：测试要等 5s 才超时失败，CI 测试时间膨胀
- 1s 足够：正常锁竞争在毫秒级释放，1s 是安全网而非预期等待时间
- 如果 1s 还超时 → 说明有死锁或锁持有时间过长，需要诊断而非等更久

## 验证

- App.slnx Debug 编译 0 警告 0 错误
- Scheduling.Tests 262/262 通过
- Hands.Shell.Tests VoiceService 7/7 通过
- AsyncLock.Tests 21/21 通过
- Infra.Utils.Tests 36/36 通过

## 替代方案

1. **保留异步 LockAsync** — 被否决：用户明确要求"所有加锁改为同步"
2. **CheckReentrancy 用 ThreadId 检测** — 被否决：async/await 线程池复用导致误判
3. **CheckReentrancy 用 AsyncLocal FlowId** — 被否决：FlowId 跨 await 丢失不可靠
