# 双击ESC子代理中断与空闲接手 PRD

> 创建: 2026-08-27
> 状态: 设计确认中
> 方案: B-revised（完美对齐 ClaudeCode 两种设计：forked agent + teammate）

## 0. 方案修订记录（2026-08-27）

### 0.1 方向转变
原方案B：改fork子代理（AgentBase.Interrupt + RunBackgroundAgentAsync idle循环）
**修订为**：改teammate + GUI切teammate

### 0.2 调研发现
1. **ClaudeCode有两种子代理**：forked agent（同步跑完即退出）+ teammate（后台idle循环）
2. **当前项目也已有两种骨架**：
   - fork子代理（AgentServiceImpl）= forked agent模型 ✅对齐
   - InProcessTeammateTaskExecutor = teammate模型骨架 ✅基本对齐
3. **teammate骨架已对齐ClaudeCode**：
   - `LifecycleCts`（lifecycle）↔ `abortController` ✅
   - `workCts = CreateLinkedTokenSource(lifecycleCts)`（per-turn）↔ `currentWorkAbortController` ✅
   - `while`循环 + `WaitForNextPromptOrShutdownAsync` ↔ `waitForNextPromptOrShutdown` ✅
   - `NotifyIdleAsync` ↔ `sendIdleNotification` ✅
4. **teammate缺Interrupt能力**：`workCts`是`using var`局部变量，外部无法abort（ESC无法只中断work不杀lifecycle）
5. **GUI子会话当前是fork子代理**（`JccChatSession.cs:174` 从 `forkManager.GetActiveForksAsync` 获取），不是teammate

### 0.3 修订方案
1. **fork子代理保持forked agent不动**（ESC=Cancel终止）— 对齐ClaudeCode forkedAgent.ts
2. **teammate补Interrupt能力**（暴露workCts + InterruptTeammateAsync）— 对齐ClaudeCode inProcessRunner
3. **GUI子会话改用teammate模型**（创建/操作从fork切到teammate）
4. **60秒空闲窗口**加在teammate的Interrupt上
5. **60秒超时唤醒mainAgent**（编排增强，ClaudeCode没有）

### 0.4 修订任务分解
| 步骤 | 内容 | 风险 |
|------|------|------|
| 1 | teammate暴露workCts给外部 + InterruptTeammateAsync方法（TDD） | 低 |
| 2 | IInProcessTeammateTaskExecutor接口加InterruptTeammateAsync | 低 |
| 3 | GUI子会话创建切teammate（引擎层fork→teammate） | **高** |
| 4 | 双击ESC路径改走teammate Interrupt | 中 |
| 5 | SubAgentIdleTimer 60秒空闲倒计时 + UI提示 | 中 |
| 6 | 60秒超时 → Cancel teammate + mainAgent接手编排 | 中 |
| 7 | StopAgentAsync补CleanupWorktreeAsync（修复fork泄漏，独立改动） | 低 |

## 1. 背景与问题

### 1.1 现状
双击ESC（`MainWindow.axaml.cs:80`，600ms窗口）调用 `StopGeneratingCommand` → `StopSubAgentAsync`（`MainViewModel.SessionTree.cs:62`）→ `StopBackgroundAgentAsync` → `StopAgentAsync`（`AgentServiceImpl.cs:249`）→ `CancelAgentAsync`（`AgentLifecycleManager.cs:144`）→ `agent.Cancel()` + 状态机转移 `TaskExecutionStatus.Cancelled`。

这是**终止**操作：子代理进终态 `Cancelled`（状态机 `Cancelled → Empty` 不可恢复），`RunBackgroundAgentAsync` 捕获 `OperationCanceledException` 后 `FireAgentCompleted` 并清理资源。

