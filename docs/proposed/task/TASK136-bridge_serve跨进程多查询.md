# TASK136 — bridge_serve 跨进程多查询（ADR 0165 阶段 2 剩余）

> 承接 [ADR 0165](../../proposed/adr/0165-everything-is-background-query-identity.md) 第 80 行裁决：
> 「阶段 0 的 bridge_serve 仍保持单活跃回合，只保证 queryId 不丢、能回显；多查询并发的跨进程行为留到阶段 1」。
> 台账出处：[TASK135](TASK135-B2查询并发缺陷台账.md) 批次 D 的 D2（`- [~]` 已裁决不在缺陷修复轮，另立任务）。

## 1. 现状（代码事实）

| 层 | 文件 | 当前形态 |
|----|------|----------|
| 协议主循环 | `modules/apps/cli/core/commands/core/BridgeServeLoop.cs:106` | `turnTask is { IsCompleted: false }` → 回 `error`「回合进行中，本次提交已忽略」 |
| 回合槽 | 同上 `turnCts` / `turnTask` / `currentQueryId` | **单槽**，新提交覆盖旧槽前必先判定忙 |
| 事件消费 | `modules/apps/engine/adapters/BridgeEventConsumer.cs` | 实例持有 `_fullResponse`/`_thinking*`/`_ackSeq`/`_queryId`，**单实例 = 单回合** |
| 组装 | `BridgeServeCommand.cs:71-79` | `controller ??= new SessionController(...)` + 单个 `mainAgent`，**首次提交后复用** |

对照进程内宿主（已支持多查询）：`EngineSession.cs:76/148` — 会话级 `sessionObjectId` 共享，
**每查询新建 `SessionController` + `CreateMainAgent`**，consumer 来自该查询自身。

## 2. 目标与不变量

目标：同一 bridge 进程内可并行承载多条查询，事件按 queryId 归因，与进程内宿主同语义。

1. **提交不再因忙而拒绝**：任意时刻可提交新查询；旧文案「回合进行中」删除。
2. **唯一拒绝情形是同 queryId 并发复用**（身份冲突 → 事件无法区分），回 error 诱导提示。
3. **每查询独立消费器**：`BridgeEventConsumer` 实例按查询新建，`_fullResponse`/`thinking` 状态不串台。
4. **定向中断**：`interrupt{queryId}` 只取消该查询；无 queryId 的旧客户端语义 = 取消全部在途（原「取消唯一回合」的退化）。
5. **退出收尾**：`quit`/`Eof`/外部取消 → 取消全部在途查询，各自发 `turn_interrupted` 后退出（顺序不承诺）。
6. **seq 幂等不变**：同 `seq` 重复提交仍只处理一次（`lastProcessedSeq`）。
7. **`background_task` 仍是会话级事件**：不打 queryId（既有语义）；泵绑定独立会话级消费者，
   不再借用某个回合的 ackSeq（多回合下「借用」本身无定义）。

## 3. 施工顺序（先红后绿）

1. 红测试：`test/unit/host.tests/bridge/BridgeServeMultiQueryTests.cs`（4 条）+
   改写 `BridgeServeLoopTests.SubmitWhileBusy_EmitsErrorWithInductiveMessage` → `SubmitWhileBusy_StartsSecondQuery`。
2. 实现 `BridgeServeLoop`：`turnCts/turnTask/currentQueryId` 单槽 → `ConcurrentDictionary<string, TurnRun>`；
   每查询 `new BridgeEventConsumer(synced)`；`RunTurnAsync` 终态自摘除并释放 Cts。
3. 实现 `BridgeServeCommand`：`controller ??=` → 每回合新建控制器 + 每查询 `CreateMainAgent`（会话级 ObjectId 共享）。
4. 全量 `host.tests` 回归 + 风格门禁。

## 4. 判据（可执行）

| 场景 | 期望 |
|------|------|
| 连发两条不同 queryId 的 submit，两条都阻塞 | 两条 `turn_started`，都执行到终态，零 `error` 行 |
| 同 queryId 二次提交 | 一条 `error`（含该 queryId），在途查询不受影响 |
| 两条在途，`interrupt{q-2}` | 仅 q-2 `turn_interrupted`；q-1 正常 `turn_completed` 或按 EOF 收尾 |
| 两条在途，`quit` | 两条各自 `turn_interrupted` |
| 两查询各写正文 | `assistant_message` 各归其 queryId，无交叉文本 |

