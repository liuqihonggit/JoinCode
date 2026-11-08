# 0155. 统一 fire-and-forget 基础设施（FireAndForgetTask + FireAndForgetTracker）

> 📍 **导航**: [docs/](../README.md) › [adr/](README.md)
> 🔗 **上游索引**: [adr/README.md](README.md) — 修改本文档后须同步更新此索引

- 状态：accepted
- 日期：2026-11-09
- 决策者：项目架构组
- 关联：[0060](../../implemented/adr/security_concurrency/0060-asynclock-sync-trylock-fireandforget-deadlock.md)、[0125](../../implemented/adr/security_concurrency/0125-actor-register-inflight-dispose-guard.md)

## 背景

项目中 fire-and-forget 模式碎片化为四种独立实现，各自为政，行为不一致：

| 模式 | 代表 | 完成保证 | Dispose 守卫 | 异常处理 | 数量 |
|------|------|---------|-------------|---------|------|
| `_ = Task.Run(...)` 裸跑 | LSP 通知、日志写入 | **无** | **无** | **无**（静默丢失） | 20+ 处 |
| `_loadTask` | PermissionChecker 等 15+ 类 | 暴露 Task 属性供 await | 各自 try-catch await | 各自处理 | 15+ 类 |
| `RegisterInFlight` | ActorBase 子类 | Actor Dispose WhenAll | ✅ 统一 | ✅ 统一 | Actor 体系内 |
| `TCS? Completion` | PersistRequest | 可选 TCS | Actor 消费侧 | Actor 内部 | 持久化管道 |

### 核心问题

1. **`_ = Task.Run(...)` 裸跑无守卫** — 20+ 处 fire-and-forget 任务没有 Dispose 守卫，进程退出时资源泄漏，异常静默丢失
2. **`_loadTask` 模式重复代码** — 15+ 类各自实现 `private readonly Task _loadTask` + `await _loadTask.ConfigureAwait(false)` + try-catch，行为不一致
3. **`RegisterInFlight` 仅 Actor 可用** — 非 Actor 类无法享受统一 Dispose 守卫
4. **释放策略一刀切** — 当前 Dispose 要么无限等（ActorBase.DisposeAsync），要么不等（裸跑 `_ = Task.Run`），没有中间地带

### 释放矛盾

当前 AGENTS.md 规则4 禁止释放函数超时，但现实中存在三种释放场景：

| 场景 | 期望行为 | 当前行为 |
|------|---------|---------|
| 正常退出（DI shutdown） | 等所有 in-flight 完成 | ✅ ActorBase 正确 |
| 内存溢出 / 资源压力 | 快速退出，放弃等待 | ❌ 卡住等 WhenAll |
| Ctrl+C / 进程退出 | 快速响应 | ❌ 卡住等 WhenAll |

一刀切的"无限等"在资源压力场景下会加剧问题（内存已溢出，Dispose 还在等 30 秒）。

## 决策

### 两层结构

**第一层：`FireAndForgetTask<T>` — 单个任务**

统一四种模式的完成保证强度，内置 CancellationToken + 异常处理 + Dispose 守卫：

```csharp
public sealed class FireAndForgetTask<T> : IAsyncDisposable {
    // 启动（替代 _ = Task.Run 和 _loadTask 两种模式）
    public static FireAndForgetTask<T> Run(Func<CancellationToken, Task<T>> factory, string? name = null);
    public static FireAndForgetTask<Unit> Run(Func<CancellationToken, Task> factory, string? name = null);

    // 强度 1：可选等待（替代 _loadTask.Task / TCS? Completion）
    public Task<T> Task { get; }
    public bool IsCompleted { get; }

    // 强度 2：正常释放 — 等完成（替代 RegisterInFlight + Dispose WhenAll）
    public ValueTask DisposeAsync();

    // 强度 2+：紧急释放 — 有限等待窗口（新增，解决释放矛盾）
    public ValueTask DisposeAsync(TimeSpan gracePeriod);
}
```

设计要点：
- **自带 CancellationTokenSource** — DisposeAsync 先 Cancel 再等，通知任务应该停止
- **异常默认吞 + 日志** — fire-and-forget 语义下异常不应传播到调用方（对齐 JCC3005 理由：async void 异常炸进程）
- **`Task` 属性天然就是可选等待句柄** — 替代 `_loadTask` 和 `TCS? Completion` 两种模式
- **`DisposeAsync(TimeSpan)` 梯度释放** — 正常释放无限等，紧急释放有限等

**第二层：`FireAndForgetTracker` — 容器**

解决 `_ = Task.Run(...)` 裸跑无守卫的问题，提供统一注册和批量释放：

```csharp
public sealed class FireAndForgetTracker : IAsyncDisposable {
    // 注册任务（替代 _ = Task.Run 裸跑）
    FireAndForgetTask<T> Register<T>(Func<CancellationToken, Task<T>> factory, string? name = null);
    FireAndForgetTask<Unit> Register(Func<CancellationToken, Task> factory, string? name = null);

    // 正常释放：等所有注册任务完成
    ValueTask DisposeAsync();

    // 紧急释放：有限等待窗口
    ValueTask DisposeAsync(TimeSpan gracePeriod);
}
```