### 1.2 问题
1. **终止而非中断**：用户双击ESC想"打断当前work再说"，但当前实现是永久终止子代理，无法恢复。
2. **用户无输入机会**：计划引入"子代理停止后mainAgent自动接手分析diff"的编排。若双击ESC后立即唤醒mainAgent，用户还没来得及思考/打字，mainAgent就开始干活了。
3. **worktree泄漏**：`StopAgentAsync` 只调 `CleanupMcpServersIfNeededAsync`，不调 `CleanupWorktreeAsync`。终止的后台子代理 worktree 静默残留（不影响功能因ID唯一，但泄漏磁盘）。

### 1.3 ClaudeCode 参考
`inProcessRunner.ts:1291-1361`：Escape后子代理进 **idle**，加 interrupt message，**不自动唤醒 leader**（注释明确："We do NOT automatically send the teammate's response to the leader"）。子代理 `waitForNextPromptOrShutdown` 等下一条消息，控制权交回用户。

## 2. 目标与非目标

### 2.1 目标
1. 双击ESC **中断**子代理当前work（非终止），子代理进 idle 状态保留，可恢复
2. 中断后启动 **60秒空闲倒计时**，用户任何打字活动**重置**倒计时
3. 60秒内完全无输入活动 → 唤醒mainAgent接手分析diff（编排增强）
4. 用户在窗口内打字/发送 → 重置倒计时，用户接管子代理（发next prompt恢复执行）
5. worktree 清理对齐：Interrupt 不清理（子代理还活着），仅 Cancel/Dispose 时清理
6. 对齐 ClaudeCode inProcessRunner 的 idle 循环设计

### 2.2 非目标
- 不改 teammate 模式（InProcessTeammateTask 已有 idle notification 机制）
- 不改主会话双击ESC路径（`_sendCts.Cancel()` 保持不变）
- 不改遥测网络（独立服务不受影响）
- 不引入"子代理完成自动唤醒mainAgent"（fork 事件保持无订阅；mainAgent接手仅由60秒超时触发）

## 3. 方案设计

### 3.1 核心交互流程

```
用户双击ESC（聚焦子会话且运行中）
  │
  ├─► AgentBase.Interrupt()
  │     ├─ _cts.Cancel()          中断当前LLM流（对齐ClaudeCode abortCurrentWork）
  │     ├─ Status = Paused        进idle（非Cancelled，可恢复）
  │     └─ _cts = new CTS()       重置CTS供下次Resume
  │
  ├─► 子代理进 idle 循环（waitForNextPrompt）
  │
  └─► 启动60秒空闲倒计时（前台UI提示"60s内无输入将移交mainAgent"）
        │
        ├─ 用户打字（KeyDown事件，未发送） ──► 重置60秒倒计时（不恢复子代理）
        ├─ 用户发送消息（Enter/Ctrl+Enter） ──► 立即取消倒计时 + ForwardInputToSubAgentAsync(next prompt) + Resume
        │     └─ 子代理立即恢复执行（用户主动接管，不等倒计时；想再中断可再双击ESC）
        │
        └─ 60秒倒计时归零（无任何输入活动）
              └─► 唤醒mainAgent接手编排
                    ├─ CancelAgentAsync（真正终止子代理）
                    ├─ CleanupWorktreeAsync（清理worktree）
                    └─ mainAgent 分析子代理worktree的diff，接手后续工作
```

### 3.2 空闲超时语义（关键纠正）

**不是**"用户打字就暂停唤醒mainAgent"
**而是**"60秒无任何输入活动才唤醒mainAgent，任何打字活动都重置倒计时"

| 事件 | 行为 |
|------|------|
| 双击ESC（中断完成） | 启动60s空闲倒计时 |
| 用户按键（KeyDown，未发送） | **重置**60s倒计时（用户还在思考/输入，别打断） |
| 用户发送消息（Enter/Ctrl+Enter） | **立即**Resume子代理 + next prompt（取消倒计时，用户主动接管） |
| 用户暂停输入（手离开键盘） | 倒计时继续递减 |
| 60s归零（无任何输入活动） | 唤醒mainAgent接手 |

**关键区分**：
- **打字**（KeyDown）= 用户还在活动 → 仅重置倒计时，不恢复子代理
- **发送**（Enter）= 用户已决定接管 → **立即**恢复子代理，不等倒计时（想再中断可以再双击ESC）

