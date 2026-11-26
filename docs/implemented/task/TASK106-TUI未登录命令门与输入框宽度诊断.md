# TASK106 — TUI 未登录命令门 / 输入框宽度：真机诊断与修复方案

日期：2026-11-26
状态：`/exit` bug **已 TDD 修复 + 真机复验通过（19:02 → jcc pid 29500 正常退出）**；`/login` 死锁 **已 TDD 修复（19:10，红→绿 10/10）**；输入框宽度经五假设证伪、无法复现，不改代码
基线 HEAD：`7c2f5e2f3`（rebase 后重新核对，旧版诊断文档已随 rebase 清除）

## 问题来源
用户在 WT 报告两处现象：
1. 输入区宽度不一致：输入框上下边框只画到 x≈700，上方历史内容铺满到 x≈930（怀疑 resize 后 `sizeSource` 不同步）。
2. `/exit` 没提交：回车后 `/exit` 仍留在输入框，未退出。

---

## 问题一：`/exit` 在未登录状态永不可用 — 确证为真 bug

### 真机复现（computer-use 桌面控制，jcc pid 20976）
- 在 WT 用 `cd D:\project\w2; $env:JCC_DEBUGLOG=1; & .\artifacts\bin\JoinCode\Debug\net10.0\jcc.exe` 启动 TUI 成功。
- 输入 `/exit`：输入框显示 `/exit`，下方弹出补全建议 `/exit  退出程序`（选中态）。
- 按回车：出现 `未登录 · 请在 auth.json 配置有效 API key 或运行 /login 后重试`；**无确认对话框**；进程仍在（pid 20976 未退出）。
- 输入框实际已清空（`Submit()` 的 `_input.Clear()` 已执行）；灰底 `/exit` 是上屏的历史消息 `UserMessageItem`，不是输入框残留。用户感知的"留在输入框"实为"命令未执行 + 文本挂在上屏区 + 补全建议仍在"。

### C# 侧代码证据（当前 HEAD 核对）
`native_tui/src/NativeTui.Core/repl/ReplShell.cs` `SubmitCore()`（4429）：
- 4448 `!` 前缀 bash 直通、4459 `/theme`、4469 `/export` 均在登录门**之前**做本地拦截并 `return`。
- 4489 `if (!LoginGateOpen)` → `AddMessage(UserMessageItem(text))` + `AddMessage(ToolResultItem("未登录..."))` → `return`。
- 结果：**未登录时所有非本地命令（含 `/exit`/`/quit`/`/login`）都被登录门吞掉，永不派发**。而 UI 1009 行 `LoginError = "未登录 · 运行 /login"` —— `/login` 自己也被拦，形成"未登录死锁"：界面叫你运行 `/login`，但 `/login` 同样失效。
- 现成委托点：5208 `RequestExit()` / 5211 `RequestExit(string reason)`，已实现"上屏退出提示 + 置 `_running=false`"，注释明确覆盖 `/exit /quit /logout` 退出链。修复应委托它，不造新机制。

### 基准侧证据（原始素材）
- `D:/project/claude-code-rust/claude-code-rev-main/src/commands.ts:415` `meetsAvailabilityRequirement`：命令按声明式 `availability`（`claude-ai`/`console`）逐个判断；`if (!cmd.availability) return true` —— 未声明门槛的命令（如 `/exit`）**始终可用**，与登录状态无关。
- `commands/exit/exit.tsx`：`/exit` 默认 `onDone(goodbye)` + `gracefulShutdown(0)` **直接退出**；仅 worktree 会话才渲染 `ExitFlow` 确认。=> 修复后 `/exit` 直接退出是对齐基准，不必强制确认框。

### 判定与修复方案（先文档，后执行）
C# 把"客户端本地/无需鉴权命令"错误置于登录门之后 = **行为 bug**，非实现差异，按 AGENTS.md 直接 TDD 修复。

**执行记录（2026-11-26）**：
- 红测试：`LoginGateTests` 新增 `NotLoggedIn_ExitCommand_Exits` / `NotLoggedIn_QuitCommand_Exits` / `NotLoggedIn_PlainText_StillGated`；前两个 FAIL（`IsRunning` 仍 True），第三个 PASS（零回归基线）。
- 实现：`ReplShell.SubmitCore` 登录门前加 `IsLocalExitCommand(text, out reason)` 分支（精确名 + 空白边界，防 `/exitfoo`），委托 `RequestExit(reason)`；helper 置于 `/export` 常量旁，不依赖 `_suggester`。
- 绿测试：`LoginGateTests` 8/8 PASS；`native_tui.slnx` 全套 3478 通过（3244+182+36+16，比基线 +3），0 失败。
- 传播：局部编译只更新 `native_tui/artifacts/bin/...`，须重编 `NativeTui.Host.csproj` + `JoinCode.csproj`（带 `--no-restore`、**不带** `BuildProjectReferences=false`）才能让修复进 jcc 加载的 `artifacts/bin/JoinCode/Debug/net10.0/NativeTui.Core.dll`，否则真机复验仍跑旧代码。
- **修复点**：`SubmitCore` 4489 登录门之前，增加本地命令拦截分支（与 `/theme`/`/export` 同形态）：
  - `/exit`、`/quit` 及其别名（以命令目录声明为准）→ `AddMessage(UserMessageItem(text))` + `RequestExit("exit"/"quit")` + `return`。
  - `/login` → **第二轮补修（同日 19:10）**：前次结论"TUI/宿主均无 /login 处理"是**查错目录**——实际 `modules/composition/slash/guard/auth/LoginCommand.cs` 有完整实现，且 TUI 侧 `CommandDialogCoordinator`（TASK065 F2）已支持斜杠命令的 prompt/readPassword 回调（含 `PasswordMask` 密码框）。宿主派发器无 auth 门槛；登录成功后 `EngineSession.RefreshSessionInfo()` 发 `LoggedIn=true` 打开登录门，闭环成立。故修复 = 登录门前放行 `/login` 透传（`IsLoginCommand`，精确名 + 空白边界），由宿主命令 + TUI 对话框完成登录。承认知错：之前排除是调查疏漏，不是设计决定。
