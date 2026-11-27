# TASK142 — 后续四项收口台账（ADR 0165 协议面 + 进程级验收）

> 用户决策（2026-11-28）：上轮 PR #746 合入后列出的遗留项「全部做」。
> 交付纪律：**统一 PR，不要高频 PR**；每项走 红测试 → 实现 → 编译 → 绿测试 → 文档 → 提交 → 差评大师 → 修复。
> 基准：`w2` = `origin/main` = `0ac889614`（2026-11-28 核对，0/0 对齐）。

## 1. TODO 表

| # | 项 | 验收判据 | 状态 |
|---|----|----------|------|
| A | bridge 子进程端到端验收 | 真实 `jcc` 子进程走 `SpawnAsync` → stdin NDJSON → stdout `result`；参数走 `BuildChildArguments`；有单测 + 真机证据 | ✅ 见 §5.A |
| B | SDK 控制协议（`control_request` / `interrupt`） | 先查基准原始规范定协议形状；红测试覆盖；真机 stdin 注入可打断在途查询 | ✅ 见 §5.B（进程级「真打断 HTTP」未复现，已记边界） |
| C | queryId 贯通 bridge websocket 协议面 | `BridgeServerMessage` 带查询身份；跨库 DTO 改动走全仓编译；单测 + 真机 | ✅ 前提不成立，不改代码，见 §5.C |
| D | 崩溃收编真进程 `kill -9` 用例 | 真进程写 streaming 旁路 → `kill -9` → 重启收编不丢已写行 | ✅ 升级为真实磁盘，进程级 `kill -9` 仍缺慢 provider，见 §5.D |
| E | 统一 PR + CI | 四项全部完成后一次性 PR，守 CI 到绿，回填台账 | ✅ PR 见 §5.E |

## 2. 不做（范围边界）

- TUI 手工验收（TASK129 §3.4 / #101）：需真人键盘，AI 探针不能冒充。
- TASK128 backlog（SSE / TUI 首帧 / GraphRAG / Pithos / CI 缓存）、TASK071 模型目录。
- 多模态输入（图片/文件内容块）：无输入通道，需单独立项。

## 3. 前置四问（按 AGENTS.md 的深度优先纪律，逐项开工前回答）

### A — bridge 子进程端到端

1. **专家隐性实践**：不只用 fake `IProcessService` 验证回调，还要拿**真实 jcc 二进制**跑一次
   SpawnAsync 拼出来的参数，确认「子进程真的能起来并停在 stdin」。
2. **原始素材**：`BuildChildArguments` 的产物（TASK138 §4.1）、`BridgeSubprocessHandle` 的
   stdout 解析分支、SDK NDJSON 输入形状（TASK138 §3）。
3. **方法与工具**：xUnit + fake `IProcessService`（确定性）；真机 `jcc` + 管道写两行 NDJSON（进程级）。
4. **独立验证来源**：真实进程 stdout 的 `system/init` 与逐查询 `result.query_id`；不是单测替代。

### B — SDK 控制协议