**核心目的**：用户思考/打字时不被打断，只有真正空闲60秒才移交mainAgent；用户主动发送则立即接管。

### 3.3 与ClaudeCode的差异

| 维度 | ClaudeCode | 本方案 |
|------|------------|--------|
| ESC后子代理状态 | idle，等next prompt | Paused，等next prompt（对齐） |
| 是否自动唤醒leader | 否（无限等） | 否，但60秒空闲超时后唤醒（编排增强） |
| worktree清理 | 显式cleanupWorktree（--force） | Interrupt不清理；Cancel/Dispose时清理（对齐） |
| next prompt来源 | waitForNextPromptOrShutdown | ForwardInputToSubAgentAsync + Resume |

60秒超时唤醒mainAgent是本方案独有的编排增强，ClaudeCode没有（ClaudeCode的leader不主动接手）。

## 4. 详细需求规格

### 4.1 AgentBase.Interrupt()

**需求**：中断当前work + 进Paused + 重置CTS

```csharp
public virtual void Interrupt()
{
    _cts.Cancel();              // 中断当前正在执行的LLM流
    Status = TaskExecutionStatus.Paused;  // 进idle（非Cancelled）
    _cts = new CancellationTokenSource();  // 重置供下次Resume
}
```

**约束**：
- `_cts` 从 `protected readonly` 改为 `protected`（去掉readonly，允许Interrupt重建）
- 仅 `Running` 状态允许 Interrupt（非Running时no-op + 日志）
- 线程安全：`_cts` 赋值是引用类型原子操作；Interrupt与ExecuteAsync在不同线程，时序为 Interrupt→cts.Cancel→ExecuteAsync捕获OCE退出→下次ExecuteAsync读新cts

**测试用例**：
- Interrupt_WhenRunning_ShouldTransitionToPaused
- Interrupt_WhenRunning_ShouldCancelCurrentCtsToken
- Interrupt_WhenRunning_ShouldResetCtsForResume（Interrupt后新cts.Token未取消）
- Interrupt_WhenNotRunning_ShouldBeNoop
- Interrupt_ThenExecuteAsync_ShouldNotThrowObjectDisposed（可恢复执行）

### 4.2 AgentLifecycleManager.InterruptAgentAsync()

**需求**：状态机 `Running → Paused` 转移 + 调用 `agent.Interrupt()`

```csharp
public async Task<bool> InterruptAgentAsync(string agentId, CancellationToken ct = default)
{
    if (_subAgents.TryGetValue(agentId, out var agent))
    {
        agent.Interrupt();
        return await _stateMachine.TryTransitionAsync(agentId, TaskExecutionStatus.Paused, "用户中断", ct);
    }
    return false;
}
```

**状态机确认**：`AgentStateMachine.cs:200` 已允许 `Running → Paused`，无需改状态机。

**测试用例**：
- InterruptAgentAsync_WhenRunning_ShouldTransitionToPaused
- InterruptAgentAsync_WhenAgentNotFound_ShouldReturnFalse

### 4.3 IAgentService.InterruptAgentAsync()

**需求**：`AgentServiceImpl` 暴露 InterruptAgentAsync，委托 `_lifecycleManager.InterruptAgentAsync`
- **不调** `CleanupMcpServersIfNeededAsync`（子代理还活着，MCP服务器保留）
- **不调** `CleanupWorktreeAsync`（子代理还活着，worktree保留）

### 4.4 fork子代理 idle 循环（核心改动）

**需求**：`RunBackgroundAgentAsync`（`AgentServiceImpl.cs:555`）从"单task跑完即退出"改为"跑完一个work后进idle等next prompt"

**对齐** `inProcessRunner.ts:1354` 的 `waitForNextPromptOrShutdown`：

