# TASK135 — hook `systemMessage` 全事件投影（A5 剩余）

> 状态：**已施工并验收（2026-11-25）** · 来源：[TASK130 A5 仍缺](TASK130-剩余事项清单与验收步骤.md)「其余 hook 事件（PreToolUse/PostToolUse/Stop/SessionStart…）的 `systemMessage` 仍未投影」
> 基线：`main` / `w4` = `613ce862a`（PR #741 合并后）
> 纪律：先本文档，再代码；每步可编译、可定向验证；PR 前先告知用户。

## 0. 结论与目标

当前只有 `UserPromptSubmit` 的 `systemMessage` 会变成用户可见旁白（`SystemNotice`），其余 hook 事件的
`systemMessage` 解析出来后无人消费。另外，官方文档示例的**顶层** `{"systemMessage":"..."}` 在本仓
`ParseJsonResponse` 里根本不解析（只认 `hookSpecificOutput.systemMessage`），属于真 bug。

目标：

1. 任何 hook 事件产出的非空 `systemMessage`，在**当前回合**内到达 `IEventConsumer.OnSystemNotice`，只上屏、
   不进助手正文、不进上下文历史；多个 hook 各发一条就各上屏一条。
2. 顶层 `systemMessage` 与 `hookSpecificOutput.systemMessage` 都能解析（后者优先）。
3. `UserPromptSubmit` 保持既有两条链（主代理直发 / 回落管道 `ChatStreamEvent.SystemNotice`）不变，
   由本通道**跳过**，避免同一旁白上屏两次。
4. 无当前回合、事件在回合外（如后台 agent 的 hook）时不抛异常、不阻塞 hook，静默丢弃并可诊断。

**不在范围**

- NDJSON / SDK 的 attachment 投影（协议无 `hook_system_message` 类型，不臆造字段）。
- 其余事件的 `additionalContext` 注入（A6 只覆盖 SessionStart / UserPromptSubmit，语义不同）。
- 后台 agent 独立 transcript 的旁白归属（TUI 已有后台任务面板；本轮不新造第二套）。

## 1. 取证（一手）

| 缺口 | 证据 |
|---|---|
| 只有 UserPromptSubmit 消费 | `modules/apps/engine/app/middlewares/UserPromptSubmitHookRunner.cs:52-54` 收集；`SessionController.cs:617-619` 直发；`ChatTelemetryPreHook.cs:47-49` → `PreChatMiddleware` 投影 |
| 其余事件结果被读后丢弃 | `modules/mcp/mcp_tool_dispatch/core/middleware/HookMiddlewares.cs:46-53`（PostToolUse 循环体为空）、`HookMiddlewares.cs:102-104`；`StopHookManager` / `SessionStartHookManager` / `NotificationHookManager` 等只取各自字段 |
| 顶层 `systemMessage` 不解析 | `HookExecutorBase.ParseJsonResponse:195-210` 只调 `ParseHookSpecificOutput`；`ParseHookSpecificOutput:219-223` 只读 `hookSpecificOutput["systemMessage"]`；测试也仅覆盖嵌套形态（`HookOutputParsingTests.cs:43-50`） |
| 基准语义 | `D:\project\claude-code-rust\claude-code-rev-main\src\utils\hooks.ts:2770-2780` 把任意事件的 `systemMessage` 变成 `hook_system_message` attachment（只展示，不进上下文）；`:545-547` 解析顶层 `json.systemMessage` |

## 2. 设计

### 2.1 旁白通道（新）

`modules/abstractions/abstractions_primitives_pipeline/src/hooks/core/HookSystemMessageChannel.cs`：

- `static IDisposable BeginCapture(Action<string> publish)` — 用 `AsyncLocal` 绑定当前异步执行流的出口，
  返回作用域；Dispose 恢复上一层（支持嵌套）。
- `static bool Publish(HookEvent hookEvent, string message)` — 空串/空白返回 false；无捕获返回 false；
  有捕获时回调并返回 true。
- 为什么用 `AsyncLocal` 而不是 DI 单例字段：回合上下文要跟着工具/钩子调用链走，且要天然隔离并行测试与
  后台任务；后台线程在创建时捕获不到当前回合 → 落空丢弃，符合边界。
- 通道只做搬运，不吞消费者异常；调用方（编排器）负责 fail-soft 记录。

### 2.2 编排器统一出口

`HookOrchestrator.ExecuteHooksAsync(HookInput)` 的 `await foreach` 内、`yield return` 之前：

```
if (!string.IsNullOrWhiteSpace(result.SystemMessage) && input.Event != HookEvent.UserPromptSubmit)
    PublishSafely(input.Event, result.SystemMessage);
```

- `PublishSafely` 捕获异常写 `LogWarning`，不影响 hook 结果与后续 hook。
- 在 `yield` 前发布：即使调用方因 `Blocking` 提前 break，旁白也已经送出（对齐基准 blocking 也产出 systemMessage）。
- 跳过 `UserPromptSubmit`：该事件既有出口已生效，通道再发会重复上屏。

### 2.3 回合捕获

`SessionController.StreamResponseAsync` 在 `_consumer.OnSessionStateChanged("running")` 之后：

```
using var hookNotices = HookSystemMessageChannel.BeginCapture(notice => {
    Diag.WriteLine($"[HOOK-NOTICE] {notice.Length} chars");
    _consumer.OnSystemNotice(notice);
});
```

