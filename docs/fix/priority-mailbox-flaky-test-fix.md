# PriorityMailbox 偶发测试失败修复

> 状态:根因已定位,修复方案待评审
> 关联测试:`lib/async_lock.tests/AsyncLock.Tests.csproj` → CI job `unit-tests / Unit - AsyncLock`
> 发现日期:2026-09-18

## 1. 问题现象

CI job `unit-tests / Unit - AsyncLock` 偶发性失败,本地压测 10 次复现:

| Run | 结果 | 失败数 |
|-----|------|--------|
| 1-4 | 通过 | 0 |
| 5 | 失败 | 1 |
| 6 | 失败 | 2 |
| 7-10 | 通过 | 0 |

失败率 20%,失败测试固定为 `PriorityMailboxTest` 中优先级排序用例:

- `NormalPriority_ProcessedBeforeLowPriority` — Run 5/6
  - 期望 `{2, 1, 3}`,实际 `{1, 2, 3}`
- `MixedPriority_HighAlwaysFirst` — Run 6
  - 期望 `IndexOf(30) < IndexOf(10)`,实际 `30` 在 `10` 之后(diff=1)

### 复现命令

```bash
dotnet build lib/async_lock.tests/AsyncLock.Tests.csproj -c Release --nologo
for i in $(seq 1 10); do
  dotnet test lib/async_lock.tests/AsyncLock.Tests.csproj -c Release --no-build --nologo \
    --filter "Category!=Integration&Category!=Benchmark" \
    --logger "console;verbosity=normal"
done
```

## 2. 根因分析

### 2.1 PriorityMailbox 当前语义

`lib/async_lock/PriorityMailbox.cs` 采用**贪心优先级**语义:

- 构造时立即启动 Consumer 循环(`Task.Factory.StartNew(ConsumeLoopAsync, ...)`)
- Consumer 循环:`TryRead High → TryRead Normal → TryRead Low`,全空时信号量等待
- 即:**只要高优先级队列非空,就先消费高优先级**;不保证"后入队的高优先级先于先入队的低优先级"

这是正确的生产语义——用户交互(High)来了应立即处理,不等后台编译(Low)入队完毕。

### 2.2 测试时序假设错误

测试用 `gate`(TaskCompletionSource)阻塞 `HandleAsync` 期望"先批量入队再消费":

```csharp
await actor.SendAsync(1, MessagePriority.Low);
await actor.SendAsync(2, MessagePriority.Normal);
await actor.SendAsync(3, MessagePriority.Low);
gateTcs.SetResult();  // 释放 gate
// 期望处理顺序 {2, 1, 3}
```

但 `gate` 阻塞的是 `HandleAsync`(处理),**不阻塞 `TryRead`(读取)**。

### 2.3 偶发失败的执行路径

**路径 A — Consumer 启动慢(顺序对,通过)**:
```
t1: Send(1,Low)    → Low:[1]
t2: Send(2,Normal) → Normal:[2]
t3: Send(3,Low)    → Low:[1,3]
t4: Consumer 启动 → TryRead High(空) → TryRead Normal(有2) → 读2 → Handle(2) → gate阻塞
t5: gate释放 → 处理2 → TryRead Normal(空) → TryRead Low(有1) → 读1 → 处理1 → TryRead Low(有3) → 读3 → 处理3
结果: {2,1,3} ✓
```

**路径 B — Consumer 启动快(顺序错,失败)**:
```
t1: Send(1,Low) → Low:[1]
t2: Consumer 启动 → TryRead High(空) → TryRead Normal(空) → TryRead Low(有1) → 读1 → Handle(1) → gate阻塞
    ⚠️ Consumer 在第一条入队后就开始读了!gate 只阻塞 Handle 不阻塞 TryRead
t3: Send(2,Normal) → Normal:[2]  (Consumer 已在处理1)
t4: Send(3,Low)    → Low:[3]
t5: gate释放 → 处理完1 → TryRead Normal(有2) → 读2 → 处理2 → TryRead Low(有3) → 读3 → 处理3
结果: {1,2,3} ✗  ← 偶发失败
```

### 2.4 根因路径链

`SendAsync(1,Low)` 入队 → `_signal.Release()` 唤醒 Consumer → Consumer 立即 `TryRead Low` 读到 1 → `HandleAsync(1)` 被 gate 阻塞。

此时 2(Normal)还没入队,优先级排序窗口已错过。**gate 阻塞的是 Handle(处理),不是 TryRead(读取)**,所以 Consumer 已经把 1 从队列里拿出来了,2 后入队时 1 已经在"处理中",无法插队。

