# TASK065 — TUI 全量对齐 Claude Code（十子代理并行冲刺）

> 状态：proposed（2026-10-22，w4）
> 来源：用户指令"短时间内复刻整个 ClaudeCode 的 tui，开十个子代理并行写码，写好关键代码才编译一次"
> 调研证据：六路只读调研子代理（REPL 键位 / 消息渲染 / 输入命令面 / 工具渲染 / 流式打断 / 终端基础），基准 `D:\project\claude-code-rust\claude-code-rev-main\src`，本地 `native_tui/`

## 一、现状基线（防重做）

- ✅ M8-1 逐字流式渲染全链（0bde2c758）：TokenDelta.Text 协议 + StreamingTextItem + Driver 惰性建流式项 + 原位替换
- ✅ M8-2 Esc 打断闭环（e86d2a41a）：排队弹回 + 无有效响应自动恢复 + partial 定格
- ✅ 已对齐良好：Ctrl+R 搜索、Shift+↑ 消息导航、排队输入、斜杠补全（参数+ghost）、权限对话框 Fallback 形态、Emacs 编辑键组+KillRing、Markdown 块模型+代码高亮、spinner 动画全套、diff 三色渲染、子代理分组、inline 主屏渲染语义、thinking 生命周期
- 基准本身已移除 `&` 后台前缀语法（`inputModes.ts:31` 仅匹配 `!`）——勿对齐不存在的行为

## 二、差距总表（六路调研合并去重，扣除已完成）