## 5. 明确不做

- 不做 SDK/NDJSON 对外协议扩展（那是 D1，独立任务）。
- 不做 TUI 侧改动：`BridgeClient`/`ReplShell` 的多查询提交已在 TASK130-B2 阶段 1 完成，
  本任务是**服务端不再拒绝**。
- 不改 `BridgeEvent`/`BridgeRequest` 契约（queryId 字段阶段 0 已贯通）。

## 6. 落地记录

### 6.1 改动

| 文件 | 改动 |
|------|------|
| `BridgeServeLoop.cs` | `turnCts/turnTask/currentQueryId` 单槽 → `ConcurrentDictionary<string, TurnRun>`；每查询 `new BridgeEventConsumer`；`RunTurnAsync` 终态自摘除 + 释放取消源；`CancelAllQueriesAsync` 统一收尾；`background_task` 改绑会话级消费器（不再借用回合 ackSeq） |
| `BridgeServeCommand.cs` | `controller ??=` → 每回合新建 `SessionController` + 每查询 `CreateMainAgent`（会话级 `ObjectId` 共享，对齐 `EngineSession`） |
| `BridgeServeLoopTests.cs` | `SubmitWhileBusy_EmitsErrorWithInductiveMessage` → `SubmitWhileBusy_StartsSecondQuery`（旧客户端无 queryId → loop 生成两个身份） |
| `BridgeServeMultiQueryTests.cs`（新） | 并发双提交 / 同 queryId 拒绝 / 定向中断 / quit 取消全部 —— 4 条 |

### 6.2 单测证据（`Host.Tests` 1387 通过 0 失败）

- `TwoSubmits_Concurrently_BothRunToCompletion` — 两条 `turn_started` + 两条 `turn_completed`，
  零 `error`，`assistant_message` 正文各归其 queryId（消费器隔离）。
- `DuplicateQueryId_IsRejected_WithoutTouchingRunningQuery` — 仅一条 error（含「已在执行」+ 诱导
  「interrupt」），在途查询未被扰动。
- `Interrupt_TargetsOnlyMatchingQuery` — `q-slow` 一条 `turn_interrupted`；`q-fast` 一条 `turn_completed`。
- `Quit_CancelsAllActiveQueries` — 两条 `turn_interrupted`，queryId 齐全。
- `SubmitWhileBusy_StartsSecondQuery` — 旧客户端（无 queryId）连发两条：零 error，两个不同生成身份。

### 6.3 真机验证（ADR 0080）

**A. 并发提交不再被拒**（`printf` 两条 submit + quit，`jcc bridge_serve`）：

```
{"type":"turn_started","queryId":"q-e2e-1"}
{"type":"turn_started","queryId":"q-e2e-2"}
{"type":"turn_interrupted","queryId":"q-e2e-2"}
{"type":"turn_interrupted","queryId":"q-e2e-1"}
```

**B. 两条查询真跑到终态**（stdin 保持 30s 再 quit，`--model nonexistent-model-xyz` 让 LLM 快速 401，
无需真实计费）：

```
{"type":"turn_started","queryId":"q-run-1"}
{"type":"turn_started","queryId":"q-run-2"}
{"type":"error","message":"回合执行失败：[LLM.StreamingChatCompletion] HTTP 401: Authorization Not Found","queryId":"q-run-2"}
{"type":"error","message":"回合执行失败：[LLM.StreamingChatCompletion] HTTP 401: Authorization Not Found","queryId":"q-run-1"}
{"type":"turn_completed","queryId":"q-run-1"}
{"type":"turn_completed","queryId":"q-run-2"}
```

判读：两条查询**真并行**（错误行 q-run-2 先于 q-run-1 返回，串行实现不可能倒序）、
各自走完真实 `SessionController` + `ChatService` 链（401 来自真实 HTTP 调用）、归因正确、退出码 0。

### 6.4 未覆盖

- 真机「两条查询都拿到真实模型回复」需要 MockServer E2E 框架支持**并发轮次**（现有
  `ConversationScript.Turns` 是顺序模型），本轮未改该框架；并发正确性已由「401 交错 + 单测两条
  完成」覆盖，模型侧语义与并发无关。
- `interrupt` 无 queryId 的旧语义由「取消唯一回合」变为「取消全部在途」——旧客户端只会有一条在途，
  等价；已在代码注释与本文件 §2 记录。

