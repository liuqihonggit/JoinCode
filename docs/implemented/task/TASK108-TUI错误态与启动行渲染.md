# TASK108 — TUI 错误态与启动行渲染验收

> 承接 TASK107（渲染主链路对齐）。本轮真机验收**错误态 / 启动行 / 工具失败**三类面，
> 沿用「mock 直连 + 剪贴板取真值」方法，先门禁后修复。

## 验证环境

- 产物：`artifacts/bin/JoinCode/Debug/net10.0/jcc.exe`（TASK107 提交后产物，21:40）
- 真实模型不可用 → MockServer 直连（`JCC_ALLOW_DIRECT_FALLBACK=1` + `JCC_ENDPOINT` + `JCC_VENDOR=deepseek`）
- 场景 A（引擎错误）：`JCC_ENDPOINT=http://127.0.0.1:19999/v1`（无监听 → 连接拒绝）
- 场景 B（工具失败）：`.xxx/mock-visual/fail-check.json` 脚本回合 `bash exit 3` / `seq 1 25`
- 取证：`ctrl+shift+a` + `ctrl+shift+c` + `Get-Clipboard -Raw` 逐行核对（14px 截图不可信）

## 缺陷清单

### P12 启动行用错误色渲染正常状态

**现象**（启动帧真值 + 截图）：首帧左上角 `引擎启动中…` 显示为**红色**（`theme.Error`）。

**根因**：`StartupLineItem.Render` 一律用 `theme.Error`：

```csharp
// MessageItems.cs StartupLineItem
var style = screen.Styles.GetOrAdd(new TextStyle { Foreground = theme.Error });
screen.WriteText(frame.Y, frame.X, Text, style, frame.Right);
```

该类同时承载两类语义完全不同的行（`ReplShell.SeedContent` 按 `_startupLines` 顺序建项）：

| 来源 | 语义 | 期望色 |
| --- | --- | --- |
| `ReplShell.EngineStartingLine`（`引擎启动中…`） | 正常启动进度，首拍 `session_info` 后撤除 | dim 灰（非警告） |
| `Program.WriteStartupLine("[diag] …")`（仅 `JCC_DEBUGLOG=1`） | 诊断信息 | Warning 色 |

首帧红色 `引擎启动中…` 会让用户误以为启动失败。类注释「dim 样式渲染纯文本行（淡红色警告色）」本身自相矛盾。

### P13 工具权限拒绝把机读 JSON 直接上屏

**现象**（真值）：

```
● Bash(echo 标准输出第一行; echo 标准错误第一行 >&2; exit 3)
  ⎿  Error: {"type":"permission_denied","tool":"bash","message":"危险操作已被阻止（未知命令 'exit' — 未在 catalog 中登记）"}
● Bash(seq 1 25)
  ⎿  Error: {"type":"permission_denied","error_code":"PD002","tool":"bash","message":"用户拒绝工具执行: bash","details":…}
```

**根因**：`ToolErrorJson`（`modules/abstractions/.../ToolErrorJson.cs`）是**有意的数据面契约**——
「AI 可直接解析 JSON 识别错误类型、原因、详情和建议」，原样放进 `ToolResult.Content[0].Text`。
UI 侧 `ToolResultItem.DisplaySummary` / `ErrorText` 直接透传该文本，因此人看到的是机读结构。

**修复方向**：数据面不动（模型仍拿 JSON），**只在展示层归一**：
新增 `ToolErrorJsonText.Humanize`，识别 `{"type":…,"message":…}` 形态并提取 `message`
（`details` 拼 `k: v`、`suggestions` 拼建议），非 JSON 原样返回。
落点 `DisplaySummary` / `ErrorText`（主屏与 transcript 共用，避免第二套真源）。

## 观察项（暂不修，记录待判）

- **Q3 权限对话框与已拒绝结果同屏**：场景 B 中 `DangerousCommandProtectionMiddleware`（Auto 模式
  Unknown 命令 → `Rejected`）与 `DangerousOperationMiddleware`（Auto 模式 `IsShellOperation` → `PendingConfirmation`）
  对同一条 bash 给出相反结论。真机现象是两个 bash 调用错位（第一个被 guard 拒、第二个弹确认），
  属 mock 脚本连发两轮工具调用造成的加速路径，需单独构造单工具场景复现再判定。
  **结论：不改**。Auto 模式下 shell 操作一律确认是既定契约（`DangerousOperationMiddleware` 显式分支），
  真机看到的"同屏"是两条不同 bash 调用的渲染位置交错，不是同一条命令的矛盾结论。
- **Q4 工具错误仍是 `⎿` 摘要行而非 `●` 错误块**：`BridgeEvent.ToolResult.IsError` 在权限拒绝这条链上是
  false（圆点不上错误色、走摘要渲染）。判定**不改**：Humanize 后摘要行本身就是红字 + `⎿`，
  与基准 `FallbackToolUseErrorMessage` 形态一致；改 IsError 需动引擎事件链，收益仅图标差异，风险不成比例。
