# ADR 0146: 轮内注入（steer）— 步首邮箱 drain + 引擎事件投影

- 状态：accepted
- 日期：2026-10-22
- 决策者：liuqihong（TASK060 B11：宿主 `session/prompt mode:"steer"` 目前诚实拒绝 `session/steer-unavailable`，需补齐 DSH 轮内注入）

## 背景

### DSH 参考语义（权威源）

| 位置 | 事实 |
|------|------|
| `dsh-agent-loop/lib/index.js:800-814` | `send(message,target,wakeup)`；`followup→next-turn`、`steer→next-step(true)`、`inject→next-step(false)`；**abort 后若 wakeup 则回落到 `next-turn`** |
| `dsh-agent-loop/lib/index.js:998-1006` | 模型本步无工具调用（`turnEnds` 已定）但 `inbox.nextStep.length > 0` → **不结束轮次**，`target="next-step"` 继续下一步；同时跳过 `agent/turn-stopping` |
| `dsh-agent-loop/lib/index.js:902-925` | `preStep` 用 `inbox.claim(target, turn)` 取出待注入消息 → `agent/pre-step` waterfall → 作为该步输入 |
| `dsh-agent-loop/lib/index.js:1061` | 步首（`firstAttempt`）把取出消息逐条 `session.append("user/message", message, { surfaceOp: "append" })` → **注入消息在轮内、步首落事件** |
| `dsh-api-session-controller/lib/index.js:882` | `request.mode === "steer" ? agent.steer(message) : agent.followup(message)`，受理即回 `{accepted:true}` |
| `dsh-api-session-controller/lib/index.js:966` | 仅当把「已排队项」升级为 steer（`action.kind==='steer'`）且目标不是 `next-turn` / agent 未 running 时才报 `session/steer-unavailable` |
| `dsh-api-session-controller/lib/client.js:1686` | 前端放置：`running ? (mode==='steer' ? 'steering' : 'queued') : 'transcript'` |
| `dsh-client-ui-conversation/lib/index.js:9` | 忙碌时回车行为可选 `["queue","steer"]`（用户设置），即 **steer 是常规可用路径** |

### 本仓现状

- 宿主 `DshRpcDispatcher` 对 `session/prompt mode:"steer"` 与 `session/updateQueue action.kind:"steer"` 直接返回 `session/steer-unavailable`（诚实拒绝，无注入点）。
- 引擎 `ChatService.StreamWithEventsAsync` → `QueryLoopMiddleware`（`modules/cognition/brain/context/services/chat/core/QueryLoopMiddleware.cs`）是工具循环：`while (totalToolCalls < 128)`，每次迭代 = 一次 LLM 请求（step）。
- 每步开头已有「可 drain 的注入」模板：`_notificationHandler.ProcessPendingNotificationsAsync(ct)`（`:125`）→ `_contextManager.AddUserMessageAsync(...)`（`BackgroundNotificationHandler.cs:39-53`）；步首 `_contextManager.GetMessageListAsync`（`:127`）即取到新注入消息。
- 结束条件（`:157-160`）：本步无工具调用且有正文 → `break`（**没有 steer 检查**，即模型最后一步会直接收尾）。
- `ChatMiddlewareContext.SessionId` 可用（`:84`），管道/`IChatContextManager` 均按会话隔离；子代理通道 `SubAgentEventChannel`（`:116`）证明「轮内通道挂 context 显式传递」是本仓既有模式（异步迭代器内禁用 AsyncLocal）。
- 宿主 `IJccChatSession`（`native_gui/.../hosting/IJccChatSession.cs`）只有 `StreamAsync`，无主会话注入方法。

## 决策

**新增按会话寻址的 steer 邮箱（Actor 邮箱模型，消息传递替代共享锁）：宿主受理 `mode:"steer"` 时投递；引擎在每步开头 drain 并作为该步输入落为轮内 `user/message` 事件。**

