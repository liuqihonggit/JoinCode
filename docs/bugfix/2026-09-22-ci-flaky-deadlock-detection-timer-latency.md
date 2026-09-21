# Bug 日志:CI 偶发测试失败 — 死锁检测纯依赖后台 Timer 导致高负载下漏检

**日期**:2026-09-22
**PR**:#270
**触发**:用户报告 run 35649777572 attempt 1 失败、attempt 2 通过,不接受偶发性异常,要求根因修复

---

## 已修复 Bug

### Bug: AsyncLock 死锁检测偶发漏检

- **CI**:run 35649777572,job `unit-tests / Unit - AsyncLock`,attempt 1
- **症状**:`LockDiagnosis.Tests.AsyncLockDiagnosisTests.死锁检测_两个线程互相等待时自动检测 [FAIL]`,`Expected boolean to be True ... but found False`,Passed: 182/183,耗时 5s(SpinUntil 超时)
- **复现**:attempt 1 失败,attempt 2(重试)通过 — 典型偶发性,CI 高负载下才触发

#### 根因

`LockRegistry.DetectDeadlock` 纯依赖后台 `System.Threading.Timer` 扫描(50ms 间隔)触发,Timer 回调在 ThreadPool 上执行。死锁检测存在**时间窗口**约束:

```
死锁检测窗口 = 锁 TryLock 超时(500ms) − 等待门槛 WaitTimeoutThreshold(200ms) = 300ms
```

- `DetectDeadlock` 只纳入等待时间 > `WaitTimeoutThreshold`(200ms)的边(避免 FlowId 复用 stale 误报)
- 锁的 `TryLock` 超时 500ms,超时后线程退出释放锁,死锁窗口消失
- 后台扫描 50ms 间隔,理论上 300ms 窗口内有 6 次扫描机会

**CI 高负载下**:ThreadPool 线程被其他并行测试占用,Timer 回调被严重调度延迟(50ms 间隔实际变成数百 ms),300ms 窗口内 0 次扫描执行 → 死锁未检测到 → 5s SpinUntil 超时 → 测试失败。attempt 2 重试时 CI 负载稍低,Timer 及时调度 → 通过。

#### 时序

```
T+0ms   t1 获取 lockA, t2 获取 lockB
T+ε     barrier 同步, t1 开始等 lockB, t2 开始等 lockA
        (OnWaitStart 设置 WaitingFlowId,但不触发 DetectDeadlock)
T+200ms 等待门槛到达, DetectDeadlock 可纳入这两条边
        ↑ 死锁检测窗口开始
T+500ms TryLock 超时, t1/t2 退出释放锁
        ↑ 死锁检测窗口结束(300ms)
T+500ms~ 若后台 Timer 在 [200ms, 500ms] 内未调度 → 漏检
```

#### 修复:OnWaitStart 即时检测(根治,不依赖 Timer 调度)

- **层1 根治**:`OnWaitStart` 末尾新增 `DetectDeadlockFromCurrentFlow(startFlowId)` — 新等待边加入时**立即**沿 wait-for graph 走,若回到起点则死锁。不再依赖后台 Timer 在窗口内运气好扫到
- **层2 复用**:重构 `DetectDeadlock` 提取 `BuildWaitEdges(exemptThreshold)` + `FindCycleFrom(waitEdges, startId)` 公共方法,后台扫描与即时检测共享图构建与环检测逻辑
- **层3 双保险**:原测试增大窗口(锁超时 500ms→3s,等待门槛 200ms→100ms),窗口 300ms→2900ms,后台扫描有 58 次机会,配合即时检测几乎不可能漏检

#### 即时检测豁免门槛的设计决策

| 方案 | 手法 | 优缺点 |
|------|------|--------|
| **即时检测豁免所有门槛(采用)** | `BuildWaitEdges(exemptThreshold: true)`,纳入所有 `WaitingFlowId != 0 && HoldingFlowId != 0` 的边 | 根治"两边同时等"场景(刚加入的边等待时间≈0);stale 误报需双重流崩溃 + FlowId 复用(int 溢出),极罕见 |
| 即时检测豁免当前锁 | 只豁免当前 OnWaitStart 的锁,其他边仍用门槛 | "两边同时等"时另一边没超门槛仍漏检,未根治 |
| 仅增大窗口 | 不改生产代码,只把测试窗口从 300ms 增到 2900ms | 降低概率未根治,极端高负载仍可能漏检 |

- **stale 误报风险评估**:`WaitingFlowId` 在 `OnWaitEnd`(超时/取消)和 `OnAcquired`(获取成功)时清零,stale 仅在流异常崩溃未清零时发生。误报需环上所有边同时 stale + FlowId 复用,概率远低于 CI 偶发失败概率,值得交换
- **原后台扫描保留门槛**:`DetectDeadlock`(后台扫描调用)仍用 `exemptThreshold: false`,保留 stale 防护,即时检测作为补充而非替代

#### 红测试(缺陷驱动)

新增 `死锁检测_OnWaitStart即时检测_不依赖后台扫描`:
- 禁用后台扫描(`StopBackgroundScan`),模拟 CI 高负载下 Timer 永不调度
- 两个 Thread 互相等待对方持有的锁(barrier 同步后同时等)
- 断言 2s 内 `DeadlockDetected` 为 true
- **修复前**:无即时检测 + 后台扫描禁用 → 稳定失败(2s 超时)
- **修复后**:OnWaitStart 即时检测 → 稳定通过(5s,即时检测生效)

- **commit**:`25b0ccd09`
- **验证**:AsyncLock.Tests 184 通过 0 失败(3 次连续运行,稳定性确认)

---

## 影响范围

- **改动文件**:
  - `lib/async_lock/lock/LockRegistry.cs` — OnWaitStart 加即时检测 + 重构 DetectDeadlock 提取公共方法
  - `lib/async_lock.tests/AsyncLockDiagnosisTests.cs` — 新增红测试 + 原测试增大窗口
- **API 兼容**:新增 `internal static void DetectDeadlockFromCurrentFlow(int)`,无公开 API 变更
- **其他项目影响**:无(DeadlockDetected/DetectDeadlock 引用仅在 async_lock 范围内)

---

## 同类风险

| 位置 | 依赖 | 风险 | 说明 |
|------|------|------|------|
| `LockRegistry.ScanHolds` 后台扫描 | Timer ThreadPool 调度 | **已修复** | 本次修复,OnWaitStart 即时检测补充 |
| 其他依赖后台 Timer 的检测 | Timer 调度延迟 | **低** | 死锁检测是唯一对时序敏感的(窗口=超时−门槛),其他扫描是告警非断言 |

---

## 建议

1. **已修复**:OnWaitStart 即时检测根治 Timer 调度依赖,原测试增大窗口双保险
2. **架构级(长期)**:任何"检测窗口 + 后台扫描"模式都应在事件发生时(OnWaitStart/OnAcquired/OnReleased)即时检测一次,后台扫描仅作兜底,不作为唯一检测路径