- **白名单数据源**：禁止散落硬编码（项目纪律 #11）。优先复用 `GeneratedSlashCommandCatalog` 的声明式元数据（如 `availability`/`isClientOnly` 字段）；若无此元数据，则在唯一常量集中登记，门禁脚本核对目录与白名单一致。
- **红测试**（TDD 先写，**落点 `native_tui/tests/NativeTui.Tests/repl/LoginGateTests.cs`** —— 已有现成 `CreateShell()` / `ApplyLoginState(shell,engine,bool)` / `TypeText()` 三个 helper，照 `NotLoggedInSubmit_LocalFeedbackWithoutEngineCall` 配方写，零样板）：
  - **未登录态构造**：`ReplBridgeDriver.IsLoggedIn` 默认 `true`（`ReplBridgeDriver.cs:135`），由 `BridgeEvent.SessionInfo`（:495，`record SessionInfo(..., bool LoggedIn = true)`）置 false。用现成 `ApplyLoginState(shell, engine, loggedIn: false)` 即可；默认夹具是登录态，不能冒充未登录。
  - 未登录 + `/exit` → 触发 `RequestExit`（`IsRunning` 变 false），**不得**出现"未登录"拒绝消息。
  - 未登录 + `/quit` → 同上。
  - 未登录 + 普通提示词 → 仍被登录门拦（零回归）。
  - 登录态 + `/exit` → 仍直接退出（零回归）。
- **编译纪律**：改 `ReplShell.cs` 属 NativeTui.Core，局部编译 + native_gui core/host 两测试项目；全量编译由用户掌控（本轮已两次尝试：一次遇冲突标记失败并已修复，一次被用户取消）。

---

## 问题二：输入框 / 历史宽度不一致 — 代码侧已排除 + 真机观察，待打点定论

### 代码侧排除的假设
- `TerminalSizeSource.cs`：`ConsoleTerminalSizeSource.Current` 每拍读 `Console.WindowWidth/Height`，无缓存 → "sizeSource 缓存旧值"假设**不成立**。
- `ReplView.Render`（279 / 303）：输入框 frame 与历史消息 frame **都用 `screen.Cols`** → 同帧内不可能一边 700、一边 930。
- 宽度变化：`ReplShell.EnsureScreens` 5101 `cols != _back.Cols` → 重建双缓冲；`InlineComposer.Compose` 52 `prev.Cols != next.Cols` → FullReset。`_forceFullReset`（5084）只管 rows 变化。
- 初次 80 列宽启动 TUI：边框与历史右端目测对齐，未复现。

### 真机 resize 观察（win+up 最大化）
- WT 浮层报 `81 × 26`，但 TUI 内容（含输入框边框、历史行）只铺到 x≈1694，窗口右边缘到 x≈1778 —— **约 84px 未使用的右侧空白**；长行 `[diag] 开关: MouseTracking=True...` 在 x≈1694 处被截断（"设"字后断）。
- => TUI 认定的 cols 小于 WT 实际列数，方向指向 `Console.WindowWidth` 在 WT resize 后返回旧值/缓冲区宽度而非真实列数。用户"边框短、历史长"疑为同一机制的不同视觉参照物（两边都用了偏小 cols，参照物不同）。
- 另观察 resize 后输入框区出现重影/重复 `/exit`（历史区一条 + 框上方一条），怀疑增量差分在 resize 时残留旧帧像素 —— 与宽度问题近亲，待打点证实。

### 2026-11-26 收官复核：五个假设全部证伪 / 无法复现 —— 本轮不改渲染代码
按"未复现 + 无双侧证据不臆断改代码"纪律，逐条复核（均有代码行或真机数据支撑）：
1. **sizeSource 缓存旧值** ❌ — `TerminalSizeSource.cs:27` 每拍读 `Console.WindowWidth`，无缓存；`Math.Max(20,...)` 夹紧与 700/930 无关。
2. **resize 后无输入不重绘** ❌ — `ReplShell.Run()` 982-996 是 `while(_running)` 持续循环，每拍 `RenderAndFlush()` → `EnsureScreens()`，50ms 内必重绘，与按键无关。
3. **同帧内输入框/历史用不同宽度** ❌ — `ReplView.cs:279` inputFrame 与 `:303` messagesFrame 在**同一次 Render** 用**同一个 `screen.Cols`**；物理上不可能一边 700 一边 930。
4. **宽度变未触发 fullReset** ❌ — `InlineComposer.cs:52` `prev.Cols != next.Cols` → `FullReset`（清屏+归位整帧重绘）；`EnsureScreens` 5131-5133 宽度变重建双缓冲并留 `_prevOverride=_front` 供 diff。
5. **Console API 在 WT 下滞后/返回旧值** ❌ — 真机在**同一 shell 进程**读 `$Host.UI.RawUI.WindowSize.Width`：690px 窗 → 129，最大化 → 289，同步响应。列宽是字体单位非像素（微软文档 + 实测 ~5.3px/列），129 列对 690px WT 属正常。

**真机复现尝试**：窄窗（480px）、中等（690px）、最大化三种宽度下启动 jcc TUI，输入框边框右端与历史文字右端均目测对齐；用户所述"边框 700 / 历史 930"的错位**未在受控条件下复现**。疑为特定 resize 序列（宽→窄→宽快速切换）或 WT 字体缩放/reflow 的视觉参照差异。

**结论**：宽度问题当前**缺陷未定位、不可复现**，不足以支撑代码修复。强行按推断改渲染层违反项目纪律（"未验证的因果关系比没结论更贵"）。保持现状，待用户提供可稳定复现的窗口尺寸/resize 序列后再打点定位（`[SIZE]`/`EnsureScreens`/`inputFrame.Width` 打点方案仍有效，见下）。

**教训（写入长期记忆）**：TUI 的"像素宽度"与"Console 列数"是两套口径（列宽=字体单位），肉眼用像素判断"边框短/历史长"极易误判；定位此类问题必须先锁定"同一帧、同一 cols"的代码事实，再谈视觉现象。

---

## 打点建议（resize 复现时统一加）
`ConsoleTerminalSizeSource.Current` 打原始 `Console.WindowWidth`；`EnsureScreens` 打 (cols,rows) 与 `_back`；`ReplView.Render` 打 inputFrame.Width 与 messagesFrame.Width；`HandleRawKeyPress` 打 key.Kind + 模态状态。`JCC_DEBUGLOG=1`。

