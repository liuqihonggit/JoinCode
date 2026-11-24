# ADR 0165: 万物皆后台 —— 查询身份与事件归因地基（TASK130-B2 阶段 0）

- 状态: accepted（2026-11-24 决策；2026-11-25 阶段 0 实现完成并全绿）
- 日期: 2026-11-24（决策）/ 2026-11-25（实现）
- 决策者: 用户主导（明确纠偏：Ctrl+B 不应 abort+续跑，查询从第一刻就是后台 task，前台只是异步订阅视图）
- 关联: [DSG054 §9](../design/DSG054-主会话后台化Ctrl+B设计.md) · [ADR 0150](0150-bridge-serve-protocol-reliability-guarantees.md) · [ADR 0143](../implemented/adr/architecture/0143-engine-library-multi-host-architecture.md)
- 影响面: bridge 双端协议、IReplEngine 全部实现/装饰器、native_tui 宿主回合槽、ChatService、SessionController

## 背景

DSG054 经多轮取证后，裁决从「abort 前台查询 + 启动独立续跑」改为「万物皆后台」：
查询从第一刻起就是一个有身份的 task；前台不拥有查询，只订阅当前聚焦 task 的事件流。
Ctrl+B 因此应是纯视图切换，不 abort、不重跑、不重复计费。

当前代码还不具备这个模型的最小前提：

1. `IReplEngine.Submit` 返回 `void`，调用方拿不到本次查询身份。
2. `BridgeEvent` 只有 `ackSeq`（ADR 0150 的请求流水号），没有 queryId；事件无法归因到具体查询。
3. `EngineTurnClient` 只有一个 `_turnCts` / `_turnTask`，忙时直接拒绝第二次提交。
4. `EngineTurnClient` 只有一个 `InProcessEventConsumer`；`_fullResponse`、`_thinkingContent`、`_messageId` 等状态跨回合复用，并发时会串台。
5. `Interrupt()` 没有目标身份，只能取消「当前唯一回合」。
6. `ChatService` 作为 Singleton 持有 `_conversationTurn` 与 `_toolUseContext`；`SessionController` 也有十余个回合级实例字段。
7. bridge 协议是双端各自定义 DTO 的镜像（`native_tui/.../BridgeProtocol.cs` ↔ `modules/apps/engine/bridge/BridgeProtocol.cs`），必须同步扩展，不能只改一侧。

这些不是 UI 层问题，而是查询身份地基缺失。阶段 0 只补地基，不做 Ctrl+B 键位、不做前台聚焦、不做独立 transcript。

## 决策

### 1. queryId 是字符串身份，不是序号

- queryId 为会话内唯一非空字符串，建议形态 `q-<guid>`。
- 由提交侧提供；调用方未给时由实现生成并返回。
- `seq`/`ackSeq` 继续表示**请求可靠性关联**（ADR 0150），queryId 表示**查询生命周期身份**。两者正交，不能互相替代：
  - 同一 queryId 可因重试对应多个 seq；
  - 同一 seq 最多启动一个 queryId（幂等仍由 `lastProcessedSeq` 负责）。

### 2. IReplEngine 契约改为「提交返回身份、打断指定身份」

```csharp
string Submit(string text, IReadOnlyList<BridgeImageAttachment>? images = null, string? queryId = null);
void Interrupt(string? queryId = null);
```

- `Submit` 返回生效 queryId；调用方可忽略返回值，旧调用点的语法零破坏。
- `Interrupt(null)` 兼容旧语义：取消当前前台/唯一查询；阶段 1 接入 focus 后由 `ReplShell` 传显式 queryId。
- 全部实现与装饰器必须把 queryId 透传到底层，不得在新包装层丢身份：
  `EngineTurnClient`、`BridgeEngine`、`BridgeClient`、`LazyEngine`、`SimulatedEngine`、
  `ClarifyModeEngine`、`CommandPickerEngine`、`PromptTransparencyEngine`、测试 `FakeReplEngine`。

### 3. BridgeEvent / BridgeRequest 增加可选 queryId

双端 DTO 都在抽象基类增加：

```csharp
[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
public string? QueryId { get; init; }
```

规则：

- `submit` / `interrupt` 请求带 `queryId`，让对端明确知道目标查询。
- 由某次查询产生的 `turn_started`、`thinking_*`、`tool_*`、`token_delta`、`assistant_message`、`token_usage`、
  `turn_completed`、`turn_interrupted`、`retry`、`context_usage`、`compact_completed`、该查询触发的 `error`
  必须带同一 queryId。
- 会话级事件（`session_info`、`permission_mode_changed`、`background_task`、非查询触发的 `system_notice`）
  不带 queryId；`background_task` 已有自己的 `TaskId`。
