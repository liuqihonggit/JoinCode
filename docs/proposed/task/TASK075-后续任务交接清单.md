# 后续任务交接清单

> **创建时间**：2026-10-28
> **目的**：综合所有后续待做任务，含当前全部资料，供后续接手者使用
> **原则**：缺失资料标注"待检索"，后续接手者需自行检索补齐

---

## 一、已完成项总结

| # | 编号 | 描述 | 完成方式 | 提交/PR |
|---|------|------|---------|---------|
| 1 | TASK071-A5 | 交互输入历史 history.jsonl | 已有等价实现（HistorySeeder 复用 transcript） | — |
| 2 | TASK071-B2 | 记忆子系统架构分歧 | 方案 C 分层保留 | ADR 0150 |
| 3 | TASK071-D | hooks 协议层 9 子项 | D1-D9 全部落地 | PR #513 |
| 4 | TASK069-C5 | 会话切换状态恢复 | SwitchSession + 成本恢复 bug 修复 | PR #514 |
| 5 | TASK069-E5 | 无人值守重试 | 方案 C 全面对齐（5 处差异） | commit a19fdfc88/87b8bb2ba |
| 6 | DSG043 | OSC 8 超链接深链路 | 三 Phase 均落地 | — |
| 7 | TASK067-截断 | 输入超 10000 截断 | TruncateIfTooLong 接线 | PR #506 |

### E5 实现详情（方案 C 全面对齐）

**5 处差异修复**：
1. **差异 1 模式驱动重试**：`CLAUDE_CODE_UNATTENDED_RETRY` 环境变量门控，DI 注入时切换配置
2. **差异 2 6h 单次 cap**：`RetryConfig.SingleDelayCap`，`NetworkRetryOptions.UnattendedSingleDelayCap = 6h`
3. **差异 3 重试期间心跳**：`RetryConfig.HeartbeatCallback` + `DelayWithHeartbeatAsync` 分块睡眠
4. **差异 4 区分 429/529**：`RetryConfig.TransientErrorPredicate` + `IsRetryAllowed`，仅 429/529 无限重试
5. **差异 5 Retry-After 优先**：`RetryConfig.RetryAfterExtractor`（机制已就绪，DI 层暂留 null）

**关键文件**：
- `modules/abstractions/infrastructure/utils/resilience/ResiliencePolicy.cs` — RetryConfig 加 5 个字段
- `modules/abstractions/infrastructure/http/ResilientHttpExecutor.cs` — CalculateDelay + DelayWithHeartbeatAsync + IsRetryAllowed
- `modules/abstractions/infrastructure/configuration/NetworkRetryOptions.cs` — UnattendedSingleDelayCap + UnattendedHeartbeatInterval
- `modules/llm/core/DependencyInjection/ServiceRegistration.cs` — DI 注入 + IsUnattendedRetryEnabled + IsTransientCapacityError
- `test/unit/infra.tests/utils/resilience/ResilientHttpExecutorTests.cs` — 5 个新测试

---

## 二、待做项 — 中高价值（第二梯队）

### 7. TASK071-B3 技能生态对齐

- **描述**：C# 步骤机+单目录 vs TS SKILL.md prompt 扩展模型
- **来源**：TASK071 差异扫描
- **资料**：待检索 — 需查 TS 基准 SKILL.md 格式 + C# 技能加载机制
- **修复面**：待评估

### 8. TASK071-C6 结构化输出体系

- **描述**：synthetic tool + 重试上限 5 + result.structured_output
- **来源**：TASK071 差异扫描
- **资料**：待检索 — 需查 TS 基准 structured output 实现 + C# 工具结果模型
- **修复面**：待评估

### 9. TASK071-A4 设置迁移机制

- **描述**：11 个 TS 迁移脚本，老版本升级配置静默失效
- **来源**：TASK071 差异扫描
- **资料**：待检索 — 需查 TS 基准 migrations/ 目录 + C# 设置加载机制
- **修复面**：待评估

---

## 三、待做项 — TASK069 域内差异项（23 项待决策）

> **处理方式**：用户选"按域批量决策"，但随后要求"先不要修复了，列清单"
> **后续接手者**：需按域逐组用 ask_user 问用户决策

### 域 1：流式渲染（S1-S6，6 项）

