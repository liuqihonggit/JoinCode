# TASK123 — #45 TaskOutput 等待语义：`shell_background_output` 的 block/timeout

> 状态：已闭合 · 日期：2026-11-23 · 基线：`w2` @ `eeb5a2f19`（PR #718 合并后）
> 取证来源：[TASK112](TASK112-Glob-Bash超时-TaskOutput语义-差异取证.md) §3；基准实现 `src/tools/TaskOutputTool/TaskOutputTool.tsx:30-34/219-269`。

## 一、差异原文与一手事实

| 维度 | 基准 `TaskOutput` | 本仓（改造前） |
|------|------------------|----------------|
| 入参 | `task_id` + `block`（默认 `true`）+ `timeout`（默认 `30000`，上限 `600000`） | 仅 `task_id` |
| 语义 | 阻塞到任务终态；超时返回 `retrieval_status=timeout` + 当时输出 | 立即返回当前输出，无等待 |
| 数据源 | 统一 `AppState.tasks`（shell / agent / remote） | `task_output` 读 todo 任务库；shell 后台输出是 `shell_background_output` |
| 等待原语 | `waitForTaskCompletion` 100ms 轮询 | `ISystemActuatorRegistry.WaitForTaskAsync` 已存在但无人调用 |

结论：**不是缺原语，是参数面没暴露**。原「立即返回」会让模型拿到空输出后反复调用，每次都要一次 LLM 往返。

## 二、四问决策

1. **挂在哪个工具？** 只改 `shell_background_output`。
   `task_output` 的数据源是 todo 任务库，把「待办任务」和「后台进程」塞进同一套 block 语义会把两套状态机搅在一起；
   等 todo 侧真有等待需求时，应在 todo 服务里按自己的终态判定实现。
2. **默认 `block`？** `true`（对齐基准）。
   模型拿到 `task_id` 的第一诉求通常是结果；默认非阻塞等于把轮询成本转嫁给模型。
3. **超时返回什么？** `retrieval_status=timeout` + 已产出的部分输出，**不是错误**。
   超时报错会被模型读成「任务失败」，进而重起任务或误判构建结果。
4. **取消归属？** 链接 token；超时只结束本次等待窗口，不取消后台任务；调用方取消（Ctrl+C / TUI 停止）照常抛出。
   等待窗口与任务生命周期必须解耦 —— 否则一次「等太久」就杀掉用户的构建。

## 三、实现

落点：

- `modules/cognition/hands_shell/shell/handlers/tool_handlers/ShellToolHandlers.cs:192` — `[McpTool]` 方法只做校验与异常包装；
- `modules/cognition/hands_shell/shell/handlers/tool_handlers/ShellToolHandlers.BackgroundOutput.cs` — 唯一实现与纯函数；
- `modules/cognition/hands.shell.tests/core/ShellBackgroundOutputWaitTests.cs` — 参数解析/分支/超时/取消；
- `modules/cognition/hands.shell.tests/core/ShellBackgroundOutputIntegrationTests.cs` — 真实注册表状态机集成。

参数面（`jcc mcp_schema shell_background_output` 实测）：

```json
{"properties":{"task_id":{"type":"string"},
 "block":{"type":"boolean","description":"Wait for the task to finish before returning (optional, default true)","default":"true"},
 "timeout_ms":{"type":"integer","description":"Max wait in milliseconds when block=true (optional, default 30000, max 600000)","default":"30000"}},
 "required":["task_id"]}
```

应答形态（状态在前，模型据此决定「继续等」还是「读结果」）：

```text
<retrieval_status>success|not_ready|timeout</retrieval_status>
<task_id>...</task_id>
<status>running|completed|failed|cancelled</status>
<exit_code>0</exit_code>

<output>...</output>

（非终态时追加一句下一步动作提示）
```

两处顺带修正：

- **任务不存在**从「返回 `(No output yet)`」改为 Rust 风格诊断错误（`Task not found: <id>` + 建议 `shell_background_list`）。
  原行为会让模型以为「任务在跑但没输出」，实际上任务 ID 根本不存在。
- 超时上限夹取在 `[0, 600000]`，与基准一致；负值归零而不是抛参数错。

## 四、验收

- `Hands.Shell.Tests` 全量 560/560（新增 19 项：参数夹取 4、状态判定 6、格式 1、注册表分支 4、真实注册表集成 1、其余格式/边界）；
- 集成用例覆盖真实 `SystemActuatorRegistry` 的三段状态：`running → not_ready` → `timeout(150ms)` → `completed → success`，并断言超时路径**不调用** `CancelTaskAsync`；
- `jcc mcp_schema shell_background_output` 真机输出与上表一致；
- CLI 重编 0 警告 0 错误。

**真机限制（如实记录）**：`mcp_serve` 模式下 bash 走权限确认，无人确认即 `PD002` 拒绝，无法非交互真跑进程；
`jcc tool call` 每次是独立进程，后台任务注册表不跨进程存活（实测 `shell_background_list` 恒为 0）。
因此「真实注册表 + 可控进程上下文」是本项能达到的最强真机路径 —— 状态机、100ms 轮询、输出拼接都是生产代码，只有进程本身是假件。

## 五、残余

- `task_output`（todo 库）仍无 block/timeout —— 需在 todo 域按自身终态实现，不属本项。
- TUI/GUI 尚未单独渲染 `retrieval_status`（当前按纯文本展示）；若要「等待中」进度条，属 UI 层另立项。