- 旧对端不发 `queryId` 时反序列化为 null，不报坏行；TUI 把它视为 legacy 单查询流。
- JSON 形状由双端契约测试锁定；禁止手写 JSON 拼接（沿用 ADR 0141/0150 约束）。

### 4. EngineTurnClient 从单回合槽升级为 queryId 映射

- 用 `Dictionary<string, QueryRun>` 取代单 `_turnCts` / `_turnTask`；`QueryRun` 持有：
  queryId、CancellationTokenSource、Task、per-query consumer。
- 第二次 `Submit` 不再因忙而拒绝；它创建另一条查询。旧错误文案「回合进行中，本次提交已忽略」在阶段 0 删除。
- 每条查询独立 `InProcessEventConsumer`，不再共享可变 `_fullResponse` / `_thinkingContent` / `_messageId`。
- `EngineTurnClient` 发出的生命周期事件、以及 consumer 发出的内部事件，都必须带 queryId。
- `IsTurnRunning` 改为「存在任一未完成查询」；新增 `IsQueryRunning(queryId)` 供测试与阶段 1 前台视图使用。
- `Dispose` 遍历所有 run 取消并释放，顺序不承诺；阶段 1 的 LIFO 聚焦切换不在此决策范围。

阶段 0 的 bridge_serve 仍保持单活跃回合，只保证 queryId 不丢、能回显；多查询并发的跨进程行为留到阶段 1。

补充两条实现期确定的边界（阶段 0 已落地）：

- **定向取消在 bridge_serve 同样成立**：`interrupt` 携带的 queryId 与当前活跃查询不一致时视为
  过期/他人的打断，不取消（旧实现无条件 Cancel，迟到的 Esc 会杀掉刚提交的新查询）。
- **唯一仍拒绝提交的情形是同 queryId 并发复用**：身份冲突会让两条查询的事件无法区分。
  拒绝必须给可执行诱导（告知占用者、给出 Interrupt(...) 与换新 id 两条出路），
  「回合进行中」不再作为拒绝理由。

### 5. ChatService / SessionController 回合状态作用域化

- `ChatService` 新增 `ChatQueryScope`（AsyncLocal 作用域）：
  - `QueryId`：当前查询身份。
  - `ToolUseContext`：当前查询专属；每个 scope 新建，不再 Singleton 共享。
  - `ConversationTurn`：**不是** scope 内部从 0 自增，而是查询开始时从 ChatService 的会话级计数器
    `Interlocked.Increment` 原子分配后注入 scope。原因：轮次语义是「会话里的第几轮」，
    scope 内部归零会让每个并发查询都自称第 0 轮，首次注入类中间件会重复触发。
- 三个 Send 方法与 `StreamWithEventsAsync` 在进入管道前进入 scope，`finally` 释放。
- `FileReadTracker` 从「持有构造时注入的固定 ToolUseContext」改为「读取当前 scope 的 ToolUseContext」；
  无 scope 时 no-op，避免后台全局污染。
- `SessionController` 不是 Singleton：阶段 0 采用**每查询新建控制器实例**承载回合级状态
  （`_hasSubstantiveOutput`、`_lastStopReason`、`_lastDoneUsage`、`_turnToolCalls` 等），
  比把十余字段搬进 AsyncLocal 更直接、风险更低；功效等价于 per-query TurnState。
  唯一会话级状态 `SessionStart` hook 标记外置为共享 `SessionStartHookState`，避免每次查询重复触发。
- `ChatQueryScope` 借 `JoinCode.Abstractions.Utils.AsyncLocalScope<T>`，不新增手写 AsyncLocal 保存/恢复样板；
  `SessionController` 不需要 AsyncLocal。

### 6. 兼容策略

- 源码兼容：所有 `Submit(text)` / `Submit(text, images)` 调用方继续编译，可忽略返回值。
- 二进制/契约兼容：queryId 可选；旧 JSON 行继续可解析。
- 行为兼容：单查询路径的事件顺序、忙碌提示之外的文案、权限模式回执、会话清理串行化均不变。
- 明确不兼容：旧实现 `void Submit` 无法满足新接口，必须随本次改动统一升级；测试 fake 同步升级。

## 考虑过的替代方案

| 方案 | 放弃原因 |
|------|----------|
| 沿用 `ackSeq` 当查询身份 | seq 是请求流水号，重试会一个查询多个 seq；且仅 bridge 路径有，进程内路径无 |
| 只给 `TurnStarted` 加 queryId，其它事件不带 | token/tool/assistant 事件无法归因，前台订阅视图必然串台 |
| `Submit` 不改签名，另加 `SubmitWithQuery` | 形成两条提交路径，身份可能只挂在其中一条；违背「查询生来有身份」 |
| 继续单回合槽 + 忙时拒绝 | 与万物皆后台冲突；Ctrl+B 只能 abort，必然打断且重复计费 |
| 多查询共用 InProcessEventConsumer | `_fullResponse`/`_thinkingContent` 会并发串写，事件归因表面正确但内容错误 |
| 阶段 0 同时做 Ctrl+B 键位与 transcript | 视图层依赖稳定身份地基；先做会返工 |

