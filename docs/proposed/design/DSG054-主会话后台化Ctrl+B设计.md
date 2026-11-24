# DSG054 — 主会话后台化（Ctrl+B）专项设计（待裁决，零实现）

> 状态：**只做设计，用户裁决前不写任何实现代码** · 日期 2026-11-24
> **✅ 可行性复核结论（2026-11-24）**：§7 曾判定"四层单回合约束使 D 前提不成立"——那是基于
> "两条查询并发"的误读。深查基准一手源码发现 Ctrl+B 的真实语义是**打断前台 + 另起一条独立 query 续跑**
> （`REPL.tsx:2555-2606` `abortController.abort('background')` → `startBackgroundSession`），并非两条并发。
> 本仓有同款生产蓝图 `BtwCommand.RunSideQuestionAsync`（`BtwCommand.cs:67-102`），积木齐全，
> **7 项架构改造不需要**。最终路线见 §8，§4/§5 作为目标态参考、§7 保留作为被否路线的取证。
> 来源：TASK130 B2 / TASK103 §2.3 #51 / TASK071-A7 · 取证底稿：[TASK120](../task/TASK120-LocalAgentTask后台化任务类型-差异取证.md)
> 基准树：`D:\project\claude-code-rust\claude-code-rev-main\src` · 本仓：`D:\project\w2`

## 0. 一句话与裁决点

Ctrl+B 的目标不是"再加一个后台任务"，而是**把用户正在等的那条主会话查询从前台剥离**：查询继续执行、输入框立刻清空可用、完成时给一条通知。它与已落地的"子 agent 后台化"是不同主体、不同风险面。

**请裁决四选一**（本设计默认推荐 D）：

| 选项 | 内容 | 代价 |
|---|---|---|
| A | 全量照基准实现（`main-session` 任务 + 独立 transcript + 双击 Ctrl+B + 前后台互切 + SDK/NDJSON 事件） | 最大：改并发语义、会话存储、UI 控制权，测试面广 |
| B | 只做"忙时 Ctrl+B 清空输入框 + 完成时通知"，查询实际不迁移线程 | 最小：语义与基准不符（假后台），但无并发风险 |
| C | 只做"前台 agent → 后台"（基准 `autoBackgroundMs`/`backgroundSignal`） | 中：不解决主会话本体 |
| **D（推荐）** | 分两阶段：① 先做"忙时 Ctrl+B → 查询转后台执行 + 独立 transcript + 完成通知 + Ctrl+B 回前台观看"；② 观察真实使用后再决定是否进 SDK/NDJSON 协议与 `/clear` 重链 | 中：一次交付闭环可用能力，把协议与边缘场景留到有真实反馈后 |

---

## 1. 为什么不能"顺手接上"

TASK120 已取证：本仓 `KeybindingAction.TaskBackground`（`ReplShell.cs` 之外）、catalog（`"task:background"`，`KeybindingNames.cs:95`）、默认绑定（`KeybindingProfile.cs:241`，`KeybindingContext.Task` + `ctrl+b`）三件齐全；但 `ReplShell.DispatchCtrlAction`（`ReplShell.cs:2004-2045`）**没有 `TaskBackground` case**，命中后落 `default: return false` → 继续走编辑键路径，等同未接线。

后端 `IForegroundTaskRegistry.BackgroundAll`（`ForegroundTaskRegistry.cs:33-53`）只处理 `TaskType.LocalBash`，全仓除定义外零调用。空壳 `AgentTask`（`AgentTask.cs:8/38/60`，无生产 `new AgentTask(`）也不能当作统一任务框架硬接——TASK120:13/61 已否决。

所以这不是"少个 switch case"的断链修复，而是**需要一个新主体 + 新状态机**。

---

## 2. 原始素材（一手，非二手总结）

**基准侧**

- `src/tasks/LocalMainSessionTask.ts:1-9`（四项语义注释）、`:60-82`（`s` 前缀 ID）、`:94-162`（`registerMainSessionTask`：沿用 `LocalAgentTaskState`、`agentType='main-session'`、独立 transcript symlink、复用传入的 `AbortController`）
- `src/hooks/useSessionBackgrounding.ts:22-43`（Ctrl+B 分两态：无 foreground 则 `onBackgroundQuery()`；有则 re-background）、`:46-155`（前台观看时把任务消息同步到主视图、abort 与终态自动退回后台）
- `src/tasks/LocalAgentTask/LocalAgentTask.tsx:334-456/466-681`（local_agent 状态与通知）、`src/utils/task/framework.ts:74-206`（registerTask/驱逐）
- `src/defaultBindings.ts:181-188`（Task 上下文 Ctrl+B）

