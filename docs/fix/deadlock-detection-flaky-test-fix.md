# 死锁检测测试偶发失败修复

> 状态:✅ 已修复并重构,完全消除 ThreadID,统一用 AsyncLocal FlowId
> 关联测试:`AsyncLockDiagnosisTests.死锁检测_async两个流互相等待时自动检测`
> 关联排查:E2E - Cluster 偶发失败同源(集群依赖 AsyncLock/ActorBase),详见第 6 节
> 发现日期:2026-09-18

## 1. 问题现象

`死锁检测_async两个流互相等待时自动检测` 偶发失败,针对 `AsyncLockDiagnosisTests` 类压测 20 次复现:

| 阶段 | 失败率 | 说明 |
|------|--------|------|
| 原始 | 15% (3/20) | 偶发 |
| 改 LongRunning 后 | 80% (16/20) | 改动让情况更糟,但**暴露了真根因** |

失败断言:
```
Expected boolean to be True because 两个 async 流互相等待对方持有的锁应被自动检测为死锁
(两轮3s共6s,容忍CI高负载), but found False.
```
即 `LockRegistry.DeadlockDetected` 等了 6s 仍为 false。

## 2. 根因分析(已确认)

### 2.1 真根因:async 下 Thread.CurrentThread 不可靠,wait-for graph 被拆碎

`LockRegistry.OnWaitStart`(108行)和 `OnAcquired` 用 `Thread.CurrentThread` 记录等待/持有线程:
```csharp
info.WaitingThread = Thread.CurrentThread;  // OnWaitStart, 108行
info.HoldingThread = Thread.CurrentThread;  // OnAcquired
```

但 async 下 `await` 会**切换线程**,导致同一个逻辑流(t1)的 `HoldingThread` 和 `WaitingThread` 是**不同线程ID**:

```
t1 在线程A 拿 lockA → HoldingThread(lockA) = 线程A
t1 await t2Ready    → 切换到线程B
t1 在线程B 等 lockB → WaitingThread(lockB) = 线程B

t2 在线程C 拿 lockB → HoldingThread(lockB) = 线程C
t2 await t1Ready    → 切换到线程D
t2 在线程D 等 lockA → WaitingThread(lockA) = 线程D
```

### 2.2 DetectDeadlock 构建的 wait-for graph 被拆碎

`DetectDeadlock()`(368行)用 `waitingThread.ManagedThreadId` → `holdingThread.ManagedThreadId` 构建边:

```
lockA: WaitingThread=线程D, HoldingThread=线程A → 边: 线程D→线程A
lockB: WaitingThread=线程B, HoldingThread=线程C → 边: 线程B→线程C
```

DFS 找环(385-401行):
```
从线程D: D→A,A无出边 → break
从线程B: B→C,C无出边 → break
找不到环!
```

**真实的逻辑流是 `t1(持A等B) ↔ t2(持B等A)` 应形成环**,但线程ID把 t1 拆成线程A+B,t2 拆成线程C+D,**图的结构被拆碎了**。

### 2.3 为什么偶发

- 有时 `await` 恰好同步完成(不切换线程),线程ID一致,图正确→检测到死锁
- 有时 `await` 切换了线程,图错误→检测不到死锁
- 取决于线程池调度,故偶发

### 2.4 为什么 LongRunning 让失败率升到 80%

`Task.Factory.StartNew(async () => {...}, LongRunning)` 创建专用线程跑 async lambda,但 async lambda 内的 `await` 后续**仍在线程池**执行(专用线程没有 SynchronizationContext)。

- 专用线程 ID 与线程池线程 ID 差异大
- `await` 后续从专用线程切到线程池,**更容易切换线程**
- 图更容易被拆碎 → 失败率从 15% 升到 80%

**这反而暴露了真根因**(之前误判为线程池饥饿)。

### 2.5 之前误判:线程池饥饿

最初假设根因是线程池饥饿(t1/t2 启动慢 + Timer 扫描不执行)。但:
- 改 LongRunning + 提高线程池最小线程数后,失败率反而从 15% 升到 80%
- 如果是线程池饥饿,提高最小线程数应缓解,但实际加剧
- 真根因是 `Thread.CurrentThread` 在 async 下不可靠,与线程池饥饿无关

## 3. 修复方案

### 3.1 方案:用 AsyncLocal<int> FlowId 替换 Thread.CurrentThread

`AsyncLocal<T>` 在 async 流中**自动流转**,不随线程切换变化:
- t1 启动时设 `AsyncLocal<int> FlowId = 1`
- t1 的所有 `await` 后续都继承 `FlowId = 1`(即使切到不同线程)
- `OnWaitStart`/`OnAcquired` 记录 `FlowId` 而非 `Thread.ManagedThreadId`
- wait-for graph 用 FlowId 构建:

```
t1(FlowId=1) 持 lockA 等 lockB → HoldingThread(lockA)=1, WaitingThread(lockB)=1
t2(FlowId=2) 持 lockB 等 lockA → HoldingThread(lockB)=2, WaitingThread(lockA)=2

边: lockA: Waiting=2, Holding=1 → 2→1
    lockB: Waiting=1, Holding=2 → 1→2
DFS: 1→2→1 → 环! 检测到死锁 ✓
```

### 3.2 改动点

#### 3.2.1 初版修复(commit `519e33ea0`,已废弃)

初版保留了 ThreadID fallback,不够彻底:
- `LockInfo` 同时保留 `HoldingThread`/`WaitingThread` + 新增 `HoldingFlowId`/`WaitingFlowId`
- `ResolveFlowId()` 在 `FlowId==0` 时回退负数 ThreadID(`-(ThreadId)`)
- 测试需手动调 `LockRegistry.RegisterFlow()`