## 红测试先行（本 ADR 的验收门禁）

1. `BridgeProtocolTests`（native_tui）：
   - `submit` 带 queryId 序列化；`turn_started` 带 queryId 反序列化；旧 JSON 不带 queryId 仍兼容。
2. `BridgeJsonContractTests`（host.tests）：
   - jcc 侧 DTO 镜像同样收发 queryId，防双端漂移。
3. `EngineTurnClientTests` / 新增查询身份测试（NativeTui.Host.Tests）：
   - `Submit` 返回 queryId，事件带同一 queryId。
   - 两个 queryId 并存，per-query consumer 文本不串台。
   - `Interrupt(queryIdA)` 只取消 A，B 正常完成。
4. `ChatQueryScopeTests`（Brain.Context.Tests）：
   - 不同 queryId 的 scope 内 ToolUseContext 不共享；嵌套退出后恢复外层 scope。

当前状态（2026-11-25）：红测试先落（四项目分别在新增 API/属性处编译失败），实现后四组全绿：

| 项目 | 结果 |
|------|------|
| `NativeTui.Tests` | 3193 通过 / 0 失败 |
| `Host.Tests` | 1379 通过 / 0 失败 |
| `NativeTui.Host.Tests` | 177 通过 / 0 失败 |
| `Brain.Context.Tests` | 967 通过 / 0 失败 |

实现期修正与旧断言处置（均记录在案，避免被误读为「测试被改绿」）：

- 旧断言「忙时提交拒绝并回 Esc 诱导」的前提被本 ADR 废除，改写为「同 queryId 并发复用被拒 + 诱导可执行」。
- 协议字节断言从「与无 queryId 旧形态字节等价」改为「必带 queryId」；`images` 为空时仍不带字段。
- 模拟引擎测试改为用 `Submit` 返回值断言 `TurnStarted.QueryId`，把归因承诺变成可执行契约。

## 阶段 0 留下的已知缺口（2026-11-25 已解决）

阶段 0 让「提交层」具备并发能力（`EngineTurnClient` 不再忙时拒绝、事件可归因），但当时**引擎执行层仍假设单活跃查询**：

- `EngineSession` 每个会话只 `CreateMainAgent` 一次，该 `AgentBase` 实例被所有 `SessionController` 共享；
  `SessionController` 执行前写 `_mainAgent.CurrentInput = input`，`AgentBase` 还有可变的
  `_context`、`ChatHistory`、`Status`、`Output`。两条查询真并发会互覆输入、交错写历史。

已按「per-query 主代理」修复：

- `EngineSession` 把会话身份（`ObjectId`）与主代理实例分开：`ObjectId` 跨查询共享，
  主代理在回合执行器内**每查询 `EngineAgentFactory.CreateMainAgent` 新建**。
  会话历史仍走共享 `IChatContextManager`，不随 agent 实例分裂。
- 连带修正通知路由：`SessionController` 的 `IterationStartInjector` 原按 `_mainAgent.UniqueId`
  排空后台通知；主代理每查询新建后 `UniqueId` 每回合都变，会让上一回合启动的后台 agent
  完成通知落进无人消费的桶。改为按顶层消费者排空（`DrainFormatted()`，null = 所有目标桶），
  与接口文档「null 表示顶层消费者」和 `HasPendingIterationInput` 的全局判定一致。

仍未做「先加锁串行化」的替代路线，理由不变：锁门禁只减不增，且串行会让 Ctrl+B 后提交的新查询
长时间静默等待，与「万物皆后台」目标相反。

## 阶段 1：前台只做订阅切换（Ctrl+B，2026-11-25 已落地）

`ReplShell` 从「持有唯一回合」变为「持有前台身份 + 若干后台桶」：

- `_foregroundQueryId`：当前前台订阅的查询身份（视图只渲染它的事件）。
- `_backgroundQueries`（`List<BackgroundQuery>`）：每条后台查询一个桶，`Pending` 按到达顺序
  留事件，`Completed` 标记终态。桶是**视图缓冲**，不是执行槽 —— 查询本身在引擎侧独立运行。
- `DrainEngine` 首行 `BufferIfBackgroundQuery` 拦截：事件归因到已知后台桶则只入缓冲、不渲染；
  命中终态时记完成并提示「后台查询 q-x 已结束 — ctrl+b 切回查看（N 条事件待显示）」。