| # | 功能点 | 差异 | 基准位置 | 修复面 |
|---|--------|------|---------|--------|
| S1 | 流式上屏粒度 | C# 逐字含未完成行（抖动）；基准截断到最后换行逐行显示 | REPL.tsx:1505 | TUI 渲染层 |
| S2 | 流式尾块渲染 | C# 尾恒纯文本；基准增长尾走完整 Markdown（代码块流式期有样式） | Markdown.tsx:231-233 | TUI 渲染层 |
| S3 | 增量解析范围 | C# 每次 Append 全量 Markdig.Parse O(全文)；基准稳定边界起 O(尾长) | Markdown.tsx:210-212 | 解析层（性能） |
| S4 | 流式项前缀 | C# 流式 ⎿ dim 落定切 ●（跳变）；基准同用 ● | Messages.tsx:703-712 | TUI 渲染层（低价值） |
| S5 | token compact 大小写 | C# 1.3k；基准 1.3K | format.ts:124-130 | TUI 格式化（低价值） |
| S6 | 提示词 XML 剥离 | 基准 stripPromptXMLTags 先剥离再切分；C# 无 | Markdown.tsx:202 | 解析层 |

### 域 6：消息项渲染（M1-M6，6 项）

| # | 功能点 | 差异 | 修复面 |
|---|--------|------|--------|
| M1 | ❯ 前缀选中变色 | 基准选中时 suggestion 色；C# 恒 Subtle | TUI 渲染层（低价值） |
| M2 | 工具圆点状态色 | 基准完成=success/error 色 + 未决闪烁；C# 恒 text 色（工具失败不可见） | TUI 渲染层 |
| M3 | diff 行号 gutter | 基准 marker+右对齐行号列 + "Added/Removed N lines" 统计头；C# 4 空格缩进无行号 | TUI 渲染层 |
| M4 | 表格列对齐 | 基准 center/right 支持；C# 仅左对齐 | TUI 渲染层 |
| M5 | 工具错误结果分支 | 基准 is_error → 错误消息组件；C# ToolResultItem 无错误形态 | TUI 渲染层 |
| M6 | 用户消息 10k 截断 | UserMessageTruncator 已实现无调用点 | TUI 接线 |

### 域 4：会话与 compact（C1-C3，3 项）

| # | 功能点 | 差异 | 修复面 |
|---|--------|------|--------|
| C1 | /resume 列表项 | C# [id 时间]+标题；基准相对时间·分支·消息数·tag·agent·PR | 引擎+TUI |
| C2 | /resume 模糊搜索 | C# enableSearch 传参未实现（静默失效） | 引擎 |
| C3 | /resume TUI 下交互 | 引擎侧 Selector WriteLineReal 直写 stdout 与 TUI 帧渲染冲突 | 引擎+TUI |

### 域 5：错误与重试（E1/E4，2 项）

| # | 功能点 | 差异 | 修复面 |
|---|--------|------|--------|
| E1 | API 错误分类 | C# 纯红单行；基准 SSL/超时/连接分类 + dim 提示行 + 限流 upsell | TUI 渲染层 |
| E4 | 错误详情展开 | 基准 ctrl+o 展开 1000 字符 + cause 链 + 堆栈；C# 不可展开 | TUI 渲染层 |

### 域 3：权限深度（P3-P7，5 项）

| # | 功能点 | 差异 | 修复面 |
|---|--------|------|--------|
| P3 | 拒绝反馈输入 | 基准 No 输入框附 "tell Claude what to do differently" | TUI 交互 |
| P4 | ctrl+e 展开内容 | C# 状态翻转无内容渲染；基准 AI 解释 + 风险分级 | TUI 渲染层 |
| P5 | 触发原因解释区 | 基准显示哪条规则触发 + /permissions 引导 | TUI 渲染层 |
| P6 | 模式切换警告 | 基准 auto 进入 800ms 防抖警告消息（≤3 次） | TUI（低价值） |
| P7 | dont-ask-again 文案与行为不符 | UI 承诺"不再问"但 30 分钟过期复问 | TUI 文案/权限层 |

### 域 2：footer（N2，1 项）

| # | 功能点 | 差异 | 修复面 |
|---|--------|------|--------|
| N2 | footer 右侧布局 | C# 行2 单槽四源互斥；基准右列多项并行堆叠 | TUI 渲染层 |

---

## 四、待做项 — 中低价值（第三梯队）