1. **专家隐性实践**：先看基准侧原始讨论/源码里的 control 消息字段与时序，不臆造协议。
2. **原始素材**：`D:\project\claude-code-rust\claude-code-rev-main\src\` 的控制协议实现与
   仓库内 `bridge_serve` 的既有 `interrupt` 语义（TASK136）。
3. **方法与工具**：`grep` 溯源 + `gh pr view` 查设计意图；xUnit 红测试；真机 stdin 注入。
4. **独立验证来源**：基准侧测试套件 / 真机可观察的取消行为；不以 AI 自述为证。

### C — bridge websocket queryId

1. **专家隐性实践**：协议扩展必须保证旧客户端零影响（`WhenWritingNull`），且查清消费方而不是只加字段。
2. **原始素材**：`BridgeServerMessage` 的现状定义 + websocket 消息的实际消费者。
3. **方法与工具**：grep 消费链；跨库 DTO 改动 → `Build-AllDomains.ps1` 全仓编译。
4. **独立验证来源**：全仓编译 + 既有 bridge 协议测试 + 真机消息快照。

### D — 崩溃收编

1. **专家隐性实践**：真进程要断言「已写行」而非「最终一定成功」，避免把断电语义混进来。
2. **原始素材**：`TranscriptServiceStreamingTests` 的既有断言 + streaming 旁路落盘格式。
3. **方法与工具**：真实 CLI 进程 + `kill -9`； juxtapose 收编前后文件。
4. **独立验证来源**：进程退出后磁盘上的文件内容（不依赖 mock）。

## 4. 施工顺序与提交策略

A → B → C → D（A 闭合上轮刚修的拼参缺口；B/C 同属协议面可复用调研；D 独立）。
每项单独提交便于回溯，但**只在全部完成后开一次 PR**。
每轮提交后做一次「差评大师」：列出该项最可能错的 3 个假设，逐条找反证，发现 P0/P1 就修。

## 5. 落地记录

### A — bridge 子进程端到端验收 ✅

**差评大师在开工前发现的 P0**：TASK138 把 `--replay-user-messages` 当「无消费方」删掉，
但 `BridgeSubprocessHandle.OnFirstUserMessage` → `BridgeMain.OnFirstUserMessage`（派生会话标题）
正是靠子进程 stdout 的 `type=user` 行驱动的 —— 参数删掉后这条链**整条断掉**。

**判据来源（不是 AI 自造）**：基准侧
`D:\project\claude-code-rust\claude-code-rev-main\src\bridge\sessionRunner.ts::extractUserMessageText`
（跳过 `parent_tool_use_id` / `isSynthetic` / `isReplay`，content 支持字符串与 text 块数组）、
`bridge/types.ts:202` 的 `onFirstUserMessage` 注释。

| 文件 | 改动 |
|------|------|
| `modules/apps/cli/core/services/CliArg.cs` | 登记 `--replay-user-messages` |
| `modules/apps/engine/app/builder/CommandLineOptions.cs` | `ReplayUserMessages` |
| `modules/apps/cli/app/builder/ApplicationBuilder.cs` | 参数映射 |
| `modules/apps/cli/entry/runners/NonInteractiveModeRunner.cs` | 流式输入每回合回显 `SdkUserMessage`（text 块数组，**不写 isReplay**） |
| `modules/services/bridge/session/core/BridgeSubprocessManager.cs` | 恢复 `--replay-user-messages`；提取器对齐基准：`isReplay`/`isSynthetic`/`parent_tool_use_id` 跳过，嵌套 `message.content` 支持数组 |
| `modules/services/bridge_tests/.../BridgeTitleTests.cs` | 2 条红测试（嵌套数组、isReplay 跳过），修复前分别返回 null / 误取文本 |
| `test/unit/host.tests/adapters/SdkStreamInputParserTests.cs` | 参数登记 + 回显 DTO 形状（无 isReplay）2 条 |
| `test/unit/host.tests/bridge/BridgeChildArgumentsTests.cs` | 从「不得出现」清单移出 `--replay-user-messages`，新增必须出现断言 |

**红→绿**：`ExtractUserMessageText` 2 条失败（Expected "你好" / null，Actual null / "回放确认"），
修复后 12/12 绿；CLI 侧 16/16 绿。

**真机（子进程实际拼出的参数，401 快速失败不烧 token）**：

```
$ printf '{"type":"user","message":{"role":"user","content":"bridge 标题测试"}}\n...' \
  | jcc --input-format stream-json --output-format stream-json --replay-user-messages --model nonexistent-xyz
{"type":"system","subtype":"init",...}
ECHO user: [{'type': 'text', 'text': 'bridge 标题测试'}] isReplay=None
RESULT q-e3a4f522… error_during_execution
ECHO user: [{'type': 'text', 'text': '第二问'}] isReplay=None
RESULT q-5f839d48… error_during_execution
```

判读：回显行可被 `ExtractUserMessageText` 的嵌套数组分支识别；每回合一条 result；
参数面、输出面、父进程消费面三段闭合。

### B — SDK 控制协议（interrupt 子集）✅（进程级取消证据有限，见下）

**原始素材**：`D:\project\claude-code-rust\claude-code-rev-main\src\entrypoints\sdk\controlSchemas.ts`
（`SDKControlRequestSchema` / `SDKControlResponseSchema`）、`src\cli\print.ts::sendControlResponseSuccess`、
`src\cli\structuredIO.ts::processLine`。**没有自造协议字段**。

| 输入行 | 本仓行为 |
|--------|----------|
| `{"type":"control_request","request_id":"r","request":{"subtype":"interrupt"}}` | 取消当前回合 CTS + 回 `control_response{success,r,{}}` |
| `{"type":"control_response",…}` / `{"type":"keep_alive"}` / `update_environment_variables` | 静默忽略（本仓不发 control_request，无待匹配应答） |
| 其它 `control_request.subtype` | stderr 诊断：`当前仅支持 interrupt…请用 type=user 提交下一回合` |

**并发结构**：stdin 读取改到独立 `Task`，控制行即时处理、用户行入 `Channel` 串行执行；
回合 CTS 用不可变快照 + `Interlocked.CompareExchange` 交接（分析器 JCC4008 禁 `lock`），
`Cancel()` 命中已释放 CTS 时记诊断后忽略。

| 文件 | 改动 |
|------|------|
| `modules/apps/cli/core/output/SdkStreamControlMessage.cs`（新） | 控制请求/应答 DTO（JsonContext 注册） |
| `modules/apps/cli/core/output/SdkStreamInputParser.cs` | `SdkStreamInputLine.Interrupt(requestId)` + 控制行分派 |
| `modules/apps/cli/entry/runners/NonInteractiveModeRunner.cs` | 独立 stdin 生产者 + Channel 消费 + `TurnCancellation`（CAS）+ `WriteControlResponse` |
| `test/unit/host.tests/adapters/SdkStreamInputParserTests.cs` | 3 条：interrupt 识别 / control_response+keep_alive 静默 / 未支持子类型诱导诊断 |
| `test/unit/host.tests/adapters/StreamJsonInterruptTests.cs`（新） | 4 条 CAS 竞态：取消当前回合 / 旧回合收尾不摘新回合 / CTS 已释放不抛 / Dispose 幂等 |

**红→绿**：新增测试先因 `InterruptRequestId` 不存在编译失败；实现后 17/17 绿。

**真机**：管道注入 user 行 + 2 秒后 interrupt：

```
{"type":"system","subtype":"init",...}
{"type":"control_response","response":{"subtype":"success","request_id":"req-interrupt-1","response":{}}}
{"type":"result","subtype":"success",...,"query_id":"q-f731128…"}
[RUN] stream-json turn start
[RUN] control_response sent for req-interrupt-1
```

**证据边界（如实记）**：本地没有可控的慢 provider（自建延迟端点未被当前 vendor 路由命中，
MockServer 需另配隔离 AppData），所以"在途 HTTP 真被 Cancel 打断"**未在进程级复现**；
真机只证明控制行被识别、应答形状正确、回合循环继续。取消竞态由 4 条确定性单测兜底。
不把单测冒充进程级证据。

**差评大师发现 P1**：初版用 `Channel.CreateUnbounded` 触发 StyleGate S5（基线 1821→1822）。
改为 `CreateBounded(256)`（`SingleReader/SingleWriter/FullMode.Wait`），既过门禁又给客户端加了背压。
StyleGate 回到 1821。

### C — queryId 贯通 bridge 协议面 ✅（前提不成立，不改代码）

**反证过程**：`grep -rn "BridgeServerMessage"` 全仓只命中 IDE 类 bridge 服务端
（`get_file` / `set_selection` / `execute_command` / `ping`），**没有查询概念** ——
给它加 `queryId` 就是造第二个真相源，正是 TASK138 §7 的教训。

真正的查询协议面是 `jcc bridge_serve` 的 stdio NDJSON 行协议（ADR 0141）：

| 证据 | 内容 |
|------|------|
| `modules/apps/engine/bridge/BridgeProtocol.cs` | `BridgeRequest.QueryId` 与 `BridgeEvent.QueryId` 双端镜像，均 `WhenWritingNull` |
| `test/unit/host.tests/bridge/BridgeQueryIdContractTests.cs` | 4 条：submit 序列化/反序列化/事件序列化/缺省不写字段（本轮实跑 4/4 绿） |
| `BridgeServeLoop.cs` | `turn_started` / `turn_completed` / `turn_interrupted` / `error` 均带 QueryId |
| 真机（本次） | 两条 submit → `turn_started q-c-1` / `turn_started q-c-2`，`quit` 后两条 `turn_interrupted` 同 id |

**结论**：TASK137 §6.3 / TASK138 §6.4 关闭为"已覆盖/前提不成立"，不引入跨库 DTO 变更，
也不触发全仓编译（无接口/共享 DTO 改动）。

### D — 崩溃收编：真实文件系统验收 ✅（进程级 kill -9 仍记未做）

** mock 文件系统 → 真实磁盘**：新增 `TranscriptCrashRecoveryRealFsTests` 2 条，
用 `PhysicalFileSystem` + `Path.GetTempPath()` 隔离目录；写旁路后**不调用回合收尾/收编**，
用新 `TranscriptService` 实例（等价进程重启）加载：

1. `SidecarWrittenBeforeCrash_SurvivesRestart_AndIsAdoptedExactlyOnce` —
   磁盘旁路内容可读 → 收编进主 transcript、`type=streaming-partial` → 删旁路 → 二次加载不重复。
2. `MultipleQuerySidecars_AllSurviveCrash_AndAreAdoptedSeparately` — q-a / q-b 两条旁路互不覆盖，
   重启后各自收编。

两条均通过；连同既有 6 条 mock 单测，`TranscriptServiceStreamingTests` 覆盖的逻辑面闭合。

**进程级 kill -9 未复现（如实记）**：用隔离 AppData + DeepSeek.MockServer 尝试让真实 `jcc -p`
跑到落旁路再 kill -9，但 MockServer 对该请求返回 **1 个 chunk**（日志：`text stream (1 chunks)`），
回复在 <0.5s 内一次性完成，旁路写完即被回合收尾清掉，轮询 50ms 未捕获到窗口。
因此**没有**"真进程被 SIGKILL 后 sidecar 留存"的进程级证据；这里只把 mock 文件系统升级为真实磁盘，
不把"丢弃实例"冒充 kill -9。CI 若要补进程级用例，需要可控的慢速流式 provider。
TASK135 C2 保持 `[~]`，备注本轮进展。

### E — 统一 PR 与回归 ✅

**PR**：<https://github.com/liuqihonggit/JoinCode/pull/761>（`w2` → `main`，四个提交：A `e97579b4c`、B `a06e3a403`、C `9f35b9f2e`、D `a003aad1c`）。

回归（四提交叠加后实跑，串行）：

| 项目 | 结果 |
|------|------|
| `Bridge.Tests` | 639 |
| `Host.Tests` | 1433（+13） |
| `Composition.Tests` | 1177 |
| `Vault.Other.Tests` | 403（+2） |
| `NativeTui.Tests` | 3367 |
| `NativeTui.Host.Tests` | 183 |
| `NativeGui.Core.Tests` | 423 |
| `NativeGui.Host.Tests` | 50 |
| StyleGate | 1821，退出 0 |

**并行踩坑**：同时跑两个 `dotnet test` 会撞 `artifacts/obj/JoinCode.Engine/.../JoinCode.Engine.dll`
的文件锁（`CS2012` / `MSB3101`，另一进程或 Defender 持有）。这是构建产物竞争，串行重跑即绿 ——
与改动无关，但值得记住：**多项目回归必须串行**。