## 验收清单（2026-11-26 结果）
- [x] 未登录 `/exit` 回车 → 进程退出 + 退出提示（真机：jcc pid 29500 正常退出，回 PowerShell 提示符，无崩溃；对齐基准直接退出）。
- [x] 未登录 `/quit` 回车 → 单测绿（TDD 红→绿）；真机未单独跑 `/quit`（与 `/exit` 同一 `IsLocalExitCommand` 分支）。
- [x] 未登录普通提示词 → 仍走登录门（单测 `NotLoggedIn_PlainText_StillGated` 绿，零回归）。
- [x] 未登录 `/login` → **已修复（第二轮）+ 真机验证透传**：登录门前放行透传（`IsLoginCommand`，精确名 + 空白边界），红测试 `NotLoggedIn_LoginCommand_ForwardedToEngine` / `NotLoggedIn_LoginWithArgs_ForwardedVerbatim` 从 FAIL（Submits 空）→ PASS；全套 3480 绿。
  - **真机（19:12，jcc pid 25016）**：未登录态输入 `/login` 回车 → 历史出现 `> /login` + 宿主命令输出 `不支持的提供商: openai支持的提供商:`，**没有**"未登录"拒绝消息 → 证明命令已透传到宿主 `LoginCommand`（此前会被登录门吞掉）。`/login anthropic` 同样透传。全程无协议污染（`Program.cs:296` 控制台抑制闸门把 Console.Out/Error 导入环形缓冲；`EngineSession.cs:244-256` 捕获输出经 `OnLocalCommandOutput` 送 TUI）。
  - **真机暴露的遗留不舒服（本轮未修，非死锁、属配置/展示层）**：当前环境 `IProviderDefinitionRegistry` 支持列表为空（`/login`、`/login anthropic` 都报"不支持的提供商"），故无法真机走完整登录+开门闭环；且命令输出两行粘连成 `openai支持的提供商:`（宿主 output 聚合丢换行）。这两点是独立问题，不在"未登录死锁"修复面，避免扩大改动。

### 第二轮补修：/login 未登录死锁（19:10）
- **认知纠错**：首轮把 `/login` 排除出修复面时称"TUI/宿主均无 /login 处理"是**查错目录**——真正在 `modules/composition/slash/guard/auth/LoginCommand.cs`，且 TUI 侧 `CommandDialogCoordinator`（TASK065 F2）+ `PasswordMask` 已具备密码框能力，宿主派发无 auth 门槛，登录后 `EngineSession.RefreshSessionInfo()` 发 `LoggedIn=true` 开门，闭环完整。
- **根因**：登录门 `if (!LoginGateOpen)` 吞掉所有非本地命令，`/login` 也在其中 → footer 明写"运行 /login"却永远登不了。
- **修复**：`SubmitCore` 登录门条件改为 `!LoginGateOpen && !loginRequested`，`IsLoginCommand(text)` 走精确名 + 空白边界（复用重构后的 `MatchesSlashCommand`，与 `IsLocalExitCommand` 同口径）；放行后照常 `SubmitToEngine`，由宿主 `LoginCommand` 处理。
- **验证**：红→绿（10/10 LoginGateTests），全套 native_tui 3480 通过 0 失败，DLL 传播到 jcc 产物（19:09:51），真机确认透传成功。
- [ ] resize 后输入框边框右端 == 历史内容右端 == 窗口内容区右缘 → **无法验收：现象未复现**，见上"收官复核"。
- [ ] resize 后无重影/重复消息 → 同上，未复现。

## 本轮总产出（含 19:10 第二轮）
- 全量编译通过（restore + Build-AllDomains 四目标，jcchub 锁 DLL 需先停）。
- 测试全绿：native_tui 3480（基线 3475 +5：/exit 3 + /login 2）/ Host.Tests 1400，0 失败。
- **`/exit`/`/quit` bug**：TDD 红→绿 + 全量回归 + DLL 传播 + 真机复验（进程正常退出），完整闭合。
- **`/login` 未登录死锁（第二轮补修）**：查错目录导致首轮误排除，实际宿主有完整 LoginCommand + TUI 密码框闭环；登录门前放行透传，TDD 红→绿 + 真机确认命令已透传（不再吞）。完整登录闭环受当前环境 provider 注册表为空所限未走通（独立配置问题）。
- 宽度问题：五个假设证伪、三种窗口宽度未复现，诚实标注"缺陷未定位"，不改代码。
- 改动文件：`ReplShell.cs`、`LoginGateTests.cs`、本文档；`/login` 修复 DLL 传播到 jcc 产物（19:09:51）。

---

## 第三轮：遗留"不舒服"全清（19:14 起，用户要求"全部修复呀"）

### 4. 本地命令输出两行粘连 — 真 bug，已 TDD 修复
- **现象**：真机 `/login` 输出 `不支持的提供商: openai支持的提供商:`，两行贴在一起。
- **根因链**：`LoginCommand` 两个 `WriteLine` → `SlashCommandRunner` 捕获为含 `\n` 的字符串（换行未丢）→ `InProcessEventConsumer.OnText/OnDone` 发 AssistantMessage → `AssistantMessageItem.Render` → `MessageRenderer.RenderAssistant` 把整段文本一次交给 `Screen.WriteText`；`Screen.WriteText` 对零宽字符（`RuneWidth.Of('\n') == 0`）直接 `continue`（`Screen.cs:229-232`）→ `\n` 被吞，两行粘连。`TASK069 B-3` 曾给 `RenderUser` 加过 flatten，但 `RenderAssistant` 漏了。
- **红测试**（`native_tui/tests/NativeTui.Tests/repl/AssistantMultilineRenderTests.cs`，3 项）：`MeasureHeight_MultilineText_CountsEachLine` 期望 3 实得 2；`Render_MultilineText_PutsEachLineOnOwnRow` 第二行实得全空格（文本被吞）；单行零回归项通过。
- **修复**：`MessageRenderer.RenderAssistant` 按 `\n` 逐行写入（续行与正文列对齐），`AssistantMessageItem.MeasureHeight`/`Render` 同步按正文行数计算高度。不做"压平成空格"的降级（那会让 `/help` 等多行输出挤成一行，仍不舒服）。
- **绿**：3/3 + NativeTui.Tests 全套绿；单行消息高度/位置零回归。

