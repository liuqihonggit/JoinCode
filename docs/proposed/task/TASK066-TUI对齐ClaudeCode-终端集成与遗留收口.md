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

### 板块 A — 终端集成与通知（2026-10-23 完成，主智能体实施）

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| A1 `ansi/Osc.cs` TabStatus/ClearTabStatus（21337 三字段+转义） | TerminalSessionStatusExtensions / Coordinator | ✅ | ✅ 单元 7 测试 golden |
| A2 `repl/terminal_status/TerminalTitleTracker.cs`（⠂⠐ 960ms/✳/失焦冻结/去抖） | TerminalStatusCoordinator → ReplShell | ✅ | ✅ 单元 15 测试 |
| A2 `TerminalSessionStatus.cs` 三态枚举 + 21337 preset 扩展（唯一数据源） | ComputeSessionStatus 归并 | ✅ | ✅ 单元 3 preset 逐色 |
| A3 `KeyKind.FocusIn/FocusOut` + KeyPressParser CSI I/O + FocusReporting(1004) | HandleKeyPress 顶部拦截 | ✅ | ✅ 单元 3 解析测试 |
| A4 `TerminalCapabilities.IsProgressReportingAvailable`（WT 排除/ConEmu/Ghostty/iTerm2） | Host 装配门控 | ✅ | ✅ 单元 7 测试 |
| A5-A8 `TerminalStatusCoordinator.cs` 三路统一（标题/tab/进度 + waiting 挂起不清进度） | ReplShell 每拍驱动 + WriteExit 对称清理 | ✅ | ✅ 单元 11+7 接线测试 |
| 宿主装配 Program.cs（Windows Console.Title/Unix OSC0 分流 + 环境开关） | 交互 REPL 主路径 | ✅ | ✅ Host.Tests 125 + selftest OK |

- 测试：NativeTui.Tests 1762→**1802 全绿**（+40）；Host.Tests 125 全绿；`--selftest` OK
- 决策记录：①进度 waiting 挂起不清（权限期任务仍进行）②tab 门控默认关由宿主开（golden 零污染）③协调器组件化替代散写三路 ④ReadKey 通道恒 unknown 视同 focused（基准语义）
- 手动验收（ADR 0080 待用户真机）：Windows Terminal 观察 tab 标题 `✳`/`⠂⠐` 动画、21337 彩点三态（Windows Terminal 实际丢弃 21337 静默安全）、失焦冻结动画
- 提交链：6d9694c61(A1) → d192a147a(A3) → ff9d2cf57(A4) → 8710e169a(A2) → 5ed934a5d(A5-A8) → 宿主装配

### 板块 B — 输入系统升级（2026-10-23 B1/B2/B4 完成，B5/B6 留待双终端验收后）

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| B1 kitty CSI u + modifyOtherKeys 解析（KeyPressParser 终点符前瞻分派） | RawStdinPump VT 全链 | ✅ | ✅ 单元 26 测试 |
| B1 KeyKind F1-F12（kitty 码点 57414-17 + xterm ~ 15-24） | 键位表/解析器 | ✅ | ✅ 单元含 F 键映射 |
| B2 键位配置系统 10 文件（20 context/105 action/116 默认绑定/保留键校验/用户 json AOT 加载） | ReplShell _keybindings | ✅ | ✅ 单元 87 测试 |
| B4 ReplShell 全局键层表驱动（esc/ctrl 组合/序列键 MatchAction/MatchSequence/IsSequenceStart） | HandleCtrlKey→DispatchCtrlAction | ✅ | ✅ 接线 13 测试 |
| B4 宿主装配（~/.jcc/keybindings.json 用户覆盖 + stderr 错误诊断不阻断） | Program.cs | ✅ | ✅ Host.Tests 全绿 |
| B5 RawStdin 切默认 | Program.cs | ⏸ | 待 B6 双终端矩阵后 |
| B6 双终端实测矩阵（WT/conhost + kitty 协议启用） | — | ⏸ | 待用户真机验收 |

- 测试：NativeTui.Tests 1802→**2157 全绿**（+355；含 rebase main 合入的 187 个 S12 新测试）
- rebase 冲突处理：①ReplShell 构造器参数（main S12 saveExportAsync/searchSource × 我方 terminalStatus/keybindings 并存）②KeyPressParser BuildSimple（main 侧 kitty 简版 'u' 分支被完整版 ParseKittyCsiU 取代成死代码，删除）
- debug 修复链：①AOT 反序列化改 `Deserialize(json, Type, Context)` 重载 ②F 键全链打通（KeyKind→KeyShortcut→Parser→ToString 四处）③保留键全拒条目保留默认（非解绑语义）④Ctrl+R 测试构造笔误（ConsoleKeyInfo 第三参 true=shift → 实际 ctrl+shift+r，旧 `case "r"` 不看 shift 宽容掩盖；表匹配严格化后暴露——修测试为纯 ctrl 位，throw 探针五层定位法）
- ⚠️ flaky 登记：GlobalSearchConfirm 偶发失败（全量高并发下 main 侧 S12 异步防抖时序竞态；单独跑/组合跑/连跑 3 次全量均绿，确认非 B4 缺陷；S12 域不展开修）
- 遗留：模态内部键（权限数字键/选择器方向键/transcript less 键位/搜索模态内循环键）保持硬编码——渐进式决策，用户表只覆盖全局键层
- 提交链（rebase 后新序）：4dbe50f66(A5-A8) → c92d12b6c(A 收官) → e0b219270(B1+B2) → 06a1fb5bb(B4)

