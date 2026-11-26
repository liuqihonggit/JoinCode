# TASK107 — 思考链 / 工具调用 / Markdown 渲染链路验收与对齐

> 触发：用户「你继续这样验证，我需要你查看思考链、工具调用、md 渲染，等等，你要帮我做好这一切」。
> 既有约束：真实模型凭据不可用（`~/.jcc/auth.json` 为空、`DEEPSEEK_API_KEY` 401、hub 报
> `Provider 'sensenova' API Key 未配置`），因此改用**项目自带 mock LLM + 直连端点**跑真实 TUI 会话，
> 只替换模型后端，不替换渲染链路（bridge → driver → IScrollItem → Screen → 终端）。

## 1. 验证环境（可复现）

| 件 | 内容 |
| --- | --- |
| 模型后端 | `artifacts/bin/DeepSeek.MockServer/Debug/net10.0/JoinCode.DeepSeek.MockServer.exe --config .xxx/mock-visual/mockserver.json --port 18086` |
| 脚本 | 第 1 轮：`thinking_content`（5 行）+ `tool_calls`（`bash`，命令 `echo 工具调用执行成功; ls -1 \| head -3`）；第 2 轮：富 Markdown（标题/粗斜体/行内代码/删除线/列表/引用/代码块/表格/分隔线/链接） |
| 启动 | `JCC_ALLOW_DIRECT_FALLBACK=1` + `JCC_ENDPOINT=http://localhost:18086/v1` + `JCC_VENDOR=deepseek` → 跳过 Hub（`EngineApplicationBuilder.LoadConfigAsync` 的 E2E/MockServer 路径） |
| 观察 | WT 单窗口，原生分辨率区域截图 + `ctrl+shift+a`/`ctrl+shift+c` 复制到剪贴板逐字符核对（截图在 14px 下会把 `❯`/`>`、`∴`/`::` 看错） |

## 2. 真机发现的问题（每条都有截图/剪贴板证据）

### P1 工具结果多行摘要被压成一行（严重）
`⎿  工具调用执行成功AGENTS.mdartifactsbuild| ExitCode | Success | Duration ||---|---|---|| 0 | OK | 377ms |```json{...}```
` —— 引擎 `BuildSummary` 取 resultText **前 200 字符（保留换行）** 下发，TUI `RenderToolResult` 单次
`WriteText`，`\n` 属零宽字符被跳过 → 四行输出 + 元数据表 + JSON 全挤在一行，且行数只算 1（高度与实际内容不符）。

### P2 引用块 / 段内换行粘连
源码 `> 引用块第一行\n> 引用块第二行` → 渲染成 `▎ 引用块第一行引用块第二行`。
根因在投影层：`MarkdigProjector.ProjectInline` 的 `case LineBreakInline: break;`（首版注释「Core 段落模型无段内断行概念」）
直接丢弃换行；Core 布局层随后把两行当同一段落折行。

### P3 工具标题行显示整段 JSON
`● bash({"command":"echo 工具调用执行成功; ls -1 | head -3","description":"列出目录前 3 项"})`
—— `ToolTitleFormatter.Format` 用 `toolName switch { BashTool => ... }` 严格匹配 `"Bash"`，而桥/部分 provider 传槽位 id `bash` →
降级 `return args`。

### P4 权限对话框同一句话出现两次
```
Tool use
   bash(工具 'bash' 可能执行危险操作，请确认是否继续？)
   工具 'bash' 可能执行危险操作，请确认是否继续？
```
`RenderPermissionToolLine` 把 description 塞进括号，紧接着 `RenderPermissionDescription` 整句重画。

### P5 工具输出块首行与摘要行重复
摘要（resultText 前 200 字符）与 rawOutput 首行天然同源 → `⎿ 工具调用执行成功` 后输出块又从 `工具调用执行成功` 开始。

### P6 表格保留 GFM 源码形态
`| 元素 |   期望   | 状态 |` + `|------|----------|------|` —— 只是做了列宽对齐，没有边框。
基准 `MarkdownTable.tsx` 用 box 边框：`┌ ─ ┬ ┐` / `│ … │` / `├ ─ ┼ ┤`（**行间也画 middle**）/ `└ ─ ┴ ┘`。

### 判定与基准一致、不改
- `hr` → `---`（基准 `formatToken` `case 'hr': return '---'`）
- 无序列表 `- `（基准 `text` in `list_item`：`orderedListNumber === null ? '-' : ...`）
- 删除线 `~~x~~` → 纯文本（基准明确禁用 del）
- 链接显示文字 + OSC 8（基准 `createHyperlink` 同语义）
- `✻ Tinkering... (bash 剩余 118s · thought for 1s)` 等 spinner 文案属状态表达，非绘制差异

## 3. 修复（全部先门禁后实现，TDD 红→绿）