```
while (!lifecycleCancelled):
    result = await lifecycleManager.ExecuteAsync(agent, ct)   # 跑当前work
    if (ct.IsCancellationRequested): break                    # lifecycle cancel
    if (result was interrupted):                               # Interrupt导致的OCE
        idleResult = await WaitForNextPromptOrShutdown(agent)  # 进idle等next prompt
        if (idleResult == Shutdown): break
        if (idleResult == NewPrompt): continue                 # next prompt到达，重新ExecuteAsync
    else:
        FireAgentCompleted(result)                             # 正常完成/失败，退出
        break
```

**WaitForNextPromptOrShutdown 实现**：
- 等待 `IAgentInputForwardQueue` 有新消息（用户通过 `ForwardInputToSubAgentAsync` 注入）
- 或等待 lifecycle CTS cancel（真正终止）
- 60秒空闲超时由 GUI 层驱动（见4.5），不在此处

**影响面**：
- `RunBackgroundAgentAsync` 重构为循环
- `FireAgentCompleted` 仅在正常完成/失败/lifecycle cancel 时触发（Interrupt不触发）
- `_backgroundCts` 语义从"整个后台任务"改为"lifecycle"（Interrupt不cancel _backgroundCts，只cancel agent._cts）

### 4.5 双击ESC路径改Interrupt + 60秒空闲窗口（GUI层）

**需求**：

**4.5.1 StopGenerating 改为 Interrupt**（`MainViewModel.SessionTree.cs:93`）：
```
当前聚焦子会话且运行中 → InterruptSubAgentAsync（非StopSubAgentAsync）
当前聚焦主会话 → _sendCts.Cancel()（不变）
```

**4.5.2 60秒空闲倒计时**（新增 `SubAgentIdleTimer`）：
- Interrupt后启动倒计时
- 绑定输入框 KeyDown 事件（未发送的按键）→ 重置倒计时（用户还在活动，别打断）
- 绑定发送命令（Enter/Ctrl+Enter）→ **立即**取消倒计时 + Resume子代理 + ForwardInputToSubAgentAsync（用户主动接管，不等倒计时）
- 60秒归零 → 触发 `MainAgentTakeoverRequested` 事件
- UI提示："子代理已中断 · 60s内无输入将移交mainAgent · 倒计时: {Ns}"

**4.5.3 倒计时配置**：
- 默认60秒，可配置（`GuiPreferences.IdleTimeoutSeconds`）
- 0 = 禁用超时（永不唤醒mainAgent，纯对齐ClaudeCode）

### 4.6 60秒超时唤醒mainAgent接手编排

**需求**：倒计时归零时：
1. `CancelAgentAsync`（真正终止子代理）
2. `CleanupWorktreeAsync`（清理worktree，有变更保留并记录reason=takeover）
3. 触发mainAgent接手：将子代理的worktree diff/结果摘要注入主会话，mainAgent分析并接手后续工作

**mainAgent接手编排细节**（后续细化）：
- 提取子代理worktree相对主仓库的diff
- 构造接手消息（"子代理X已中断，它在worktree里改了以下内容：{diff摘要}，请接手分析"）
- 注入主会话 ChatHistory，触发mainAgent新一轮

### 4.7 worktree清理对齐

| 操作 | worktree清理 | MCP清理 |
|------|-------------|---------|
| Interrupt（中断进idle） | ❌ 保留 | ❌ 保留 |
| Cancel（终止） | ✅ CleanupWorktreeAsync | ✅ CleanupMcpServers |
| Dispose（释放） | ✅ DisposeWorktreeCleanupMiddleware | ✅ |
| 正常完成 | ✅ Dispose管道 | ✅ |

**修复**：`StopAgentAsync`（`AgentServiceImpl.cs:249`）补 `CleanupWorktreeAsync` 调用（当前遗漏）。

## 5. 技术现状与Gap分析

### 5.1 已有基础设施
- ✅ `TaskExecutionStatus.Paused` 枚举已存在
- ✅ 状态机 `Running → Paused` 已允许（`AgentStateMachine.cs:200`）
- ✅ `PauseAgentAsync`/`ResumeAgentAsync` 已实现（`AgentLifecycleManager.cs:123,136`）
- ✅ `ForwardInputToSubAgentAsync` 已实现（`JccChatSession.cs:156`）
- ✅ `IAgentInputForwardQueue` 用户输入转发队列已存在
- ✅ 子代理ID为分配器唯一ID（`{parentSessionId}-sub-{counter:D2}`），worktree路径基于ID，不会撞名

