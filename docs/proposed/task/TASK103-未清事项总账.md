# TASK103 — 未清事项总账（防遗漏 · 唯一清单）

> 状态：active（2026-11-21 建立）
> 目的：用户「未来的开工，先做小的逐个清；但怕忘记，先把当前没清的全部记录」。
> 本文是**唯一权威未清清单**——所有未清项在此登记，逐项清理后在此打勾，避免散落在多份过期清单里漏账。
> 纪律：
> 1. **功能相同实现不同 = 一问一答**，禁止 AI 自行对齐（AGENTS.md 基准对比工作流第 1 条）。
> 2. **确认 bug / 明确缺口（已实现无调用点、参数静默失效）= 直接 TDD 修复**，不请示。
> 3. 状态列只在**代码 + commit 取证后**打勾；未核实项标 `待核实`，不假装已查。
> 4. 规模分级用于「先做小的」：S（单行/单文件接线或文案）、M（单域多文件）、L（跨域/大改造）。

## 0. 数据来源与已知过期问题

- `TASK075-后续高价值任务工作清单.md`（57 项）、`TASK075-后续任务交接清单.md`（含 23+TASK069 域内项）：**两份状态互相矛盾**，均已过期，不可照抄。
  - 已核实反例：`TASK069-C5` 会话切换恢复 = **已实现**（`SessionResumeStep.cs:75` SwitchSession，PR #514），工作清单仍标「待开工」；
    `TASK071-B3` 技能生态 = **已实现**（`modules/cognition/hands_core/skills/` 全套），工作清单仍标「待开工」。
- `TASK071-TS基准全仓差异扫描.md` §待决策清单（A/B/C/D）、`TASK068` C 类 3 键、`TASK062` P7/P8、`TASK053` 决策4 backlog、`TASK069` 差异全文：作为原始素材，本文做归一登记。

## 1. 可直接清理（无需决策：确认缺口/bug）— 按规模从小到大

| # | 编号 | 事项 | 来源 | 规模 | 核实状态 | 处置 |
|---|------|------|------|------|----------|------|
| ~~1~~ | ~~TASK069-M6~~ | ~~用户消息 10k 截断接线~~ | TASK069 域6 | S | ✅ **已完成 2026-11-21**（见 §4） | DisplayText 接线，34/34 + 真机 selftest EXIT=0 |
| ~~2~~ | ~~TASK062-P8~~ | ~~Completion Run 前占位语义~~ | TASK062 | S | ✅ **误判纠正**：TASK063 `c3c5ec77a` 已文档化契约+守卫锁，**刻意不加门闩**（挂死风险）→移 §3 | 见 §4 |
| ~~3~~ | ~~TASK062-P7~~ | ~~tokenGain 恒 0~~ | TASK062 | M | ✅ **误判纠正**：TASK063 `853d9e75b` 已用完成态 TokenTotal 权威校正闭合→移 §3 | 见 §4 |
| ~~4~~ | ~~TASK069-C2~~ | ~~`/resume` 模糊搜索 enableSearch~~ | TASK069 域4 | M | ✅ **归类误判**：非接线 bug，是功能对齐差异（CLI/TUI 两条路径均无搜索）→移 §2.2 | 见 §4 |
| ~~5~~ | ~~TASK071-C6~~ | ~~结构化输出 synthetic tool 重试 + 自动填充~~ | TASK071 | M | ✅ **归类误判**：原文归 C 类「行为差异待逐项裁决」第6项「需整体设计」→移 §2.2 | 见 §4 |
| ~~6~~ | ~~TASK071-A4~~ | ~~通用设置迁移框架~~ | TASK071 | L | ✅ **归类误判**：原文归 A 类「整域缺失（做/不做/何时）」第4项，总账原话也是"需设计另立专项"→移 §2.3 | 见 §4 |

## 2. 需用户逐项决策（功能相同实现不同 / 口味差异）— 按域分组

> 处置：**不自行对齐**。每项用 ask_user 一问一答，决策「保留 C# 现状 / 对齐基准 / 折中」后施工。按规模从小到大排，先问小项。