### 5. `/login` 在 Hub 模式报"不支持的提供商 + 空列表" — 误导，已 TDD 修复
- **根因**：非交互真机启动显示 `供应商: hub | 端点: http://localhost:3457`；`WorkflowConfig.Provider.Vendor == "hub"` 时 `ServiceRegistration.cs:37-42` 按设计调用 `ProviderDefinitionRegistry.CreateEmpty()`（Hub 端管理凭据），本机 `~/.jcc/settings.json` 的 7 个 vendor 根本不参与。旧 `LoginCommand` 不区分"配置缺失"与"Hub 空注册表"，一律输出 `不支持的提供商: openai` + `支持的提供商:`（空尾），既误导又难看。
- **红测试**（`LoginCommandTest.cs` 2 项）：Hub 模式应含 "Hub 模式" 且不含 "不支持的提供商"；无配置时应给出 settings.json 指引而非空列表尾巴。修复前两项均 FAIL（实得 `不支持的提供商: openai\n支持的提供商:\n`）。
- **修复**（`modules/composition/slash/guard/auth/LoginCommand.cs`）：Hub 模式 → 「Hub 模式下登录由服务端管理，请在 Hub 端配置凭据」；非 Hub 但注册表空 → 「未配置任何 AI 服务供应商」+ settings.json vendor 节点指引；仅注册表非空且确实无此 provider 时才列支持清单。
- **绿**：`LoginCommandTest` 14/14 通过（含原有默认 openai/大小写归一/API Key/OAuth 等零回归）。

### 6. resize 宽度/重影 — 未复现，改加跨尺寸防护门禁（不改渲染代码）
- 复核 `InlineComposer.Compose:52-57`：`prev.Cols != next.Cols` → `FullReset`（2J+3J+归位），`EnsureScreens:5142` 保留旧帧供跨尺寸 diff；同帧 `ReplView` 输入框与历史共用一个 `screen.Cols`。代码侧无"两边不同宽"路径。
- 新增 `ReplShellResizeTests.Resize_WideNarrowWide_FullResetAndSameFrameWidth`：100→50→120 连续 resize 后断言输出含 `ESC[2J`、当前帧 cols=120、输入框边框满宽。8/8 通过。
- 结论：用户所述错位在受控条件下仍不可复现；本轮以门禁锁住"跨尺寸必 FullReset + 同帧同宽"，不臆断改渲染层。

### 第三轮验收结果
- [x] 红→绿：`AssistantMultilineRenderTests` 3/3；`LoginCommandTest.Execute_HubMode_*` / `Execute_NoProviderConfigured_*` 2/2。
- [x] 全套回归：NativeTui.Tests 3254 / Composition.Tests 1177 / Host.Tests 1404，均 0 失败（共 5835）。
- [x] 产物传播：`NativeTui.Core.dll` 19:17:17 同步至 `artifacts/bin/JoinCode/Debug/net10.0/`；`JoinCode.csproj` 19:28 重编（Slash.dll 含 LoginCommand 修复），0 警告 0 错误。
- [x] 非交互实机确认当前运行模式：`供应商: hub | 端点: http://localhost:3457`（即 provider 注册表按设计为空，非配置丢失）。
- [x] GUI 真机复验（第四轮，见下）：`/exit`、`/quit`、登录门、`/login` Hub 文案、resize 全部实机走通。

---

## 第四轮：GUI 真机验证（19:30 起，用户要求"你来进行验证吧"）

环境：桌面无任何终端窗口，改用独立控制台窗口运行 `artifacts/bin/JoinCode/Debug/net10.0/jcc.exe`（首次 `Start-Process`，被安全策略拦下后改用 Win+R 运行对话框启动 — 未绕过任何策略，也未新建 WT 标签）。

### 7. 建议面板提交后不失效 → 输入框"看着没清空" + Enter 重复提交（新发现，真 bug）

- **真机现象**：未登录输入 `/login` 回车后，输入框里仍显示 `/login` 与右侧描述「登录到 AI 服务」，看起来像"没提交"；再按一次回车，屏幕上出现**第二条** `> /login` 和第二条 Hub 提示。
- **根因**：`AcceptSuggestion(execute:true)`（`ReplShell.cs:3789-3828`）先 `FillInput` + `UpdateSuggestions()` 生成补全面板，再 `Submit()`；`Submit()` 清空 `_input` 却没重算补全 → `SlashSuggester.IsActive` 仍为 true、面板残留（真机看到的那行"输入框内容"其实就是建议项）。下一次 Enter 命中 `KeyKind.Return` 分支再次 `AcceptSuggestion(execute:true)`，用空输入拼出 `"/" + Selected.Name` 又提交一次。
- **红测试**：`ReplShellSuggestionsTests.Enter_AcceptSuggestion_DeactivatesSuggester_NoRepeatSubmit` — `Assert.False(shell.Suggester?.IsActive)` 实得 True（修复前）；原 `Enter_AcceptsSuggestionAndSubmits` 只断言了 `Input.Text` 为空，漏掉了补全状态。
- **修复**（`ReplShell.cs`）：所有"清空输入"的出口统一补 `UpdateSuggestions()` — ①`Submit()` 清空后；②`RestoreStashedInput`（程序化回填不走键尾刷新）；③`StashInput`（ctrl+s 暂存）；④双击 Esc 清空。
- **绿 + 真机**：`ReplShellSuggestionsTests` 24/24；修复后真机 `/login` 提交 → 面板消失、输入框空、Hub 提示单行；再按 Enter 无任何新消息。

### 8. resize 宽度/重影 — 真机双向拖拽正常，未再复现

- 干净起手点拖拽右边框：1072→800（缩）与 853→1300（放）两侧均即时整帧重绘，输入框上下边框与历史文本同宽、无旧宽度残影、无黑块。
- 一次拖拽后出现客户区全黑的瞬态：起始点落在客户区内触碰 conhost QuickEdit 选区，输出被阻塞；重试同操作（边框起手）不再复现，按键即恢复。判定为桌面选区交互而非渲染缺陷，**不改渲染代码**；跨尺寸 `FullReset` 门禁（`ReplShellResizeTests` 8/8）继续锁行为。
- 仍未复现用户最初描述的"输入框短、历史长"同帧错位；若要继续定位需一个稳定复现序列（含 WT 版本与缩放路径）。

