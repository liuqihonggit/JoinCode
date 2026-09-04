# 0059. AsyncLock 同步重入检测 — LockReentrancyException 提早暴露死锁

- 状态：superseded by 0060
- 日期：2026-09-03
- 决策者：项目架构组
- 关联：[0052](0052-asynclock-unified-mutex-file-access.md)（AsyncLock 统一互斥锁）、[0056](0056-cache-break-detection-enhancement.md)（死锁检测 wait-for graph）
- 取代：被 [0060](0060-asynclock-sync-trylock-fireandforget-deadlock.md) 取代 — CheckReentrancy 已移除（ThreadId 在 async/await 下因线程池复用不可靠，误报）；改为 TryLock 超时返回 null + TrySetResult 移到锁外（ADR-0060 决策1/3）。本 ADR 的避坑指南（锁内只操作字段、副作用移到锁外）仍然有效。

## 背景

AsyncLock 基于 `SemaphoreSlim(1,1)`，**不支持重入**。迁移前用 `lock` 语句（Monitor.Enter），Monitor 可重入，同一线程多次获取同一把锁不会死锁。迁移到 AsyncLock 后，`Toggle()` 等方法在持锁时调用 `Enable()`/`Disable()`，而这些方法内部又获取同一把锁，导致 **永久死锁**（SemaphoreSlim.Wait 阻塞等自己释放）。

### 已发现的生产代码重入死锁

| 文件 | 方法 | 根因 | 修复方式 |
|------|------|------|----------|
| `SimpleModeService.Toggle()` | 锁内调 `Enable()`/`Disable()` | 两者都获取 `_lock` | 锁内直接操作字段，锁外触发事件 |
| `FastModeService.Toggle()` | 锁内调 `Deactivate()`/`Activate()` | 两者都获取 `_lock` | 同上 |
| `FastModeService.Deactivate()` | 锁内调 `StopCooldownTimer()` | `StopCooldownTimer` 获取 `_lock` | 锁内直接 dispose 计时器 |

### 为什么 `DetectDeadlock` 没有提早发现？

`LockRegistry.DetectDeadlock` 能检测到重入自环（线程A持有锁X → 线程A等待锁X），但只通过 `Emit` 输出告警到 stderr，**不抛异常中断**。程序仍卡在 `SemaphoreSlim.Wait()`，直到 60s 超时。

## 决策

### 1. 同步重入检测：`CheckReentrancy` + `LockReentrancyException`

在 `Lock()` / `Lock(CancellationToken)` 方法中，`_semaphore.Wait()` **之前**调用 `LockRegistry.CheckReentrancy`。检测到同一线程已持有此锁时，立即抛 `LockReentrancyException`，将死锁转为**立即失败**。

```csharp
// AsyncLock.cs
public IDisposable Lock()
{
    ThrowIfDisposed();
    LockRegistry.CheckReentrancy(_registryId, _name);  // 重入检测
    LockRegistry.OnWaitStart(_registryId, _name);
    _semaphore.Wait();  // 如果重入，上面已抛异常，不会到达这里
    ...
}
```

```csharp
// LockRegistry.cs
internal static void CheckReentrancy(int id, string name)
{
    if (_locks.TryGetValue(id, out var info)
        && info.HoldingThread == Thread.CurrentThread)
    {
        throw new LockReentrancyException(name, id, ...);
    }
}
```

### 2. 仅同步方法检测，`LockAsync` 不检测

| 方法 | 检测重入？ | 原因 |
|------|-----------|------|
| `Lock()` / `Lock(ct)` | ✅ 检测 | 同线程同步重入必然死锁，持锁代码正在执行 |
| `LockAsync()` / `LockAsync(timeout)` | ❌ 不检测 | 线程池复用时不同 Task 可能在同一线程执行，线程ID检测会**误报** |
| `TryLock()` / `TryLockAsync()` | ❌ 不检测 | 非阻塞/有超时，重入返回 null 即可，不会卡死 |

### 3. async 重入靠现有 `DetectDeadlock` 告警

async 重入（同一异步流跨 `await` 后再次获取同一把锁）用线程ID检测不到（`await` 后可能换线程）。`AsyncLocal` FlowId 理论上可检测，但 `AsyncLocal.Value` 在 async 方法内部设置**不传播回调用方**，导致两次 `LockAsync` 调用的 FlowId 不同，检测失效。

async 重入由 `DetectDeadlock` 的 wait-for graph DFS 检测并告警（现有机制，不抛异常）。

## 避坑指南

### ❌ 禁止：锁内调用同一把锁的方法

```csharp
// 死锁！Toggle 持有 _lock，Enable 内部又获取 _lock
public bool Toggle()
{
    using (_lock.Lock())
    {
        if (_isSimpleMode) Disable();  // ← Disable 内部 using (_lock.Lock()) → 死锁
        else Enable();                 // ← Enable 内部 using (_lock.Lock()) → 死锁
    }
}
```

### ✅ 正确：锁内直接操作字段，锁外处理副作用

```csharp
public bool Toggle()
{
    bool newState;
    using (_lock.Lock())
    {
        newState = !_isSimpleMode;
        _isSimpleMode = newState;        // 直接操作字段
    }

    // 锁外处理副作用（调用其他服务、触发事件）
    if (newState) _briefModeService?.Enable();
    else _briefModeService?.Disable();

    SimpleModeChanged?.Invoke(this, ...);
    return newState;
}
```

### 规则

1. **锁内只操作 `_` 字段**，不调用任何获取同一把锁的方法
2. **副作用移到锁外**：触发事件、调用其他服务、启动/停止计时器
3. **私有方法标注"调用方须已持有锁"**：如果私有方法需要在持锁状态下调用，不加 `using (_lock.Lock())`，用注释标注前置条件
4. **`Monitor`/`lock` 可重入但 `AsyncLock` 不可重入**：从 `lock` 迁移到 `AsyncLock` 时，必须检查所有锁内调用链

## 验证

- 22 个 AsyncLock 单元测试全通过（含 4 个新增重入检测测试）
- 17 个 SimpleModeService 测试全通过（之前 `Toggle_Should_Switch_State` 卡死 60s）
- 17 个 FastModeService 测试全通过
- E2E `SimpleCommand_ShouldToggleSimpleMode` 从卡死（57s 超时）变为 4s 通过

## 排查范围

对 54 个包含 `AsyncLock _lock` 字段的生产代码类逐一排查，确认仅 `SimpleModeService` 和 `FastModeService` 存在重入死锁，均已修复。其余 52 个类采用安全模式（锁内只操作字段 / 私有方法标注前置条件 / 事件在锁外触发）。