### 2.1 S 级（单行/文案/配色，先问）
| # | 编号 | 事项 | 来源 |
|---|------|------|------|
| ~~7~~ | ~~TASK069-S5~~ | ~~token compact 大小写 `1.3k` vs `1.3K`~~ | TASK069 域1 | ✅ 已完成 2026-11-21：`FormatCompact` 后缀 k→K，红→绿测试 5 项 + `/stats` 真机 EXIT=0 |
| ~~8~~ | ~~TASK069-S4~~ | ~~流式项前缀跳变~~ | TASK069 域1 | ✅ **裁决维持现状（已对齐基准）2026-11-21**：一手证据 `StreamingTextItem.cs:5` 实现注释「对齐基准 Messages.tsx 尾部 ⎿ 前缀」+ TASK064:12 记基准行 703-709 为 ⎿；TASK069「基准同用 ●」是转述误读（基准落定才 ●）。跳变是基准自身行为 |
| ~~9~~ | ~~TASK069-M1~~ | ~~❯ 前缀选中变色~~ | TASK069 域6 | ✅ **裁决维持现状（已对齐基准）2026-11-21**：`RenderUser` ❯ 恒 Subtle 是对齐基准；C# picker/dialog 选中态 `MessageRenderer.cs:350,394` 已是 suggestion 色；`MessageCursor` 是 w2 自创无基准对应物，高亮为整块背景覆写。TASK069 误套 picker 选中态到用户消息前缀 |
| 10 | TASK069-P6 | 模式切换警告：基准 auto 进入 800ms 防抖警告（≤3 次） | TASK069 域3 |
| 11 | TASK069-P7 | dont-ask-again 文案与行为不符（承诺不再问但 30 分钟复问）——改文案 or 改持久化 | TASK069 域3 |
| 12 | TASK074-D7 | bash mode 使用埋点 `logEvent('tengu_input_bash')` | TASK074 |
| 13 | TASK069-缺失 | reducedMotion/yank-bug 禁用流式预览 | TASK069 缺失 |
| 14 | TASK069-缺失 | spinner 连接警告（Reconnecting/Disconnected 行） | TASK069 缺失 |
| 15 | TASK069-缺失 | ultrathink 彩虹文字 | TASK069 缺失 |
| 16 | TASK069-缺失 | 工具执行中耗时显示（elapsedTimeSeconds 进度行） | TASK069 缺失 |
| 17 | TASK069-缺失 | 用户中断专用消息 | TASK069 缺失 |
| 18 | TASK069-缺失 | redacted_thinking 消息 | TASK069 缺失 |

### 2.2 M 级（单域多文件）
| # | 编号 | 事项 | 来源 |
|---|------|------|------|
| 19 | TASK069-S1 | 流式上屏粒度：逐字抖动 vs 截断到最后换行逐行 | TASK069 域1 |
| 20 | TASK069-S2 | 流式尾块渲染：纯文本 vs 完整 Markdown | TASK069 域1 |
| 21 | TASK069-S3 | 增量解析范围：全量 Markdig.Parse vs O(尾长) | TASK069 域1（性能） |
| 22 | TASK069-S6 | 提示词 XML 剥离（基准 stripPromptXMLTags） | TASK069 域1 |
| 23 | TASK069-M2 | 工具圆点状态色：恒 text vs success/error + 未决闪烁 | TASK069 域6 |
| 24 | TASK069-M3 | diff 行号 gutter + 统计头 | TASK069 域6 |
| 25 | TASK069-M4 | 表格列对齐：仅左 vs center/right | TASK069 域6 |
| 26 | TASK069-M5 | 工具错误结果分支 is_error → 错误消息组件 | TASK069 域6 |
| 27 | TASK069-N2 | footer 右侧布局：单槽四源互斥 vs 右列多项堆叠 | TASK069 域2 |
| 28 | TASK069-C1 | /resume 列表项增强（相对时间/分支/消息数/tag/PR） | TASK069 域4 |
| 28a | TASK069-C2 | /resume 模糊搜索：CLI Selector `enableSearch` 是占位参数（`SelectorComponents.cs:29-30` 忽略），TUI SessionPickerDialog 仅 ↑↓/enter/esc 无搜索——两条路径均无搜索 = 功能对齐差异（非静默失效 bug） | TASK069 域4 |
| 29 | TASK069-C3 | /resume TUI 下交互：Selector 直写 stdout 与 TUI 帧冲突 | TASK069 域4 |
| 30 | TASK069-E1 | API 错误分类：纯红单行 vs SSL/超时/连接分类 | TASK069 域5 |
| 31 | TASK069-E4 | 错误详情展开 ctrl+o（1000 字符 + cause 链 + 堆栈） | TASK069 域5 |
| 32 | TASK069-P3 | 拒绝反馈输入框 "tell Claude what to do differently" | TASK069 域3 |
| 33 | TASK069-P4 | ctrl+e 展开内容（AI 解释 + 风险分级） | TASK069 域3 |
| 34 | TASK069-P5 | 触发原因解释区 + /permissions 引导 | TASK069 域3 |

