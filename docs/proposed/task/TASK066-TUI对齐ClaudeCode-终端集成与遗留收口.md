# TASK066 — TUI 对齐 Claude Code：全量板块分派总规划

> 调研日期：2026-10-23（代码级核对，非文档推断）
> 基准源码：`D:\project\claude-code-rust\claude-code-rev-main\src\`（React + Ink fork）
> 对齐对象：`D:\project\w2\native_tui\`（自研 cell 网格渲染，1740+125+18+16 测试全绿）
> 分派模式：板块 A-G 可并行分派多人；**每板块内部先基建后连线**；**跨板块组合项集中在 G 最后做**

## 〇、分派总览

| 板块 | 主题 | 规模 | 可并行 | 依赖 |
|------|------|------|--------|------|
| A | 终端集成与通知（OSC 标题/tab 状态/进度/焦点） | 中 | ✅ | 无 |
| B | 输入系统升级（kitty 协议/键位配置/RawStdin 切默认） | 大 | ✅ | 无 |
| C | 鼠标与全屏模式（SGR/选区/alt-screen） | 大 | ✅（C7 留 G） | 无 |
| D | 对话框与交互补全（Idle/Export/搜索/effort 行） | 中 | ✅（D6 留 G） | 无 |
| E | 编辑增强（vim 模式/图片粘贴） | 大 | ✅ | 无 |
| F | 渲染细节增强（diff gutter/OSC 8 链接/塌缩/SIGWINCH） | 中 | ✅ | 无 |
| G | 跨板块收口（联动验证/引擎事件源/总验收） | — | ❌ 最后做 | A-F |

统一规范：每项 TDD（🔴单元红→🟢单元绿→编译→文档→提交）；纯 Core 组件走 internal 测试；宿主接线走 NativeTui.Host.Tests + selftest 探针；真实终端行为走 ADR 0080 手动验收。

***

## 板块 A — 终端集成与通知

### 基建（先做）

| # | 项 | 基准定位 | w2 落点 |
|---|-----|---------|---------|
| A1 | OSC 21337 tab 状态序列 | `ink/termio/osc.ts:476` tabStatus / `:453` CLEAR（三字段 `indicator=;status=;status-color=`，`;`→`\;`、`\`→`\\` 转义） | `ansi/Osc.cs` 加 `TabStatus(...)`/`ClearTabStatus()` |
| A2 | 终端标题状态机 | `screens/REPL.tsx:484` AnimatedTerminalTitle + `:473-475`（帧 `⠂⠐` 960ms、静态 `✳`、`CLAUDE_CODE_DISABLE_TERMINAL_TITLE` 禁用、失焦暂停）；`ink/hooks/use-terminal-title.ts:25`（win32=process.title，其余 OSC 0） | 新建 `repl/terminal_status/TerminalTitleTracker.cs`（三态优先级 waiting>busy>idle，`REPL.tsx:1187`） |
| A3 | DEC 1004 焦点上报 | `ink/terminal-focus-state.ts`（focused/blurred/unknown，unknown 视同 focused 不节流）；`CSI I`/`CSI O` 解析 | `DecPrivateModeKind.FocusReporting = 1004`；KeyPressParser 产出 `KeyKind.FocusIn/FocusOut`；焦点三态持有者 |
| A4 | 进度能力判定 | `ink/terminal.ts:25` isProgressReportingAvailable（**WT_SESSION 显式排除**——Windows Terminal 把 9;4 当通知；ConEmuANSI/ConEmuPID 支持） | `terminal/TerminalCapabilities.IsProgressReportingAvailable()` |

### 连线（后做）

| # | 项 | 接线点 |
|---|-----|--------|
| A5 | 三态源驱动 | ReplShell：permission pending/命令对话框 → waiting；TurnStarted→busy；Completed/Interrupted→idle；每拍 `TerminalTitleTracker.Tick()`（动画帧推进 + 变更检测写序列） |
| A6 | 退出清理对称 | WriteExit 尾：标题还原 + `ClearTabStatus()` + `Disable(1004)` + 进度 clear |
| A7 | 失焦冻结 | blurred 时 `AdvanceAnimClock` 冻结（spinner/shimmer/标题动画全停，渲染循环继续）；focused/unknown 不冻结 |
| A8 | 进度上报 | busy→`Osc.Progress(indeterminate)`；回合结束→clear（A4 门控，WT 上零输出） |

### 验收
单元（Tracker 帧序列/三态转移/21337 转义/能力判定表）；selftest 扩展标题/tab 序列断言；手动：Windows Terminal 观察 tab 标题 `✳`/`⠂⠐` 动画。

***

## 板块 B — 输入系统升级

### 基建（先做）

| # | 项 | 基准定位 | w2 落点 |
|---|-----|---------|---------|
| B1 | kitty 键盘协议解析 | `ink/parse-keypress.ts`（CSI u 编码；目前 P4 池——本板块只做**解析能力**不启用） | KeyPressParser 加 `CSI code;mods u` + modifyOtherKeys `27;m;k~` |
| B2 | 键位配置系统 | `keybindings/`（14 文件：defaultBindings 12 context 分组 / loadUserBindings / schema / validate / reservedShortcuts ctrl+c+d 不可重绑） | 新建 `repl/keybindings/`（KeybindingProfile 模型 + 默认表 + json 加载校验 + 保留键检查） |
| B3 | RawStdin 切默认准备 | DSG042 P1/P2 已收口（熔断常驻） | 稳定性观察 + `--readkey-input` 反向逃生开关参数化 |

### 连线（后做）

| # | 项 | 接线点 |
|---|-----|--------|
| B4 | 键位表分派 | ReplShell 六交互态分派链（permission > modelPicker > search > cursor > transcript > suggestion）改经 `profile.Match(context, key)`；context 切换时机对齐基准 |
| B5 | 切默认 | Program.cs：默认 `RawStdinPump`，`--readkey-input` 降级 ReadKey |
| B6 | 双终端矩阵 | Windows Terminal/conhost 实测（paste/IME/宽字符/焦点序列）；通过后归档 ReadKey paste 重组状态机（移 `.xxx/`，修复引用） |

### 验收
单元（kitty 解析/键位匹配优先级/校验报错 Rust 风格）；selftest-input 探针扩展；手动：两终端全键位表实测。

***

## 板块 C — 鼠标与全屏模式

### 基建（先做）

| # | 项 | 基准定位 | w2 落点 |
|---|-----|---------|---------|
| C1 | SGR 鼠标解析 | `ink/parse-keypress.ts`（`CSI < b;x;y M/m` → press/release/wheel/drag） | KeyPressParser/InputPump 产出 MouseEvent |
| C2 | 命中测试 | `ink/hit-test.ts`（nodeCache rect 反向遍历，后绘制兄弟优先） | 渲染后 cell→item rect 缓存 + 反向命中 |
| C3 | alt-screen 全屏模式 | `ink/components/AlternateScreen.tsx` + `components/FullscreenLayout.tsx`（DEC 1049 + StickyPrompt + 模态覆盖层） | ReplShell 双模式扩展（inline 现役 + fullscreen 新增）；DecPrivateModeKind.AlternateScreen 已定义 |
| C4 | 选区状态机 | `ink/selection.ts`（word/line 选择 + shift 扩展 + getSelectedText） | 新建 `repl/selection/SelectionState.cs` |

### 连线（后做）

| # | 项 | 接线点 |
|---|-----|--------|
| C5 | 滚轮滚动 | wheel→滚动区行数滚动（对齐基准 Scroll context 映射） |
| C6 | 选中即复制 | 点击/drag 选择 + `useCopyOnSelect` 语义 + OSC 52 剪贴板（Osc.cs 加 `SetClipboard`，tmux passthrough 包裹） |
| C7 | 全屏×鼠标组合 | **留板块 G**（alt-screen 进入时启用 1006/1049 组合验证） |

### 验收
单元（SGR 解析/命中/选区提取）；手动：真实终端拖选复制、滚轮。

***

## 板块 D — 对话框与交互补全

### 基建（先做）

| # | 项 | 基准定位 | w2 落点 |
|---|-----|---------|---------|
| D1 | IdleReturnDialog | `components/IdleReturnDialog.tsx`（continue/clear/never 三选项 + Esc dismiss；阈值 75min，`CLAUDE_CODE_IDLE_THRESHOLD_MINUTES` 覆盖，`REPL.tsx:3326`；idle 时长格式化 `<1m/Nm/Nh Nm`） | 新建 `repl/idle_return/`（对话框组件 + 空闲计时器） |
| D2 | ExportDialog | `components/ExportDialog.tsx`（导出邀请链） | `repl/export/`（transcript 导出 markdown，复用 CopyTextExtractor sanitize） |
| D3 | GlobalSearchDialog | `components/GlobalSearchDialog.tsx`（全文搜索 + 高亮） | `repl/global_search/`（transcript 快照检索 + 搜索高亮 hook 对齐 use-search-highlight） |
| D4 | QuickOpenDialog | `components/QuickOpenDialog.tsx` | `repl/quick_open/`（复用 AtFileSuggester BFS 索引 + FuzzyPicker 模糊匹配） |
| D5 | effort 行 | /model 对话框 effort 循环（low/medium/high/max） | ModelPickerDialog 加 effort 行（M7-6 遗留登记项） |
| D6 | CostThresholdDialog | `components/CostThresholdDialog.tsx` | **留板块 G**（依赖引擎成本事件源） |

### 连线（后做）

| # | 项 | 接线点 |
|---|-----|--------|
| D7 | 触发时机 | 提交时 idle≥阈值弹 IdleReturn；/export /search /quickopen 注册 SlashCommandSource 命令清单 |
| D8 | clear 语义 | "新会话发送" → 引擎 NewSession/clear（IReplEngine 扩展 + 宿主实现） |

### 验收
单元（各对话框状态机/触发条件）；selftest-commands 探针扩到 56+ 条。

***

## 板块 E — 编辑增强

### 基建（先做）

| # | 项 | 基准定位 | w2 落点 |
|---|-----|---------|---------|
| E1 | vim 引擎 | `src/vim/`（motions：hjkl/w/e/b/0/$/gg/G…；operators：d/c/y/p…；textObjects：iw/aw…；模式状态机 normal/insert/visual） | 新建 `repl/vim/`（纯逻辑引擎，与 InputBuffer 解耦） |
| E2 | 图片粘贴 | `utils/imagePaste.ts` + `pasteStore.ts` + `useClipboardImageHint`（win=alt+v，其余 ctrl+v） | `repl/attachments/`（剪贴板图片检测 + 附件项 DTO） |

### 连线（后做）

| # | 项 | 接线点 |
|---|-----|--------|
| E3 | vim 策略接入 | InputBuffer vim 模式开关（策略组合，对齐 BaseTextInput→VimTextInput）；FooterState 加 vim 模式指示；状态栏模式显示 |
| E4 | 图片粘贴路径 | alt+v/ctrl+v 检测 → 附件占位项上屏 → 随回合提交（引擎消息 DTO 扩展） |

### 验收
单元（motion/operator 契约黄金表）；手动：真实 vim 编辑体验、截图粘贴。

***

## 板块 F — 渲染细节增强

### 基建（先做）

| # | 项 | 基准定位 | w2 落点 |
|---|-----|---------|---------|
| F1 | diff 语法高亮 + 行号 gutter | `StructuredDiff/`（colorDiff）+ FileEditToolDiff | DiffModels 扩展（块级语法域 + +/- gutter 列）；高亮复用 TextMateCodeHighlighter |
| F2 | OSC 8 超链接 | `ink/renderer.ts`（hyperlinkPool intern 池）+ `Link.tsx` | Cell 加 HyperlinkId + HyperlinkPool（StylePool 同款）+ FrameComposer/InlineComposer 序列输出 |
| F3 | 小终端塌缩 | REPL.tsx 小高度单行塌缩 | ReplView 塌缩分支（rows < N*9+7 时 footer/选择器单行化，M7-7 P2 登记） |
| F4 | lastToolInfo 聚合 | spinner 行最近工具摘要 | SpinnerRow 摘要行（M7-7 P2 登记） |
| F5 | SIGWINCH 事件源 | Unix 物理信号 | ConsoleTerminalSizeSource 升级（Unix 信号驱动；win32 保留轮询） |
| F6 | 虚拟滚动增强 | `VirtualMessageList.tsx`（overscan/scrollToIndex/jumpState）+ `OffscreenFreeze.tsx` | VisibleLimit 既有 → overscan 窗口 + 离屏冻结渲染开关 |

### 连线（后做）

| # | 项 | 接线点 |
|---|-----|--------|
| F7 | 消费接线 | StructuredDiff 渲染消费 F1；MarkdownMessageItem 链接渲染消费 F2；ReplView 塌缩分支 F3；SpinnerRow F4 |

### 验收
单元（golden 快照扩展）；L2 截图像素校准（tool/screenshot 管线）。

***

## 板块 G — 跨板块收口（最后做）

| # | 项 | 组合 | 说明 |
|---|-----|------|------|
| G1 | 键位系统全局联动 | B×C/D/E | keybindings 12 context 覆盖鼠标（Scroll context）、对话框（Confirmation/Settings context）、vim 键位联动 |
| G2 | 全屏×鼠标 | C3+C7 | alt-screen 内滚轮/点击/拖选组合验证 |
| G3 | 引擎侧事件源扩展 | D6 + 新域 | CostThresholdDialog 成本事件源；teammate/swarm 域（引擎域扩展，非 native_tui 域）；TrustDialog（宿主 jcc 域决策） |
| G4 | 总收口验收 | A-F 全部 | 全量回归（4961+）；selftest 探针全集；双终端矩阵；L2 截图；**合并 TASK055/064/065/066 所有"待用户执行"手动验收清单**一次性出清 |

***

## 已核对确认已有（防重复造轮子，禁止重做）

双击 ctrl+c/ctrl+d 退出、剪贴板复制委托、bracketed paste 整块/重组、Esc 打断闭环、中断恢复 rewind、权限三档对话框、模型/供应商选择器、斜杠补全+@ 文件补全、transcript 模式+搜索、工具折叠、子代理分组、后台任务面板、历史环+Ctrl+R、spinner 全套动画、未读分隔线、通知队列、Markdown+代码高亮、流式投影。

## 执行说明

1. 各板块独立 branch/PR（w2 域，auto-merge squash）
2. 板块内顺序：基建全部 → 连线全部（编译+提交粒度按项）
3. 跨板块组合项（C7/D6/全部 G）严禁提前做
4. 基准行为以本文档源码定位为准（2026-10-23 代码级核对）

## 收口记录

（各板块实施时逐批追加）
