# Thread.CurrentThread async 漏报排查 — AsyncLocal FlowId 推广

> 状态:✅ 2 处同源 bug 已修复并重构,完全消除 ThreadID,统一用 AsyncLocal
> 关联修复:`deadlock-detection-flaky-test-fix.md`(已修复死锁检测,commit 519e33ea0 → 重构 54ac13d2e)
> 关联排查:E2E - Cluster 偶发失败同源(集群依赖 AsyncLock/ActorBase)
> 发现日期:2026-09-18

## 1. 背景

修复 `LockRegistry.DetectDeadlock` 死锁检测偶发失败后,排查全项目 `Thread.CurrentThread` 使用点,发现 **2 处同源 bug**(async 下 `Thread.CurrentThread` 不可靠导致漏报)。

## 2. Bug 1:LockRegistry 锁顺序违反检测漏报

### 位置
`lib/async_lock/LockRegistry.cs:143-150`(`OnWaitStart` 内,锁顺序违反检测)

### 代码
```csharp
var currentThread = Thread.CurrentThread;
foreach (var other in _locks.Values)
{
    if (other.HoldingThread == currentThread && other.Id > id)
        Emit($"[LOCK-ORDER-VIOLATION] 锁顺序违反: ...");
}
```

### 根因
- `currentThread` 是 `OnWaitStart` 调用时刻(await 前)的线程
- `other.HoldingThread` 是另一把锁 `OnAcquired` 调用时刻(await 后)的线程
- async 下 await 切换线程,同一逻辑流两把锁的 `Thread` 对象不同
- `other.HoldingThread == currentThread` 永远 false → **漏报锁顺序违反**

### 影响
锁顺序违反检测失效。锁顺序违反是2PL死锁的常见原因,漏报会导致潜在的死锁风险不被发现。

### 修复方案
改为用 FlowId 比较:
```csharp
var currentFlowId = ResolveFlowId();
foreach (var other in _locks.Values)
{
    if (other.HoldingFlowId == currentFlowId && other.Id > id)
        Emit($"[LOCK-ORDER-VIOLATION] 锁顺序违反: ...");
}
```
**重构后**(commit `54ac13d2e`):`HoldingThread`/`WaitingThread` 字段完全删除,只用 `HoldingFlowId`/`WaitingFlowId`,
`ResolveFlowId()` 直接返回 `_currentFlowId.Value` 不回退 ThreadID。

## 3. Bug 2:ActorBase 循环 Ask 死锁检测漏报

### 位置
`lib/async_lock/ActorBase.cs:241`(ConsumeLoopAsync 开头记录映射)
`lib/async_lock/ActorBase.cs:338`(TryGetCallerActorId 读取映射)

### 代码
```csharp
// Line 241: ConsumeLoopAsync 开头
_consumerThreadIdToActorId[Environment.CurrentManagedThreadId] =$ = Id;

// Line 338: TryGetCallerActorId
_consumerThreadIdToActorId.TryGetValue(Environment.CurrentManagedThreadId, out var id);
```

### 根因
- Consumer 用 `LongRunning` 专用线程启动,Line 241 在循环开头记录专用线程 ID
- 但 `await HandleAsync`(Line 249)后,若 `HandleAsync` 内部先 await 异步操作再调用 `AskAwait`,此时已**切换到线程池线程**
- `TryGetCallerActorId`(Line 338)用线程池线程 ID 查映射 → 查不到 → `callerId = null`
- 循环 Ask 等待图环检测(Line 287-292)跳过 → **漏报循环 Ask 死锁**

### 影响
循环 Ask 死锁检测失效。循环 Ask(A→B→A)是 Actor 模型核心死锁风险,漏报后果严重。

### 修复方案
新增 `AsyncLocal<string?>` 存当前 ActorId:
```csharp
private static readonly AsyncLocal<string?> _currentActorId = new();

// ConsumeLoopAsync 入口
_currentActorId.Value = Id;

// TryGetCallerActorId 改读 AsyncLocal
return _currentActorId.Value;
```
AsyncLocal 跨 await 自动流转,await 切换线程后仍可读到正确 ActorId。

