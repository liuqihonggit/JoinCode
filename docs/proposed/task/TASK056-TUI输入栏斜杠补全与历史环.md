# TASK056 — TUI 输入栏：斜杠命令补全 + 历史环

> 状态：in-progress
> 参考基准：`D:\project\claude-code-rust\claude-code-rev-main\src\`（useTypeahead.tsx / commandSuggestions.ts / useArrowKeyHistory.tsx / PromptInputFooterSuggestions.tsx）
> 历史数据源：复用现有 `~/.jcc/sessions/{id}/transcript.json`（`ITranscriptService`，`TranscriptPersistMiddleware` 已自动落盘，无需新存储）

## 任务分解（渐进式：每步 编译 → 测试 → commit）

| # | 任务 | 产物 | 状态 |
|---|------|------|------|
| 1 | 历史环纯逻辑 | `repl/history/HistoryRing.cs` + 测试 | ⬜ |
| 2 | 补全过滤排序纯逻辑 | `repl/suggestions/SlashSuggester.cs` + `CommandSuggestion.cs` + 测试 | ⬜ |
| 3 | 建议列表渲染 | `repl/suggestions/SuggestionBox.cs` + 测试 | ⬜ |
| 4 | ReplView 渲染集成 | 建议区插入输入区与 footer 之间 | ⬜ |
| 5 | ReplShell 键位集成 | Tab/Enter/Esc/Up/Down 分派 + 键位仲裁 + 历史导航 + 提交入史 | ⬜ |
| 6 | Host 组装 | 命令清单注入（SlashCommandCatalog）+ 历史种子（ITranscriptService） | ⬜ |
| 7 | 手动 exe 验收 | ADR 0080 方式 | ⬜ |

## 关键规格（TS 对齐）

### 斜杠补全
- 触发：text 以 `/` 开头、光标>0、无空格、非 Esc 抑制态
- 排序链：精确名 > 精确切名 > 前缀名(短优先) > 前缀切名 > 子序列名 > 子序列切名；空查询全量字母序；隐藏命令输入全名时前插
- 渲染：≤6 条两列（命令列宽=最长名+6 预计算；描述列截断），选中高亮未选中 dim，滚动窗口以选中项为中心
- 键位：Tab 接受不执行（补全 `/cmd ` 光标末尾）；Enter 接受并提交；Esc 只关建议（优先于打断/清空）；Up/Down 循环导航

### 历史环
- 容量 100（MAX_HISTORY_ITEMS）；提交时追加（末条相同去重）；导航 index 0=当前输入
- Up：暂存草稿（首次离开 0）→ 翻旧；到底保持不动
- Down：翻新；回到 0 恢复草稿（无草稿清空）
- 不回绕（对齐 GUI NavigateHistory）
- 翻历史时抑制建议刷新（程序化填充不触发补全）
- 种子：启动时从最近会话 transcript 的 user 条目加载（宿主注入）

## 架构决策

- NativeTui.Core 保持零依赖（ADR 0140）：命令清单与历史种子均以纯数据 `IReadOnlyList<>` 注入，宿主（NativeTui.Host）经 `engine.Services` 拉取转换
- Up/Down 仲裁：建议激活 → 补全导航；否则 inline 模式 → 历史环；viewport 旧模式保持滚动
- Esc 仲裁：建议激活 → 关建议（优先于忙打断/清输入/退出）

<!-- 🤖 Auto Decision: 2026-10-21 -->
<!-- 决策: 历史数据源复用 .jcc/sessions transcript 而非新建 history.jsonl -->
<!-- 原因: 用户指明已有 sessions 存储且 TranscriptPersistMiddleware 已自动落盘 TUI 输入，无需第二套存储 -->
<!-- 替代方案: 对齐 TS 新建 ~/.jcc/history.jsonl（用户已否决） -->
<!-- 验证: 待实现 -->