| # | 差距 | ts 证据 | 级别 |
|---|------|---------|------|
| 1 | ctrl+o transcript 全屏模式（less 键位+/ 搜索+n/N+cur/count+冻结快照+verbose+专用 footer） | REPL.tsx:4446-4456、ScrollKeybindingHandler.tsx:900-959 | **P0** |
| 2 | 工具结果折叠（Bash 3 行+差1行特例/Write 10 行/… +N lines (ctrl+o) 展开） | terminal.ts:7-113、FileWriteTool/UI.tsx:26 | **P0** |
| 3 | Read N files 聚合折叠+700ms 防闪烁 | CollapsedReadSearchContent.tsx:142-396 | **P0** |
| 4 | 工具标题行摘要派生（Bash 2行/160字符截断、per-tool 参数解析） | BashTool/UI.tsx:26-130 | P1 |
| 5 | 消息操作复制（c 全文+p 主参数 12 工具映射+底部操作条） | messageActions.tsx:70-119,296-398 | P1 |
| 6 | 命令面交互闭环（clearScreen/onExitRequested/confirm/prompt 回调全 null） | EngineSession.cs:140-155 | P1 |
| 7 | token/上下文告警（N% until auto-compact / Context low · Run /compact）+ usage 上报 | TokenWarning.tsx:166-177、autoCompact.ts:62-120 | P1 |
| 8 | 通知系统（队列+优先级+timeout 8s+invalidates+fold） | notifications.tsx:38-229 | P1 |
| 9 | per-tool 权限对话框（Bash 命令全文+危险警告） | BashPermissionRequest.tsx:34-70 | P1 |
| 10 | 多行编辑（换行插入+多行渲染+↑↓ 光标优先） | PromptInput.tsx:2173 | P1 |
| 11 | bracketed paste 粘贴折叠（[Pasted text #N] 阈值 800 字符） | PromptInput.tsx:1224-1235 | P1 |
| 12 | `!` bash 直通（模式检测+边框+直执行+进度） | inputModes.ts:12-33 | P1 |
| 13 | 退出确认链（ctrl+c/d 双击+ExitFlow+ExportDialog 邀请） | useExitOnCtrlCD.ts:60-76 | P1 |
| 14 | 未读分隔线（── N new messages ──） | Messages.tsx:631-634 | P1 |
| 15 | Light 主题色值错误（4-5 键继承 Dark 错值）+ 主题选择 | theme.ts:115-191 | P1 |
| 16 | 200 条渲染上限+用户消息 10k 截断 | Messages.tsx:307-339 | P1 |
| 17 | 命令分组排序（recently-used top5→builtin→user→project） | commandSuggestions.ts:292-380 | P2 |
| 18 | ctrl+s stash / '?' 帮助面板 / ctrl+l 重绘 | defaultBindings.ts:85,42 | P2 |
| 19 | spinnerTip（30s /btw、1800s /clear）+ idle dim + darwin/ghostty 字符集 | Spinner.tsx:255-259 | P2 |
| 20 | undo 栈（ctrl+_） | defaultBindings.ts:80-81 | P2 |
| 21 | 小终端塌缩单行（rows < N*9+7） | AgentTool/UI.tsx:469-503 | P2 |

P3 池（本轮不做）：vim 模式、图片粘贴、GlobalSearch/QuickOpen 对话框、ExportDialog、后台任务面板（引擎事件源依赖）、bash 流式进度、teammate/swarm 域、alt-screen 鼠标选择、OSC 8 链接、@ 文件补全、ctrl+g 外部编辑器、CoT Markdown 渲染、diff 语法高亮+行号 gutter。

## 三、十子代理分工（文件独占原则）

| ID | 域 | 独占文件 | 任务 |
|----|----|---------|------|
| S1 | 桥接全链 | `bridge/`（BridgeEvent.cs、ReplBridgeDriver.cs、SimulatedEngine.cs）+ engine 侧（BridgeProtocol.cs、BridgeEventConsumer.cs、usage 收集）+ Host/InProcessEventConsumer.cs | ToolResult 协议扩展（RawOutput/HiddenLines）+ TokenUsage 事件全链 + Driver 路由（usage→S4 契约、RawOutput→S2 item） |
| S2 | 工具折叠渲染 | repl/MessageItems.cs、repl/MessageRenderer.cs + 新 tool_folding/ | 差距 #2#3#4#21：标题行摘要派生、折叠渲染+ctrl+o 展开数据、Read 聚合+防闪烁、塌缩 |
| S3 | 消息列表与操作 | repl/ReplView.cs、repl/MessageCursor.cs + 新 actions/、dividers/ | 差距 #5#14#16：复制提取器+主参数映射+操作条组件、未读分隔线、200 条上限+10k 截断、SetVerboseMode 契约 |
| S4 | 通知与 footer | repl/FooterState.cs、repl/FooterBar.cs、repl/SpinnerRow.cs、repl/SpinnerItem.cs + 新 notifications/ | 差距 #7#8#19：通知队列状态机、TokenWarning 显示、ReplNotifications/TokenUsageTracker 契约类、spinnerTip |
| S5 | transcript 控制器 | 新 transcript/ 目录 | 差距 #1：TranscriptController（less 键位+搜索+n/N+cur/count+冻结快照）+ TranscriptSearchBar |
| S6 | ReplShell 交互集成 | repl/ReplShell.cs（独占） | 全部键位接线：transcript 态分派（S5 契约）、c/p/enter 动作（S3 契约）、ctrl+o 迁移、'?' 帮助、退出确认链（#13）、IShellInteraction 实现（S8 契约）、`!` 检测（S10 契约）、ctrl+l 重绘 |
| S7 | 输入编辑增强 | repl/InputBuffer.cs、repl/PromptInputBox.cs、repl/KeyPressParser.cs + 新 input/ | 差距 #10#11#20：多行编辑、bracketed paste 解析+粘贴折叠、undo 栈 |
| S8 | 权限与命令面 | repl/PermissionRequestItem.cs、repl/PermissionCoordinator.cs + Host EngineSession.cs + 新 IShellInteraction.cs | 差距 #6#9：per-tool 权限形态（Bash 命令全文）、权限键位补齐（tab/space/cycleMode）、命令回调闭环接 TUI |
| S9 | 主题与终端基础 | Theme.cs、terminal/、repl/BannerItem.cs | 差距 #15：Light 色值修复、主题选择 API（ThemeSelection）、退出恢复完善（光标显隐）、darwin/ghostty 字符集 |
| S10 | 命令面与直通 | repl/CommandPickerEngine.cs、repl/suggestions/ + 新 commands_ux/ | 差距 #12#17：recently-used 分组+持久化、`!` bash 直通执行器（BashDirectExecutor）、@ 文件补全（选做） |

## 四、跨子代理契约（编译对齐锚点）

1. **S1 协议**：`ToolResult(Summary, Patch, RawOutput=null, HiddenLines=0)`（WhenWritingNull）；新 `TokenUsage(InputTokens, OutputTokens, ContextTokens=null, ContextLimitTokens=null)`（type: `token_usage`）；Driver 路由调用 S4 的 `ReplNotifications`/`TokenUsageTracker`、S2 的 ToolResultItem 新字段
2. **S4**：新类 `ReplNotifications`（Enqueue(text, priority, timeout)）+ `TokenUsageTracker`（RecordUsage(...) / PercentLeft）
3. **S3**：`ReplView.SetVerboseMode(bool)`；`MessageCursor.ExtractFullText()/ExtractPrimaryInput()`
4. **S5**：`TranscriptController`：IsActive / Toggle / TryHandleKey(KeyPress) : bool / 冻结快照
5. **S8**：接口 `IShellInteraction`（ClearScreen / RequestExit / ConfirmAsync / PromptAsync）；S6 在 ReplShell 实现
6. **S10**：`BashDirectExecutor.ExecuteAsync(command, ct)`
7. **S9**：`ThemeSelection.Resolve(name)` 静态类
8. **S7**：InputBuffer 多行 API（InsertNewLine/MoveCursorUpDown）+ KeyPressParser 产出粘贴块事件

## 五、流程（一次编译策略）

1. ✅ 六路调研 → 本计划
2. **并行写码**：10 子代理同时写生产代码+单测，**禁止 dotnet build / dotnet test / git commit**（并行锁+一次性编译），仅静态自查；发现需越独占文件 → 记未决问题返回
3. **主代理统一集成**：全量 build 一次 → 按报错分派修复 → 跑 NativeTui.Tests + host.tests 受影响域 → commit
4. **验收**：selftest 冒烟 + 手动 exe（ADR 0080）+ 像素回归

## 六、规范要点（子代理必读）

- `.cs` 文件禁止 `using`（GlobalUsings.cs 统一）；新命名空间需在对应目录 GlobalUsings 登记
- XML 文档注释必须（新增/修改函数同步更新，禁删）
- TreatWarningsAsErrors 零警告；AOT 兼容（无反射 emit、JsonContext DTO）
- 功能性文案中文，符号/动词资产保留基准形态（⏺⎿∴ 等）
- 资源 `using var`；枚举集合 BitMask；Span 切片；LINQ 链式+有名函数
- 测试写但不跑（主代理统一验证）；测试文件按域归属独占

<!-- 🤖 Auto Decision: 2026-10-22 -->
<!-- 决策: 十子代理按文件独占域切分 + 契约预定义 + 编译权收主代理 -->
<!-- 原因: ReplShell/MessageRenderer/BridgeEvent 是共享热点，并行 Edit 会互相覆盖；契约先行让并行代码可一次编译对齐；用户要求子代理最少编译次数 -->
<!-- 替代方案: 各子代理端到端自编译（冲突大、编译次数多）；串行批次（慢，违背用户并行要求）——均弃 -->
<!-- 验证: 独占文件清单已逐项查重（每文件唯一 owner）✅ -->

## 七、执行状态（2026-10-22 收口）

| 批次 | 提交 | 内容 |
|------|------|------|
| S1-S5 并行+集成 | b001b0d13/49a228b30 | 桥接协议扩展/折叠渲染/消息操作/通知队列/transcript 控制器 |
| S6 串行接线 | 3ab5c7624 | transcript 收敛+键位接线+聚合路由+帮助面板+退出确认 |
| S7-S10 并行+集成 | cf51e9d92 | 多行编辑+粘贴折叠+undo/per-tool 权限形态+Light 主题修正+命令分组+bash 直通 |

三轮 rebase 融合上游 #448-450（M8 全八批）：聚合/复制保我们的实现，BackgroundTaskPanel/context_usage/命令面闭环取上游，MessageActionsBar 类名让渡上游、我们版改名 MessageActionsFooter。

验证：NativeTui.Tests 1549 + Host.Tests 95 + bridge 126 全绿；Core/Tests/Host/Engine 编译 0 警告。

⚠️ 未接线项（ReplShell/Host 域，下批）：shift+enter 换行/bracketed-paste 启用/undo 键位/ExpandedText 提交（S7）；权限四键位/ModeCycleRequested 订阅（S8）；主题切换入口/光标恢复/DEC 2026 探测/spinner 字符集（S9）；SlashSuggester 分组注入/RecordUse 时机/`!` 前缀检测/@ 触发（S10）。
