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

## 门禁

- 新增 `repl/StartupLineRenderTests.cs`（3 项）：状态行 dim 非 Error、文本不变、诊断行 Warning 非 Error。
- 新增 `repl/ToolErrorJsonRenderTests.cs`（9 项）：message 提取、屏上无 JSON 语法、details 拼接、
  普通 JSON 输出不改写、`Error: ` 非 JSON 原样、畸形 JSON 回退、缺 message 回退、isError 路径归一、
  diff 统计优先不被覆盖。

## 验收

**真机（修复后）**

- 启动帧：「引擎启动中…」为**灰色**（dim），不再红色；右侧 `未登录 · 运行 /login` 仍为红（真警告，正确）。
- 工具拒绝（guard 拦截）真值：
  `⎿  Error: 危险操作已被阻止（未知命令 'exit' — 未在 catalog 中登记）。如确需执行请切换到 Ask 模式确认`
- 用户拒绝（PD002）真值：`⎿  Error: 用户拒绝工具执行: bash`

**门禁**：`native_tui.slnx` **3329 + 182 + 38 + 16 全绿**（NativeTui.Tests 3317 → 3329，+12）。
`NativeTui.Host.csproj` / `JoinCode.csproj` 重编 **0 警告 0 错误**。