**重构后**(commit `54ac13d2e` + `16de25e23`):`_consumerThreadIdToActorId` 字典完全删除,只用
`_currentActorId` AsyncLocal。`ConsumeLoopAsync` finally 清除 `_currentActorId.Value = null` 防御
AsyncLocal 拘留(LongRunning 线程复用场景)。

## 4. 推广可行性

- 项目已有 **8 处 AsyncLocal 成熟使用**(CallTrace 链路追踪、SessionContext 会话上下文、SubAgentContext、TeammateContext、SubAgentEventChannel、PromptConfigSnapshot、LockRegistry.FlowId)
- FlowId 模式与项目风格一致
- LockRegistry 已提供 `RegisterFlow()`/`CurrentFlowId`/`ResolveFlowId()` 基础设施
- ActorBase 只需新增一个 `AsyncLocal<string>` 即可对齐

## 5. 无需推广的 11 处

| 位置 | 用途 | 不推广原因 |
|------|------|------------|
| LockRegistry 诊断日志(6处) | 显示线程 ID | 仅诊断显示,非功能性 bug |
| LockRegistry 诊断读取(6处) | 读取已记录的 Thread | 不涉及 CurrentThread |
| PlanModeManager:152 | 生成 session slug 文件名 | 纯命名唯一性,无正确性影响 |

## 6. 决策占位

> ~~待用户决定是否修复 2 处 bug。~~ 已修复并重构。

## 7. 决策记录

- 2026-09-18:排查全项目 Thread.CurrentThread,发现 2 处同源 bug(漏报)
- 2026-09-18:文档记录根因、影响、修复方案,待决策
- 2026-09-18:用户决策全部修复,实施 Bug1(锁顺序检测改 FlowId)+ Bug2(ActorBase 改 AsyncLocal<string>)
- 2026-09-18:初版验证通过(commit `519e33ea0`),但保留 ThreadID fallback + 字典冗余,不够彻底
- 2026-09-18:重构一(commit `54ac13d2e` + `16de25e23`)— 完全消除 ThreadID:
  - LockRegistry 删除 `HoldingThread`/`WaitingThread`,只用 `HoldingFlowId`/`WaitingFlowId`
  - `ResolveFlowId()` 不回退 ThreadID,纯 AsyncLocal
  - `AsyncLock.EnsureFlowRegistered()` 惰性注册,调用方无需手动 `RegisterFlow()`
  - ActorBase 删除 `_consumerThreadIdToActorId` 字典,只用 `_currentActorId` AsyncLocal
  - ConsumeLoopAsync finally 清除 `_currentActorId` 防御拘留
- 2026-09-18:重构二(commit `f8534d1b2` + `a1fb3efd7` + `10908543d` + `080b0cf92`)— 合并 AsyncLocal + 提取工具类:
  - 新增 `AsyncFlowIdentity` — 单个 AsyncLocal 合并 FlowId+ActorId,SetFlowId/SetActorId 独立设置互不影响
  - LockRegistry/ActorBase 委托 AsyncFlowIdentity,删除 `_currentFlowId` + `_currentActorId`
  - 新增 `AsyncLocalScope<T>` 工具类 — `using var scope = AsyncLocalScope.Enter(store, value)` 消除 4 处 ScopeRestore 样板
  - SubAgentContext._cwdOverride 改实例属性,GetSections 迭代器 finally 清除 PromptConfigSnapshot
- 2026-09-18:重构验证通过 — AsyncLock.Tests 183/183 + E2E - Cluster 本地 20/20 + CI 9/9
- 2026-09-18:E2E - Cluster 关联确认 — 集群 `GoalGraphEngine`/`GoalHeartbeat` 依赖 AsyncLock/ActorBase,
  Thread.CurrentThread 漏报导致集群偶发死锁无法检测 → 60s 超时 → 测试失败。修复后消除。
