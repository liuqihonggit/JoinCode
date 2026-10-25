# transcript 增强键位差异 — 四问深度版

> **状态**：implemented — 三项全部落地（2026-10-25）
> **来源**：四问深度法实战调研（BaselineSurveyPromptTemplate 派发 explore 代理）
> **创建时间**：2026-10-25
> **关联**：TASK067 差异文档第 269 行"transcript ctrl+e 全展开 / `[` dump / `v` 编辑器 — 低 — 阅读增强小项"、M8-3 ctrl+o 全局 transcript 模式（已实现）

---

## 一、四问回答（调研前置）

### 1. 专家隐性实践
1. **跨终端实测 less 键位生态**：`less --help` 验证 `v` 是标准键位（"Edit the current file with $VISUAL or $EDITOR"），`ctrl+e` 在 less 里是"向下滚动一行"、`[` 无标准绑定 — Claude Code 借用 less 生态但语义重定义
2. **基准 git blame 考古**：`git log -S` 追 `dumpMode`/`showAllInTranscript`/`openFileInExternalEditor` — 本仓库只有 `971fdf85` 一次性导入，需去 Anthropic 上游仓库考古拿原始 PR 讨论
3. **量化收敛度量**：`showAllInTranscript` 18 处消费 / `dumpMode` 25 处消费 / `v` 仅 1 处 handler — ctrl+e 是"声明+多消费方"、`[` 是"声明+多消费方"、`v` 是"单 handler"

### 2. 原始素材清单
1. **基准 REPL.tsx:4237-4446** transcript 模式原始键位代码（`[` handler 4332-4338 / `v` handler 4340-4382 / dumpMode state 741 / showAll state 736）
2. **基准 defaultBindings.ts:160-170** Transcript 桶键位表（ctrl+e→toggleShowAll / ctrl+c,escape,q→exit）— `[` 和 `v` **不在键位表**，是 useInput 硬编码裸字母
3. **基准 useGlobalKeybindings.tsx:232-246** handleToggleShowAll 的 isActive 门控：`isInTranscript && !virtualScrollActive` — **ctrl+e 在 virtualScroll 下不生效**
4. **基准 REPL.tsx:737-741** dumpMode 注释原文："Diagnostic escape hatch so terminal/tmux cmd-F can find anything"
5. **less --help** 官方手册：`v Edit the current file with $VISUAL or $EDITOR`（标准键位）

### 3. 具体方法与工具
1. **grep 键位分派@派**：C# 侧 `TranscriptController.TryHandleKey`（HandleBrowseKey 163-190 / TryScroll 223-282）+ `ReplShell.HandleTranscriptKey`（2423-2450）确认未分派 ctrl+e/`[`/`v`
2. **源码对比+调用时机对照**：C# `KeybindingProfile.cs:229` 声明 ctrl+e→TranscriptToggleShowAll，但 `HandleTranscriptKey` 只分派 `AppToggleTranscript`+ctrl+c，不分派 TranscriptToggleShowAll — "声明绑定但无分派"
3. **gh pr list**：查基准仓库 PR（5 个都是 MCP/模型/编译修复，无 transcript 键位讨论）
4. **less --help 对照**：确认 `v` 是 less 标准键位（用户习惯强），`ctrl+e`/`[` 是 Claude Code 自创

### 4. 独立验证来源
1. **less --help 官方手册**：`v` 键行为有独立标准来源（非 AI/用户主观）
2. **基准代码注释原文**：`dumpMode` "Diagnostic escape hatch"（REPL.tsx:737）、`v` "less-style: v opens the file in $VISUAL/$EDITOR"（REPL.tsx:4341）— 设计意图有代码注释证据
3. **基准无测试套件**：`rg transcript *.test.*` 无结果 — 三键位无自动化测试覆盖，只能靠注释+less 标准验证

---

## 二、差异清单

### 差异 1：ctrl+e 全展开（TranscriptToggleShowAll）

| 项 | 内容 |
|---|---|
| 两侧 文件:行号 | 基准 `REPL.tsx:4332-4338` + `useGlobalKeybindings.tsx:234-237` + `Messages.tsx:467`；C# `KeybindingProfile.cs:229`（声明绑定）+ `KeybindingAction.cs:150`（枚举）+ `ReplShell.cs:2423-2450`（HandleTranscriptKey **未分派**）+ `TranscriptController.cs:163-190`（不处理 ctrl+e） |
| 差异描述 | C# 侧**声明了 ctrl+e→TranscriptToggleShowAll 绑定+action 枚举**，但 HandleTranscriptKey 未分派该 action。基准侧在 virtualScroll 下 ctrl+e 也不生效（isActive=`!virtualScrollActive`） |
| 影响面 | C# 侧 transcript 全屏模态按 ctrl+e 无响应。但 C# 侧 TranscriptLines.Project 本就投影全部消息全文行（无截断概念），即使分派也无消费方。影响=零功能损失，仅"绑定声明悬空" |
| 原始素材引用 | 基准 `defaultBindings.ts:163` / `useGlobalKeybindings.tsx:236`（isActive 门控）/ `Messages.tsx:467`（shouldTruncate）；C# `KeybindingProfile.cs:229` / `KeybindingAction.cs:149-150` |
| 独立验证来源 | 基准 `Messages.tsx:467` 代码逻辑（virtualScrollRuntimeGate=true 时 shouldTruncate 恒 false）；C# `TranscriptLines.cs:23-42` Project 全量投影无截断 |