### 5.2 需要新增/改动
| Gap | 改动 | 文件 |
|-----|------|------|
| `Pause()`不中断work | 新增`Interrupt()` | AgentBase.cs |
| `_cts` readonly | 去掉readonly | AgentBase.cs:15 |
| 无InterruptAgentAsync | 新增 | AgentLifecycleManager.cs, AgentServiceImpl.cs, IAgentService.cs |
| fork单task即退出 | 改idle循环 | AgentServiceImpl.cs:555 RunBackgroundAgentAsync |
| 双击ESC走Cancel | 改走Interrupt | MainViewModel.SessionTree.cs |
| 无空闲倒计时 | 新增SubAgentIdleTimer | JoinCodeGui/ViewModels/ |
| StopAgentAsync不清理worktree | 补CleanupWorktreeAsync | AgentServiceImpl.cs:249 |

### 5.3 风险
| 风险 | 缓解 |
|------|------|
| idle循环重构影响所有fork调用方 | 渐进式：先Interrupt+测试，再idle循环，每步编译+测试+commit |
| _cts去readonly影响子类 | grep确认子类未直接访问_cts（已确认） |
| 60秒窗口GUI复杂度 | 用DispatcherTimer，对齐现有_runStatusTimer模式 |
| mainAgent接手编排未定义清晰 | 先做Interrupt+idle+60秒窗口，编排最后做 |

## 6. 任务分解与实现顺序

按渐进式，每步：红测试 → 实现 → 编译 → 绿测试 → commit

| 步骤 | 内容 | 依赖 | 风险 |
|------|------|------|------|
| 1 | AgentBase.Interrupt() + 去readonly + 测试 | 无 | 低 |
| 2 | AgentLifecycleManager.InterruptAgentAsync() + 测试 | 1 | 低 |
| 3 | IAgentService/AgentServiceImpl.InterruptAgentAsync() + 测试 | 2 | 低 |
| 4 | StopAgentAsync 补 CleanupWorktreeAsync（修复泄漏） | 无 | 低 |
| 5 | fork idle循环重构 RunBackgroundAgentAsync | 1-3 | **高** |
| 6 | 双击ESC路径改Interrupt | 3 | 中 |
| 7 | SubAgentIdleTimer 60秒空闲倒计时 + UI提示 | 6 | 中 |
| 8 | 60秒超时 → Cancel + CleanupWorktree + mainAgent接手编排 | 5,7 | 中 |

## 7. 验收标准

### 7.1 功能验收
- [ ] 双击ESC中断子代理，子代理状态=Paused（非Cancelled）
- [ ] 中断后子代理的LLM流停止（不再消耗token）
- [ ] 60秒空闲倒计时启动，UI显示剩余秒数
- [ ] 用户打字（未发送）→ 倒计时重置（不恢复子代理）
- [ ] 用户发送消息 → 子代理**立即**恢复执行（Resume + next prompt，取消倒计时）
- [ ] 60秒无输入 → mainAgent被唤醒，子代理Cancelled，worktree清理
- [ ] Interrupt后worktree保留（不清理）
- [ ] 主会话双击ESC行为不变（_sendCts.Cancel）

### 7.2 对齐ClaudeCode验收
- [ ] ESC后子代理进idle等next prompt（非终止）
- [ ] 不自动唤醒mainAgent（仅60秒超时触发）
- [ ] worktree清理时机对齐（Interrupt保留，Cancel/Dispose清理）

### 7.3 非回归验收
- [ ] 现有AgentBase测试全部通过
- [ ] 现有AgentLifecycleManager测试全部通过
- [ ] 现有fork/AgentService测试全部通过
- [ ] 现有GUI MainViewModel测试全部通过
- [ ] 编译零警告（TreatWarningsAsErrors）

