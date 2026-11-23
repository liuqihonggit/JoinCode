# TASK120 — LocalAgentTask / 主会话后台化任务类型（TASK103 §2.3 #51 取证）

> 状态：**本地 agent 后台执行链已等价实现；主会话后台化属新行为，挂专项设计，本轮不做** · 日期 2026-11-22 · 总账编号：TASK103 §2.3 **#51 / TASK071-A7**
> 基准树：`D:\project\claude-code-rust\claude-code-rev-main\src`
> 本仓库：`D:\project\w2`

---

## 0. 结论（先看这里）

原文「LocalAgentTask/主会话后台化任务类型」必须拆成三层，不能按文件名词直接判缺：

1. **任务类型枚举与 ID 前缀已对齐**：基准 `Task.ts:6-13/78-106` 的七种 `TaskType` 与前缀，本仓 `TaskType.cs:6-13/38-47` 一一对应（`local_agent='a'`、`local_bash='b'` 等）。但本仓 `AgentTask` 实体在 `AgentTask.cs:8/38` 只是默认 `LocalAgent` 定义，全仓 `new AgentTask(` 只在实体自身/测试中出现，`TaskType.LocalAgent` 除枚举与测试外无生产构造点——**不能把空壳实体当作已实现统一任务框架**。
2. **本地 agent 后台执行、通知、面板、停止已由本地链路覆盖**：`AgentBackgroundSpawnMiddleware.cs:27-77` 走 fire-and-forget 后台 spawn；`AgentServiceImpl.cs:800-832` 发完成事件并把含 result/error/usage/worktree 的 `AgentTaskNotification` 入队（`IAgentService.cs:158/187` + `AgentTaskNotificationTests.cs:4-83` 锁 XML 字段）；`BackgroundTaskEventPump.cs:15-19/59-88/139-157` 把 fork/teammate 终态推到 TUI 常驻面板与详情/停止入口（`native_tui/.../BackgroundTaskPanel.cs`、`BackgroundTaskDetailDialog.cs`、`ReplShell.cs:1629-1648`）。TASK103 S14 已真实验证后台回调进入问答案与查询循环（`TASK103-ReAct循环合并-两套归一.md:314-379`）。这些与基准 `LocalAgentTask.tsx` 的“后台 agent + 完成通知 + 进度/输出展示”是同语义层，保留本仓实现。
3. **真正缺的是“主会话查询自身后台化”这一交互新行为**：基准 `LocalMainSessionTask.ts:1-9/94-162/168-263/270-322/338-479` 在 Ctrl+B 后把当前查询注册成 `agentType='main-session'` 的 `local_agent` 任务，写独立 transcript symlink、清空前台、完成时入队通知，并支持前台/后台相互切换；`LocalAgentTask.tsx:521-681` 另有前台 agent 的自动/手动后台化信号。本仓全仓无 `main-session`；`ForegroundTaskRegistry.BackgroundAll` 只处理 `TaskType.LocalBash`（`ForegroundTaskRegistry.cs:33-53`）且全仓除定义外零调用，不能证明主会话入口存在。该行为改变并发查询、会话存储与 UI 控制权，属新行为（同 #40/#42/#45 边界），无人值守不引入，另立专项。

**裁决**：不迁移 TS 的全局 `AppState.tasks` 多态任务框架，不给空壳 `AgentTask` 硬接生产；保留已落地的后台 agent 链；主会话后台化与前台 agent 自动后台化挂专项设计。零代码改动。

---

## 1. 四问回答

### 问1 专家隐性实践

“后台任务”要先分清主体：子 agent 在后台跑，主会话仍在等待；主会话自身后台化，是把用户正在等的那条查询从前台剥离、释放输入框。前者只涉及任务生命周期，后者涉及会话 transcript 隔离、取消令牌归属、UI 焦点、通知去重和 `/clear` 后的存活，不能靠多注册一个 task type 完成。把不同主体的能力放在同一行对齐，容易把“已有后台 agent”误判成“已有主会话后台化”，或反过来为了一个名称重写整条链路。

