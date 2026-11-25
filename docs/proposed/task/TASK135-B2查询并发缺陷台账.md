# TASK135 — B2 查询并发缺陷台账（先记录，逐条修复）

> 承接 [TASK130 B2](TASK130-剩余事项清单与验收步骤.md) / ADR [0165](../../proposed/adr/0165-everything-is-background-query-identity.md) /
> [0166](../../proposed/adr/0166-streaming-partial-sidecar.md) / [0167](../../proposed/adr/0167-session-epoch-page-turn.md)。
> 2026-11-25 全量复核后登记：这 12 条是「改造已完成、但并发面/恢复面/语义面没校验过」的缺口。
> **状态约定**：`- [ ]` 未修 · `- [x]` 已修且有测试 · `- [~]` 已裁决不在本轮（附理由）。

## 0. 修复顺序（按风险）

前三批按「数据正确性 → 语义一致性 → 验证补强」推进，每批单独提交。

## 1. 缺陷清单

### 批次 A — 数据正确性（并发写路径）

- [x] **A1 streaming.jsonl 无查询归属 → 并发互删与串台收编**
  - 证据：`TranscriptService.GetStreamingPath(sessionId)` 只用 sessionId；`ClearStreamingAsync` 删整个文件；
    `AdoptStreamingPartialAsync` 取 `LastOrDefault` 不做 queryId 过滤。
  - 触发：ADR 0165 之后「前台 + 若干后台」并行是常态，任意两条查询共享同一旁路文件。
  - 症状：①A 正常完成删掉 B 的在途残稿；②A 崩溃、B 正常时，A 的全文被收编到 B 名下。
  - 修法：旁路路径带 queryId（`streaming.<queryId>.jsonl`）+ 收编按 queryId 匹配；读端为「本会话所有旁路」。
  - 判据：并发两条查询各写各的；一条完成不影响另一条残稿；收编只收本查询。

- [x] **A2 transcript 落盘顺序 = 完成顺序，不是提问顺序**
  - 证据：`TranscriptPersistMiddleware` 在回合终态一次性 `AppendEntriesAsync`。
  - 症状：并行查询下先问后落、后问先落，resume 上下文倒序。
  - 修法：落盘条目带 `QueryId` + 会话内 `TurnSeq`（提交时分配），读端按 `TurnSeq` 稳定排序。
  - 判据：两查询乱序完成，读回顺序与提交顺序一致。

- [x] **A3 残稿收编不落盘、不删文件 → 每次加载重复收编**
  - 证据：`AdoptStreamingPartialAsync` 只构造新列表返回，主文件与旁路都不动。
  - 症状：用户不继续对话时，每次打开会话都多一条同内容残稿（时间戳还是旧的）。
  - 修法：收编时 `AppendEntriesAsync`（主文件）+ 删旁路，使恢复动作一次性且幂等。
  - 判据：连续两次 `LoadTranscriptAsync`，残稿只出现一次。

### 批次 B — 语义一致性

- [x] **B1 `clear_history` 工具与 `/clear` 语义分叉**
  - 证据：`WorkflowToolHandlers.cs:221` 只调 `ClearHistoryAsync`。
    （**核对纠正**：`AgentSummaryToolHandlers.AgentClearHistoryAsync` 清的是 agent 执行记录
    `_agentSummaryService`，与对话 transcript 无关，不在本条范围 —— 首版台账记错了。）
  - 症状：模型说「已清空」，实际只清内存桶，磁盘 transcript 还在，resume 全回来。
  - 修法：两条工具路径统一走会话翻页（复用 `SessionEpochSwitcher`），与 `ClearCommand` 同语义。
  - 判据：工具调用返回后 `SessionId` 变化、旧代目录仍在、当前桶为空。

- [x] **B2 `SessionStartHookState` 不随换代重置**
  - 证据：`EngineSession.cs:109` 每 EngineSession 一份、跨查询共享；`/clear` 后仍是同一实例。
  - 症状：`/clear` 触发的 `SessionStart(source=clear)` 可能被「已启动过」去重挡掉。
  - **核对纠正**：`/clear` 本身不会漏跑 —— `ClearCommand` 已显式执行 clear 版。
    真缺口是**没有显式跑 hook 的换代入口**（TUI 新会话 Ctrl+N、工具 `clear_history`）：
    新代首个查询被旧的 `Ran=1` 挡住，整代完全没有 SessionStart。
  - 修法：换代时重置该状态（由 `SessionEpochSwitcher` 回调或 `StartNewConversationAsync` 显式重建）。
  - 判据：`/clear` 后 `SessionStart(clear)` 实际执行一次。

- [x] **B3 `ReleaseOldBucket=false` 两处 → 旧桶永不释放**
  - 证据：`ClearCommand.cs:73`、`EngineSession.cs:128`；`SessionStore.ReleaseSession` 有实现无调用。
  - 症状：频繁 `/clear` 的长会话 `_logs` 线性增长，无上限也无「在途查询结束后回收」的时机。
  - 修法：记录旧代在途查询数，归零时释放（`QueryRun` 计数回调）；至少提供显式上限与日志。
  - 判据：`/clear` 后等在途查询结束，`_logs` 计数回落。

