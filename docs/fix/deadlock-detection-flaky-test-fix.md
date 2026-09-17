# 死锁检测测试偶发失败修复

> 状态:✅ 已修复,10次全量压测 183/183 全通过
> 关联测试:`AsyncLockDiagnosisTests.死锁检测_async两个流互相等待时自动检测`
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

**生产代码(`lib/async_lock/LockRegistry.cs`)**:
1. `LockInfo` 加 `int FlowId` 字段(替代/补充 `WaitingThread`/`HoldingThread`)
2. `OnWaitStart`/`OnAcquired` 记录 `AsyncLocal<int>` 的值作为 FlowId
3. `DetectDeadlock()` 用 FlowId 构建 waitEdges 而非 `Thread.ManagedThreadId`
4. 加 `static AsyncLocal<int> CurrentFlowId`,提供 `RegisterFlow()` 返回唯一 ID

**测试代码(`lib/async_lock.tests/AsyncLockDiagnosisTests.cs`)**:
1. `死锁检测_async两个流互相等待时自动检测` 的 t1/t2 启动时设 `CurrentFlowId`
2. 撤销 LongRunning 改动(回归原始 Task.Run,因为 AsyncLocal 修复后不需要)

### 3.3 验证计划

1. 保留当前 LongRunning 改动(80% 失败率)
2. 实施 AsyncLocal FlowId 修复
3. 压测 20 次,确认从 80% 降到 0%
4. 撤销 LongRunning 改动,压测 20 次确认 0%
5. 全量测试通过

## 4. 新设计占位(待用户补充)

> 用户表示要在此基础上加入新设计。以下为占位区,待用户明确后补充。

<!-- 新设计内容待补充 -->

## 5. 决策记录

- 2026-09-18:初始误判为线程池饥饿,改 LongRunning 后失败率从15%升到80%,暴露真根因
- 2026-09-18:真根因定位为 async 下 Thread.CurrentThread 不可靠导致 wait-for graph 拆碎
- 2026-09-18:修复方案选 AsyncLocal<int> FlowId 替换 Thread.CurrentThread
- 2026-09-18:实施修复 + 加 ResolveFlowId fallback(同步场景 FlowId==0 时用负数 ThreadId 避免冲突)
- 2026-09-18:验证通过,10次全量压测 183/183 全通过(此前死锁检测 15% 失败率)