- **spinner 英文动词**：`Smooshing… (bash 剩余 99s)` / `Effecting… (thinking)` —— `SpinnerVerbs`
  对齐基准 187 个英文动词（基准本身就是 whimsical 英文），判定**不改**（与基准一致）。
- **P14 撤销**：低分辨率截图 OCR 曾把 `⚠ 轮次闸门连续 6 次未通过（上限 6），本轮已强制结束。`
  读成「次遍历」，剪贴板真值确认原文为「轮次闸门」，文案无缺陷（仅 mock 耗尽触发 loop gate 时出现）。
- **Q5 `📎` 行疑似两行**：剪贴板真值出现过
  `📎 首次注入 · 系统提示词 87432 字符 · 点击展开` + 38 空格 + `约 48531 tokens · 点击展开` 两行；
  原分辨率区域截图证实屏幕上是**单行**（`…字符 · 约 48531 tokens · 点击展开`），
  38 空格恰等于前缀显示宽度 —— 剪贴板复制的软折行产物，非渲染缺陷。

## 修复

| 位置 | 改动 |
| --- | --- |
| `repl/MessageItems.cs` `StartupLineItem` + 新增 `StartupLineKind` | Status → dim、Diagnostic → Warning（不再统一 `theme.Error`） |
| `repl/ReplShell.cs` | `_startupLines` 改 `(Text, Kind)`，`AddStartupLine(line, kind = Status)` |
| `apps/native_tui_host/Program.cs` | `WriteStartupLine(line, kind)`；`[diag]` 三行传 `Diagnostic` |
| 新增 `repl/tool_folding/ToolErrorJsonText.cs` | `Humanize`：`Error: ` 前缀 + JSON object + 非空 `message` 三条件成立时取 message，`details` 折 `（k: v, …）`（上限 3 项）；否则原样 |
| `repl/MessageItems.cs` `ToolResultItem` | `_summaryLine` 与 `ErrorText` 经 `Humanize`；`DisplaySummary` 改用归一后的 `_summaryLine`（`Summary`/`Text` 仍存原文，搜索导出不受影响） |

**第一批门禁**：新增 `repl/StartupLineRenderTests.cs`（3 项：状态行 dim 非 Error、文本不变、
诊断行 Warning 非 Error）；新增 `repl/ToolErrorJsonRenderTests.cs`（9 项：message 提取、屏上无 JSON 语法、
details 拼接、普通 JSON 输出不改写、`Error: ` 非 JSON 原样、畸形 JSON 回退、缺 message 回退、
isError 路径归一、diff 统计优先不被覆盖）。

## 第二批：diff 预览链路（P15/P16）

### P15 工具 patch 从未下发到 TUI —— diff 预览全空

**现象**（真机，mock 调 `edit` 改 `.xxx/scratch/diff-demo.txt`，文件确实被改）：

```
● Update(D:\project\w2\.xxx\scratch\diff-demo.txt)
  ⎿  The file D:\project\w2\.xxx\scratch\diff-demo.txt has been updated successfully. (30 bytes, 7 lines)
```

没有统计行、没有 hunk、没有 +/- 行 —— 只有引擎摘要。基准应显示
`Added 2 lines, removed 1 line` + hunk 头 + 带行号的增删行。

**根因**（链路逐段核对）：

| 环节 | 是否带 patch |
| --- | --- |
| `FileToolHandlers.Edit` → `ToolResult.StructuredPatch` | ✓ 有 |
| `QueryEngine` 构造 `ToolCallEnd` 事件 | ✗ **漏了**（内联 `new QueryStreamChunk{…}` 只填 7 个字段） |
| `AgentStreamChunkAdapter` → `ChatStreamEvent.ToolEnd` | ✓ 透传 `chunk.StructuredPatch` |
| `SessionController.OnToolEnd` → `BridgeEventConsumer/InProcessEventConsumer.ProjectPatch` | ✓ |
| `ReplBridgeDriver.ApplyToolResult` → `ToolResultItem(patch)` | ✓ |

断点唯一且明确：`QueryEngine.cs` 内联构造漏字段，导致 Edit/Write 的 diff 预览在真机上**从未显示过**。

**修复**：抽出 `QueryEngine.BuildToolCallEndChunk(toolName, toolCallId, toolResult, callNumber)`
（`internal static`，便于单测），补 `StructuredPatch = toolResult.StructuredPatch`。

**门禁**：新增 `brain_other_tests/query/core/QueryEngineToolEndChunkTests.cs`（3 项）——
patch 透传 / 无 patch 为 null / 错误文本仍带 `Error: ` 前缀（摘要来源契约）。

### P16 详情页 diff 丢行号

**现象**（`ctrl+o` 真值，修复前）：

```
@@ -1,6 +1,7 @@
 第一行
-第二行旧内容
+第二行新内容
```

主屏是 `     @@ … @@` + `  1  第一行`（4 缩进 + 行号 gutter），详情页只有裸 `+/-/空格` 标记 ——
改文件后按 `ctrl+o` 看全文反而无法按行定位。

