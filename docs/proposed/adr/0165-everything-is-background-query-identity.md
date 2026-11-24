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

## 阶段 0 留下的已知缺口（阶段 1 前置条件，必须解决后才可放开并发）

阶段 0 让「提交层」具备并发能力（`EngineTurnClient` 不再忙时拒绝、事件可归因），但**引擎执行层仍假设单活跃查询**：

- `EngineSession.cs:70` 每个会话只 `CreateMainAgent` 一次，该 `AgentBase` 实例被所有 `SessionController` 共享
  （`EngineSession.cs:111` 每查询新建 controller，但 `mainAgent:` 传的是同一实例）。
- `SessionController.cs:232` 执行前写 `_mainAgent.CurrentInput = input`，随后 `ExecuteStreamAsync()`；
  `AgentBase` 还有可变的 `_context`、`ChatHistory`、`Status`、`Output`（`agents_fork/AgentBase.cs`）。
- 两条查询真的并发执行时会互相覆盖 `CurrentInput`、交错写 `ChatHistory` —— 事件归因正确但**内容错误**。

当前不可达：`ReplShell` 忙时仍把输入排入 `_queuedInputs`，生产路径一次只跑一条查询；
只有单元测试（注入假执行器）会并发。所以阶段 0 不为它加投机性串行化代码，
但**阶段 1 把 ReplShell 改成并发提交之前，必须先给每条查询独立的 agent 实例**（复用
`EngineAgentFactory.CreateMainAgent`，历史仍走共享 `IChatContextManager`），或引入等价的 per-query 执行槽。

未做「先加锁串行化」的理由：那条路径今天没有任何生产消费者，加了也无法验证是真生效还是被绕开
（本项目已两次栽在「声明了、有实现，但生产链根本不走」上）。

## 后果

- 正面：
  - 查询身份成为一等公民，Ctrl+B 后续只做视图切换，不需要 abort/续跑。
  - 事件天然带归因，前台可异步订阅任意 task，后台查询不再依赖单一回合槽。
  - bridge 双端契约同步扩展，保留旧对端兼容。
- 代价：
  - `IReplEngine` 签名变更，需要同时升级 9 个实现/装饰器与测试 fake。
  - `EngineTurnClient`、`ChatService`、`SessionController` 的状态模型改动较大，必须靠红测试逐项锁定。
  - bridge_serve 阶段 0 仍单活跃回合，跨进程多查询要等阶段 1，不要误判为已完成。
- 下一步（阶段 1）：task 登记/聚焦视图、Ctrl+B 纯切换、独立 transcript；阶段 2：SDK/NDJSON 协议与逐 token 落盘。