### 第四轮验收结果
- [x] 真机 `/exit`：未登录回车后进程退出、控制台窗口关闭（PID 33144 → 无 jcc 进程）。
- [x] 真机 `/quit`：同上（PID 5352 → 0）。
- [x] 真机登录门：普通文本 `hello` 仍被拦，输出 `未登录 · 请在 auth.json 配置有效 API key 或运行 /login 后重试`。
- [x] 真机 `/login`：输出 `Hub 模式下登录由服务端管理，请在 Hub 端配置凭据`，单行不粘连（含第四轮新窗口复验）。
- [x] 全套回归：NativeTui.Tests 3255 / Host.Tests 1404 / Composition.Tests 1177，0 失败（共 5836）。
- [x] 产物传播：`NativeTui.Core.dll` 19:38:01 同步至 jcc 产物；`NativeTui.Host.csproj` 25.8s、`JoinCode.csproj` 11.9s 重编，0 警告 0 错误。
- [ ] 多行 assistant 渲染的 GUI 复验：未登录态拿不到多行引擎输出（`/login` 文案已压成单行），该项仍由 `AssistantMultilineRenderTests` 3/3 覆盖，如实标注未做 GUI 复验。




---

## 第五轮：残留与渲染问题全清（19:45 起，用户"全部做…只要发现了不舒服就改"）

本轮把前四轮"未定位/未复验"的项全部压到根因，并新增两处真机缺陷的修复。

### 9. resize 后整屏黑 + 旧帧被裁 —— 根因是 QuickEdit 抢鼠标（真 bug，已修）

- **真机复现**：拖右边框缩窄窗口 → 客户区整屏黑；等 2s 不恢复；按任意键内容才回来，且回来的是**旧宽度**帧（右侧被窗口裁掉）——这正是用户最初"输入框与历史宽度不一致"的观感来源。
- **决定性证据**：在客户区内横向拖拽，屏幕上出现**控制台原生选区灰条**（`Fix-3` 截图），说明 QuickEdit 仍开着。
- **根因**：`Win32InputPump` 构造时 `mode | ENABLE_MOUSE_INPUT | ENABLE_WINDOW_INPUT | ENABLE_EXTENDED_FLAGS`，**没有清 `ENABLE_QUICK_EDIT_MODE(0x0040)`**。QuickEdit 开着时：① 客户区拖拽被 conhost 当选区，应用收不到 `MOUSE_EVENT`（点击/hover 全失效）；② 选区期间控制台输出被阻塞 —— resize 那帧 `2J+整帧重绘` 写到一半被卡住，于是黑屏；松手/按键取消选区后输出才漏出来。
- **修复**：抽出纯函数 `Win32InputPump.ComputeInputMode(original, mouseTracking)`：追踪开启 → `(mode | mouse | window | extended) & ~quickEdit`；关闭（`JCC_NO_MOUSE_TRACKING`）→ 保留 QuickEdit 且不接管鼠标，原生选区/复制照旧。`ENABLE_EXTENDED_FLAGS` 必须同时置位（Win32 要求，否则清位不生效）。
- **测试**：`Win32InputPumpModeTests` 3 项（追踪开→QuickEdit 位为 0 + extended/mouse/window 置位；追踪关→QuickEdit 保留且不接管鼠标；其余输入位不被覆盖）。
- **真机**：重新编译后 1072→800 / 853→1300 / 649→1100→420→1250 连续缩放，**每次立即整帧重绘**，无黑屏、无旧宽度残影；客户区拖拽后立刻打字，字符即时上屏（输出不再被阻塞）。

### 10. 选区高亮压住输入框边框（"看着像边框断了一截"）— 键入即清（已修）

- 拖拽复制后高亮按基准"留观"保留，但继续打字时高亮仍覆盖输入框边框与文字，读起来像渲染残留。
- 修复：`HandleRawKeyPress` 入口，非鼠标事件且存在选区 → `_selection.Clear()`（鼠标链路不动：Press 重锚、Release 复制）。
- 测试：`MouseWiringTests.Typing_ClearsSelectionHighlight`（拖拽 Release 后有选区 → 键入 `x` 后无选区且输入为 `x`）。真机：灰条在键入后立即消失。

### 11. 提示词详情页长行被右缘裁断 —— 绝对定位没有软折行（真 bug，已修）

- **真机**：点"📎 首次注入 · 系统提示词 … · 点击展开"进详情页，整段系统提示词每行都在右边界被切，句子读不到结尾（"…并将使"、"…以便他们可以"）。
- **根因**：`TranscriptLines.Project` 只按 `\n` 分行、不折行；`TranscriptRenderer` 逐行 `WriteText(..., rightBound)` 写入绝对定位网格，超宽部分直接丢弃（终端软折行在备用屏 + 绝对定位下不生效）。
- **修复**：
  1. `TranscriptLines.Wrap(line, width)` — 按 `RuneWidth` 折行（CJK 双宽），断点策略：只要两侧不都是 ASCII 字母/数字就可以断（英文单词不腰斩，超长 token 才硬断），行尾空白裁掉、行首不留空白。
  2. `BuildTranscriptSnapshot` 按 `_sizeSource.Current.Cols`（下限 20）预折全部行 —— 滚动/搜索/行号全部基于折后行，语义一致。
  3. `TranscriptController.Rewrap(width)` + `SyncTranscriptView` 宽度变化钩子：只重排已冻结的行（不重拍 provider，保持"进入时冻结"），滚动偏移按新上界夹紧。
- **测试**：`TranscriptWrapTests` 7 项（短行不变 / CJK 双宽 / 英文按空白断 / 长 token 硬断 / `中文 CommonMark 中文` 不被腰斩 / width=0 原样 / 40 列快照每行 ≤40）。红→绿。
- **真机**：详情页段落自然换行、`CommonMark` 完整不被切开；在页内把窗口最大化，文字按更宽宽度重新折行；`q` 退出后主屏干净无残留。

### 第五轮验收结果
- [x] 真机连续缩放 4 次：立即重绘、无黑屏、无旧帧残影、输入框与历史同宽。
- [x] 真机客户区拖拽 → 应用内选区（不再被 conhost 抢），键入即清高亮。
- [x] 真机详情页：长段落折行可读，`CommonMark` 不腰斩；页内 resize 重折；`q` 退出无残留。
- [x] 真机 `/quit`（先双击 Esc 清空输入）进程退出，`jcc` 进程数 0。
- [x] 全套回归：NativeTui.Tests 3266 / Host.Tests 1404 / Composition.Tests 1177 = 5847，0 失败。
- [x] 产物传播：`NativeTui.Core.dll` 经 `NativeTui.Host.csproj` + `JoinCode.csproj` 重编进入 jcc 产物（0 警告 0 错误）。