1. **抽象**：`ISteerInputMailbox`（`modules/abstractions/...`）：`Enqueue(sessionId, text)` / `HasPending(sessionId)` / `DrainAll(sessionId)`（FIFO，线程安全，无锁竞争——`ConcurrentDictionary<string, ConcurrentQueue<string>>`，空队列即移除键）。
2. **引擎消费点**：`QueryLoopMiddleware` 步首（紧邻 `ProcessPendingNotificationsAsync`）drain 本会话邮箱 → 逐条 `_contextManager.AddUserMessageAsync(text, MessageOriginKind.SteerInput, ct)`，并 `yield return` 新事件 `ChatStreamEventType.SteerInput`（携带文本）供宿主投影。
3. **轮次延续**：`:157` 的收尾分支加「`HasPending` 则 `continue`」——对齐 DSH `next-step 非空不结束轮次`。
4. **枚举扩展**：`MessageOriginKind` 增 `[EnumValue("steer")] SteerInput`（唯一数据源，源码生成消费）；`ChatStreamEventType` 增 `[EnumValue("steerInput")] SteerInput`。
5. **宿主路由**：
   - `session/prompt mode:"steer"`：轮次运行中 → 投递邮箱并返回 `{accepted:true}`（**不再** `steer-unavailable`）；**无运行轮次 → 退回正常开轮**（等价 DSH 空闲时 steer 会唤醒一次 turn）。
   - `session/updateQueue action.kind:"steer"`（把排队项升级为轮内注入）：从队列移除 + 投递邮箱；未运行或队列项不存在仍按 DSH 语义报错（`session/steer-unavailable` / `session/queue-item-not-found`）。
   - 宿主观察引擎 `SteerInput` 事件 → 落 `user/message`（`surfaceOp:"append"`，新 `Id`，`Source.Kind="user"`），位置即步首，**仍在当前 turn 内**。
6. **契约扩展**：`IJccChatSession` 增注入方法（如 `bool TrySteerInput(string sessionId, string text)`），实现从会话服务解析邮箱；不改变 `StreamAsync` 签名（一轮独占语义保留）。

## 替代方案（考虑过但放弃）

- **宿主侧只入队、由宿主自己发 `user/message`**：事件位置在「收到 steer 那一刻」，可能落在某步中途（工具执行中），与 DSH「步首 append」不符，且无法让 LLM 在下一步看到（引擎不消费队列就永远看不到）。
- **`IChatMiddleware` 中间件实现**：中间件包裹整个 QueryLoop，只能在轮首/轮尾各跑一次，插不到步循环内部（实测包裹边界）。
- **改 `IChatService.StreamAsync` 签名传 Channel**：把轮内注入变成流调用参数，破坏「一轮一次调用」的现有抽象，且 CLI/TUI/子代理所有消费方都要改。
- **复用 `IAgentNotificationQueue`**：那是全局单例、内容被包成任务通知 XML 且 `MessageOriginKind.TaskNotification`，无法表达「人类 steer 输入」与按会话寻址。
- **`AgentInputForwardQueue`（子代理输入转发的 `Channel<string>`）**：语义是转发给**子代理**且只在回合构建阶段 drain（`AgentBase.cs:514`），主会话不可用。

## 影响面

- 新增：`ISteerInputMailbox` 抽象 + 实现 + DI 注册；`MessageOriginKind.SteerInput`；`ChatStreamEventType.SteerInput`。
- 修改：`QueryLoopMiddleware`（步首 drain + 结束条件）、`DshTurnRunner`/`DshRpcDispatcher`（steer 路由）、`JccChatSession`（注入方法）、宿主 `SteerInput` 事件投影。
- 测试：引擎单测（步首注入进下一次请求 / 有注入时不收尾）；宿主单测（运行中 steer 受理 + 未运行回落 / updateQueue 升级）；E2E `dsh-webui-steer.mjs`（桩服务器按注入文本应答，断言轮内 `user/message` + 单轮 + 下步请求含注入文本）。
- 与 `assistant-stream`（逐字流式）无关，两者独立，可分别落地。