### 问2 原始素材（一手）

- 基准：`src/Task.ts:6-13/44-57/72-125`（TaskType/TaskStateBase/Task 接口与 ID 前缀）、`src/utils/task/framework.ts:74-117/119-206`（registerTask/SDK task_started/驱逐）、`src/tasks/LocalAgentTask/LocalAgentTask.tsx:116-161/264-315/334-456/466-681`（local_agent 状态、kill/complete/fail、通知、注册、前台→后台信号）、`src/tasks/LocalMainSessionTask.ts:1-9/54-82/94-162/168-263/270-322/338-479`（主会话后台化）、`src/hooks/useSessionBackgrounding.ts:1-158`（Ctrl+B 前后台切换与消息同步）、`src/screens/REPL.tsx:212/2591`（真实调用点）、`src/tasks.ts:1-39`（任务类型注册表）。
- 本仓：`modules/abstractions/abstractions_primitives_utils/src/enums/TaskType.cs:6-13/38-47`（七类型与前缀）、`modules/abstractions/abstractions_entity/core_entity/task_planning/AgentTask.cs:8/38/60`（空壳实体）、`modules/cognition/hands_core/tool_handlers/system_tools/agent/AgentBackgroundSpawnMiddleware.cs:27-77`（后台 spawn）、`modules/llm/agents_coordinator_core/AgentServiceImpl.cs:780-832`（完成事件+通知入队）、`modules/abstractions/abstractions_interfaces/interfaces/agent/IAgentService.cs:158/187` 与 `modules/llm/agents_tests/Agents/core/agent/AgentTaskNotificationTests.cs:4-83`（通知 XML 字段）、`modules/apps/engine/adapters/BackgroundTaskEventPump.cs:15-19/59-88/139-157`（引擎事件泵）、`native_tui/src/NativeTui.Core/repl/BackgroundTaskPanel.cs` + `BackgroundTaskDetailDialog.cs` + `ReplShell.cs:1629-1648`（面板/详情/停止）、`modules/cognition/hands_shell/shell/services/ForegroundTaskRegistry.cs:33-53`（仅 shell 的 Ctrl+B 注册表，零调用）、`docs/implemented/task/TASK103-ReAct循环合并-两套归一.md:314-379`（后台回调真实注入验证）。

### 问3 具体方法与工具

先 grep 名称（会命中空壳实体和注释），再 grep 每一职责的**生产消费点**：`new AgentTask(`、`TaskType.LocalAgent`、`AgentTaskNotification.Enqueue`、`OnBackgroundTask`、`Ctrl+B`/`main-session`。对“存在”的判定一律要求运行期消费点，不按定义/注释计数；对“缺失”的判定要求 grep 全词后仍无产生点。最后按主体分层（子 agent 后台 vs 主会话后台）裁决是否同一功能。

### 问4 独立验证来源

`TASK064-TUI对齐ClaudeCode-M8批次差距规划.md:128-134` 记录 M8-8 后台任务面板已 implemented 并有真机 selftest；`TASK103-ReAct循环合并-两套归一.md:360-378` 用真实 PTY + MockServer 证明后台 callback 可关闭 ask 并注入查询历史。两份独立验收与源码中的事件泵/通知 XML 互证“本地 agent 后台链已活”。基准侧 `LocalMainSessionTask.ts:4-7` 的头部注释明确定义主会话后台化的四项语义，不能由 agent 后台链推导出来。

---

## 2. 差异与判定

