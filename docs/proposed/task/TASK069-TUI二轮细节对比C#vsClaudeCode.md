# TASK069 — TUI 二轮细节对比（C# native_tui vs ClaudeCode 基准）

> 状态：调研完成，确认 bug 修复中；差异项待用户逐项决策（一问一答）
> 对比范围：第二轮 6 个此前未深挖的域（流式渲染/通知 footer/权限深度/会话 compact/错误重试/消息项渲染）
> 方法：6 个并行调研代理，返回体量受控（行数上限约束）；判定纪律 = 双侧证据 + 自身契约违背才判 bug
> 前置：TASK067 一轮 11 批次 + w2 板块 A-E 已覆盖底层与功能域；本轮聚焦细节层

## 一、确认 bug（直接 TDD 修复，无需决策）

| # | 症状 | C# 证据 | 基准证据 | 严重度 |
|---|------|---------|---------|--------|
| B-1 | **Error 事件后 IsBusy 永久卡死**：回合异常（API 错误/超时/空回复/工具拒绝）只发 Error 无终态事件 → IsBusy 卡 true → 提交永久排队、Esc 无效、死亡检测被挡 | EngineTurnClient.cs:121-122（异常只发 Error）；ReplBridgeDriver.cs:181-211（IsBusy 仅 TurnCompleted/TurnInterrupted 复位） | REPL.tsx:2951-2961（finally 兜底 "Always reset loading state"） | **最高**（每次 API 错误必现） |
| B-2 | 引擎 busy 中死亡无提示：死亡检测双条件 `!IsAlive && !IsBusy` → busy 中死亡 IsBusy 卡 true → spinner 永转不退出 | ReplShell.cs:732 | REPL.tsx:2958（finally 无条件） | 高 |
| B-3 | 用户消息换行符被吞：'\n' 宽度 0 被 WriteText 跳过 → "a\nb" 渲染为 "ab" 无分隔 | MessageItems.cs:22,25-26 + Screen.cs:163-169 + RuneWidth.cs:136-138 | UserPromptMessage.tsx:76-78 | 高（多行/粘贴消息显示错误） |
| B-4 | 流式项跨工具存活吞并新文本：text→tool→text 序列（agent loop 真实路径）第二段文本追加到旧流式项 → 顺序错乱 + OnDone 落定重复 | ReplBridgeDriver.cs:163-168,133-137（ToolCall 不清 _streamingItem） | messages.ts:3003（新块开始清空流式文本） | 高 |
| B-5 | mac+ghostty spinner 字符集选择反序：darwin 优先命中含 ✽ 集（基准刻意规避 ghostty 渲染偏移） | SpinnerRow.cs:183-186 | Spinner/utils.ts:4-11（ghostty 优先） | 低 |
| B-6 | --continue 跨项目恢复：全局最近会话无 ProjectPath 过滤（引擎域 modules/） | SessionResumeStep.cs:83-86 | sessionStorage.ts:4119-4122 | 中（跨域） |

## 二、差异清单（高价值节选，待一问一答）

### 域1：流式渲染与 spinner

| # | 功能点 | 差异 | 基准位置 |
|---|--------|------|---------|
| S1 | 流式上屏粒度 | C# 逐字含未完成行（抖动）；基准截断到最后换行逐行显示 | REPL.tsx:1505 |
| S2 | 流式尾块渲染 | C# 尾恒纯文本；基准增长尾走完整 Markdown（代码块流式期有样式） | Markdown.tsx:231-233 |
| S3 | 增量解析范围 | C# 每次 Append 全量 Markdig.Parse O(全文)；基准稳定边界起 O(尾长) | Markdown.tsx:210-212 |
| S4 | 流式项前缀 | C# 流式 ⎿ dim 落定切 ●（跳变）；基准同用 ● | Messages.tsx:703-712 |
| S5 | token compact 大小写 | C# 1.3k；基准 1.3K | format.ts:124-130 |
| S6 | 提示词 XML 剥离 | 基准 stripPromptXMLTags 先剥离再切分；C# 无 | Markdown.tsx:202 |

### 域2：通知与 footer

| # | 功能点 | 差异 |
|---|--------|------|
| N1 | 通知 key 契约 | C# key=文本本身（同文本异语义误合并）；基准独立 key + fold 回调 + invalidates 跨 key 失效 |
| N2 | footer 右侧布局 | C# 行2 单槽四源互斥；基准右列多项并行堆叠 |
| N3 | 窄屏降级 | 基准 columns<80 上下堆叠；C# 恒左右同行互挤 |
| N4 | percentLeft 分母 | C# (limit-ctx)/limit；基准 (threshold-usage)/threshold — 同用量百分比偏高 ~6.5% |
| N5 | 上下文告警阈值 | C# 百分比口径；基准绝对 token 缓冲（13k/20k）— 1M 窗口下严重偏离 |

### 域3：权限深度

| # | 功能点 | 差异 |
|---|--------|------|
| P1 | always-allow 持久化 | C# 内存 30 分钟临时；基准 settings.json 永久 + 规则字符串体系（Bash(npm run:*) 前缀粒度） |
| P2 | dont-ask-again 三档 | C# 单档固定文案；基准工具级/前缀级/会话级三档 + 可编辑前缀 |
| P3 | 拒绝反馈输入 | 基准 No 输入框附 "tell Claude what to do differently" 进 tool result；C# 无 |
| P4 | ctrl+e 展开内容 | C# 状态翻转无内容渲染；基准 AI 解释 + 风险分级 |
| P5 | 触发原因解释区 | 基准显示哪条规则触发 + /permissions 引导；C# 无 |
| P6 | 模式切换警告 | 基准 auto 进入 800ms 防抖警告消息（≤3 次）；C# 无 |
| P7 | dont-ask-again 文案与行为不符 | UI 承诺"不再问"但 30 分钟过期复问（判据：30 分钟系 CLI 先例有意设计 → 待用户决策改文案还是改持久化） |

