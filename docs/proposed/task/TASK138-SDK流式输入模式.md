# TASK138 — SDK 流式输入模式（`--input-format stream-json`）

> 承接 [TASK137](TASK137-NDJSON协议查询身份.md) §6.1/§6.2 记录的两处缺口，以及
> [ADR 0165](../../proposed/adr/0165-everything-is-background-query-identity.md) 阶段 2「SDK/NDJSON 协议」。
> 台账出处：[TASK135](TASK135-B2查询并发缺陷台账.md) 批次 D（D1 的收尾项）。

## 1. 现状事实（改造前）

| 观察点 | 事实 |
|--------|------|
| `--input-format` | 只在 `JccCliArg` 声明过；`CliArg` 未登记 → **未知选项**，子进程启动即死 |
| `--output-format` | 同上。本仓方言是 `--format text/json/ndjson`，`stream-json` 当时也**不是合法值** |
| 进程模型 | 一次性 `-p`：一条进程 = 一次查询。SDK 客户端（长驻子进程、多轮对话）无处落脚 |
| `BridgeSubprocessManager` 拼参 | 照 TS 端拼 `--print` / `--sdk-url` / `--session-id` / `--replay-user-messages` / `--debug-file` + `--output-format stream-json`。**五个选项本仓根本没登记**，第一个 token 就报 `Unknown option` |

**负向实验（改造前后各一次）**：

```
$ jcc --print --input-format stream-json
错误: Unknown option: --print          ← 子进程一行代码都没跑到就退出
使用 --help 查看可用选项。
```

结论：bridge 拉起子进程这条路径**从未真实执行过**（无 E2E 覆盖，单测也不碰它）。

## 2. 本轮范围（D2 = 一条进程内多次查询的输入侧）

1. **参数面**：登记 `--output-format`（SDK 方言，等价 `--format`）与 `--input-format`；
   `stream-json` 在两者中都是合法值，应用层归一为 `ndjson`。
2. **选项面**：`CommandLineOptions.InputFormat`、`IsStreamJsonInputMode`；
   `--input-format stream-json` **隐含非交互模式**（stdin 驱动，不需要 `-p`）。
3. **输入 DTO + 纯函数解析器**：一行 stdin → 一段用户文本 / 一条诊断 / 静默忽略（空行）。
4. **回合循环**：初始化管道只跑一次，stdin 每行驱动一次执行管道 + 一条 `result`；EOF/取消收尾。
5. **`BridgeSubprocessManager` 拼参纠偏** + 门禁单测（拿同一份 `CliArgParser` 核对）。

## 3. 判据

| 场景 | 期望 |
|------|------|
| `--input-format stream-json` 且未给输出格式 | 自动 `ndjson`（否则人类可读文本污染协议流） |
| 两条 stdin 用户行 | 两个不同 `query_id`，各自一条 `result`，进程不退出 |
| 非 user 类型 / 坏 JSON / 空文本 | 诊断写 **stderr**，循环继续；stdout 保持纯协议流 |
| 单回合失败（401） | 该回合 `result.is_error=true`，循环继续；退出码 = 有成功则 0，否则最后一回合 |
| `system/init`、`auth_status` | 不带 `query_id`（回合作用域外，TASK137 既有约定） |
| bridge 子进程参数 | 每一项都能被本仓 `CliArgParser` 解析（无 `Unknown option`） |

## 4. 落地

