# ADR 0147: assistant-stream 逐字流式 — 宿主合成 DSH 瞬态帧

- 状态：proposed
- 日期：2026-10-22
- 决策者：liuqihong（TASK060 B13：`session/follow` 的 `assistantStream` 恒 `revision=0`，界面上助手回复只在整段完成后才出现，与 DSH 的逐字流式不一致）

## 背景

### DSH 参考语义（权威源）

| 位置 | 事实 |
|------|------|
| `dsh-api-session-controller/lib/index.js:1451-1545` | `follow` 收到 `assistantStream:true` 时：快照必须带 `assistantStream` 基线；随后每条帧以 `{type:'assistant-stream',frame}` 推送；帧是**瞬态**（不进会话日志、不占 seq） |
| `dsh-api-session-controller/lib/types/client/transport.js:78-108` | 客户端**强校验**：快照缺 `assistantStream` → 直接抛 `gateway/internal`；每帧 `revision` 必须等于 `上一帧+1`，否则抛 `session assistant stream skipped revision` 并断开流 |
| `dsh-api-session-controller/lib/typert.host.js:2084-2089` | `SessionAssistantStreamBaseline {revision, activeAttempt?}`；`SessionAssistantStreamFrame = start{attemptId,revision,startedAfterSeq,turn,step} \| chunk{attemptId,revision,index,time,chunk} \| end{attemptId,revision,index,outcome{committed{eventType,seq}\|abandoned}}` |
| `dsh-agent-loop/lib/index.js:1050-1110` | 每次 LLM 尝试：`live.start()` → 逐 chunk `push` → 落盘 `assistant/message`/`assistant/attempt` 后 `settle()` 发 `end{committed,seq}`；失败/取消 `abandon()` |
| `dsh-llm/lib/types/assembler.js:8-20` | 组装器「容忍纯 delta 协议（无 block-start/end）」，`text-delta`/`reasoning-delta` 即可增量成块 |
| `dsh-client-ui-chat/client.js`（`updateChunk`） | 前端把 `assistant/live-chunk` 折成 `status:"running"` 的助手节点：`text-delta{index,text}`、`reasoning-delta{index,text}` 直接追加到该 index 的块 |
| `dsh-llm/lib/types/assistant-stream.js:validateRecord` | 基线里的紧凑流记录必须是 `{type:'chunk',time,chunk}`（精确键）或打包记录；重连时客户端 `expandAssistantStream` 逐条展开 |

### 本仓现状

- `DshSessionFollowSnapshot.AssistantStream` 存在但恒为 `{revision:0}`（无 `activeAttempt`），前端因此只渲染「正在生成」而无正文增量。
- `DshSessionStreamHub.PublishAsync` 只支持**持久**事件（分配 seq + 落 journal）；没有瞬态帧通道。
- `DshTurnRunner.RunTurnAsync` 已按 delta 收到引擎的 `Content`/`Thinking` 事件（`:129-133`），但只累加，整轮结束才落一条 `assistant/message`（`:150`）。
- 引擎的 step 粒度（一次 LLM 请求 = 一个 step）目前被宿主压平：所有工具事件与最终回复都写 `Step=1`，一条 `assistant/message`。

## 决策

**宿主在 `DshSessionStreamHub` 内维护每会话的 assistant-stream 状态机，由 `DshTurnRunner` 把引擎 delta 翻译成 DSH 瞬态帧。**

1. **帧合成**（`DshTurnRunner`）：
   - 本轮首个 `Content`/`Thinking` delta → `start`（`startedAfterSeq` = 当时最后一个持久 seq）；
   - `Thinking` delta → `chunk{index:0, chunk:{type:'reasoning-delta',index:0,text}}`；`Content` delta → `chunk{index:1, chunk:{type:'text-delta',index:1,text}}`（与最终 `assistant/message` 的「reasoning 在前、text 在后」块序一致）；
   - `assistant/message` 落盘后 → `end{outcome:{kind:'committed',eventType:'assistant/message',seq:<该事件 seq>}}`；
   - 取消 → `end{abandoned}`；异常 → `end{abandoned}`（不伪造 committed）。
2. **状态机**（hub）：每会话 `revision` 单调递增（**永不重置**，跨尝试、跨重连），`activeAttempt{attemptId,startedAfterSeq,turn,step,nextIndex,stream[]}` 只存原始 `{type:'chunk',time,chunk}` 记录（不打包，正确优先于体积）。
3. **快照基线**：`session/follow` 打开时把当前 `{revision, activeAttempt?}` 放进快照的 `assistantStream`；订阅者从 `revision+1` 起的帧继续收（满足客户端密度校验，重连能重建未完成文本）。
4. **传输**：帧以 `{type:'assistant-stream',frame}` 经同一 WebSocket 发；**不落 journal、不分配 seq**（瞬态语义，重放以 `assistant/message` 为准）。
5. **粒度**：一次「一轮」（turn）一个 attempt。引擎的 step 折叠属于既有投影约定，流式帧随该约定（后续若要 step 级流式，改为按 `Complete` 事件切 attempt，协议不变）。

## 备选方案

| 方案 | 不采用原因 |
|------|-----------|
| 让引擎直接产出 DSH chunk 协议 | 引擎是自有抽象（`ChatStreamEvent`），把宿主协议钉进引擎会污染领域层，且引擎 AOT 序列化与 DSH zod 语义不一致 |
| 前端只显示「正在生成」不显示增量 | 就是本次要修的缺陷；DSH 前端已内置 `assistant/live-chunk` 渲染，白给的能力不用 |
| 把帧落进会话日志（占 seq） | 违反 DSH 瞬态定义：重连基线已经用 `activeAttempt.stream` 重建，落盘只会让历史投影出现重复/半截消息 |
| 用 `block-start`/`block-end` 包裹 delta | 组装器明确容忍纯 delta，多两类帧只增加密度校验失败面，无收益 |
| 在 Web 层单独开一条流 | 前端只认 `session/follow` 的多态帧（同一 streamId），另开流前端拿不到 |

## 后果

- 正向：界面逐字出字；中途刷新/重连能看到已生成的部分文本且不重复；`end.seq` 让前端把瞬态行替换成持久消息（不残影）。
- 负向：`tool-call` 的流式参数仍未合成（工具卡片继续由持久 `tool/call` 驱动），已记录为遗留项。
- 约束：帧 `revision` 必须严格 +1（客户端强校验，跳号直接断流）；`assistant/message` 必须先落盘再发 `end`（`seq` 是替换锚点）。
