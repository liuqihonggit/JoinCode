# TASK142 — 后续四项收口台账（ADR 0165 协议面 + 进程级验收）

> 用户决策（2026-11-28）：上轮 PR #746 合入后列出的遗留项「全部做」。
> 交付纪律：**统一 PR，不要高频 PR**；每项走 红测试 → 实现 → 编译 → 绿测试 → 文档 → 提交 → 差评大师 → 修复。
> 基准：`w2` = `origin/main` = `0ac889614`（2026-11-28 核对，0/0 对齐）。

## 1. TODO 表

| # | 项 | 验收判据 | 状态 |
|---|----|----------|------|
| A | bridge 子进程端到端验收 | 真实 `jcc` 子进程走 `SpawnAsync` → stdin NDJSON → stdout `result`；参数走 `BuildChildArguments`；有单测 + 真机证据 | ☐ |
| B | SDK 控制协议（`control_request` / `interrupt`） | 先查基准原始规范定协议形状；红测试覆盖；真机 stdin 注入可打断在途查询 | ☐ |
| C | queryId 贯通 bridge websocket 协议面 | `BridgeServerMessage` 带查询身份；跨库 DTO 改动走全仓编译；单测 + 真机 | ☐ |
| D | 崩溃收编真进程 `kill -9` 用例 | 真进程写 streaming 旁路 → `kill -9` → 重启收编不丢已写行 | ☐ |
| E | 统一 PR + CI | 四项全部完成后一次性 PR，守 CI 到绿，回填台账 | ☐ |

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