| 文件 | 改动 |
|------|------|
| `modules/apps/cli/core/services/CliArg.cs` | 新增 `OutputFormat`、`InputFormat` 两个 `[CliOption]`（引用 `JccCliArgEnumConstants`，维持参数名单一数据源） |
| `modules/apps/cli/app/builder/ApplicationBuilder.cs` | `NormalizeOutputFormat`：`stream-json → ndjson`；`--input-format stream-json` 且未给输出格式时默认 `ndjson` |
| `modules/apps/cli/core/commands/core/Program.cs` | `ValidateEnumArgs` 放行 `stream-json`（`--format` 与 `--output-format` 同判），新校验 `--input-format` |
| `modules/apps/engine/app/builder/CommandLineOptions.cs` | `InputFormat` / `IsStreamJsonInputMode`；`IsNonInteractiveMode` 增该分支 |
| `modules/apps/cli/core/output/SdkStreamInputMessage.cs`（新） | 输入 DTO（`type` + `message.role` + `message.content` 原样 `JsonElement`） |
| `modules/apps/cli/core/output/SdkStreamInputParser.cs`（新） | `SdkStreamInputLine`（`Text`/`Diagnostic`，`Blank`/`Failed`/`Turn` 工厂）+ `Interpret`：字符串 content / 内容块数组（只拼 text 块） |
| `modules/apps/cli/core/output/CliOutputJsonContext.cs` | 注册 `SdkStreamInputMessage`（源生成，禁反射） |
| `modules/apps/cli/entry/runners/NonInteractiveModeRunner.cs` | `RunStreamJsonInputAsync`（init→回合→cleanup 三段管道）+ `ResetTurnState` + `WriteResultMessage`（带 `QueryId`） |
| `modules/services/bridge/session/core/BridgeSubprocessManager.cs` | `BuildArgumentList` → 公开的 `BuildChildArguments`：只拼 `--input-format`/`--output-format`/`--debuglog`/`--permission-mode` + `ScriptArgs` |
| `test/unit/host.tests/adapters/SdkStreamInputParserTests.cs`（新） | 8 条：字符串 content / 块数组拼接 / 非 user 诊断 / 空行静默 / 坏 JSON 带形状提示 / 空文本 / 缺 content / 选项隐含关系 |
| `test/unit/host.tests/bridge/BridgeChildArgumentsTests.cs`（新） | 5 条：可解析门禁 / 两侧都是 stream-json / TS 独有选项不出现 / `--debuglog` 条件开关 / `ScriptArgs` 前置 |

### 4.1 `BridgeSubprocessManager` 五个 TS 独有选项的取舍

| TS 选项 | 本仓判定 | 依据 |
|---------|----------|------|
| `--print` | **删** | 非交互已由 `--input-format stream-json` 隐含（`IsStreamJsonInputMode` → `IsNonInteractiveMode`），且 `-p` 缺提示词本来也进不了正轨 |
| `--sdk-url` | **删** | 无消费方。本仓子进程事件走 stdout，由父进程（`BridgeSubprocessHandle`）转发到 ingress；会话令牌/ingress 走 env（`JCC_SESSION_ACCESS_TOKEN` + `JCC_POST_FOR_SESSION_INGRESS_V2`） |
| `--session-id` | **删** | 语义不兼容：本仓 `--resume` 认的是**本地会话 ID**，bridge 的 `cse_*` 不是它。子进程自己开新会话并通过 `system/init` 的 `session_id` 上报 |
| `--replay-user-messages` | **当时删，TASK142-A 恢复** | 当时本仓无消费方；但删掉后父进程 `OnFirstUserMessage`（会话标题派生）整条链断掉。TASK142-A 补上回显消费方并恢复此参数 |
| `--debug-file` | **删** | 无消费方。子进程侧唯一支持的是 `--debuglog`（stderr 开关）；文件回溯由父进程把 stdout 全量落 `bridge-transcript-{id}.json` |

## 5. 验证

### 5.1 单测

`SdkStreamInputParserTests` 8 条 + `BridgeChildArgumentsTests` 5 条全绿；`Host.Tests` 全量见 §5.3。

### 5.2 真机（`--model nonexistent-xyz` 换快速 401，不烧 token）

**两条 stdin 用户行 = 两个查询**（bridge 子进程的真实形状）：