设计要点：
- **容器级释放策略** — 单个 FireAndForgetTask 不决定释放策略，由最外层容器决定
- **进程级 Ctrl+C handler → `tracker.DisposeAsync(3s)`** — 给 3 秒优雅退出
- **DI 容器正常 shutdown → `tracker.DisposeAsync()`** — 无限等
- **测试 teardown → `tracker.DisposeAsync(10s)`** — 10 秒上限

### 梯度释放行为

```
DisposeAsync()           → Cancel + 无限等完成（正常关闭）
DisposeAsync(5s)         → Cancel + 等 5 秒，超时记录泄漏警告（资源压力）
DisposeAsync(TimeSpan.Zero) → Cancel + 立即走（紧急）
```

`DisposeAsync(TimeSpan)` 的实现用 `Task.WhenAny(Task.WhenAll(all), Task.Delay(gracePeriod))`：
- 这不是"单个资源释放超时"（AGENTS.md 规则4 禁止的场景）
- 而是"容器级系统策略决策"——给所有任务一个统一的 grace period
- 两者本质不同：前者是"一个 stream.Dispose() 不应该超时"，后者是"进程退出时给所有后台任务 5 秒收尾"

### 四种现有模式映射

| 现有模式 | 统一后 |
|---------|--------|
| `_ = Task.Run(...)` 裸跑 | `tracker.Register(async ct => ...)` — 必须注册到容器 |
| `_loadTask` | `var fft = FireAndForgetTask.Run(...); _loadTask = fft.Task;` — Dispose 时 `await fft.DisposeAsync()` |
| `RegisterInFlight` | ActorBase 内部用 `FireAndForgetTracker` 替代手写 `_inFlightTasks` |
| `TCS? Completion` | `FireAndForgetTask.Task` 属性天然就是可选等待句柄 |

### 异常策略

| 场景 | 行为 |
|------|------|
| 默认 | 吞异常 + 日志记录（fire-and-forget 语义） |
| `await fft.Task` | 异常传播给 await 方（正常 Task 语义） |
| `DisposeAsync()` | 异常吞 + 日志（释放不应因单个任务异常而中断其他任务等待） |

## 替代方案

1. **只保留 `_loadTask` 模式，不统一** — 被否决：20+ 处裸跑 `_ = Task.Run` 无守卫，异常静默丢失，资源泄漏
2. **用 `Channel<T>` 统一所有 fire-and-forget** — 被否决：Channel 是 Actor 模型的基础设施，适合消息传递场景，不适合简单的后台任务启动
3. **扩展 `RegisterInFlight` 到非 Actor 类** — 被否决：RegisterInFlight 依赖 ActorBase 的 Consumer 线程串行不变量，非 Actor 类没有这个保证
4. **只做 FireAndForgetTask，不做 Tracker** — 被否决：20+ 处裸跑 `_ = Task.Run` 需要一个容器来统一管理生命周期，否则每处都要手动 Dispose
5. **Dispose 一律无限等** — 被否决：内存溢出等资源压力场景下，无限等会加剧问题

## 影响

### 新增代码

| 组件 | 位置 |
|------|------|
| `FireAndForgetTask<T>` | `modules/async_primitives/async_lock/` 下新建 |
| `FireAndForgetTracker` | `modules/async_primitives/async_lock/` 下新建 |
| 单元测试 | `modules/async_primitives/async_lock.tests/` 下新建 |

### 需重构的现有代码

| 现有模式 | 涉及文件数 | 优先级 |
|---------|-----------|--------|
| `_ = Task.Run(...)` 裸跑 → `tracker.Register(...)` | 20+ 处 | P1（无守卫最危险） |
| `_loadTask` → `FireAndForgetTask.Run` | 15+ 类 | P2（有守卫但重复代码） |
| `RegisterInFlight` → `FireAndForgetTracker` | ActorBase + 子类 | P3（已有守卫，统一接口） |
| `TCS? Completion` → `FireAndForgetTask.Task` | PersistRequest | P3（已有可选等待） |

### 分析器规则更新

| 规则 | 更新内容 |
|------|---------|
| JCC3015 | 检测 `_ = Task.Run(...)` 未注册到 `FireAndForgetTracker` |
| JCC3001/3002 | 检测 `FireAndForgetTask.Run` 未传 CancellationToken |

### AGENTS.md 规则更新

- 规则4（释放函数禁止超时）需增加例外：容器级 `DisposeAsync(TimeSpan gracePeriod)` 是系统策略决策，不是单个资源释放超时
- 新增规则：`_ = Task.Run(...)` 裸跑禁止，必须用 `FireAndForgetTask.Run` 或 `tracker.Register`

## 验收标准

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| `FireAndForgetTask<T>` 类型 | 替换 `_loadTask` 模式的 12 类 | ✅ | ✅ P2 完成 |
| `FireAndForgetTracker` 容器 | 替换 `_ = Task.Run` 裸跑的 40 处 | ✅ | ✅ P1 完成 |
|' `RegisterExistingTask` + 水位线清理 | 替换 `RegisterInFlight` 的 ActorBase | ✅ | ✅ P3 完成 |
| `EnqueueAndWaitAsync` + internal CompletionSource | 替换 `TCS? Completion` 的 PersistRequest | ✅ | ✅ P4 完成 |
| `DisposeAsync(TimeSpan)` 梯度释放 | 进程退出 / 内存溢出 / 测试 teardown | ✅ | ✅ 单元测试覆盖 |
| 分析器规则 JCC3021 | 检测 `_ = Task.Run(...)` 裸跑 | ✅ | ✅ Warning 级别 |