**本仓侧（可复用，不重建）**

- 键位：`KeybindingAction.cs:168-169`、`KeybindingNames.cs:95`、`KeybindingProfile.cs:241`
- 后台任务面板/详情/停止：`BackgroundTaskPanel.cs`、`BackgroundTaskDetailDialog.cs`、`BackgroundTaskEventPump.cs:15-19/59-88/91-92`
- 后台 agent 执行链：`AgentBackgroundSpawnMiddleware.cs:27-77`、`AgentServiceImpl.cs:800-832`（完成事件 + `AgentTaskNotification` 入队）
- transcript 隔离：`AgentTranscriptService.cs:34-41`（按 `sessionId+agentId` 分文件）、`TranscriptController.cs:54-55/114-125`（冻结快照/ live 双模，可借来做"观看态"）
- 注意同名无关键：`TranscriptController.cs:11` 的 `ctrl+b` 是 less 风格整屏下翻，仅 transcript 模态内生效——**新逻辑必须让模态优先消费，避免抢键**。

---

## 3. 调研四问（按 AGENTS.md）

1. **专家隐性实践**：先分清"谁在后台"。子 agent 后台时主会话仍在等，主体是子任务；主会话后台是把用户正在等的那条查询剥离，涉及 transcript 归属、取消令牌归属、UI 焦点、通知去重、`/clear` 后存活。把两者放在一行对齐，会把"已有后台 agent"误判为"已有主会话后台化"。
2. **原始素材清单**：见 §2，全部为一手源码；不引用 TASK120 的转述作为设计依据，只把它当取证目录。
3. **具体方法与工具**：判定"存在"一律以运行期消费点为准（`DispatchCtrlAction` 的 case、`BackgroundAll` 的调用点），不按枚举/注释计数；设计以"状态机 + 唯一数据源"落地，禁止给空壳 `AgentTask` 再造一套状态副本。
4. **独立验证来源**：TASK120 的两份独立验收（M8-8 面板 selftest、`TASK103-ReAct循环合并-两套归一.md:314-379` 真 PTY + MockServer 证明后台回调注入）只能证明**子 agent 后台链已活**，不能证明主会话后台化；基准 `LocalMainSessionTask.ts:4-7` 的四项语义是这条能力的独立规范源。

---

## 4. 设计（推荐 D 的第一阶段）

### 4.1 主体与唯一数据源

新增一个**主会话后台任务记录**，只放一份，挂在现有会话状态旁（不进 `AppState.tasks` 那类全局多态表，也不塞进空壳 `AgentTask`）：

```
MainSessionBackground {
  TaskId            // 's' 前缀，复用 TaskIdPrefixes 增加 main-session 段（TASK120 已确认七类型，新增第八需裁决）
  SessionId         // 归属会话，/clear 后仍可找到
  TranscriptPath    // 独立文件（借 AgentTranscriptService 的 sessionId+agentId 分文件约定）
  AbortController   // 当前唯一持有者；前台观看时临时借用，退回后台归还
  State             // Running | Foregrounded | Completed | Failed | Cancelled
  StartedAt / LastSeq
}
```

**不做**：不迁移 `AppState.tasks` 全局表、不写 `AgentTask.Registry`、不引入 symlink/offset 双轨（TASK120:59-62 已否决）。

### 4.2 并发模型

- 触发条件：**仅忙时**（有正在流式执行的查询）。闲时 Ctrl+B 不消费、保持 less 风格 transcript 下翻（对齐 `TranscriptController.cs:11`）。
- 后台执行：沿用现有查询管道，**不新建线程/进程**；只是把"等待结果"从 UI 前台移到 `MainSessionBackground` 状态。查询一旦启动，其取消令牌归任务记录持有。
- 并发上限：主会话后台任务与子 agent 共用现有 `PermitPool`（`AgentCoordinator.cs:76-77`），避免双套限流。

### 4.3 transcript 写谁