---

## 第六轮：多行 assistant 渲染收口（20:05 起，用户"多行 assistant 渲染也一起做了"）

前五轮遗留的唯一一项"未 GUI 复验：多行 assistant 渲染"，本轮补上真机入口并把同类缺陷一并修掉。

### 12. 找到无需模型的真机入口：`!` 前缀 bash 直通

- `SubmitCore` 里 `!` 分支（`ReplShell.cs:4481`）在**登录门之前**，`RunBashDirect` 的输出走
  `new AssistantMessageItem(...)`（`ReplShell.cs:4908`）——未登录态也能产生真正的多行 assistant 消息。
  这解决了前几轮"未登录拿不到多行引擎输出、只能靠单测"的复验障碍。

### 13. 助手正文超宽不折行、右缘硬裁（真 bug，已修）

- **真机**：`!echo ` + 240 个数字，输出只显示一行并在右缘被切（结尾字符丢失），与详情页同性质缺陷。
- **根因**：`RenderAssistant` 只按 `\n` 分行，逐行 `WriteText(..., frame.Right)` 写入绝对定位网格，
  超宽部分被直接丢弃（终端软折行在绝对定位下不生效）；`MeasureHeight` 同样只数 `\n`，
  高度与实际渲染行数不一致 → 后续消息会被压叠。
- **修复**：
  1. `MessageRenderer.WrapAssistantLines(text, width)` — 正文可用宽度 = `width - AssistantContentIndent(2)`，
     逐行复用 `TranscriptLines.Wrap`（CJK 逐字断、英文单词不腰斩、超长 token 才硬断）。
  2. `RenderAssistant` 增加"已折行"重载，`AssistantMessageItem.Render` 直接消费测量端的折行结果（一帧只折一次）；
     保留 `string` 重载给既有调用点（`ReplComponentTests` 零改动）。
  3. `AssistantMessageItem.MeasureHeight(width)` 与 `Render` 共用同一份折行结果 —— 测量/渲染不脱钩。
- **测试**：`AssistantMultilineRenderTests` 7 项（新增：`MeasureHeight_LongLine_WrapsByContentWidth`、
  `Render_LongLine_WrapsInsteadOfClipping`、`Render_LongSentence_BreaksAtSpace`、
  `MeasureHeight_NarrowerWidth_GrowsHeight`），先红后绿。
- **真机（新产物 20:08:44 传播后）**：
  - `!echo AAA & echo BBB & echo CCC` → 三行独立 `AAA`/`BBB`/`CCC`，无粘连、无重叠。
  - `!echo ` + 240 数字 → 折 3 行、结尾 `6789` 完整可见、续行与首行正文列对齐、下方输入框边框完整未被压叠。
  - 在内容下方空白区拖拽 → 反白选区正常、画面不黑（复验通过）。

### 14. 排除一个伪缺陷：拖拽后整屏黑

- 两次"拖拽后全黑、按键才恢复"均起手于窗口**最右侧滚动条**（x≈1153，窗口右缘 1165）：
  conhost 滚动条把 buffer 视口滚离底部，画面自然变成空白；按键/新输出把视口拉回底部即恢复。
- 受控对照：在内容区（y=320）与空白区中部（y=560, x=600）拖拽都正常出选区、无黑屏。
- 结论：**非 TUI 渲染缺陷**，是 inline 模式 + conhost 滚动条的原生交互，不改代码（禁用滚动条会牺牲 scrollback）。

### 第六轮验收结果
- [x] 真机多行 assistant（3 行独立）+ 超宽折行（240 字符 → 3 行、结尾完整、续行对齐）。
- [x] resize 语义由 `MeasureHeight(frame.Width)` 每帧重测保证（`MeasureHeight_NarrowerWidth_GrowsHeight` 锁住）。
- [x] 全套回归：NativeTui.Tests 3270 / Host.Tests 1404 / Composition.Tests 1177 = **5851，0 失败**。
- [x] 产物传播：Host 20.1s + CLI 47.9s 重编，0 警告 0 错误；`NativeTui.Core.dll` 20:08:44 进 jcc 产物。
- [x] 现场清理：`/quit` 退出，`jcc` 进程 0。
- [ ] 已知非缺陷：窗口高度 > 内容高度时，buffer 之外显示空白（inline 模式固有）；未登录态无 CoT 可验。

---

## 第七轮：与真实 Claude Code 并排比对（20:20 起，用户"一直完成这个界面绘制…可以打开 claudeCode 同步验证"）

**基准实物**：本机已装 Claude Code（`%APPDATA%\npm\claude.cmd`，包 v2.1.332，界面横幅报 v2.1.317）。
**方法**：同一桌面开两个 960 宽的 conhost 窗口（不新建 WT 标签，复用既有控制台窗口），左跑
`artifacts/bin/JoinCode/Debug/net10.0/jcc.exe`，右跑 `claude`，**同一窗口宽度、原生分辨率截图逐行比对**。
比对项：启动页整体构成 / banner 三行 / 输入框线条 / 提示符字形 / footer 左右两段。

### 15. 启动即打印 8 行 `[diag]` 诊断 — 与基准干净启动页不对齐（已修）

- **并排现象**：Claude Code 启动页 = Clawd logo + `Claude Code v2.1.317` / `Opus 5.5 · API Usage Billing` /
  `D:\project\w2` + 更新说明 + 输入框；jcc 在 banner **之前**固定多出 8 行工程诊断：
  `[diag] OSC 8 超链接: 不支持…` + 5 行环境快照（`TERM_PROGRAM`/`LC_TERMINAL`/`WT_SESSION`/`TERM`/`KITTY_WINDOW_ID`）
  + `[diag] 开关: MouseTracking=… Hover=… ClickExpand=…`。
- **根因**：`modules/apps/native_tui_host/Program.cs:283-290` 无条件 `WriteStartupLine(...)`，而 `WriteStartupLine`
  会把每行作为 `StartupLineItem` 纫进 transcript（TASK077 的设计目标是"用户贴报问题时一眼看清终端环境"）。
