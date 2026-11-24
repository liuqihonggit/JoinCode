# DSG054 — 主会话后台化（Ctrl+B）专项设计（待裁决，零实现）

> 状态：**只做设计，用户裁决前不写任何实现代码** · 日期 2026-11-24
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
