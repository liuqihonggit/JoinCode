# 任务：diff渲染修正 + 斜杠命令前缀树

## 背景

用户对命令补全改造后追加需求（撤回需求已确认暂不做）：
1. **diff 渲染做法错误**：
   - 要有对应的行号（**双列**：git 标准旧/新两列）
   - + 是绿色，- 是红色
   - diff 窗口上下各留出 4 行上下文（`StructuredPatchGenerator.DefaultContextLines` 3→4）
   - "这些内部应该有对应的做法" — 指改 `DefaultContextLines` 即可
2. ~~每条消息撤回~~（用户已确认：先不做）
3. **斜杠命令前缀字符树**：输入 `/a` `/ap` 即可识别出 `/apple`（前缀树匹配，非全词匹配）

## 需求确认（ask_user 结果 2026-08-09）

- 需求2「消息撤回」→ **先不做**
- 需求1 diff 行号 → **双列行号**（git 标准，推荐项）
- 需求3 前缀树 → 保持，实现 SlashCommandTrie

## 现状勘察

### #1 diff 渲染
- `StructuredPatchGenerator.cs:12` `DefaultContextLines = 3` → 改 4，所有调用方自动生效（FileEditor/FileWriter 均用默认参数）
- `DiffViewer.cs` BuildDiffLine：已用 `+`/`-` 前缀 + `SuccessText`(绿)/`ErrorText`(红) 着色，但**行号单列**（Grid 三列 `Auto,Auto,*`：行号/前缀/内容）→ 需改四列双行号
- 行号数据已存在：`PatchLine.OldLineNumber` / `NewLineNumber`（Context 两列都有，Added 只有 New，Removed 只有 Old）
- DiffViewer 已接线：MainWindow.axaml:336 `<md:DiffViewer Hunks="{Binding StructuredPatch}" IsVisible="{Binding HasDiff}" />`

### #3 斜杠命令前缀树
- 现状 `SlashCommandItem.Filter` 用 `Name.StartsWith(prefix)` 线性过滤
- 需求：前缀树（trie）匹配，`/a` `/ap` 识别 `/apple`，支持唯一前缀自动补全
- 无现成 Trie 类；GUI 命令源：`MainViewModel.GetAvailableSlashCommands()` → `SlashCommandItem.FromMetadata()` / `BuiltInCommands`
- 新增 `SlashCommandTrie`（app/JoinCodeGui/ViewModels/）

## Red Tests 列

| # | 测试文件 | 测试名 | 断言（红） | 状态 |
|---|----------|--------|-----------|------|
| 1 | `tests/Unit/Infra.Tests/IO/Diff/StructuredPatchGeneratorContextTests.cs` | Generate_DefaultContextLines_IsFour | 变更行上下各保留 4 行上下文 | ⬜ |
| 2 | `tests/Unit/JoinCodeGui.Tests/Markdown/DiffViewerTests.cs` | Render_ShowsOldAndNewLineNumberColumns | 上下文行渲染出旧/新两个行号 | ⬜ |
| 3 | `tests/Unit/JoinCodeGui.Tests/ViewModels/SlashCommandTrieTests.cs` | Match_PrefixA_ReturnsApple | 前缀 `/a` 匹配 `/apple` | ⬜ |
| 4 | 同上 | Match_PrefixAp_ReturnsApple | 前缀 `/ap` 唯一识别 `/apple` | ⬜ |
| 5 | 同上 | Match_PrefixEmpty_ReturnsAll | 空前缀返回全部命令 | ⬜ |
| 6 | 同上 | Match_PrefixNoMatch_ReturnsEmpty | 无匹配前缀返回空 | ⬜ |

## 调试发现（2026-08-09 红测试触发）

- 红测试 1/2/3 均失败，其中 1/2 暴露**更严重的隐藏 bug**：
  - `BuildHunk` 不使用 diff 核心产出的 `EditOp`，而是用双指针 `oldLines[oi] == newLines[ni]` **重新推断**变更
  - 当插入/删除使后续行错位时（如文件末尾变更、中间插入），行错位导致**整段被当成全量替换**或变更被吞掉
  - 已用独立参考实现（RefMyers，相同算法）验证：diff 核心 `Backtrack` 输出正确，问题在 hunk 构建层
- 修复方向：`BuildHunk` 改为直接遍历 `EditOp`（按 op 类型输出 Context/Removed/Added），不再靠行内容比对
- 同时按需求 `DefaultContextLines` 3→4

## 实施计划

| 步骤 | 内容 | 状态 |
|------|------|------|
| 1 | 写红测试（上述 6 条） | ✅ 1/2/3 已写并验证红 |
| 2 | 修复 BuildHunk 直接消费 EditOp（root cause） | ✅ 编译0错 + 3绿测试 |
| 3 | 实现 StructuredPatchGenerator.DefaultContextLines 3→4 | ✅ |
| 4 | 实现 DiffViewer 双列行号（Grid 四列） | ✅ 测试绿 |
| 5 | 实现 SlashCommandTrie + 接入 SlashCommandItem.Filter | ✅ 测试绿 |
| 6 | 绿测试 + 编译验证 | ✅ GUI 161 + Infra 588 |
| 7 | 差评大师 + 修复 | ✅ 修复 hunk 头 0 基对齐 jsdiff |
| 8 | git 提交 | ⬜ |

## 涉及文件
- `infrastructure/Infrastructure/IO/Services/Diff/StructuredPatchGenerator.cs`：上下文行数 3→4
- `app/JoinCodeGui/Markdown/DiffViewer.cs`：行号双列 + 增绿删红
- `app/JoinCodeGui/ViewModels/SlashCommandTrie.cs`：前缀树（新增）
- `app/JoinCodeGui/ViewModels/SlashCommandItem.cs`：Filter 接入前缀树
- `app/JoinCodeGui/Views/MainWindow.axaml`：DiffViewer 绑定（已接，无需改）
- 测试：Infra.Tests + JoinCodeGui.Tests

## Auto Decision
<!-- 🤖 Auto Decision: 2026-08-09 -->
<!-- 决策: 需求2撤回暂不做，聚焦 diff 双列行号 + 上下文4行 + 前缀树 -->
<!-- 原因: ask_user 用户明确回答"先不做这个"，缩小交付范围避免浪费 -->
<!-- 替代方案: 三项全做（被用户否决） -->
<!-- 验证: 待编译测试 -->

<!-- 🤖 Auto Decision: 2026-08-09 -->
<!-- 决策: 修复 BuildHunk 改为直接遍历 EditOp 输出，而非按行内容双指针比对 -->
<!-- 原因: 红测试 1/2 暴露隐藏 bug — 插入/删除导致行错位时，按内容比对会误判整段为全量替换 -->
<!-- 替代方案: 仅改 DefaultContextLines 3→4（治标，变更仍会错位/被吞，不采纳） -->
<!-- 验证: 编译0错，3个红测试转绿，diffdbg 验证 5 种边界场景输出正确 ✅ -->

<!-- 🤖 Auto Decision: 2026-08-09 -->
<!-- 决策: SlashCommandTrie 用 SortedDictionary<char,Node> + 小写折叠大小写，ConditionalWeakTable 按命令列表实例缓存 -->
<!-- 原因: 前缀匹配 O(前缀长)，优于线性 StartsWith；CWT 让不同缓存列表各持一棵树，GC 安全 -->
<!-- 替代方案: 每次重建 trie（列表小，性能差异可忽略，但失去 trie 意义）；普通 Dictionary 缓存（强引用泄漏） -->
<!-- 验证: 6 trie 测试 + 6 MainViewModel slash 测试全绿 ✅ -->