### 2.3 L 级 / 架构分歧 / 整域缺失（需评估适用性，最后问）
| # | 编号 | 事项 | 来源 |
|---|------|------|------|
| 35 | TASK068-C1 | sandbox（需 OS 级沙箱子系统） | TASK068 |
| 36 | TASK068-C2 | enabledPlugins（PluginContext 无 IServiceProvider） | TASK068 |
| 37 | TASK068-C3 | actor（10+ Actor 子类硬编码背压） | TASK068 |
| 38 | TASK071-B1 | 工具命名体系 snake_case vs PascalCase | TASK071 |
| 39 | TASK071-B4 | 插件钩子链 PluginHookInjector 无消费方（死链） | TASK071 |
| 40 | TASK071-B5 | dream 自动触发路径（stop hook 内联） | TASK071 |
| 41 | TASK071-C1 | DiminishingReturns 判据 | TASK071 |
| 42 | TASK071-C2 | 召回算法（关键词打分 vs LLM sideQuery） | TASK071 |
| 43 | TASK071-C3 | /init /memory /loop /branch+/fork 命令分叉 | TASK071 |
| 44 | TASK071-C4 | TaskCreate/AskUserQuestion schema 分叉 | TASK071 |
| 45 | TASK071-C5 | Glob 大小写/Bash 超时/TaskOutput 语义分叉 | TASK071 |
| 45a | TASK071-C6 | 结构化输出体系（synthetic tool + 重试上限5 + `result.structured_output`）— 原文标「需整体设计」，C# 无限重试风险 | TASK071 C类第6项 |
| 46 | TASK071-C7 | Complete 协议字段（duration_api_ms/num_turns/modelUsage/permission_denials） | TASK071 |
| 47 | TASK071-A1 | upstreamproxy（CONNECT→WS 隧道中继） | TASK071 |
| 48 | TASK071-A2 | remote 会话订阅端（CCR viewer） | TASK071 |
| 49 | TASK071-A3 | 直连服务器客户端（server 模式） | TASK071 |
| 49a | TASK071-A4 | 通用设置迁移框架（版本号 + 脚本目录）— `SettingsSourceMigration` 只落地一件，通用框架未建（TASK089）；做/不做/何时需评估 | TASK071 A类第4项 |
| 50 | TASK071-A6 | 团队记忆密钥扫描（gitleaks 规则） | TASK071 |
| 51 | TASK071-A7 | LocalAgentTask/主会话后台化任务类型 | TASK071 |
| 52 | DSG042-P3 | RawStdinPump 切默认退役（双终端矩阵跑齐后） | TASK075 |

## 3. 已关闭（曾登记为未清，经核实已实现）— 仅留档不清理

| 编号 | 事项 | 关闭证据 |
|------|------|----------|
| TASK069-C5 | 会话切换状态恢复 | `SessionResumeStep.cs:75` SwitchSession；PR #514（237fb5a9f） |
| TASK071-B3 | 技能生态对齐 | `modules/cognition/hands_core/skills/` 全套（SkillExecutor/SkillService/SkillDiscoveryService） |
| TASK071-A5 | 交互输入历史 | HistorySeeder 复用 transcript（等价实现） |
| TASK071-B2 | 记忆子系统 | 方案 C 分层保留，ADR 0150 |
| TASK071-D | hooks 协议层 D1-D9 | PR #513（D7 suppressOutput 遗留单列，见下） |
| TASK069-E5 | 无人值守重试 | 方案 C 全面对齐，a19fdfc88/87b8bb2ba |
| DSG043 | OSC 8 超链接 | 三 Phase 落地 |
| TASK067-截断 | 输入超 10000 截断 | PR #506 |
| TASK069-缺失 | 成本 USD 计账 / 自定义 statusline | 已实现（工作清单 §五） |
| TASK071-D7 | hooks suppressOutput | HookResult 加字段遗留——⚠️ 待核实：`TASK075` 提「D7 遗留需 HookResult 加字段」，与「D1-D9 全落地」矛盾 |
| TASK062-P8 | Completion Run 前占位语义 | TASK063 `c3c5ec77a`（2026-10-22）：契约文档化 + 守卫锁；**刻意不加 `_started` 门闩**（"行为变更引入等待挂死风险"）。误判根因：只 grep 门闩符号未见即判悬置，未读关联 TASK063 |
| TASK062-P7 | tokenGain 恒 0 | TASK063 `853d9e75b`：方案2 完成态权威校正（`OnSubagentFinished(tokenTotal: evt.Usage?.TotalTokens)` + `SubagentFinished.TokenTotal` 全链）；三处 `tokenGain:0` 保留（逐活动增量不存在，否决方案1 接 RecordTokenUsage 差值） |