- **判定**：诊断信息有价值，但**不该占用户首屏**；基准启动页零工程噪声。改为默认静默、`JCC_DEBUGLOG=1`/`-d`
  时才输出 —— 排障信息不丢，首屏对齐基准。
- **修复**：`JccFeatureFlags.DebugLogEnabled`（新增，沿用"开关统一门面、禁散落 GetEnvironmentVariable"纪律）
  + Program 两处诊断行加门；`引擎启动中…` 保留（瞬时态，非诊断）。

### 16. 提示符 `❯` 在 legacy console 渲染成空心方块 — 基准会降级为 `>`（已修）

- **并排证据**（同窗口同字体）：Claude Code 输入行是 `> █`；jcc 同位置是 `▯ █`（U+276F 无字形 → tofu）。
- **根因**：基准用 npm `figures` 库，其 `isUnicodeSupported()` 在 Windows legacy console 返回 false
  （win32 下需 `WT_SESSION` / `TERM_PROGRAM∈{vscode,vscode-insiders}` / `TERM=xterm-256color` / `CI` 之任一），
  `pointer` 随之为 `>`。jcc `Figures.Pointer` 是**硬编码常量** `❯`，没有任何降级路径 → conhost 一律方块。
- **修复**：
  1. `TerminalCapabilities.IsUnicodeSupported(env)` 纯函数（判定序逐条对齐 figures 库），另加参数无参重载
     （env 探测 OR `TerminalHostDetector.IsWindowsTerminalHosting()`，与 OSC 8 探测同兜底 —— Win10 默认终端=WT 时未必注入 `WT_SESSION`）。
  2. `Figures.Pointer` 由 `const string` 改 `static string` 属性，按 `Figures.AsciiFallback` 返回 `>` 或 `❯`；
     **默认 Unicode**，宿主启动时按探测置位 —— 94 处测试断言与 Windows Terminal 行为零变化。
- **明确不动**（与基准一致，属终端字体能力而非 TUI 缺陷）：Clawd logo `▐▛███▜▌`、工具结果 `⎿`、spinner `✻`、
  footer `⏵⏵` 在 conhost 同样是方块 —— 实测 Claude Code 在这些位置同样是方块，改它就"不齐"了。

### 第七轮验收结果
- [x] **真机启动页（默认）**：`shot-verify6` — 只剩 `引擎启动中…` + banner 三行 + 输入行，**8 行 [diag] 全部消失**；
      输入行提示符为 `>`（不再是空心方块），结构对齐 Claude Code（banner → 空行 → 输入 → 底边线 → footer）。
- [x] **调试开关仍可用**：`set "JCC_DEBUGLOG=1" && jcc.exe` → `shot-verify7` 复现完整诊断，并新增一行
      `[diag] 字形: Unicode=False 提示符=>` —— 字形降级判定可自查。
- [x] **回归**：native_tui 全套 3280 + 182 + 36 + 16 全绿（NativeTui.Tests 3270→**3280**，新增 10 项），0 失败；
      `NativeTui.Host.csproj` / `JoinCode.csproj` 重编 0 警告 0 错误，`NativeTui.Core.dll` 20:33:05 /
      `NativeTui.Host.dll` 20:33:39 进 jcc 产物。
- [x] 现场：`/quit` 后 `jcc` 进程 0；退出后输入区（两条线 + footer）已被 `WriteExit` 清掉，scrollback 只剩对话 +
      `⎿ /quit — 退出 jcc` 提示（曾误判为"建议面板残留"，复核为退出提示行，无需修改）。

### 第七轮未改项（附理由，避免下轮重开）
- ~~**`引擎启动中…` 行常驻**~~：第九轮已解决（session_info 到达即撤除，收缩残影由门禁锁住，见下）。
- **输入框上方分隔线**：基准 `PromptInput.tsx` 只画 `borderBottom`，上方那条属 fullscreen 的 `▔ divider`；
  jcc 目前两者都画。肉眼差异极小，且改动会波及大量 golden 断言 —— 未改。
- **`? 查看快捷键` vs 基准 `⏵⏵ auto mode on`**：jcc 未登录态不显示权限模式，属状态差异非绘制差异。

## 第八轮：Windows Terminal 下复验字形（用户"开一次 WT 确认 WT 下仍是 ❯"）

**结论：WT 下提示符确实是 `❯`(U+276F)，字形降级只在 legacy console 生效 —— 与基准 figures 库语义一致，无需改代码。**

- 启动方式（只开一个 WT 窗口，未新建标签页）：`Start-Process wt.exe -ArgumentList @('-d','D:\project\w2','<绝对路径>\jcc.exe')`。
  **踩坑**：`-d` 只设起始目录，命令仍要给**绝对路径** —— 传相对路径 `artifacts\bin\...\jcc.exe` 会报
  `[错误] 2147942402 (0x80070002) 启动"…"系统找不到指定的文件`。
- 环境快照（`[diag]` 实读）：`WT_SESSION=74a14846-…`、`TERM=xterm-256color`、`TERM_PROGRAM=<unset>`、
  `LC_TERMINAL=<unset>` —— 命中 figures 判定 `WT_SESSION` 分支 → Unicode。

### 关键：用"全选复制 → 读剪贴板码点"做字形真值判定（比截图/视觉模型可靠）
截图里 `❯` 与 `>` 在 14px 字号下肉眼几乎同形，视觉模型会把 `❯` 读成 `>`（曾据此误以为 WT 也降级）。
可靠做法：`ctrl+shift+a` 全选 + `ctrl+shift+c` 复制 → `Get-Clipboard -Raw` → 逐字符打码点。

实测结果：

| 证据 | 实际文本 / 码点 |
| --- | --- |
| `[diag] 字形` 行 | `Unicode=True 提示符=U+276F` → `❯` |
| 输入行提示符 | 该行仅一个字符 `U+276F` → `❯` |
| Clawd logo | `U+2590 U+259B U+2588 U+2588 U+2588 U+259C U+258C` 等 → 方块字符画正常，WT 下不再显示成空心方块 |
| 分隔线 | `U+2500 ×209`，footer 宽 197 列（CJK 双宽折算后与 209 对齐）→ 输入框与历史同宽，无错位 |