| 问题 | 门禁 | 实现 |
| --- | --- | --- |
| P1 | `repl/ToolResultSummaryRenderTests`（6 项：只渲染首行、高度恒 1、`\r\n` 不漏、跳过前导空行、原文保留供搜索、重复行只出现一次） | `ToolResultItem._summaryLine` = 首个非空行；`HasSummaryRow` 统一测高与渲染 |
| P2 | `markdown/MarkdownSoftBreakTests`（6 项：段落/引用/列表项断行、`\r\n`、首尾换行不产生空行）+ `NativeTui.Markdown.Tests/MarkdigProjectorTests`（2 项投影保 `\n`） | `MarkdigProjector` 把 `LineBreakInline` 投影成 `"\n"` 段；`MarkdownLayout.Tokenize` 产出硬换行标记、`WrapSegments` 遇标记 `FlushLine()`（`\r` 丢弃） |
| P3 | `ToolTitleFormatterTests`（+4 项：`bash`/`read`/`edit` 小写派生、`FormatToolInfo` 归一名） | `Format`/`DisplayToolName` 改大小写不敏感 `Matches`；新增 `CanonicalName`（`bash`→`Bash`，未知原样） |
| P4 | `PermissionRequestItemFormTests.Render_FallbackForm_ZeroBehaviorChange` + `PermissionDialogTests.Render_ToolRowBoldNameDescriptionDim`（已更新为新形态） | `RenderPermissionToolLine` 只画工具名；描述只由描述行承载；工具名走 `CanonicalName` |
| P5 | `ToolResultSummaryRenderTests.Render_SummaryEqualsFirstOutputLine_RendersItOnce` | `DuplicatedOutputRows`：输出块首行与摘要行相同则跳过 1 行（`VisibleOutputRowCount` 与渲染共用） |
| P6 | `MarkdownLayoutTests`（表格 3 项改为 box 边框期望 + 新增多行 middle 用例） | `AppendTable` 生成 top/header/middle/rows/bottom；`TableBorder` 生成 `┌─┬─┐` 系列；`TableRow` 用 `│`；删除 `TableSeparator` |

## 4. 验收

- 测试：`native_tui.slnx` 全套 **3306 + 38 + 16 + 182 全绿**（NativeTui.Tests 3289 → 3306，Markdown 36 → 38）。
- 真机第一轮（修 P1/P2/P6 之前）：见第 2 节截图证据。
- 真机第二轮（修后）：工具栏 `● Bash(echo 工具调用执行成功; ls -1 | head -3)`、结果行 `⎿ 工具调用执行成功`（无重复）、
  引用块两行各带 `▎`、表格 box 边框、权限对话框 `Tool use / Bash / 提示句` 各一次。
- 产物：`NativeTui.Host.csproj` + `JoinCode.csproj` 重编 0 警告 0 错误（DLL 时间戳见提交）。

## 5. 未做 / 已知边界

- 表格超宽（> 终端宽）无垂直降级：基准有 `renderVerticalFormat` + `SAFETY_MARGIN` 守卫，jcc 仍走右缘裁剪。列宽分配也尚未按可用宽度收缩。
- 行尾填空：TUI 绝对定位写满整行，`ctrl+shift+a` 复制会带尾随空格（终端复制整行，非渲染缺陷）。
- 思考链渲染目前只有「`∴ 思考 · ctrl+o 展开推理` + 展开态逐行 dim」，未做基准的折叠动画；`thinking_redacted` 场景仅单测覆盖。

## 6. 第二轮：transcript 详情页（P7–P11）

> 第一轮只看了主屏。`ctrl+o` 详情页是**另一套投影**（`TranscriptLines`），自带一份「块拍平」实现，
> 于是主屏已修好的形态在详情页全部回退——这正是「两处渲染不同源」的代价。

### 现象（修前真值，`ctrl+shift+a`/`ctrl+shift+c` 逐行复制）

| 编号 | 详情页实际输出 | 主屏对照 |
| --- | --- | --- |
| P7 | `元素 \| 期望 \| 状态` / `标题 \| 待验` | `┌──────┬──────────┬──────┐` + `│ 元素 │ 期望 │ 状态 │` |
| P8 | `- 第二项 - 嵌套子项`（子项被拼进父项） | `- 第二项` / `  - 嵌套子项` |
| P9 | `⎿ 工具调用执行成功` / `⎿ AGENTS.md`（连续两行 `⎿`） | `⎿ 工具调用执行成功` / `   AGENTS.md` |
| P10 | `📎 📎 首次注入 · 系统提示词 87432 字符` | `📎 首次注入 · 系统提示词 87432 字符` |
| P11 | `● Bash echo 工具调用执行成功; ls -1 \| head -3` | `● Bash(echo 工具调用执行成功; ls -1 \| head -3)` |

同理顺带发现：详情页表格还直接把 GFM 分隔行 `|---|---|---|` 上了屏（P7 同源问题）。

### 修复