### 板块 C — 鼠标与全屏模式（2026-10-23 C1-C6 完成，C7 留板块 G）

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| C1 `repl/Mouse.cs` + KeyKind.Mouse + KeyPress.Mouse 通道 + ParseSgrMouse 解码 | HandleMouseEvent 路由 | ✅ | ✅ 单元 15 测试 |
| C4 `repl/selection/SelectionState.cs` 流式选区状态机（三分类词边界/500ms 双击窗/shift 扩展） | C6 连线 HandleMouse | ✅ | ✅ 单元 24 测试 |
| C2 `repl/hit_test/HitTestRegistry.cs` 半开区间后注册优先 + IScrollItem.HitTestId DIM | 待 item 标注接线（G 域） | ✅ | ✅ 单元 7 测试 |
| C3 `repl/fullscreen/AlternateScreenController.cs`（DEC 1049 + 四鼠标模式 1000/1002/1003/1006 严格反序） | transcript 升级真备用屏独占 | ✅ | ✅ 单元 7 + 集成 6 测试 |
| C5 滚轮行滚动（1 行/事件 — 基准 readScrollSpeedBase；加速度 ramp 登记未做） | HandleMouseEvent | ✅ | ✅ 接线测试 |
| C6 选中即复制（Release 后 settled 语义 + 纯空白跳过） | copySelection 委托 → 引擎剪贴板 | ✅ | ✅ 接线 9 测试 |
| 宿主装配（备用屏 + copySelection） | Program.cs | ✅ | ✅ Host.Tests 125 + selftest OK |

- 测试：NativeTui.Tests 2283→**2377 全绿**（+94）；Host.Tests 125 全绿
- debug 修复链：①主构造函数字段初始化语法错（改传统构造）②`?? static lambda` 需括号 ③`Func<int, ReadOnlySpan<char>>` ref struct 泛型 CS9244 → 自定义 `SelectionLineAt` delegate ④滚轮吞噬旧测试期望改结构化 Mouse 解码
- 基准核实修正：SGR 位序（bit2=Shift bit5=motion bit6=wheel，parse-keypress.ts:573-679）；词边界三分类（空白/词字符/其它——"连续非空白"假设错误）；滚动量 1 行（非 3）；复制时机 Release 后（非拖拽中）
- 遗留：滚轮加速度 ramp（computeWheelStep 40ms 窗 +1 cap 6）、C7 全屏×鼠标组合验证（G 域）、HitTestId item 标注（G 域）、OSC 52 剪贴板序列（当前走引擎 IClipboardService，OSC 52 留 P3）
- 提交链：5b33c2879(C1+C4) → 83991bcd7(C2+C3) → c1ae416ac(C5+C6) → 宿主装配

### 板块 D — 对话框与交互补全（2026-10-23 D1/D5/D7/D8 完成，D6 留板块 G）

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| D1 （三选项+Esc dismiss+格式化时长） | ReplShell 触发状态机 | ✅ | ✅ 组件 16 + 集成 9 测试 |
| D7 触发时机（Submit 拦截 — 五条件：非never/有回合/非斜杠/token门槛/超阈值） | SubmitCore | ✅ | ✅ 集成测试 |
| D8 Clear 语义 | IReplEngine 无新会话接口 — 降级重发+TODO | ✅ 降级 | ✅ 降级路径测试 |
| D5 effort 行（ 独立区块+left/right 循环） | BeginModelPicker 回调+宿主 ApplyEffort | ✅ | ✅ 22 测试 |
| D2/D3/D4 Export/GlobalSearch/QuickOpen | **main S12 已完成 — 不重做** | ✅(main) | ✅ |
| D6 CostThresholdDialog | 留板块 G（引擎成本事件源） | ⏸ | — |
| 宿主装配（CLAUDE_CODE_IDLE_THRESHOLD_MINUTES env 读入） | Program.cs | ✅ | ✅ Host.Tests 125 |

- 测试：2396→**2485 全绿**（+89）
- debug 修复：①Action<PickerEffortLevel> 可空 lambda 过滤 ②**哨兵 -1 修 0 时刻歧义**（_lastTurnCompletionMs>0 防御判定与测试时钟 0 起点撞车——throw 探针隔离五条件定位）
- 基准修正：dismiss = setInputValue 恢复文本（非丢弃）；token 门槛 100_000 + 斜杠豁免（任务书未列、基准五条件补全）；effort 渲染独立区块非列表行、键位全局 left/right（任务书猜测被基准否定）
- 提交链：2077885bf

### 其他板块

E/F 待分派；G 跨板块收口最后做。