**为什么偶发**:t1(第一条入队)和 t2(Consumer 启动)的相对顺序由线程调度决定。Consumer 是 `LongRunning` 专用线程,启动快;`SendAsync` 在调用线程上执行。两者竞态——Consumer 赢则走路径 B(失败),SendAsync 赢则走路径 A(通过)。

### 2.3 影响面

- `PriorityMailbox<TCommand>` **无生产派生类**,只有测试中的 `PriorityTestActor` / `ErrorTestActor`
- 可大改 API,不影响生产调用方

## 3. 修复方案对比

| 方案 | 改动点 | 优点 | 缺点 |
|------|--------|------|------|
| A. 改测试断言为弱顺序 | 测试 | 零生产改动 | 测试失去验证优先级排序的意义 |
| B. 加 `StartConsuming()` 显式启动 | 生产 + 测试 | 贪心语义不变,测试可靠验证贪心;生产行为不变(默认自动启动) | API 多一个方法,但无生产调用方 |
| C. 加 Pause/Resume API | 生产 + 测试 | 不改启动时机 | 测试专用 API 污染生产接口 |
| D. 改测试 gate 阻塞 TryRead | 测试 | 不改生产 | gate 机制无法阻塞 TryRead,需重写测试基础设施 |
| E. 严格排序(单通道+优先级堆) | 生产 | 全局严格优先级 | **会卡死**:高优先级要等低优先级入队窗口才能排序,饥饿/死锁 |

### 决策:选方案 B(贪心/宽容语义)

**用户决策(2026-09-18)**:PriorityMailbox 应保持**宽容(贪心)语义**,不应用严格排序——严格排序会导致卡死(高优先级等低优先级入队窗口,饥饿/死锁)。

**方案 B 的本质**:
- **语义不变**:Consumer 启动后仍是 `TryRead High→Normal→Low`(贪心)
- **测试可靠**:测试传 `startConsuming: false` 先入队完所有消息,再调 `StartConsuming()` 启动 Consumer,确保 TryRead 时刻所有消息都在队列里,按贪心优先级消费
- **不引入严格排序**:不会卡死,高优先级仍立即处理
- **生产行为不变**:默认 `startConsuming: true`,构造即启动,与当前完全一致

## 4. 修复方案 B 实施计划

### 4.1 生产代码改动(`lib/async_lock/PriorityMailbox.cs`)

1. 构造函数加 `bool startConsuming = true` 参数(默认 true,生产行为不变)
2. `_consumerTask` 改为 `Task?`(nullable)
3. 加 `protected void StartConsuming()` 方法,幂等启动 Consumer
4. `ConsumerTask` 属性处理 null → `Task.CompletedTask`
5. `DisposeAsync` 处理 `_consumerTask` null 检查

```csharp
protected PriorityMailbox(
    ActorBackpressure? highBackpressure = null,
    ActorBackpressure? normalBackpressure = null,
    ActorBackpressure? lowBackpressure = null,
    bool startConsuming = true)
{
    // ... 初始化 channels
    if (startConsuming) StartConsuming();
}

protected void StartConsuming()
{
    if (Interlocked.Exchange(ref _consumingStarted, 1) != 0) return;
    _consumerTask = Task.Factory.StartNew(
        ConsumeLoopAsync, CancellationToken.None,
        TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
        TaskScheduler.Default).Unwrap();
}
```

### 4.2 测试代码改动(`lib/async_lock/tests/PriorityMailboxTest.cs`)

1. `PriorityTestActor` 构造加 `bool startConsuming = true` 参数透传基类
2. 加 `public void StartConsumer() => StartConsuming();` 暴露
3. 5 个优先级排序测试改为:
   - 构造传 `startConsuming: false`
   - 入队所有消息
   - 调 `StartConsumer()` 启动 Consumer
   - gate 释放后 WaitUntilAsync 等处理完
   - 断言严格顺序

需要改造的测试(5 个):
- `HighPriority_ProcessedBeforeLowPriority`
- `NormalPriority_ProcessedBeforeLowPriority`
- `HighPriority_ProcessedBeforeNormalPriority`
- `MixedPriority_HighAlwaysFirst`
- `SamePriority_FifoOrder`(同优先级 FIFO,虽不依赖跨优先级排序,但统一改造保持一致)

### 4.3 验证

- 编译通过(Release)
- 压测 10 次全绿
- 183 测试全部通过

## 5. 新设计占位(待用户补充)

> 用户表示要在此基础上加入新设计。以下为占位区,待用户明确后补充。

<!-- 新设计内容待补充 -->

## 6. 决策记录

- 2026-09-18:根因定位,选方案 B(StartConsuming 显式启动),文档待评审