#### 3.2.2 最终重构(commit `54ac13d2e` + `16de25e23`,当前)

**完全消除 ThreadID,统一用 AsyncLocal FlowId**:

**`lib/async_lock/LockRegistry.cs`**:
1. `LockInfo` 删除 `HoldingThread`/`WaitingThread` 字段,只用 `HoldingFlowId`/`WaitingFlowId`
2. `ResolveFlowId()` 直接返回 `_currentFlowId.Value`,**不回退 ThreadID**
3. `OnWaitStart`/`OnAcquired` 记录 `ResolveFlowId()` 到 `WaitingFlowId`/`HoldingFlowId`
4. `DetectDeadlock()` 用 `FlowId` 构建 `waitEdges` 并 DFS 找环
5. 锁顺序违反检测用 `HoldingFlowId == currentFlowId` 比较

**`lib/async_lock/AsyncLock.cs`**:
6. 新增 `EnsureFlowRegistered()` — 锁入口惰性注册:若 `CurrentFlowId == 0` 则 `RegisterFlow()`
7. `TryLock`/`TryLockAsync` 入口调 `EnsureFlowRegistered()`,调用方无需手动注册

**`lib/async_lock/ActorBase.cs`**:
8. 删除 `_consumerThreadIdToActorId` 字典,新增 `static AsyncLocal<string?> _currentActorId`
9. `ConsumeLoopAsync` 入口设 `_currentActorId.Value = Id`,finally 清除 `= null`(防御 AsyncLocal 拘留)
10. `TryGetCallerActorId()` 直接返回 `_currentActorId.Value`

**测试代码(`lib/async_lock.tests/AsyncLockDiagnosisTests.cs`)**:
11. 撤销手动 `RegisterFlow()` 调用(`EnsureFlowRegistered` 已惰性注册)
12. 撤销 LongRunning 改动(回归原始 `Task.Run`)

### 3.3 验证结果

| 验证项 | 结果 |
|--------|------|
| AsyncLock 编译 | 0 警告 0 错误 |
| AsyncLock.Tests 全量 | 183/183 通过 |
| E2E - Cluster 本地压测 | 20/20 通过(详见第 6 节) |
| CI runs(9 个) | 9/9 全通过 |

## 4. 为什么初版不够彻底(重构动机)

初版(commit `519e33ea0`)保留了 ThreadID fallback(`ResolveFlowId` 在 `FlowId==0` 时回退负数 ThreadId),原因
是担心纯同步场景没有 FlowId。但这导致:

- `LockInfo` 同时有 `HoldingThread`/`WaitingThread` + `HoldingFlowId`/`WaitingFlowId`,字段冗余
- `ResolveFlowId()` 有两条路径(AsyncLocal → ThreadID),维护复杂
- `ActorBase` 保留 `_consumerThreadIdToActorId` 字典 + 新增 `_currentActorId` AsyncLocal,两套机制并存
- 测试需手动调 `RegisterFlow()`,易遗漏

**重构后**:`EnsureFlowRegistered()` 在锁入口惰性注册,保证 `FlowId != 0`,无需 ThreadID fallback。全部删除
Thread 相关字段/字典,统一用 AsyncLocal,更简洁更彻底。

## 5. E2E - Cluster 偶发失败关联排查

### 5.1 关联链

集群流程 `cluster_analyze → expand → worker → gather → merge → review` 通过 `GoalGraphEngine` 执行,
直接依赖 AsyncLock + ActorBase:

| 位置 | 用途 |
|------|------|
| `lib/clock/goal/core/GoalGraphEngine.cs:124,154` | `AsyncLock` 状态锁 + 并发限制器 |
| `lib/clock/goal/core/EventDrivenGraphScheduler.cs:34,132` | `AsyncLock` 并发限制器 |
| `lib/clock/goal/core/GoalHeartbeat.cs:43` | 继承 `ActorBase` |
| `lib/clock/goal/core/GoalEngine.cs:16` | `AsyncLock _stateLock` |

### 5.2 偶发失败根因

`Thread.CurrentThread` async 漏报导致:
1. 集群 `GoalGraphEngine` 的 `AsyncLock _stateLock` 死锁时,检测器无法检测 → 集群卡死
2. `GoalHeartbeat : ActorBase` 若 worker 循环 Ask 主进程,检测器漏报 → 卡死
3. 集群卡死 → jcc.exe 60s 超时 → `exitCode=-1` → `ClusterE2ETests` 断言 `exitCode.Should().Be(0)` 失败

### 5.3 验证

| 验证项 | 结果 |
|--------|------|
| CI/CD workflow(9 runs) | 0 失败 |
| PR #252 `e2e / E2E - Cluster` check | SUCCESS |
| 本地压测 E2E - Cluster(20 次) | 20/20 通过,每次 11-16s |

CI + 本地共 29 次全通过,偶发失败已消除。

## 6. 决策记录

- 2026-09-18:初始误判为线程池饥饿,改 LongRunning 后失败率从15%升到80%,暴露真根因
- 2026-09-18:真根因定位为 async 下 Thread.CurrentThread 不可靠导致 wait-for graph 拆碎
- 2026-09-18:修复方案选 AsyncLocal<int> FlowId 替换 Thread.CurrentThread
- 2026-09-18:初版实施(commit `519e33ea0`)— 保留 ThreadID fallback,不够彻底
- 2026-09-18:重构(commit `54ac13d2e` + `16de25e23`)— 完全消除 ThreadID,统一 AsyncLocal FlowId + EnsureFlowRegistered 惰性注册
- 2026-09-18:验证通过 — AsyncLock.Tests 183/183 + E2E - Cluster 本地 20/20 + CI 9/9