| 维度 | 基准 | 本仓 | 判定 |
|---|---|---|---|
| 七种任务类型/ID 前缀 | `TaskType` 联合类型 + `TASK_ID_PREFIXES` | `TaskType` 枚举 + `TaskIdPrefixes`，七项一致 | 已对齐（仅值层） |
| 统一任务状态注册表 | `AppState.tasks` + `registerTask/updateTaskState/evictTerminalTask` | 无同名全局表；fork/teammate 由管理器，todo 由 `ITaskService`，shell 由 actuator registry 分域管理 | 架构分歧，保留；不为空壳 `AgentTask` 硬接消费方 |
| 后台子 agent 注册/取消/终态通知 | `registerAsyncAgent`/`killAsyncAgent`/`enqueueAgentNotification` | `AgentBackgroundSpawnMiddleware` + `AgentServiceImpl` + `AgentTaskNotification` 入队 | 等价实现，保留 |
| 后台任务 UI/进度/停止 | CoordinatorTaskPanel + BackgroundTasksDialog + progress/activities | `BackgroundTaskEventPump` + TUI 常驻面板/详情/停止（fork/teammate 两类） | 等价实现，保留；不做 `recentActivities` 逐字段搬运 |
| 输出文件与 transcript | `initTaskOutputAsSymlink` + `getTaskOutputPath` + `outputOffset` 增量 | agent transcript 由 `AgentTranscriptService` 持久化；通知含可查询文本结果 | 语义近似，保留；不引入 symlink/offset 双轨 |
| 前台 agent 自动后台化 | `registerAgentForeground` + `autoBackgroundMs` + `backgroundSignal` | 无对应信号；agent `run_in_background` 是创建时显式参数 | 新行为，挂专项 |
| 主会话 Ctrl+B 后台化 | `LocalMainSessionTask` + `useSessionBackgrounding` + REPL 接线 | 全仓无 `main-session`；`BackgroundAll` 只处理 shell 且零调用 | 真差异、新行为，挂专项 |
| `/clear` 后保留后台主会话 | 隔离 transcript + symlink 重链 | 无该任务主体，不适用 | 随专项设计 |

---

## 3. 为什么不做（无人值守边界）

- **统一任务框架是替换式改造**：基准是 React `AppState` 全量任务表 + 多态 kill/渲染；本仓是 C# 域服务 + Actor/事件泵。为对齐而重建全局表会造成第三套任务状态（管理器、`ITaskService`、新表），违反“唯一数据源”纪律。
- **主会话后台化改变产品行为**：一次查询拆成前台查询 + 独立后台查询、多 transcript、UI 控制权切换、通知时机与取消归属；与 #40 dream 自动触发同类，不是修复断链。
- **空壳实体不能“顺手接上”**：`AgentTask.Registry` 没有生产 `Add` 路径，强行让 agent 生命周期再注册一次只增加状态副本，不产生新能力。
- **已等价部分不做外观迁移**：通知 XML 字段、面板 upsert、详情/停止、问答唤醒均有消费点且经真机验收，逐字段照搬只会制造双源。

---

## 4. 专项启动条件（保留待办，非不适用）

若后续决定实现主会话后台化，需先回答四问：

1. **入口与主体**：触发键（基准双击 Ctrl+B）在本仓 native TUI/CLI/GUI 哪一层？是否只允许忙时触发？前台 agent 是否共用同一信号，还是只做主会话？
2. **会话存储隔离**：后台查询写独立 transcript 还是复用当前会话？`/clear` 重链时保留哪些 ID、如何避免把后台消息写进清空后的主 transcript？
3. **前台/后台切换**：前台化时消息增量如何同步、取消令牌归谁、上一前台任务如何回后台？终态通知在“正在观看”时是否抑制（基准按 `isBackgrounded` 判定）？
4. **协议与开关**：`task_type='local_agent'` + `agentType='main-session'` 是否进 SDK 事件与 NDJSON？默认关闭还是开启？失败/取消如何去重通知与 GC？

---

## 5. 本轮验收

- 零代码改动；不触发编译与真机测试（仅取证、判据与专项登记）。
- 总账 #51 回填为“部分等价实现/主会话后台化挂专项”，下一动作推进 #52。