- 后台查询写入**独立 transcript**（借 `AgentTranscriptService.AppendEntryAsync(sessionId, agentId, entry)`，agentId 用 `s`-前缀任务 ID）；主 transcript 在后台期间不写该查询的消息。
- `/clear` 不清后台任务记录与它的 transcript；清空后主视图仍可通过任务 ID 找到它（第一阶段不做基准的 symlink 重链）。
- 完成时通知只带可查询文本结果（沿用 `AgentTaskNotification` 的 XML 字段契约，已由 `AgentTaskNotificationTests` 锁定），不搬 outputOffset。

### 4.4 状态机（Ctrl+B / Esc）

```
Idle ──Ctrl+B(忙)──> Running(后台, 输入框清空)
Running ──Ctrl+B──> Foregrounded(消息同步到主视图, 临时借 AbortController)
Foregrounded ──Ctrl+B──> Running(归还令牌, 清空主视图)
Foregrounded ──查询终态──> Completed/Failed (自动退回后台, 发一次通知)
Running ──查询终态──> Completed/Failed (发一次通知, 面板可点开)
任意态 ──Esc/面板停止──> Cancelled (取消令牌, 去重通知一次)
```

关键约束：**终态通知去重**——同一 TaskId 只发一次；用户正在 `Foregrounded` 观看时抑制通知（对齐基准 `isBackgrounded` 判定）；`Cancelled` 不发完成通知，只写一条取消记录。

### 4.5 失败回滚

| 失败点 | 回滚 |
|---|---|
| 注册任务失败 | 保持原前台查询不变，Ctrl+B 返回"未消费"，不丢输入 |
| 独立 transcript 写失败 | 降级为写主 transcript + 日志告警，任务仍可后台（不让存储故障阻断交互） |
| 前台观看同步失败 | 退回 `Running`，主视图清空，通知仍发 |
| 取消令牌交接失败 | 令牌归任务记录，UI 只发取消请求，绝不直接 `Cancel` |

### 4.6 协议与开关（第二阶段，先不做）

`task_type='local_agent'` + `agentType='main-session'` 是否进 SDK 事件/NDJSON、默认开还是关、跨会话恢复——**均留到第二阶段**，本阶段只在 TUI 内部可视（后台面板 + 通知），不污染协议。

---

## 5. 验收判据（裁决通过后才施工）

- 红测试：`DispatchCtrlAction(TaskBackground)` 忙时有 case、闲时不消费；状态机 5 态转移与通知去重各有单测。
- 真机：真实 PTY + MockServer 跑 `Ctrl+B` 两次 → 观察"后台继续、输入清空、完成通知、再按回前台、终态自动退回"五段，配产物哈希与窗口身份。
- 回归：Ctrl+B 在 transcript 模态仍为 less 下翻（不抢键）；后台 agent 面板/停止原 4/4 用例不变。
- 门禁：StyleGate + 节点/顺序门禁全绿；新增 `s` 前缀须同步 `TaskIdPrefixes` 与 `Verify-ToolParamNames` 等相关门禁。

## 6. 不做清单（沿用 TASK120）

- 不迁移全局任务框架；不给空壳 `AgentTask` 硬接；不重写后台 agent 链（已等价）；不做前台 agent 自动后台化（选项 C，除非单独裁决）。
- 不做 symlink/offset 双轨、不做 `recentActivities` 逐字段搬运。
- 用户裁决前：零实现代码、零编译、零真机测试。

---

## 7. 可行性复核 — 四层单回合硬约束（覆盖前文，2026-11-24）

用户裁决选 D 后做了引擎侧深查，结论是 **D 第一阶段的核心交互在当前架构下不可实现**：要让"查询转后台后用户继续发新查询"成立，引擎必须支持同一会话两条在途查询，但本仓在四层显式禁止这一点：

| 层 | 证据 | 后果 |
|---|---|---|
| 回合槽 | `EngineTurnClient.cs:20-25/63-64/247-252`：单个 `_turnCts`/`_turnTask`，忙时直接拒绝 `"回合进行中，本次提交已忽略…请先按 Esc 打断"`；`BridgeServeLoop.cs:52-56/102-107` 同款 | 新查询进不去 |
| 事件通道 | `BridgeEvent.cs:35-42` 只有 `AckSeq`，无 queryId/sessionId；`InProcessEventConsumer.cs:29-39/59-67` 单缓冲且每回合 `Reset()` | 两条查询的 token/tool/assistant 事件在同一 FIFO 交错，且第二条启动会抹掉第一条缓冲 |
| transcript | `TranscriptPersistMiddleware.cs:41-43/57-83` + `SessionController.cs:694-731`：按"回合开始消息数 → 结束快照差量"写 `{sessionId}/transcript.jsonl` 同一文件 | 两条各写含对方的差量 → 重复/乱序/上下文污染 |
| 取消 | `IReplEngine.Interrupt()` 无参 → `EngineTurnClient.cs:228-230` 取消唯一回合 CTS | 无法定向取消后台那条 |