`using` 覆盖所有 return/catch 出口。SessionStart hook 在 `EnsureSessionStartHookAsync`（同方法内）执行，
因此也落在捕获期内；回合终态 `TurnCompleted` 仍由外层 `EngineTurnClient` 在本方法返回后发出，旁白先于完成事件。

### 2.4 解析补齐

`HookExecutorBase.ParseJsonResponse`：在 `ParseHookSpecificOutput` 之前读取顶层
`TryGetString(rootObj, "systemMessage")`；`hookSpecificOutput` 再解析时覆盖（后者优先）。
非字符串值不抛（沿用 `TryGetString` 语义）。

## 3. 改动清单

| 文件 | 改动 |
|---|---|
| `modules/abstractions/abstractions_primitives_pipeline/src/hooks/core/HookSystemMessageChannel.cs` | 新增通道 + XML 文档 |
| `modules/guard/guard_hooks/execution/core/HookOrchestrator.cs` | 统一出口发布（跳过 UserPromptSubmit）+ fail-soft |
| `modules/guard/guard_hooks/execution/hook_executors/HookExecutorBase.cs` | 顶层 `systemMessage` 解析 |
| `modules/apps/engine/adapters/SessionController.cs` | 回合捕获 + `[HOOK-NOTICE]` 诊断打点 |
| `modules/guard/guard_hooks_tests/permission/hooks/events/HookSystemMessageChannelTests.cs` | 新增：捕获/嵌套/无捕获/空白/异常隔离 |
| `modules/guard/guard_hooks_tests/permission/hooks/configuration/HookOrchestratorTests.cs` | PreToolUse/Stop 发布、UserPromptSubmit 跳过、空值不发 |
| `modules/guard/guard_hooks_tests/permission/hooks/events/HookOutputParsingTests.cs` | 顶层 `systemMessage` + 嵌套优先 + 非字符串不抛 |
| `test/unit/host.tests/adapters/SessionControllerTests.cs` | 回合中发布 → `OnSystemNotice` 一次；回合后发布 → 不投递 |

## 4. 验收

```powershell
dotnet test modules\guard\guard_hooks_tests\Guard.Hooks.Tests.csproj -c Debug --no-build --filter "FullyQualifiedName~HookSystemMessageChannel|FullyQualifiedName~HookOrchestrator|FullyQualifiedName~HookOutputParsing"
dotnet test test\unit\host.tests\Host.Tests.csproj -c Debug --no-build --filter "FullyQualifiedName~SessionControllerTests"
dotnet build modules\guard\guard_hooks\guard_hooks.csproj -c Debug
dotnet build modules\apps\engine\engine.csproj -c Debug   # 以实际 csproj 名为准
```

**真进程证据**（无 key 也可）：临时应用数据目录 + `SessionStart` hook 输出
`{"systemMessage":"顶层旁白","hookSpecificOutput":{"systemMessage":"嵌套旁白"}}`，运行
`JCC_DEBUGLOG=1 jcc -p hi`，日志应出现两条 `[HOOK-NOTICE]`（长度分别对应两段文案），且不出现重复的
UserPromptSubmit 旁白；NDJSON 不渲染旁白属已知边界，不作为失败。

**负向验证**：临时移除编排器发布两行 → PreToolUse/Stop 用例转红；恢复转绿。

### 4.1 验收结果（2026-11-25）

| 项 | 结果 |
|---|---|
| `Guard.Hooks.Tests` 过滤（Channel + Orchestrator + Parsing） | 38/38 |
| `Guard.Hooks.Tests` 全量 | 643/643 |
| `Host.Tests` 全量（含 `SessionControllerTests` 36/36） | 1375/1375 |
| 编译 | `Guard.Hooks` / `JoinCode.Engine` / `JoinCode`（jcc）均 0 警告 0 错误 |
| 全仓编译 | `Build-AllDomains.ps1`：solution / native_tui / native_gui 全部通过（证据 `.xxx/logs/t135-build-all.txt`） |
| 负向验证 | 注释掉编排器 `PublishSystemMessageSafely` 调用 → 仅 2 条转红（`PreToolUseSystemMessage_PublishesToCapture`、`BlockingWithSystemMessage_PublishesBeforeBreak`），恢复后 38/38 绿（证据 `.xxx/logs/t135-negative-red.txt`、`.xxx/logs/t135-green.txt`） |
| 真机冒烟 | 隔离 `JCC_APP_DATA_FOLDER=.xxx\t135-home`，SessionStart 两条 hook 分别输出顶层 / 嵌套 `systemMessage`；`JCC_DEBUGLOG=1 jcc -p "t135 final"` 日志两条 `[HOOK-NOTICE] 21 chars` / `[HOOK-NOTICE] 18 chars`，无 UserPromptSubmit 重复旁白（证据 `.xxx/logs/t135-real-hook-notice.txt`）。无 key 照常 401，不作为失败 |
| 施工期发现 | 未信任工作区时 D9 trust 门禁会**静默跳过所有 hook**（本次首跑因此零结果）；隔离目录补 `trusted_folders.json` 后才触发。属既有安全行为，非缺陷，真机采集需先信任 |

## 5. 风险与回滚

- `AsyncLocal` 依赖异步执行流；若某 hook 在独立 `Task.Run` 且父上下文已结束，则丢弃（已登记边界，不抛）。
- 消费者异常被编排器吞为 LogWarning，不阻断工具执行；日志带事件名便于排障。
- 回滚只需还原上述 4 个生产文件 + 测试，无数据迁移、无协议变更。