## 8. 配置项

| 配置 | 默认值 | 说明 |
|------|--------|------|
| `GuiPreferences.IdleTimeoutSeconds` | 60 | 空闲超时秒数，0=禁用（纯ClaudeCode模式） |
| `GuiPreferences.DoubleEscStop` | true | 双击ESC手势开关（已有） |

## 9. 开放问题

1. **mainAgent接手编排的具体消息格式**：注入什么diff摘要？mainAgent的system prompt要不要加"你正在接手子代理的工作"？（步骤8细化）
2. **idle循环中子代理的输出channel**：idle期间用户看到的子代理窗口显示什么？（"已中断，等待你的输入"提示）
3. **多个子代理同时被中断**：每个子代理独立60秒倒计时？还是全局一个？（倾向独立，每个子会话一个timer）

<!-- 🤖 Auto Decision: 2026-08-27 -->
<!-- 决策: 步骤1+2完成 — teammate暴露CurrentWorkCts+InterruptTeammateAsync，用_teammateLock保护读写 -->
<!-- 原因: workCts原是using var局部变量外部无法abort;改为显式try-finally管理,lock内赋值/清空保证Interrupt线程安全 -->
<!-- 替代方案: 用Volatile字段无锁,但TeammateState是public class用属性风格,加lock更清晰且Interrupt低频可接受 -->
<!-- 验证: Scheduling.csproj编译通过(0警告0错误),10个测试全通过(8原有+2新增) ✅ -->

<!-- 🤖 Auto Decision: 2026-08-27 -->
<!-- 决策: 步骤1+2合并实现(接口+实现+测试一起改),因接口方法不存在则测试无法编译,TDD红绿在同一次改动 -->
<!-- 原因: 渐进式要求每步可编译可commit;分两次(先加throw NotImplemented再实现)会产生中间无意义commit -->
<!-- 验证: commit 1d690ddae ✅ -->

<!-- 🤖 Auto Decision: 2026-08-27 (步骤3-5批量.完成) -->
<!-- 决策: GUI子会话切teammate完整链路打通:AgentForkMiddleware优先teammate→JccChatSession归并teammate+fork读取→双击ESC走Interrupt→60秒IdleTimer -->
<!-- 原因: 对齐ClaudeCode inProcessRunner ESC+idle;teammate循环改正常完成退出,Interrupt后进idle等next prompt -->
<!-- 关键改动: -->
<!--   3a: teammate循环正常完成退出(非每轮等next prompt),Interrupt(OCE且lifecycle未取消)后进idle -->
<!--   3b: GetActiveTeammateSnapshotsAsync返回snapshot(TeammateId/ParentSessionId/Task/IsIdle/TurnCount/LastResult) -->
<!--   3f: TeammateCompleted事件(teammate退出时触发供GUI移除卡片) -->
<!--   3d: AgentForkMiddleware注入teammateExecutor优先走teammate,回退fork;Hands加引用Scheduling -->
<!--   3e: JccChatSession.GetSubSessionsAsync归并teammate+fork;StopBackgroundAgentAsync加teammate段 -->
<!--   4: IJccChatSession+JccChatSession加InterruptSubAgentAsync;StopGenerating改调InterruptSubAgentAsync -->
<!--   5a: SubAgentIdleTimer(DispatcherTimer 60秒倒计时,Reset/Stop/MainAgentTakeoverRequested) -->
<!--   5b: MainViewModel集成IdleTimer,Interrupt后启动,OnInputKeyDown调Reset,发送调Stop,超时OnMainAgentTakeoverRequested -->
<!-- 替代方案: 改fork本身支持Interrupt(违反PRD方案B-revised);或GUI层独立触发(绕过中间件链,需新接口) -->
<!-- 待完成: 步骤3c(worktree支持)+步骤6完整版(mainAgent分析diff接手,依赖worktree) -->
<!-- 验证: Core.slnx+JoinCodeGui编译通过(0警告0错误),Scheduling 11测试全通过 ✅ -->