另有实例级回合状态：`SessionController` 的 `_hasSubstantiveOutput`/`_lastStopReason`/`_totalTurns` 等十余字段、`ChatService` 单例 `_conversationTurn`/`_toolUseContext`（`ChatService.cs:11-12/29`）——并发下直接串台。

**两个必须纠正的事实**

1. `ReplShell.cs:1926-1928` 显示 **Ctrl+B 当前绑定是"光标左移"**（`case "b": _input.Left()`），不是本文 §1 说的"未接线落编辑路径"；`KeybindingAction.TaskBackground` 的分派缺失仍在，但键位语义冲突需一并处理（transcript 模态内 `ctrl+b` 另作整屏下翻）。
2. 基准之所以能实现，是因为它有 queryId 可归因的事件/任务框架；本仓 TASK120 已明确裁决**不迁移 `AppState.tasks` 全局任务表**。要支持并发等于推翻该裁决。

**改造面（若用户仍要做，7 项，属架构级）**：①回合槽改 `queryId → (Cts,Task)` 映射 + `Submit` 返回 queryId；②`BridgeEvent` 加 queryId、每 query 独立 consumer；③`ReplBridgeDriver`/`ReplShell` 单一 `IsBusy` 改多槽、Ctrl+B 从光标左移解绑；④`SessionController` 实例级回合状态移入 per-turn context；⑤`ChatService`/`AgentBase` 共享对话状态按 query 隔离；⑥transcript 改 `{sessionId}/queries/{queryId}.jsonl` 再归并；⑦协议 ADR 0150/TASK060 与两组忙时拒绝测试重写。

**当前处置**：设计稿保留为目标态参考，**不改任何生产代码**；B2 重新回到用户裁决点——(甲) 接受 7 项架构改造，(乙) 降级为 C（只做前台 agent 后台化，不动主会话并发），(丙) 暂缓。

---

## 8. 最终路线 — 绕行方案（abort + 独立续跑，2026-11-24 定稿）

### 8.1 基准的真实语义（一手源码纠正 §7 的误读）

我此前把 Ctrl+B 理解为"前台查询不中断地转后台 + 用户并发发新查询"，据此判定需要 7 项引擎改造。
读基准 `screens/REPL.tsx:2555-2606` 后确认这个理解错了：

```
handleBackgroundQuery():
  abortController?.abort('background')        // ① 打断前台那条
  removedNotifications = removeByFilter(task-notification)
  getToolUseContext / getSystemPrompt / ...   // ② 重建 query 上下文
  startBackgroundSession({                    // ③ 用已有 messages 起一条全新独立 query
    messages: [...messagesRef.current, ...uniqueNotifications], ...
  })
```

`tasks/LocalMainSessionTask.ts:338-479` `startBackgroundSession` 内部 `for await (const event of query({...}))`
是一条**全新的 query 调用**，事件写进 task 自己的 `bgMessages[]` 与独立 transcript
（`recordSidechainTranscript`），不进主视图。任意时刻只有一条流在跑——**没有并发**。

`hooks/useSessionBackgrounding.ts:41-66` 的"再按 Ctrl+B 回前台"也只是把 task 的 `messages` 同步到主视图
并置 `foregroundedTaskId`，不是把两条流来回切。

### 8.2 本仓积木盘点（全部现成，无需改引擎并发）