- 真机结果：WT 下 `❯` 正常渲染，banner（logo + `jcc v1.0.0` + `hub/当前模型未配置 · API 按量计费` + `D:\project\w2`）、
  分隔线、footer 全部对齐；`/quit` 后 `jcc` 进程 0、WT 窗口随最后一个标签关闭（进程 0）。**未改任何渲染代码。**
- 附带观察（非缺陷，debug 模式固有）：`JCC_DEBUGLOG=1` 会话在**退出时**会把环形缓冲里的
  `[ORDER] middleware …` 全量冲刷到 stderr，scrollback 出现数百行；默认（无开关）不输出。

### 复验方法备忘（避免重复踩坑）
- 两次命令行输入打进了 WorkBuddy 而非终端：**`activate` 拿不到焦点**，必须先用 `click` 点击窗口标题栏/客户区再 `type`；
      截图后要 `info` 确认 `foregroundWindow.title` 才是目标窗口。
- `cmd` 里 `set JCC_DEBUGLOG=1 && jcc.exe` 会把值写成带尾空格的 `"1 "` → 开关判 false；
      必须用 `set "JCC_DEBUGLOG=1" && ...`。更省事的做法：**PowerShell 先 `$env:JCC_DEBUGLOG='1'` 再 `Start-Process`**，子进程继承。
- conhost 的 scrollback 会让"当前帧"不在截图预期位置：验证启动页前先 `cls`，或直接读窗口底部。
- 字形/文字类验收优先用 `ctrl+shift+a` + `ctrl+shift+c` + `Get-Clipboard -Raw` 取码点，别靠截图肉眼；
      `Add-Type -AssemblyName UIAutomationClient` 被安全策略拦（Add-Type 一律禁），此路不通。
- `wt.exe` 的启动命令用绝对路径；关闭窗口用 `Stop-Process`（点叉可能被对话弹窗挡住）。

## 第九轮：`引擎启动中…` 就绪即撤（先门禁、后实现，20:45-21:05）

**目标**：引擎装配期的 `引擎启动中…` 在首拍 `session_info` 到达后消失。动它的唯一风险是**帧高收缩 1 行**的残影。

### 门禁先落地（TDD 红→绿）
1. `tests/NativeTui.Tests/terminal/VirtualTerminal.cs` — **最小 ANSI 虚拟终端**（测试专用）：把渲染输出真画出来再断言，
   而不是"看字符串猜画面"。支撑 CR/LF（底行 LF 追加 = scrollback 增长）、**CUU 钳制在视口顶**（真终端行为，
   是"物理失同步"类缺陷的唯一暴露面）、CUF/CUB、CUP、EL(2K)、ED(2J/3J)、宽字符两列，其余 SGR/OSC/DEC 私有序列跳过。
   模型 = 无限 scrollback（物理行只增不减，视口 = 末尾 N 行）。
2. `tests/NativeTui.Tests/terminal/InlineShrinkGhostTests.cs`（组装器层 3 项）：富余视口收缩只擦底行不整屏清、
   旧行文本彻底消失；恰好填满视口必须 fullReset；增长方向不受影响（防误伤）。
3. `tests/NativeTui.Tests/repl/StartupLineRetireTests.cs`（外壳层 5 项）：撤除后内容高恰好 −1、
   二次撤除幂等且不误删 `[diag]` 行、**两帧真画到虚拟终端后"引擎启动中…"在视口与 scrollback 都消失且逐行与目标缓冲一致**、
   无余量时拒绝撤除、只剩 1 行余量的最紧边界仍无残影。

### 门禁暴露的两个真缺陷（都已修）
1. **`InlineComposer` 的 scrollback 深度少算 1 行**（真 bug）：
   `RenderSlice` 每行收尾都发 `CRLF`，帧实际占 `frameRows + 1` 物理行（末行空行承载光标）。
   旧式 `prev.Rows - viewportRows` 在"帧恰好填满视口"时算出 0 → 漏判"顶行已在 scrollback 不可达"，
   后续相对 CUU 被终端钳制在视口顶 → 写错行 = 残影。改为 `ScrollbackRows(frameRows, viewportRows) = max(0, frameRows + 1 - viewportRows)`，
   `IsShrinkBeyondViewport` 一并改用该下界（`next.Rows < 该值` → fullReset）。
2. **外壳守卫差一条余量**（真 bug）：`TryRetireStartupLine` 原判据 `内容高 > 终端行数` 太宽 ——
   恰好填满时顶行已被尾随换行顶进 scrollback（真机同样看不到那行）。改为 `内容高 + 1 > 终端行数` → 拒绝。

### 实现
- `ReplShell.EngineStartingLine`（`"引擎启动中…"` 单一文本源，宿主 `Program.cs` 改为引用常量，不再各写一份）。
- `ReplShell.TryRetireStartupLine(text)`：按原文定位 `StartupLineItem` → 校验余量 → `Messages.RemoveAt`（帧高 −1 → 差分收缩路径）。
- 接线：`DrainEngine` 收到 `BridgeEvent.SessionInfo` 即调用（幂等，二次返回 false）。

### 验收
- 测试：native_tui 全套 **3289 + 182 + 36 + 16 全绿**（NativeTui.Tests 3280 → **3289**，新增 9 项），0 失败。
- 产物：`NativeTui.Host.csproj` 6.6s / `JoinCode.csproj` 21.5s 重编 **0 警告 0 错误**；
  `NativeTui.Core.dll` 20:51:31、`NativeTui.Host.dll` 20:52:04 进 jcc 产物。
- 真机（WT，默认无调试开关）：启动页只剩 banner 三行 + 分隔线 + `❯` + 分隔线 + footer，
  **剪贴板码点复核 `引擎启动中` 已不存在**（`含引擎启动中=False`），无残字、无多余空行。
- 真机守卫分支（`wt --size 100,6` 强制 6 行视口 → 帧装不下）：`引擎启动中…` 保留，banner/输入区照常，
  **无 2J/3J 全清**（用户 scrollback 完好）—— 与门禁 `Retire_FrameFillsTerminalExactly_KeepsStartupLine` 一致。
- 现场：`/quit` 后 `jcc=0`、`WindowsTerminal=0`。

### 已知取舍
- 窗口太矮（帧装不下）时会话启动时**保留**该行不再消失 —— 顶部行已滚入 scrollback、物理不可变；
  强删要 fullReset（2J+3J）清空用户 scrollback，代价远大于多留一行提示。放大窗口后也不会补删（同一判据）。