- `Ctrl+B` 一个键两个互斥语义：前台忙 → 转后台（`_driver.EndTurn()` + 释放前台身份 + 定格已渲染内容，
  不调用 `Interrupt`）；否则 → 切回最近的后台桶（未完成先 `BeginTurn`，再按序 `_driver.Apply` 补投）。

键位分层按基准 `schema.ts:48`：`task:background` 属 `Task` 上下文（默认 `ctrl+b`），`Chat` 上下文
的 `ctrl+b` 仍是编辑域光标左移。故 `HandleCtrlKey` 在全局单键分派**之前**插入判断，且条件为
`_driver.IsBusy || _backgroundQueries.Count > 0` —— 闲且无后台查询时不接管，保住既有编辑行为。
`DispatchCtrlAction` 同步新增 `TaskBackground` case（原先无此分支，走不到）。

两条实现期才暴露的坑（都已用测试锁定）：

- **前台身份必须在终态释放**。首版只在转后台/切回时改 `_foregroundQueryId`，回合自然结束时不清，
  于是下一次提交误判「前台仍有主」被吞掉 —— 9 条既有提交测试（`IdleReturnDialogTests`、
  `ReplShellWiringInputTests`）同时红，症状是「输入框清空了但引擎没收到」。
- **不能靠 `??=` 静默丢弃**。首版 `_foregroundQueryId ??= _engine.Submit(...)` 在同一拍内连续提交
  （或终态未到）时会把新查询直接扔掉。改为「总是提交 → 前台有空位则接管 → 否则注册为后台查询并提示」。
  丢输入比多一条后台查询糟得多。

验收：`NativeTui.Tests` 3199 通过 / 0 失败（新增 `QueryBackgroundTests` 5 条：提交接管前台、
Ctrl+B 不打断、后台事件只入缓冲、后台完成不抢前台 busy、闲时 ctrl+b 仍是光标左移）；
`NativeTui.Host.Tests` 178 通过；`Verify-StyleGate.py` 1817（新文件零违规）、
`Verify-NoLockPrimitives.py` 64 处存量（未加锁）、`Verify-ExplicitOrder.py` /
`Verify-RegistrationOrder.py` 与真机装配一致。

## 阶段 1b：transcript 按 queryId 归属（2026-11-25 已落地）

阶段 1 只解决了「渲染分流」，没解决「记录完整性」：没被切回的后台查询，事件全躺在
`BackgroundQuery.Pending` 里，ctrl+o 的 transcript 是主视图投影 —— 看不到就等于永久丢失。

补法（`ReplShell.ProjectBackgroundQueries`）：进入 transcript 拍摄快照时，对每个 `Pending` 非空的桶
用**离屏 driver** 重放事件，再把结果交给 `TranscriptLines.Project` 投影，前置一行归属头
`── 后台查询 q-xxx（运行中/已完成，N 条事件）──`。

两条设计约束：

- **不另写一套文本投影**。离屏 `ReplView` + `SpinnerItem` + `BannerItem` 构造一个临时 `ReplBridgeDriver`，
  事件走与主屏完全相同的 `Apply` 路径 —— 渲染实现只有一份，字段格式不会两处漂移。
- **靠 `Pending` 清空天然去重**。切回时 `FocusMostRecentBackgroundQuery` 已经 `Pending.Clear()` 且把事件
  补投进主视图，所以「已切回的桶不再进 transcript」不需要额外标记位；无后台桶时零追加（不多空行）。

验收：`NativeTui.Tests` 3202 通过 / 0 失败，新增 `TranscriptQueryAttributionTests` 3 条
（后台内容可见 + 带归属头、切回后不重复、无后台零破坏）。

## 后果

- 正面：
  - 查询身份成为一等公民，Ctrl+B 后续只做视图切换，不需要 abort/续跑。
  - 事件天然带归因，前台可异步订阅任意 task，后台查询不再依赖单一回合槽。
  - bridge 双端契约同步扩展，保留旧对端兼容。
- 代价：
  - `IReplEngine` 签名变更，需要同时升级 9 个实现/装饰器与测试 fake。
  - `EngineTurnClient`、`ChatService`、`SessionController` 的状态模型改动较大，必须靠红测试逐项锁定。
  - bridge_serve 阶段 0 仍单活跃回合，跨进程多查询要等阶段 1，不要误判为已完成。
- 下一步（阶段 2）：transcript 按 queryId 归属、独立 transcript 视图、SDK/NDJSON 协议与逐 token 落盘；
  `bridge_serve` 跨进程多查询仍未放开（阶段 0 遗留）。