| 基准机制 | 本仓等价积木 | 证据 |
|---|---|---|
| 独立 `query()` 调用 | `IQueryEngine.QueryAsync` + `AgentBase.ExecuteStreamAsync` | `IQueryEngine.cs:12-22`、`AgentBase.cs:361-424` |
| 复制主历史做前缀 | `IChatContextManager.GetMessageListAsync()` 逐条复制 | `BtwCommand.cs:72-82`（**生产蓝图，同款模式**） |
| 不写回主上下文 | `new AgentBase` 不注入 ContextManager | `BtwCommand.cs:84-92` |
| 独立事件缓冲 | `new InProcessEventConsumer(独立 ConcurrentQueue<BridgeEvent>)` | `InProcessEventConsumer.cs:52-55` |
| 独立 transcript | `IAgentTranscriptService.AppendEntryAsync(sessionId, agentId, entry)` | `AgentTranscriptService.cs:34-55`（按 subagents/{agentId} 分文件） |
| 完成通知 | `IAgentNotificationQueue` + `AgentTaskNotification.ToXml()` | `AgentServiceImpl.cs:823-839`、`IAgentService.cs:158-210` |
| 面板/详情/停止 | `OnBackgroundTask → BackgroundTaskPanel.Upsert`，kind 是自由字符串 | `ReplBridgeDriver.cs:256-259`、`BackgroundTaskPanel.cs:95-113` |
| abort 前台 | 现有 `IReplEngine.Interrupt()` | `ReplShell.cs:1933-1936`、`EngineTurnClient.cs:228-230` |

**结论**：`EngineTurnClient` 单回合槽、`BridgeServeLoop` 忙拒绝、BridgeEvent 无 queryId、
SessionController 实例级状态——这四层约束**对绕行方案都不构成阻塞**，因为新 query 根本不进回合槽、
不与前台流共用事件队列。§7 的 7 项改造是"真并发"路线才需要的，本方案不采用。

### 8.3 第一阶段改动清单（3 处新增 + 2 处改）

**新增**

1. **主会话后台续跑编排服务**（引擎侧，可注入）：`MainSessionBackgroundService` —— 
   `BackgroundAsync(history, prompt, ct)`：生成 `s`-前缀 taskId → 复制历史 → `new AgentBase`（不注入
   ContextManager，对齐 BtwCommand）→ 绑独立 `InProcessEventConsumer` → `ExecuteStreamAsync` →
   chunk 投影为 `TranscriptEntry` 写独立 transcript → 终态推 `OnBackgroundTask(kind="main-session", ...)` +
   `IAgentNotificationQueue.Enqueue`。**取消**：服务自持 per-task CTS，面板 `x` 停止时定向取消（不碰全局
   `Interrupt()`）。
2. **chunk → transcript 投影胶水**：`AgentStreamChunk`（Content/ToolUse/ToolResult/Usage）→ `TranscriptEntry`，
   含 modelId 与 tool call 空 content 的处理；第一阶段只写完整消息，不做逐 token 落盘。
3. **状态机**（纯逻辑可测）：`MainSessionBackgroundState` 五态
   `Running/Foregrounded/Completed/Failed/Cancelled` + 通知去重（沿用基准 `notified` 原子 check-and-set
   语义，`completeMainSessionTask` 对应本仓实现）。

**改**

4. `ReplShell.DispatchCtrlAction` 加 `TaskBackground` case（当前缺失）：忙时调用编排服务 +
   `_engine.Interrupt()` + 清空输入/排队；闲时不消费。
5. **键位冲突**：`ReplShell.cs:1926-1928` 的 `case "b": _input.Left()` 当前占用 Ctrl+B；按上下文分层——
   transcript 模态内保留 less 下翻（`TranscriptController` 优先消费），忙时让位 `TaskBackground`，
   闲时 prompt 态保留光标左移。**这一点必须真机确认默认绑定 `KeybindingProfile.cs:241` 的 Task 上下文
   是否在 prompt 态生效**，若全局生效则需调整绑定表而不是抢 switch。

**不做（第一阶段）**：前后台"观看态"消息同步到主视图（基准 `foregroundMainSessionTask`）、
SDK/NDJSON 事件、`/clear` 后 symlink 重链、逐 token 实时落盘——均留第二阶段（对齐用户裁决 D）。

### 8.4 验收判据（第一阶段）

- 红测试：状态机五态转移 + 通知去重；编排服务独立队列不污染主 consumer；`DispatchCtrlAction` 忙时消费/闲时不消费。
- 真机：PTY + MockServer，忙时 Ctrl+B → 前台清空可输入、后台 query 跑完推面板通知、点详情可见完整回复；
  `x` 停止只取消后台那条、不影响新前台查询。
- 回归：Ctrl+B 闲时仍是光标左移；transcript 模态下翻不变；后台 agent 链 4/4 用例不变；忙时拒绝测试仍绿。
- 门禁：StyleGate + 节点/顺序门禁全绿。

---

## 9. 用户纠偏 — "万物皆后台"（覆盖 §8，2026-11-24 定稿）

用户指出：Ctrl+B 不该打断查询再重跑，"默认转后台的行为就应该一开始就放后台，只是异步拉取状态展示在前台——万物皆后台"。