| # | 编号 | 描述 | 价值 | 资料状态 |
|---|------|------|------|---------|
| 33 | TASK071-B1 | 工具命名体系 — C# snake_case vs TS PascalCase | 中 | 待检索 |
| 34 | TASK071-B4 | 插件钩子链 — PluginHookInjector 注入后无消费方 | 中 | 待检索 |
| 35 | TASK071-B5 | dream 自动触发路径 — C# dream 独立进程 vs TS stop hook | 中 | 待检索 |
| 36 | TASK071-C1 | DiminishingReturns 判据 — C# 增长率<0.1 vs TS delta<500 | 中 | 待检索 |
| 37 | TASK071-C2 | 召回算法 — 关键词打分取10 vs LLM sideQuery 选≤5 | 中 | 待检索 |
| 38 | TASK071-C3 | /init /memory /loop /branch+/fork 命令目标分叉 | 中 | 待检索 |
| 39 | TASK071-C4 | TaskCreate/AskUserQuestion schema 分叉 | 中 | 待检索 |
| 40 | TASK071-C5 | Glob 大小写敏感/Bash 超时/TaskOutput 语义分叉 | 中 | 待检索 |
| 41 | TASK071-C7 | Complete 协议字段 — duration_api_ms/num_turns/modelUsage/permission_denials | 中 | 待检索 |
| 42 | TASK074-D7 | bash mode 使用埋点 — logEvent('tengu_input_bash') | 低 | 待检索 |
| 43 | DSG042-P3 | RawStdinPump 切默认退役 — 双终端实测矩阵跑齐后切默认 | 中 | 待检索 |

---

## 五、待做项 — TASK071 整域缺失（5 项，需评估是否适用 C# 产品）

| # | 编号 | 描述 | 价值 | 资料状态 |
|---|------|------|------|---------|
| 44 | TASK071-A1 | upstreamproxy（CONNECT→WS 隧道中继+CA bundle） | 待评估 | 待检索 |
| 45 | TASK071-A2 | remote 会话订阅端（CCR viewer-only 模式） | 待评估 | 待检索 |
| 46 | TASK071-A3 | 直连服务器客户端（server 模式） | 待评估 | 待检索 |
| 47 | TASK071-A6 | 团队记忆密钥扫描（gitleaks 规则） | 中 | 待检索 |
| 48 | TASK071-A7 | LocalAgentTask/主会话后台化任务类型 | 中 | 待检索 |

---

## 六、待做项 — TASK069 基准有 C# 缺失（7 项待决策）

| # | 功能 | 状态 | 资料状态 |
|---|------|------|---------|
| 49 | 自定义 statusline | ✅ 已实现 | — |
| 50 | reducedMotion/yank-bug 禁用流式预览 | 待决策 | 待检索 |
| 51 | spinner 连接警告（Reconnecting/Disconnected 行） | 待决策 | 待检索 |
| 52 | ultrathink 彩虹文字 | 待决策 | 待检索 |
| 53 | 工具执行中耗时显示（elapsedTimeSeconds 进度行） | 待决策 | 待检索 |
| 54 | 用户中断专用消息 | 待决策 | 待检索 |
| 55 | redacted_thinking 消息 | 待决策 | 待检索 |

---

## 七、关键参考路径

### 工作清单
- `docs/proposed/task/TASK075-后续高价值任务工作清单.md` — 57 项总清单

### 差异文档
- `docs/proposed/task/TASK069-TUI二轮细节对比C#vsClaudeCode.md` — TASK069 差异全文
- `docs/proposed/task/TASK069-E5-无人值守重试差异分析.md` — E5 差异分析（已完成）
- `docs/proposed/task/TASK071-TS基准全仓差异扫描.md` — TASK071 差异全文

### 设计文档
- `docs/proposed/design/DSG043-markdown-osc8-hyperlink-deep-chain.md` — OSC 8 设计（已完成）

### 基准侧（只读参考）
- `D:\project\claude-code-rust\claude-code-rev-main\src\` — ClaudeCode TS 基准

### E5 关键文件（已改）
- `modules/abstractions/infrastructure/utils/resilience/ResiliencePolicy.cs` — RetryConfig
- `modules/abstractions/infrastructure/http/ResilientHttpExecutor.cs` — 韧性执行器
- `modules/abstractions/infrastructure/configuration/NetworkRetryOptions.cs` — 网络重试配置
- `modules/llm/core/DependencyInjection/ServiceRegistration.cs` — DI 注入

---

## 八、后续接手者指引

1. **先读 AGENTS.md** — 项目规则和编码准则
2. **先读 TASK075 工作清单** — 57 项总览
3. **差异项处理流程**：按域分组 → 写差异摘要 → 用 ask_user 问用户 → 按决策修复
4. **修复流程**：红测试 → 实现 → 编译 → 绿测试 → 提交
5. **跨域改动**：需全仓编译 `pwsh non_deliverables_tools/scripts/Build-AllDomains.ps1`
6. **缺失资料**：标注"待检索"的项需自行检索双侧源码补齐差异描述

---

<!-- 🤖 Auto Decision: 2026-10-28 -->
<!-- 决策: 用户要求停止修复，创建完整交接清单 -->
<!-- 原因: 用户想把所有后续功能列一个清单，含全部资料，供后续接手者使用 -->
<!-- 验证: 清单已落地，含已完成/待做/参考路径/接手指引 -->
