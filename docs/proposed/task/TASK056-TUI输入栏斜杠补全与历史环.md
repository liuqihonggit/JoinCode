# TASK056 — TUI 输入栏：斜杠命令补全 + 历史环

> 状态：implemented（待手动 exe 验收）
> 参考基准：`D:\project\claude-code-rust\claude-code-rev-main\src\`（useTypeahead.tsx / commandSuggestions.ts / useArrowKeyHistory.tsx / PromptInputFooterSuggestions.tsx）
> 历史数据源：复用现有 `~/.jcc/sessions/{id}/transcript.json`（`ITranscriptService`，`TranscriptPersistMiddleware` 已自动落盘，无需新存储）

## 任务分解（渐进式：每步 编译 → 测试 → commit）

| # | 任务 | 产物 | 状态 |
|---|------|------|------|
| 1 | 历史环纯逻辑 | `repl/history/HistoryRing.cs` + 17 测试 | ✅ e69297a |
| 2 | 补全过滤排序纯逻辑 | `repl/suggestions/SlashSuggester.cs` + `CommandSuggestion.cs` + 20 测试 | ✅ 3f45ff8 |
| 3 | 建议列表渲染 | `repl/suggestions/SuggestionBox.cs` + 12 测试 | ✅ 4721996 |
| 4+5 | ReplView/ReplShell 集成 | 键位仲裁 + 建议区渲染 + 历史导航 + 提交入史 + 47 集成测试 | ✅ 6264f58 |
| 6 | Host 组装 | `tui_assembly/SlashCommandSource.cs` + `HistorySeeder.cs` + 9 测试 | ✅ ea5bdf2 |
| 6b | 渲染时序修复 | SyncSuggestionState 先于 EnsureScreens | ✅ e86a001 |
| 7 | 手动 exe 验收 | ADR 0080 方式（需真实终端交互） | ⬜ 待用户执行 |

## 四列验收表

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| `HistoryRing`（容量100+草稿+不回绕）`repl/history/HistoryRing.cs` | `ReplShell.NavigateHistory/Submit/SeedHistory` | ✅ | ✅ 单元 17 + 集成 10（HandleKey 全链路注入真实 ConsoleKeyInfo） |
| `SlashSuggester`（二级排序+Esc抑制+循环导航）`repl/suggestions/SlashSuggester.cs` | `ReplShell.HandleSuggestionKey/AcceptSuggestion/UpdateSuggestions` | ✅ | ✅ 单元 20 + 集成 10 |
| `SuggestionBox`（6条两列+选中居中滚动）`repl/suggestions/SuggestionBox.cs` | `ReplView.Render` 建议区（输入区与 footer 之间） | ✅ | ✅ 单元 12 + ReplView 渲染 4 |
| 键位仲裁（Autocomplete 上下文优先于 Chat）`ReplShell.HandleKeyPress/HandleSuggestionKey` | inline 模式 Up/Down=历史、Tab/Enter/Esc=补全 | ✅ | ✅ 集成测试（建议激活时 Up 归补全不翻历史等） |
| 命令清单注入 `Host/tui_assembly/SlashCommandSource.cs`（DI 解析 Catalog 兜底直接实例化） | `Program.RunInteractiveAsync` 构造 ReplShell | ✅ | ✅ 单元 2 + selftest 引擎组装真实链路 |
| 历史种子 `Host/tui_assembly/HistorySeeder.cs`（同项目最近5会话聚合+排除当前会话） | `Program.RunInteractiveAsync` SeedAsync | ✅ | ✅ 单元 6（项目过滤/时序/容量/降级） |
| 视觉/手感验收（建议浮层像素 + 历史翻阅手感） | 真实终端 | ✅ 代码 | ⬜ 用户手动：`dotnet run --project native_tui/apps/NativeTui.Host` |

## 回归基线

- NativeTui.Tests：558 → **634**（+76，全绿，含像素回归 snapshot）
- NativeTui.Host.Tests：35 → **43**（+8，全绿）
- selftest（真实引擎组装+握手+权限回执）：✅（`OPENAI_API_KEY=dummy-key` 过本机配置校验后通过——本机 settings.json 的 openai vendor 缺 key，与本次改动无关，stash 对照已验证）

## 关键规格（TS 对齐落地情况）

### 斜杠补全
- 触发：text 以 `/` 开头、无空格、非 Esc 抑制态 ✅
- 排序链：精确名 > 精确切名 > 前缀名(短优先) > 前缀切名 > 子序列名 > 子序列切名 ✅（Fuse 简化为子序列）
- 渲染：≤6 条两列（命令列宽=全量最长名+6 预计算防跳动），选中 Suggestion 色/未选中 dim，滚动窗口以选中项为中心 ✅
- 键位：Tab 接受不执行（`/cmd ` 光标末尾）；Enter 接受并提交（不带尾随空格）；Esc 仅关建议（优先于打断/清输入/退出）；Up/Down 循环导航 ✅

### 历史环
- 容量 100、末条去重、index 0=输入框态、Up 暂存草稿（光标行首）、Down 恢复草稿（光标末尾）、不回绕 ✅
- 翻历史时抑制建议刷新（程序化填充跳过）✅
- 种子：最近同项目 5 会话 user 输入聚合（排除当前会话防双份，旧→新时序，末尾 100 截断）✅

### 已知简化（后续边缘场景）
- 参数补全（/model xxx）未做（TS Argument 模式）— GUI 侧有 `SlashCommandParser` ArgumentCompletableCommands 先例
- 幽灵文本（ghost text / mid-input slash）未做
- 历史导航时光标行首（TS cursorToStart）已实现；ctrl+r 历史搜索未做

<!-- 🤖 Auto Decision: 2026-10-21 -->
<!-- 决策1: NativeTui.Core 保持零依赖,命令清单与历史种子以纯数据 IReadOnlyList 注入,宿主经 engine.Services 拉取转换 -->
<!-- 原因: ADR 0140 独立框架约束 + IReplEngine 桥接模式一致性 -->
<!-- 替代方案: 提升 GUI slash_commands 到共享层供两端引用（破坏 Core 零依赖,否决） -->
<!-- 验证: 编译零警告 + selftest 真实引擎组装通过 ✅ -->

<!-- 🤖 Auto Decision: 2026-10-21 -->
<!-- 决策2: HandleKey 以文本变化触发建议刷新,程序化填充(历史导航/补全接受)设 _programmaticFill 跳过 -->
<!-- 原因: 导航/移动键不得重置选中索引;历史翻到 / 开头条目不得弹建议(对齐 TS suppressSuggestions) -->
<!-- 替代方案: 无条件刷新(导航索引被归零,实测失败后否决) -->
<!-- 验证: ReplShellSuggestionsTests + ReplShellHistoryTests 全绿 ✅ -->

<!-- 🤖 Auto Decision: 2026-10-21 -->
<!-- 决策3: 渲染管线顺序 = SyncSuggestionState(状态投影) → EnsureScreens(尺寸) → Render -->
<!-- 原因: EnsureScreens 屏高依赖建议区高度,状态后置会导致建议激活首帧屏高滞后一帧被裁剪 -->
<!-- 替代方案: 原顺序(自审发现一帧裁剪 bug,修复于 e86a001) -->
<!-- 验证: 634 全量回归绿 ✅ -->