## 4. 清理进度

- 2026-11-21：建立总账。已核实关闭 10 项（§3）；§1 六项可直接清理、§2 四十六项待决策。
- **#1 TASK069-M6 已完成（2026-11-21）**：
  - 设计决策：`UserMessageItem.DisplayText`（= `UserMessageTruncator.Truncate(text)`）仅供渲染，
    `Text` 属性保留全文（复制 `CopyTextExtractor` / 搜索 `ContainsText` / 排队断言不受影响）——对齐基准「显示截断、复制全文」。
  - TDD 红→绿：先写 `UserMessageItemTruncationWiringTests` 4 项（DisplayText 不存在 → CS1061 红），
    再改 `MessageItems.cs` 的 `UserMessageItem`。`UserMessage` 相关 34/34 绿。
  - 回归：NativeTui.Tests 全量 3026~3027/3028，2 个偶发失败均为 `RawStdinPumpTests` 输入泵时序 flaky
    （单跑该 fixture 18/18 连续 3 次全绿，每次全量失败的用例不同——TASK101 B3 已登记的 Flaky 分层，与本次渲染改动无关）。
  - 真机（ADR 0080）：重编 NativeTui.Core + CLI，DLL 字符串核验含 `DisplayText`；
    `jcc.exe --selftest-tui` seed provider 后 `HOST SELFTEST OK` EXIT=0。
- **#2/#3 TASK062-P8/P7 误判纠正（2026-11-21）**：
  - 根因：上一轮回填 TASK062 时只 grep 本任务符号（`_started`/`RecordTokenUsage` 未见）即判「悬置」，未读关联任务 TASK063——**"选择了文档化/替代方案"≠"未实施"**。
  - P8 铁证：commit `c3c5ec77a`（契约文档化 + 契约锁测试）；TASK063 决策原文「契约文档化而非行为修正（不加 `_started` 门闩等待）——行为变更反而引入等待挂死风险」；`McpPluginBootstrapHandleTests.Completion_BeforeRun_Placeholder_NotReadinessSignal` 锁死占位语义。
  - P7 铁证：commit `853d9e75b`（方案2 完成态权威校正）；`SessionController.cs:508` 传 `tokenTotal: evt.Usage?.TotalTokens ?? 0`；`SubagentFinished.TokenTotal` 协议全链；三处 `tokenGain:0` 是逐活动增量（调研证明不存在，否决方案1）。
  - 处置：TASK062 顶部状态 + 总表 + §4 遗留已纠正为 P1–P8 全闭合；总账 #2/#3 移入 §3。**零代码改动**（代码现状即拍板结果）。
- **#4 TASK069-C2 归类纠正（2026-11-21）**：
  - 深查：CLI `Selector<T>`（`SelectorComponents.cs:29-30`）的 `enableSearch` 是**占位参数**，构造函数直接忽略，`SearchKey` 字段存而不用；TUI `SessionPickerDialog.TryHandleKey` 仅 ↑↓/enter/esc 无搜索分支。
  - 判定：`ResumeCommand.cs:202` 传 `enableSearch: true` 不是"参数静默失效 bug"，而是**基准 LogSelector 有搜索、C# 两条路径都未实现**的功能对齐差异 → 按 AGENTS 纪律一问一答，从 §1 移入 §2.2（#28a）。