### 差异 2：`[` dump-to-scrollback

| 项 | 内容 |
|---|---|
| 两侧 文件:行号 | 基准 `REPL.tsx:741`（dumpMode state）+ `REPL.tsx:4332-4338`（`[` handler）+ `REPL.tsx:4455-4456`（消费）；C# 侧**完全无声明、无绑定、无处理** |
| 差异描述 | 基准 `[` 是"诊断逃生舱"：把 transcript 从 virtualScroll 切回普通 scrollback，取消渲染上限，让终端原生 cmd-F/tmux 搜索能找到历史内容。C# 侧无对应概念 — C# 用备用屏 DEC 1049 独占模态，退出即恢复主屏，无"dump 到 scrollback 保留"语义路径 |
| 影响面 | C# 侧 transcript 模态按 `[` 无响应。用户无法用终端原生搜索找 transcript 内容（只能在模态内用 `/` 搜索）。但 C# 侧 `/` 搜索已实现（n/N 跳转），覆盖大部分搜索需求 |
| 原始素材引用 | 基准 `REPL.tsx:737-741`（"Diagnostic escape hatch so terminal/tmux cmd-F can find anything"）/ `exportRenderer.tsx:48`（"write to stdout for `[` dump-to-scrollback"） |
| 独立验证来源 | 基准 `REPL.tsx:4455-4456` 代码逻辑（dumpMode 消费 transcriptScrollRef + disableRenderCap）；C# 侧备用屏模式 `ReplShell.cs:2475-2490` SyncAlternateScreen（Enter/Exit 对称，无"dump 保留"路径） |

### 差异 3：`v` 编辑器（$VISUAL/$EDITOR）

| 项 | 内容 |
|---|---|
| 两侧 文件:行号 | 基准 `REPL.tsx:4340-4382`（`v` handler：renderMessagesToPlainText → writeFile(tmpdir) → openFileInExternalEditor）+ `REPL.tsx:4389`（isActive=`!searchOpen`）；C# 侧**完全无声明、无绑定、无处理**。C# 有 `_launchExternalEditor`（`ReplShell.cs:351` 委托 / `4246-4287` 实现）但是"编辑-回填"语义（Func<string, Task<string?>>），不是"打开浏览"语义，不能直接复用 |
| 差异描述 | 基准 `v` 是 less 标准键位：把 transcript 全文渲染成纯文本写临时文件，用 $VISUAL/$EDITOR 打开浏览。C# 侧无对应 — TranscriptController 不处理 `v`，ReplShell.HandleTranscriptKey 未消费。C# 已有 TranscriptExporter.Export（Markdown 格式，/export 用）和 TranscriptLines.Project（纯文本行，verbose 形态）可作为文本源 |
| 影响面 | C# 侧 transcript 模态按 `v` 无响应。用户无法用 $EDITOR 浏览/搜索 transcript 全文。less 用户习惯强（`v` 是 less 通用键） |
| 原始素材引用 | 基准 `REPL.tsx:4341-4342`（"less-style: v opens the file in $VISUAL/$EDITOR"）；`less --help` 官方手册（"v Edit the current file with $VISUAL or $EDITOR"） |
| 独立验证来源 | `less --help` 输出（独立标准来源）；基准 `REPL.tsx:4372-4374` 代码（writeFile + openFileInExternalEditor 实现） |

---

## 三、bug 判定

**无 bug**。三键位均归差异，理由：
- **ctrl+e**：C# 侧 `HandleTranscriptKey` 注释契约是"IsActive ? (TryHandleKey || toggle) : toggle"（ReplShell.cs:2417），未承诺分派所有 Transcript 桶 action；基准在 virtualScroll 下 ctrl+e 也不生效。无自身注释契约违背
- **`[` / `v`**：基准侧是 useInput 硬编码裸字母（不在 defaultBindings 键位表），C# 侧无对应声明，无契约可违。基准注释是"设计意图"非"规范条款"

---

## 四、价值评估 + 接线建议

