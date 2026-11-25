# TASK137 — NDJSON/SDK 协议查询身份（ADR 0165 阶段 2 剩余 D1）

> 承接 [ADR 0165](../../proposed/adr/0165-everything-is-background-query-identity.md) 第 236 行阶段 2 清单：
> 「transcript 按 queryId 归属、独立 transcript 视图、**SDK/NDJSON 协议**与逐 token 落盘」。
> 台账出处：[TASK135](TASK135-B2查询并发缺陷台账.md) 批次 D 的 D1。

## 1. 现状事实（改造前）

| 观察点 | 事实 |
|--------|------|
| NDJSON 出口 | `CliEventConsumer.WriteSdkJson<T>` 是**唯一**序列化点，23 个调用；消息基类只有 `uuid` / `session_id` |
| 查询身份来源 | `ChatQueryScope.Enter()` 生成 `q-{guid}`（`ChatQueryScope.cs:43`），仅引擎内部可见 |
| 结果消息 | `NonInteractiveModeRunner` 直接 `SdkResultMessage.Create(context.SessionId)`，无查询身份 |
| 参数面 | `--input-format` 在 `JccCliArg` 声明，但**全仓无解析方**（`grep InputFormat` 只命中声明与 `BridgeSubprocessManager` 的拼参） |
| 传参错配（副产物） | `BridgeSubprocessManager.BuildArgumentList` 传 `--output-format stream-json` + `--input-format stream-json`；实测本仓二进制 `--output-format` 是**未知选项**，`--format stream-json` 是**非法值**（合法 `text/json/ndjson`） |

## 2. 本轮范围（D1 = 协议面查询身份）

1. `SdkMessageBase` 增 `query_id`（`[JsonPropertyName("query_id")]` + `WhenWritingNull`）—— 旧消费方零影响。
2. `CliEventConsumer.WriteSdkJson<T>` 逐条打标：取 `ChatQueryScope.CurrentOrNull?.QueryId`；
   回合作用域外的握手类消息（`system/init`）不带该字段。
3. 结果消息（`type:"result"`）带同一身份：`SessionController.LastQueryId` → `CliSession.LastQueryId`
   → `StartupContext.QueryId` → `SdkResultMessage.QueryId`；**失败/取消路径同样回填**（查询身份在回合开始时就已生成）。

## 3. 判据

| 场景 | 期望 |
|------|------|
| 回合作用域内 `OnText`/`OnDone` | NDJSON 每行含 `"query_id":"q-…"`，与 `session_id` 并存 |
| 作用域外（`system/init`） | 不含 `query_id` 字段（不是 `null`） |
| 结果消息 | `query_id` 与事件流一致；`null` 时字段不写 |
| 旧协议形状 | `uuid` / `session_id` / 既有字段一字不变（纯增量） |

## 4. 落地

| 文件 | 改动 |
|------|------|
| `modules/apps/cli/core/output/SdkMessage.cs` | `SdkMessageBase.QueryId`（`query_id`，WhenWritingNull） |
| `modules/apps/cli/adapters/CliEventConsumer.cs` | `WriteSdkJson<T>` 约束 `where T : SdkMessageBase` + `message with { QueryId = CurrentQueryId() }`；`CurrentQueryId()` 读 AsyncLocal 作用域 |
| `modules/apps/engine/adapters/SessionController.cs` | 回合开始即 `LastQueryId = queryScope.QueryId` |
| `modules/apps/cli/core/services/CliSession.cs` | `LastQueryId => _controller?.LastQueryId` |
| `modules/apps/cli/entry/startup/core/IStartupStep.cs` | `StartupContext.QueryId` |
| `modules/apps/cli/entry/startup/non_interactive/NonInteractiveExecuteStep.cs` | `try/finally` 回填 `context.QueryId`（失败也归因） |
| `modules/apps/cli/entry/runners/NonInteractiveModeRunner.cs` | 结果消息 `with { QueryId = context.QueryId }` |
| `test/unit/host.tests/adapters/CliEventConsumerQueryIdTests.cs`（新） | 4 条：作用域内打标 / 作用域外不写字段 / 结果消息蛇形命名 / 结果消息空值不写 |
| `test/mock/mock_server.e2e.tests/scripts/VfCliSwitchScripts.cs` | vf03（`--format ndjson`）增断言：输出含 `query_id` |

## 5. 验证

- **单测**：`Host.Tests` 全量（含新增 4 条）全绿。
- **真机（`jcc -p "hi" --format ndjson --model nonexistent-xyz`，401 快速失败不烧 token）**：

  ```
  {"type":"system","subtype":"init",...}                     ← 回合前握手，无 query_id
  {"type":"auth_status",...}                                 ← 同上
  {"type":"system","subtype":"session_state_changed","state":"running",...,"query_id":"q-32d60f…"}
  {"type":"system","subtype":"session_state_changed","state":"idle",...,"query_id":"q-32d60f…"}
  {"type":"result","subtype":"error_during_execution",...,"query_id":"q-32d60f…"}
  ```

  判读：回合内消息与结果消息归因到同一 `q-…`；握手类消息不带该字段；**失败路径也有身份**
  （证明 `try/finally` 回填生效，而非只在成功分支）。
- **E2E 断言**：vf03 已加 `query_id` 断言，但本机 E2E 跑不通——常驻 `jcchub` 抢占 provider，
  子进程走 Hub 而非 MockServer（401，既有环境问题），留 CI 验证；上面那条真机证据已独立覆盖同一判据。
- **坑（血的教训）**：`-p` 会**吞掉下一个 token 当提示词**，所以 `jcc -p --json "hi"` 里的 `--json`
  被当成 prompt —— 标志必须写在提示词之后（`jcc -p "hi" --json`）。此前用错顺序，误判过"结构化输出不生效"。
- **协议兼容**：`query_id` 为增量字段且空值不写；`SdkResultMessage_WithoutQueryId_OmitsField` 锁住形状。

## 6. 本轮不做（已由 [TASK138](TASK138-SDK流式输入模式.md) 承接）

1. **`--input-format stream-json` 流式输入模式**：一条进程内多次查询的真正前置。当时
   `JccCliArg.InputFormat` 只有声明，无解析、无 runner；`BridgeSubprocessManager` 已按该模式拼参，
   所以 bridge 服务拉起 jcc 的路径**目前必然失败**（`--output-format` 未知 + `stream-json` 非法值）。
   → **TASK138 已补参数面 + 输入 DTO/解析器 + 回合循环**。
2. **`BridgeSubprocessManager` 参数纠偏**：应与 (1) 一起改（单独改成 `--format ndjson` 仍会因
   `-p` 缺提示词而失败，改一半比不改更难查）。
   → **TASK138 已改：只拼本仓真实解析的选项，并加门禁单测 `BridgeChildArgumentsTests`**。
3. **queryId 贯通 `bridge` 服务协议面**（websocket `BridgeServerMessage`）—— 仍待单独立项。

## 7. 教训

- **声明式 CLI 参数会伪装成"已支持"**：`--input-format` 在枚举里、能通过参数校验、被静默忽略。
  判「功能是否存在」必须 grep **消费方**，不能只看选项声明。
- **打包脚本自己拼的参数要用同一二进制验证**：`BridgeSubprocessManager` 拼的
  `--output-format` 在本仓根本不存在，说明该路径从未被真实执行过（无 E2E 覆盖）。