- **#5 TASK071-C6 归类纠正（2026-11-21）**：原文 `TASK071:67,122` 明确归 C 类「行为差异待逐项裁决」第 6 项、标注"需整体设计"（保留 C# 设计 vs 对齐 TS 的选择题），非"确认缺口"→ 从 §1 移入 §2.2（#45a）。
- **#6 TASK071-A4 归类纠正（2026-11-21）**：原文 `TASK071:96,100-103` 归 A 类「整域缺失（做/不做/何时）」第 4 项，总账原话也是"需设计另立专项"——决策项非施工缺口 → 移入 §2.3（#49a）。
- **§1 清理阶段总结**：原列 6 项「可直接清理」，深查后 5 项全是误判（P8/P7 已完成、C2/C6/A4 属决策项），**唯一真缺口 #1 M6 已在上一轮完成**。教训：归"可直接清理"前必须读**原始任务文档的分类**，不能只看总账转述——转述会把"待裁决/需设计"压缩成"缺口/待修"。
- **#7 TASK069-S5 已完成（2026-11-21）**：用户拍板对齐基准大写 K。`NumberFormatter.FormatCompact` 后缀 k→K（M/B 本已大写）；红测试断言 `1.3K` 失败→改码→绿；TDD 5 项 + StatsCommand 回归 17/17；真机 `slash_call /stats` EXIT=0（空数据全 0，UsageTracker 内存态无法预置，但单测直接锁定新代码）。
- **#8/#9 S4/M1 证据冲突冻结待重裁（2026-11-21）**：
  - 用户拍板"对齐基准恒 ● / 前缀高亮"是基于总账转述，但施工前深查发现转述与原始证据冲突：
  - **S4**：TASK064:12 引用基准 Messages.tsx:703-709 读作 `⎿` 前缀流式行（C# `StreamingTextItem` 刻意对齐，注释明写）；TASK069:31 对同一行读作"基准同用 ●"。两份文档矛盾，极可能基准流式期本就是 ⎿、落定才 ●（跳变是基准行为非 C# 缺陷）。
  - **M1**：`RenderUser` 的 ❯ 是用户消息固定前缀恒 Subtle（基准同）；C# picker/dialog 选中态 `MessageRenderer.cs:350,394` 已是 suggestion 色（与基准一致）；`MessageCursor`（Shift+↑）是 w2 自创特性**无基准对应物**（`ReplShell.cs:2465` 原话），选中高亮是整块背景覆写非前缀变色。极可能是把 picker 选中态误套到用户消息前缀。
  - 处置：**不施工**，向用户如实报告证据冲突并请重新裁决。教训（第三次印证）：总账转述不可信，施工前必须回到**原始证据**（基准行引用 + 代码注释 + 原始任务分类）复核。
- **#8/#9 S4/M1 裁决闭合（2026-11-21，用户授权「参考 Claude Code 设计裁决」）**：
  - 用户授权后不猜、先找一手证据：本机装 Claude Code 2.1.317 原生 exe（编译后无可读源码/行号），文档引用的旧版 `.tsx` 拿不到 → 以**实现时对照基准写的代码注释** + **早期带行号的详细规划**（TASK064）为准。
  - S4：`StreamingTextItem.cs:5`「对齐基准 Messages.tsx 尾部 ⎿ 前缀行渲染」+ TASK064:12（基准行 703-709 = ⎿）→ 基准流式期本就是 ⎿、落定才 ●；TASK069「基准同用 ●」是转述误读 → **维持 C# 现状（已对齐），关闭**。
  - M1：`RenderUser` ❯ 恒 Subtle 对齐基准；C# picker/dialog 选中态 `MessageRenderer.cs:350,394` 已是 suggestion 色；`MessageCursor` 是 w2 自创无基准对应物（`ReplShell.cs:2465`）→ TASK069 误套 picker 选中态到用户消息前缀 → **维持现状，关闭**。
  - **方法论沉淀**：遇「TASK069 转述 vs 实现注释」冲突，一手证据优先级 = 实现时对照基准写的代码注释 > 早期带行号规划 > 二轮转述表。本机 Claude Code exe 可作最终仲裁（需运行时截图/抓帧，非静态源码）。
- 下一动作：§2 剩余项施工前一律先做一手证据复核（代码注释 / 原始规划 / 必要时 Claude Code 真机），确认是真差异再请示或对齐。