```
$ printf '{"type":"user","message":{"role":"user","content":"hi"}}\n
          {"type":"user","message":{"role":"user","content":"第二问"}}\n' \
  | jcc --input-format stream-json --output-format stream-json --debuglog --permission-mode auto
system        | init                  | (无 query_id)
auth_status   | —                     | (无 query_id)
system        | session_state_changed | q-7e142bf7…
system        | session_state_changed | q-7e142bf7…
result        | error_during_execution| q-7e142bf7…    is_error=True
system        | session_state_changed | q-fbe50418…
system        | session_state_changed | q-fbe50418…
result        | error_during_execution| q-fbe50418…    is_error=True
```

判读：两个查询各自独立归因；进程**没有**在第一条 `result` 后退出（这是「一条进程多次查询」）；
握手消息不带身份；退出码 10（401）。

**默认输出格式兜底**（只给输入不给输出）：

```
$ printf '{"type":"user",...}\n' | jcc --input-format stream-json --model nonexistent-xyz
{"type":"system","subtype":"init",...}          ← 直接就是 NDJSON，没有人类可读文本
{"type":"auth_status",...}
{"type":"system","subtype":"session_state_changed","query_id":"q-2d2fc884…"}
```

**诊断不打断循环**：`{"type":"bogus"}` 与坏 JSON 各写一行 stderr，后续 user 行照常执行。

### 5.3 回归

- `dotnet build` 三处（Bridge / CLI / Host.Tests）0 警告 0 错误。
- `Host.Tests`、`NativeTui.Tests`、`NativeTui.Host.Tests`、`Composition.Tests`、`Bridge.Tests`、
  `Brain.Context`、`Vault.Other` 与 StyleGate 基线见 PR 描述。
- **桥子进程路径仍无 E2E**：这次只保证「参数能被解析、子进程能起来并停在 stdin」。
  端到端（父进程写 stdin → 子进程 event → 父进程转发 ingress）需要真实 bridge 服务端，留后续。

## 6. 本轮不做

1. **控制协议**（`control_request` / `control_response` / `interrupt` line-protocol）：
   本仓已有 `bridge_serve` 走自己的 JSON 行协议（TASK136）；SDK 侧的 **interrupt 子集**
   已由 TASK142-B 实现（解析 + control_response + 取消在途回合），其余子类型仍给诊断。
2. **多模态输入**（图片/文件内容块）：只拼 `text` 块，其他块静默忽略。
3. **`--sdk-url` / `--replay-user-messages` 的替代实现**：若将来要让子进程直连 ingress，
   应新增有真实消费方的选项，而不是把 TS 名字复活。
4. **queryId 贯通 bridge websocket 协议面**（TASK137 §6.3）—— TASK142-C 已查证前提不成立
   （bridge_serve 行协议早已带 queryId，`BridgeServerMessage` 与查询无关），无需改代码。

## 7. 教训

- **拼参数的人和解析参数的人必须共用一个真相源**：一套 8 个参数里有 5 个本仓不存在，
  却挂了两年没人发现 —— 因为没有任何用例执行过这条路径，而「参数名看起来很像 TS 的」骗过了所有 review。
  修法不是把名字改对，而是**加一门禁：拿同一份 `CliArgParser` 去解析产物**。
- **「声明≠支持」是本仓的复发型病灶**（`--input-format`、`--print`、`--sdk-url` 同病）：
  判「功能是否存在」必须 grep **消费方**，而不是选项枚举。
- **异步多条流不能靠"忙就拒"**：TASK136 已把 `bridge_serve` 改成每查询一个 `TurnRun`；
  这次输入侧也必须是「一行一查询」，否则 SDK 客户端没法并发。两处形状必须一致。
- **诊断写 stderr，stdout 只留协议**：诊断混进 stdout 会让 SDK 客户端 JSON 解析直接崩，
  比"静默跳过"更糟。这条在 TASK137 已经踩过一次（`--format ndjson` 首轮无输出那次）。