### 9.1 为什么这个批评是对的

§8 的 abort+续跑（照抄基准）有**本质缺陷**：`REPL.tsx:2555-2606` 的 `abortController.abort('background')`
打断跑了一半的查询——已生成的 token、正在执行的工具调用、已扣的费用全部作废，然后用复制的历史重跑一条。
基准那么写不是因为它是好设计，而是因为**基准的前台 query 没有 task 身份**，只能事后补票。
代码里那一堆 `removeByFilter`、`Dedupicate`、`alreadyNotified` 竞态清理正是这个妥协的代价。

用户的模型更本质：**查询从第一刻就是有身份的后台 task，前台只是订阅其状态的视图**。
Ctrl+B 变成纯视图切换（换"当前显示哪个 task"），不 abort、不重跑、无费用损失。
这与基准 `LocalMainSessionTask` 目标相同，但不需要事后补票。

### 9.2 本仓具备的地基（AsyncLocal 先例）

基准用 `AsyncLocalStorage` 隔离并发 query 的上下文。本仓已有同款机制：
`SubAgentContext.cs:4`、`SubAgentEventChannel.cs:11`、`SessionContext.cs:9` 均为 `AsyncLocal`，
`AsyncLocalScope<T>`（`abstractions_primitives_utils/src/core/scopes/AsyncLocalScope.cs`）提供 Enter 作用域。
所以"并发流隔离"不是从零造轮子。

`ChatService` 的两个实例字段（Singleton）是主要污染点：`_conversationTurn`（`ChatService.cs:12`）、
`_toolUseContext`（`:29`），消费面只在 `ChatService` 内部 7 处（`:58/72/92/114/132/162/280`），
改成 AsyncLocal 作用域可控。

### 9.3 实施顺序（地基先行，不做 abort+续跑）

**阶段 0 — 查询身份与事件归因（地基，必须先做）**

1. `IReplEngine.Submit(text)` → 返回/携带 `queryId`（taskId）；`BridgeEvent`（`BridgeEvent.cs:35-42`）增加 `queryId`。
2. `EngineTurnClient` 单 `_turnCts/_turnTask` → `queryId → (Cts, Task)` 映射，`Interrupt(queryId)` 定向取消。
3. `ChatService._conversationTurn` / `_toolUseContext` 改 AsyncLocal 作用域（借 `AsyncLocalScope<T>`），
   隔离并发查询的工具权限/内容替换状态。
4. `SessionController` 实例级回合状态（`_hasSubstantiveOutput`/`_lastStopReason` 等十余字段，
   `:12-37`）移入 per-turn context。

**阶段 1 — 万物皆后台（task 一等公民）**

5. 每个 query 注册为 task：`IAgentNotificationQueue`/`BackgroundTaskPanel` 已有面板与通知出口；
   `AgentTranscriptService.AppendEntryAsync(sessionId, taskId, entry)` 写独立 transcript。
6. 前台变订阅视图：`ReplBridgeDriver` 单一 `IsBusy`/spinner（`ReplBridgeDriver.cs:129/508/536`）按 queryId 多槽；
   `ReplShell.DrainEngine` 按当前聚焦 taskId 投影事件到主视图。
7. Ctrl+B = 切换聚焦 taskId（非 abort）：忙时切到后台 task、清空输入；再按切回；
   键位冲突见 §8.3 第 5 点（`ReplShell.cs:1926-1928` 光标左移）。

**阶段 2 — 协议与边缘**（SDK/NDJSON 事件、`/clear` symlink 重链、逐 token 落盘）

### 9.4 与 §7/§8 的关系

- §7 列的 7 项改造，在"万物皆后台"模型下**全部需要**，但性质从"为并发付出代价"变成"给查询建立身份的地基"。
- §8 的 abort+续跑**作废**：它是用重复计算换架构不改，与用户"无损切换"的诉求冲突。
- 保留 §4/§5 的状态机与 transcript 隔离设计（仍然成立）。

### 9.5 当前状态

用户已明确方向，但阶段 0 触及核心契约（`IReplEngine`、`BridgeEvent`、`IChatService` 行为），
属跨域契约变更，需先写 ADR + 红测试，编译面覆盖 native_tui + 引擎 + host.tests。
**开工前确认**：是否接受"阶段 0 地基先行"（而非先做键位探针或阶段 1 视图）。