| 问题 | 门禁（`repl/transcript/TranscriptVerboseRenderTests`，先红后绿） | 实现 |
| --- | --- | --- |
| P7/P8 | `Markdown_Table_ProjectsBoxBorders`、`Markdown_NestedList_ProjectsNestedLine` | `TranscriptLines.MarkdownLines` 改为复用主屏同一套 `MarkdownLayout.Layout(document, UnboundedLayoutWidth)`，删掉自带的 `BlockToLines`/`TableLines`/`ItemText` 等拍平辅助方法（单一渲染真源）；宽度给无界值，折行仍由 `TranscriptLines.Wrap` 按详情页真实宽度统一处理 |
| P9 | `ToolResult_OutputFirstLine_NotPrefixedTwice` | 摘要行已带 `  ⎿  ` 时，输出块整体走续行缩进（`IndentedLines(text, firstWithPrefix: summaryLine.Length == 0)`） |
| P10 | `PromptContext_CollapsedSummary_NotPrefixedTwice` | `PromptContextItem.Summary` 自带 `📎 `，投影层不再补前缀（展开态正文是裸提示词，仍需补） |
| P11 | `ToolCall_Title_MatchesMainScreenForm`、`ToolCall_MultilineArgs_ClosesParenOnLastLine` | 详情页工具行按 `MessageRenderer.RenderToolCall` 形态生成：单行 `Name(args)`、多行末行闭括号、续行 2 空格对齐 |

### 验收

- 测试：`native_tui.slnx` 全套 **3317 + 182 + 38 + 16 全绿**（NativeTui.Tests 3311 → 3317，新增 6 项门禁）。
- 真机（产物 `NativeTui.Core.dll` 21:40 重编，Host/CLI 0 警告 0 错误）详情页真值：
  ```
  📎 首次注入 · 系统提示词 87432 字符
  ❯ 跑一遍渲染验收 demo
  ● Bash(echo 工具调用执行成功; ls -1 | head -3)
    ⎿  工具调用执行成功
        AGENTS.md
        artifacts
        build
  ┌──────┬──────────┬──────┐
  │ 元素 │   期望   │ 状态 │
  ├──────┼──────────┼──────┤
  ```
  主屏回归同一份内容逐行核对无变化（`● Bash(...)`、单 `⎿`、`∴ 思考 · ctrl+o 展开推理`、box 表格、`▎` 引用、嵌套列表 2 空格缩进）。
- **样式维度**（剪贴板取不到，只能原分辨率区域截图）：另起短脚本 `.xxx/mock-visual/style-check.json`（单轮、
  无工具调用 → 无需放行、整段落在一屏内）核对 —— h1 `一级标题` = 粗+斜+下划线、h2 仅加粗；
  `粗体`/`斜体`/`粗斜` 字重倾斜可辨；行内代码 `Screen.WriteText` 走 permission 色（青）；列表 `- ` / `1. ` 正常；
  引用 `▎` 行斜体暗色；删除线 `废弃` 为纯文本（与第 2 节「判定与基准一致」项一致，非缺陷）。
  注：主屏回滚区不可键盘上滚（内容已进 scrollback），要在一屏内看全就得把样例缩短。

### 判定不改 / 新增已知边界

- 详情页工具结果里仍能看到引擎下发的元数据 Markdown（`| ExitCode | Success | Duration |` + `|---|` + ` ```json `）：
  `RawOutput` 是**原样终端输出**，详情页按原样呈现；若把它当 Markdown 重排，`cat xxx.md` 这类输出会被错误渲染，
  风险大于收益。主屏仍按折叠预览（`… +7 lines (ctrl+o)`）承载，不受影响。
- 详情页折行后仍无行首缩进（`Wrap` 只保证不越右缘），跨行段落的可读性依赖 `▎`/`∴`/`●` 前缀区分。

## 7. 复现方法与踩坑（下轮直接用）

- **不能用 `-p` 非交互**：权限确认会自动拒绝，bash 永远不执行；必须走 TUI 交互并手动放行。
- **mock 脚本一轮一消耗**：`/v1/chat/completions` 每请求吃一个 `scripted_turns`，探测就绪不能用 POST
  （会吃掉第一轮），改用 `GET /__control__/state`（`total_requests: 0` 即复位成功）；用完重启进程即复位。
- **中文提示词不能靠 `type` 注入**：computer-use 的 unicode 注入在本机控制台被降级成 `?`（`跑` → `?`），
  而空输入时的裸 `?` 正好命中 `ReplShell.IsHelpTrigger` → 弹出「快捷键」面板，看着像 TUI 挂了。
  正确姿势：`Set-Clipboard` 写中文 → 点输入行 → `ctrl+v`（TUI 粘贴路径正常，CJK 显示无误）。
- **`esc` 会清空输入框**（不是取消焦点）：验证输入内容时别按 esc，直接截图/粘贴。
- **截图看坐标不可靠**：全屏截图经预览面板缩放，肉眼读出的坐标要乘缩放比；input 行真实位置用区域截图逐步逼近，
  或直接问区域截图内部的相对 y。
- **14px 下 box 边框/`▎` 在截图里几乎看不见**，结论一律以剪贴板复制的真值为准（截图只做布局佐证）。