**根因**：`TranscriptLines.PatchLines` 是独立的纯文本拍平实现，不经过 `DiffGutter`。

**修复**：`PatchLines` 复用 `DiffGutter.GutterDigits/AssignLineNumbers` +
`MessageRenderer.GutterText`（后者 private → internal），输出 `4 缩进 + gutter + marker + 正文`，
与 `RenderDiffLine` 同一列格式（行号列单一真源）。

**门禁**：`TranscriptVerboseRenderTests.ToolResult_PatchLines_CarryGutterLineNumbers`。

**真机复验**（修复后详情页真值，与主屏逐字符一致）：

```
● Update(D:\project\w2\.xxx\scratch\diff-demo.txt)
  ⎿  Added 2 lines, removed 1 line
    @@ -1,6 +1,7 @@
     1  第一行
     2 -第二行旧内容
     2 +第二行新内容
     3 +新增的第三行
     4  第三行
     5  第四行
     6  第五行
     7
```

## 第二批观察项

- **P17 小终端塌缩与逐条混排**（判定不改）：一轮 3 个 bash 时，`ShouldCondense(rows, count)` 用**进行中**计数判定，
  前 2 条已落 `● Bash(...)`，第 3 条才触发塌缩 → 屏上 2 条 + `⎿ In progress… · 3 tool uses`。
  计数是权威信息（用户可推断省略 1 条），而"塌缩时移除已落条目"会造成视觉跳变并丢失工具名；
  基准靠"本批工具总数"一次性判定（架构差异，需延迟决策），不改。
- **spinner 动词英文**、`Added 2 lines, removed 1 line` 英文：对齐基准文案，不改。

## 第三批：`/help` 面板（P18，仅记录未修）

真机 `/help` 真值（80+ 命令）：

```
❯ /help

● [其他]

  其他

    /fork [name]             创建当前对话的分支
    /compact [自定义摘要指令]       压缩对话上下文以节省 Token，可选自定义摘要指令
    /privacy-settings [show|telemetry on|off|…] 管理隐私设置
    /falv <假定内容> | /falv --status | /falv --judge | /falv --evidence | /falv --continue [rounds|tokens|both
|default]]
  | /falv --budget | /falv --cone | /falv --conflict | /falv --reset 结构化推理引擎（假定→验证→事实）
```

三处问题：

| 编号 | 现象 | 根因 | 判定 |
| --- | --- | --- | --- |
| P18a | 分组标题重复（`● [其他]` + `  其他`） | `HelpCommand` 在 `TabPanel` 内容里又打印一次分类名（tab 栏已显示） | 可修（composition 层，单行） |
| P18b | 只有「其他」一个分组，80+ 命令一锅粥 | `CategorizedRegistry.GetCategorizedEntries` 用 `_categories` 字典查分类，注册链未传 category → 全落 `_defaultCategory` | 需查注册链（`[ChatCommand(Category=…)]` 是否被解析），影响 CLI/TUI 共用面 |
| P18c | 说明列不对齐、长 usage 腰斩 | `$"  {cmd.Usage,-24} {cmd.Description}"` 固定 24 列宽；`/falv` 这类超长 usage 直接撑破并硬折 | 可修（按组内最长 usage 计算列宽 + 超长换行缩进） |

`?` 触发的快捷键面板（`HelpPanel`，11 行固定清单）是另一套实现，形态正常（dim 单列，位于输入框上方）。

> 本批未改代码：P18a/P18c 属 `modules/composition/slash/ai/info/HelpCommand.cs`（CLI+TUI 共用输出），
> P18b 需先确认 `ChatCommandRegistry` 注册时 category 的传递路径，改动面超出「渲染链路」范围，留待用户拍板。

## 门禁与总验收

**真机（修复后）**

- 启动帧：「引擎启动中…」为**灰色**（dim），不再红色；右侧 `未登录 · 运行 /login` 仍为红（真警告，正确）。
- 工具拒绝（guard 拦截）真值：
  `⎿  Error: 危险操作已被阻止（未知命令 'exit' — 未在 catalog 中登记）。如确需执行请切换到 Ask 模式确认`
- 用户拒绝（PD002）真值：`⎿  Error: 用户拒绝工具执行: bash`
- Edit：主屏 `⎿ Added 2 lines, removed 1 line` + hunk + 行号增删行；
  `ctrl+o` 详情页同形态（行号一致）。

**门禁**

- `native_tui.slnx` **3330 + 182 + 38 + 16 全绿**（NativeTui.Tests 3317 → 3330，+13）。
- `Brain.Other.Tests` **607 全绿**（含新增 3 项 patch 透传）。
- `JoinCode.csproj` 带依赖重编 **0 警告 0 错误**。

> 踩坑记录：带依赖重编 CLI 前必须确认无残留 `jcc` 进程占用 `artifacts/bin/JoinCode/**`，
> 否则大面积 MSB3027/MSB3021（本次 222 个拷贝错误，杀进程后 6.5 秒通过）。
> `jcchub` 一并清掉，jcc 启动会自动重拉。