### 域4：会话与 compact

| # | 功能点 | 差异 |
|---|--------|------|
| C1 | /resume 列表项 | C# [id 时间]+标题；基准相对时间·分支·消息数·tag·agent·PR |
| C2 | /resume 模糊搜索 | C# enableSearch 传参未实现（静默失效） |
| C3 | /resume TUI 下交互 | 引擎侧 Selector WriteLineReal 直写 stdout 与 TUI 帧渲染冲突（静态分析） |
| C4 | 自动压缩触发 | C# AutoCompactService 阈值常量齐备但无生产调用方（不触发） |
| C5 | 会话切换状态恢复 | C# 仅纯文本消息；基准恢复文件/agent/成本/plan 等全套 + TUI 历史重放（BridgeEvent 无会话切换事件） |
| C6 | 压缩后警告抑制 | TokenUsageTracker.Reset 无调用方（注释承诺但三处引用点均未调） |

### 域5：错误与重试

| # | 功能点 | 差异 |
|---|--------|------|
| E1 | API 错误分类 | C# 纯红单行；基准 SSL/超时/连接分类 + dim 提示行 + 限流 upsell |
| E2 | 自动重试退避 | C# 主回合零重试；基准 10 次指数退避 + Retry-After + 529 熔断 + 401 OAuth 刷新 |
| E3 | 重试中 UI | 基准 "Retrying in Xs… (attempt N/M)" 倒计时；C# 无 |
| E4 | 错误详情展开 | 基准 ctrl+o 展开 1000 字符 + cause 链 + 堆栈；C# 不可展开 |
| E5 | 无人值守重试 | 基准 429/529 无限重试 6h cap + 30s 心跳；C# 无 |

### 域6：消息项渲染

| # | 功能点 | 差异 |
|---|--------|------|
| M1 | ❯ 前缀选中变色 | 基准选中时 suggestion 色；C# 恒 Subtle |
| M2 | 工具圆点状态色 | 基准完成=success/error 色 + 未决闪烁；C# 恒 text 色（工具失败不可见） |
| M3 | diff 行号 gutter | 基准 marker+右对齐行号列 + "Added/Removed N lines" 统计头；C# 4 空格缩进无行号 |
| M4 | 表格列对齐 | 基准 center/right 支持；C# 仅左对齐 |
| M5 | 工具错误结果分支 | 基准 is_error → 错误消息组件；C# ToolResultItem 无错误形态 |
| M6 | 用户消息 10k 截断 | UserMessageTruncator 已实现无调用点（同截断接线模式） |

## 三、基准有 C# 缺失（高价值节选）

| 功能 | 基准位置 | 说明 |
|------|---------|------|
| 成本 USD 记账 | cost-tracker.ts:49-120 | C# 仅 token 计数 |
| 自定义 statusline | StatusLine.tsx:30-100 | settings.statusLine 外部命令 |
| reducedMotion/yank-bug 禁用流式预览 | REPL.tsx:1495 | 无条件渲染 |
| spinner 连接警告 | Spinner.tsx:348 | Reconnecting/Disconnected 行 |
| ultrathink 彩虹文字 | HighlightedThinkingText.tsx:87 | 触发词特殊渲染 |
| 工具执行中耗时显示 | BashTool/UI.tsx:145 | elapsedTimeSeconds 进度行 |
| 用户中断专用消息 | UserTextMessage.tsx:83 | C# 仅红色通用错误 |
| redacted_thinking 消息 | 独立组件 | 无对应消息项 |

## 四、判定修正/关闭

| 项 | 判定 |
|----|------|
| 权限模式集合五 vs 四模式 | 有意架构差异（jcc 引擎唯一数据源）— 关闭 |
| 流中断 partial 保留语义相反 | 双侧均为有意设计（场景不同）— 关闭 |
| 引擎死亡检测/优雅关闭 | C# 双进程架构特有 — 关闭 |
| 连续用户消息间距推广 | C# 注释声明有意 — 关闭 |
| spinner 帧集/120ms/红渐变/平滑增量/thinking 2s | 已逐项对齐 — 关闭 |
| 通知 FIFO/8s 超时/单串行/Immediate 抢占 | 已对齐 — 关闭 |
| sticky 滚动语义 | 已对齐 — 关闭 |
| markdown 标题/列表/引用/hr/块间空行 | 已对齐 — 关闭 |
| 粘贴引用 pill 形态 | 已对齐 — 关闭 |

## 五、工具使用不适（二轮新增）

1. python heredoc 经 bash 写含转义的 C# 时 `\n`/`\x03` 被反复展开（本轮 P3 测试 3 次返工均源于此）——建议后续一律 Write/Edit 工具写含转义代码
2. PowerShell `Move-Item` 曾误移活跃文件 ReplShell.cs 到 .xxx/keep（已立即移回 + 编译验证）——建议 AI 归档操作前强制 git status 核对清单

<!-- 🤖 Auto Decision: 2026-10-24 -->
<!-- 决策: 6 项确认 bug 直接 TDD 修复（B-1 死锁最优先），约 60 项差异落文档一问一答 -->
<!-- 原因: AGENTS.md 基准对比工作流——bug 双侧证据确凿直接修 -->
<!-- 替代方案: 全部落文档问用户 —— 因 B-1 每次 API 错误必现死锁不可等 -->
<!-- 验证: 修复后全量回归（见 git log）-->