| 键位 | 价值 | 理由 | 是否值得实现 | 接线建议 |
|---|---|---|---|---|
| **ctrl+e 全展开** | **低** | C# 侧 TranscriptLines.Project 本就全量投影无截断，无消费方；基准在同路径下也不生效。实现=空操作 | **不建议实现**（当前） | 二选一：① 删 `KeybindingProfile.cs:229` 绑定 + `KeybindingAction.cs:150` 枚举（消除悬空声明）；② 保留待未来加消息截断时再接 |
| **`[` dump** | **中** | 是"诊断逃生舱"，让终端原生 cmd-F 搜索 transcript。但 C# 侧 `/` 搜索已覆盖大部分需求；备用屏 DEC 1049 模式下"dump 到 scrollback"语义需重新设计 | **可选实现**（优先级低） | 若实现：① TranscriptController 加 DumpMode 状态 + `[` handler；② dump 时把 Snapshot.Lines join 写到主屏 scrollback；③ n/N 在 dumpMode 下禁用。成本中（需改备用屏退出路径） |
| **`v` 编辑器** | **中-高** | less 标准键位（`less --help` 官方手册），用户习惯强；C# 侧已有 TranscriptLines.Project（纯文本源）+ 备用屏 suspend/resume 基础设施；实现成本低 | **建议实现**（优先级中） | ① 新增"打开文件浏览"委托 `Action<string>`（路径→打开，不回填）— 不能复用 `_launchExternalEditor`（编辑-回填语义不同）；② TranscriptController.HandleBrowseKey 加 `v` 分派：Snapshot.Lines join → 写 `Path.Combine(Path.GetTempPath(), $"cc-transcript-{timestamp}.txt")` → 调委托；③ ReplShell 构造注入新委托；④ footer 可选加"v 编辑"提示。成本低（纯增量，不动现有路径） |

---

## 五、决策表（一问一答）

| 差异项 | 价值 | 建议 | 用户决策 | 状态 |
|--------|------|------|----------|------|
| ctrl+e 全展开 | 低 | 不实现（或删悬空声明） | ✅ 删悬空声明 | implemented（0f0399107） |
| `[` dump | 中 | 可选（`/` 已覆盖搜索） | ✅ 实现 | implemented（52dc00dbc） |
| `v` 编辑器 | 中-高 | 建议实现（less 标准+低成本） | ✅ 实现 | implemented（124b13c7d） |

---

## 七、验收表（四列强制）

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| `TranscriptController` 构造加 `openTranscriptInEditor` 委托 + `HandleBrowseKey` v 分派（join Lines 调委托） | `ReplShell.OpenTranscriptInEditor`（复用 `_launchExternalEditor` 通道 fire-and-forget 打开浏览，结果丢弃不回填） | ✅ | ✅ 3 测试（按v触发委托/无委托静默/搜索栏优先）+ NativeTui.Tests 全量 2957 回归 |
| `TranscriptController` 加 `_pendingDumpText` + `[` handler（保存 join 全文 + ExitTranscript + 设 pending）+ `TakePendingDump` 方法 | `ReplShell.DumpTranscriptToScrollbackIfPending`（HandleTranscriptKey 在 SyncAlternateScreen 后调，写 `_output` 主屏） | ✅ | ✅ 3 测试（dump+退出/取后清空/未激活不消费）+ NativeTui.Tests 全量 2957 回归 |
| 删 `KeybindingAction.TranscriptToggleShowAll` 枚举 + `KeybindingNames` 名称映射 + `KeybindingProfile` ctrl+e 绑定 | 无消费方（悬空声明已删） | ✅ | ✅ NativeTui.Tests 全量 2954 回归无破坏 |

---

## 六、关键结论

1. **C# 侧已实现**：j/k/g/G/ctrl+u/d/b/f/n/p/空格/↑↓/home/end + `/` 搜索 + n/N + q/Esc/ctrl+c/ctrl+o 退出（`TranscriptController.cs:90-331` 完整 less 键位）
2. **C# 侧未实现**：ctrl+e（声明悬空）/ `[`（完全缺失）/ `v`（完全缺失）
3. **最高价值缺口是 `v`**：less 标准键位 + 用户习惯强 + 实现成本低（纯增量）
4. **无 bug**：三键位均归差异（无自身注释契约违背）
5. **基准无测试套件**：三键位无自动化测试覆盖，只能靠 less 标准手册 + 代码注释验证

<!-- 🤖 Auto Decision: 2026-10-25 -->
<!-- 决策: 落差异文档走用户一问一答决策,而非直接实现 -->
<!-- 原因: 用户选"落差异文档再决策"; AGENTS.md 判定纪律要求差异项一问一答,禁止 AI 自行重构对齐 -->
<!-- 替代方案: 直接实现 v 编辑器(最高价值)——放弃,因用户明确选先落文档再决策 -->
<!-- 验证: 四问深度法实战调研产出完整四问回答+三差异+价值评估,文档已落地待决策 -->