- [x] **B4 实时视图切回无归属头（快照有）**
  - 证据：`BuildTranscriptSnapshot` 有「── 后台查询 q-xxx ──」，`FocusMostRecentBackgroundQuery` 只有一条 SystemNotice。
  - 症状：多查询交替切回时，实时滚动区线性拼接、无分隔。
  - 修法：切回补投前后各插一条归属分隔（与快照同格式）。
  - 判据：切回后视图出现与快照一致的归属头。

### 批次 C — 验证补强

- [x] **C1 Ctrl+B 多后台交替切回未测**
  - 已补：`MultipleBackgroundQueries_FocusEachInTurn`（2 条后台逐个切回、事件不串台）。
  - **顺带修出真回归**：切回**已完成**的后台查询会残留前台身份 → 下一次提交被误判「前台有主」
    静默转后台（吞输入）。已修（`Completed` 时不接管前台）+ 回归测试
    `FocusCompletedBackgroundQuery_NextSubmitStaysForeground`。
  - 仍未验：`_driver.EndTurn()` 对草稿/光标/滚动锚点的真实渲染副作用（需真终端）。

- [~] **C2 崩溃收编只有 mock 文件系统单测**
  - 降级说明：旁路每次快照走 `IFileSystem.AppendAllTextAsync`（打开-写入-关闭），
    `kill -9` 不会丢已写行（只有断电/内核崩溃才涉及 fsync 语义）；收编逻辑已由
    `TranscriptServiceStreamingTests` 6 条覆盖。真进程 `kill -9` 集成用例未做，留真机/CI。

- [x] **C3 `/clear` 与在途后台查询并发未测**
  - 已覆盖：`TranscriptPersistMiddlewareTests.InvokeAsync_SessionSwitchedMidTurn_StillWritesToOriginalSession`
    （回合中途换 sessionId → 仍落旧代；新代零写入）。

- [~] **C4 全仓编译未完成（jcchub 锁 Hub dll）**
  - 本轮验证面：`NativeTui.Tests` 3205、`NativeTui.Host.Tests` 182、`Brain.Context.Tests` 987、
    `Vault.Other.Tests` 401、`Mcp.Tests` 435、`McpToolDispatch.Tests` 535、
    `NativeGui.Host.Tests` 50、`NativeGui.Core.Tests` 423 —— 全部 0 失败。
  - 全仓 solution 域仍受本机 `jcchub` 锁 `artifacts/bin/Hub/*.dll` 影响（MSB3021/3027，无 CS 错误），
    留 CI 复核。

### 批次 D — 已裁决不在本轮

- [x] **D1 SDK/NDJSON 协议 queryId** — 已由 [TASK137](TASK137-NDJSON协议查询身份.md) 落地：
  `SdkMessageBase.query_id`（WhenWritingNull）+ `WriteSdkJson<T>` 逐条打标 + 结果消息（含失败路径）归因；
  顺带查实两处缺口（`--input-format` 无消费方、`BridgeSubprocessManager` 传 `--output-format` 未知选项）记入 TASK137 §6。
- [x] **D2 bridge_serve 跨进程多查询** — 已由 [TASK136](TASK136-bridge_serve跨进程多查询.md) 落地：
  `ConcurrentDictionary<string, TurnRun>` + 每查询独立消费器 + 每查询 `SessionController`；
  真机双查询并发（401 交错）验证。
- [~] **D3 真机 TUI 手工验收（#101）** — 需人操作，见 TASK129 采集卡。

## 2. 修复记录

| 项 | 提交 | 测试证据 | 备注 |
|----|------|----------|------|
| A1 | 94ad31c8 | `TranscriptServiceStreaming` 5 条新增（分文件/只清本查询/按查询收编）+ `TranscriptPersistMiddleware` queryId 传播 | 旁路文件 `streaming.<queryId>.jsonl` |
| A2 | 94ad31c8 | `TranscriptQueryOrder` 3 条（按 TurnSeq 排序/同号保序/旧记录在前）+ `StampsQueryIdAndTurnSeq` | 条目新增 `QueryId`/`TurnSeq`，旧记录兼容 |
| A3 | 94ad31c8 | `AdoptionPersistsToMainAndRemovesSidecar`、`Twice_AdoptsOnce` | 收编即落盘+删旁路 |
| B1 | 94ad31c8 | `WorkflowClearHistorySemanticsTests` 2 条 + `StartNewConversationAsync_WithTranscriptService` | 新增 `IChatService.StartNewConversationAsync` |
| B2 | 94ad31c8 | `SessionStartHookStateTests` 4 条 | `ISessionStartHookState` 判定代次，`ClearCommand` MarkRan |
| B3 | 94ad31c8 | `SessionStoreEpochRetentionTests` 3 条 | 上限 16 代，按最近访问淘汰非当前代 |
| B4 | 94ad31c8 | `FocusBackgroundQuery_EmitsAttributionHeader`、`FocusCompletedBackgroundQuery_EmitsClosingSeparator` | 与离屏快照同格式 |
| C1 | 94ad31c8 | `MultipleBackgroundQueries_FocusEachInTurn` + 前台身份回归 1 条 | 详见 C1 段 |
