# 后续高价值任务工作清单

> **状态**：进行中 — 逐项推进
> **来源**：TASK067/TASK069/TASK071 差异文档盘点 + DSG043 spec
> **创建时间**：2026-10-26
> **原则**：渐进式开发 — 每次只完成一个功能，编译，单元测试，git 提交

---

## 一、第一梯队（高价值，优先推进）

| # | 编号 | 描述 | 来源 | 价值 | 状态 |
|---|------|------|------|------|------|
| 1 | TASK071-A5 | 交互输入历史 history.jsonl — 跨会话 Up/Ctrl+R 召回 | TASK071 | 高 | 待开工 |
| 2 | TASK071-B2 | 记忆子系统架构分歧 — C# JSON 衰减删除 vs TS Markdown 永不过期（数据丢失风险） | TASK071 | 高 | 待决策 |
| 3 | TASK071-D | hooks 协议层系统性对齐 — trust 门禁缺失 + snake_case/cwd/CLAUDE_* 环境变量族 | TASK071 | 高（安全） | 待开工 |
| 4 | TASK069-C5 | 会话切换状态恢复 — C# 仅纯文本 vs 基准恢复文件/agent/成本/plan 全套+TUI 历史重放 | TASK069 | 高 | 待开工 |
| 5 | TASK069-E5 | 无人值守重试 — 基准 429/529 无限重试 6h cap+30s 心跳；C# 无 | TASK069 | 高 | 待开工 |

## 二、第二梯队（中高价值）

| # | 编号 | 描述 | 来源 | 价值 | 状态 |
|---|------|------|------|------|------|
| 6 | DSG043 | OSC 8 超链接深链路 — 数据层已就绪（102 测试全绿），渲染链未消费 | DSG043 | 中高 | 待开工 |
| 7 | TASK071-B3 | 技能生态对齐 — C# 步骤机+单目录 vs TS SKILL.md prompt 扩展模型 | TASK071 | 中高 | 待开工 |
| 8 | TASK071-C6 | 结构化输出体系 — synthetic tool + 重试上限5 + result.structured_output | TASK071 | 中高 | 待开工 |
| 9 | TASK071-A4 | 设置迁移机制 — 11 个 TS 迁移脚本，老版本升级配置静默失效 | TASK071 | 中高 | 待开工 |
| 10 | TASK069-S1 | 流式上屏粒度 — C# 逐字含未完成行抖动 vs 基准截断到最后换行逐行显示 | TASK069 | 中 | 待决策 |
| 11 | TASK069-S2 | 流式尾块渲染 — C# 纯文本 vs 基准增长尾走完整 Markdown | TASK069 | 中 | 待决策 |
| 12 | TASK069-S3 | 增量解析范围 — C# 全量 Markdig.Parse O(全文) vs 基准 O(尾长) | TASK069 | 中（性能） | 待决策 |
| 13 | TASK069-M2 | 工具圆点状态色 — 基准完成=success/error 色+未决闪烁；C# 恒 text 色 | TASK069 | 中 | 待决策 |
| 14 | TASK069-M3 | diff 行号 gutter — 基准 marker+右对齐行号列+统计头；C# 4 空格缩进无行号 | TASK069 | 中 | 待决策 |
| 15 | TASK069-M4 | 表格列对齐 — 基准 center/right 支持；C# 仅左对齐 | TASK069 | 中 | 待决策 |
| 16 | TASK069-M5 | 工具错误结果分支 — 基准 is_error→错误消息组件；C# ToolResultItem 无错误形态 | TASK069 | 中 | 待决策 |
| 17 | TASK069-M6 | 用户消息 10k 截断 — UserMessageTruncator 已实现无调用点 | TASK069 | 中 | 待决策 |
| 18 | TASK069-C1 | /resume 列表项 — C# [id 时间]+标题 vs 基准相对时间·分支·消息数·tag·agent·PR | TASK069 | 中 | 待决策 |
| 19 | TASK069-C2 | /resume 模糊搜索 — C# enableSearch 传参未实现，静默失效 | TASK069 | 中 | 待决策 |
| 20 | TASK069-C3 | /resume TUI 下交互 — 引擎侧 Selector WriteLineReal 直写 stdout 与 TUI 帧渲染冲突 | TASK069 | 中 | 待决策 |
| 21 | TASK069-E1 | API 错误分类 — C# 纯红单行 vs 基准 SSL/超时/连接分类+dim 提示行+限流 upsell | TASK069 | 中 | 待决策 |
| 22 | TASK069-E4 | 错误详情展开 — 基准 ctrl+o 展开 1000 字符+cause 链+堆栈；C# 不可展开 | TASK069 | 中 | 待决策 |
| 23 | TASK069-P3 | 拒绝反馈输入 — 基准 No 输入框附 "tell Claude what to do differently" | TASK069 | 中 | 待决策 |
| 24 | TASK069-P4 | ctrl+e 展开内容 — C# 状态翻转无内容 vs 基准 AI 解释+风险分级 | TASK069 | 中 | 待决策 |
| 25 | TASK069-P5 | 触发原因解释区 — 基准显示哪条规则触发+/permissions 引导 | TASK069 | 中 | 待决策 |
| 26 | TASK069-P7 | dont-ask-again 文案与行为不符 — UI 承诺"不再问"但 30 分钟过期复问 | TASK069 | 中 | 待决策 |
| 27 | TASK069-S6 | 提示词 XML 剥离 — 基准 stripPromptXMLTags 先剥离再切分；C# 无 | TASK069 | 中 | 待决策 |
| 28 | TASK069-N2 | footer 右侧布局 — C# 行2 单槽四源互斥 vs 基准右列多项并行堆叠 | TASK069 | 中 | 待决策 |

## 三、第三梯队（中低价值，按需排期）

| # | 编号 | 描述 | 来源 | 价值 | 状态 |
|---|------|------|------|------|------|
| 29 | TASK071-B1 | 工具命名体系 — C# snake_case vs TS PascalCase | TASK071 | 中 | 待决策 |
| 30 | TASK071-B4 | 插件钩子链 — PluginHookInjector 注入后无消费方（死链根因是架构） | TASK071 | 中 | 待决策 |
| 31 | TASK071-B5 | dream 自动触发路径 — C# dream 独立进程 vs TS stop hook 内联自动触发 | TASK071 | 中 | 待决策 |
| 32 | TASK071-C1 | DiminishingReturns 判据 — C# 增长率<0.1 vs TS delta<500 | TASK071 | 中 | 待决策 |
| 33 | TASK071-C2 | 召回算法 — 关键词打分取10 vs LLM sideQuery 选≤5 | TASK071 | 中 | 待决策 |
| 34 | TASK071-C3 | /init /memory /loop /branch+/fork 命令目标分叉 | TASK071 | 中 | 待决策 |
| 35 | TASK071-C4 | TaskCreate/AskUserQuestion schema 分叉 | TASK071 | 中 | 待决策 |
| 36 | TASK071-C5 | Glob 大小写敏感/Bash 超时/TaskOutput 语义分叉 | TASK071 | 中 | 待决策 |
| 37 | TASK071-C7 | Complete 协议字段 — duration_api_ms/num_turns/modelUsage/permission_denials | TASK071 | 中 | 待决策 |
| 38 | TASK069-S4 | 流式项前缀 — C# 流式⎿dim 落定切●跳变 vs 基准同用● | TASK069 | 低 | 待决策 |
| 39 | TASK069-S5 | token compact 大小写 — C# 1.3k vs 基准 1.3K | TASK069 | 低 | 待决策 |
| 40 | TASK069-M1 | ❯ 前缀选中变色 — 基准选中时 suggestion 色；C# 恒 Subtle | TASK069 | 低 | 待决策 |
| 41 | TASK069-P6 | 模式切换警告 — 基准 auto 进入 800ms 防抖警告消息 | TASK069 | 低 | 待决策 |
| 42 | TASK074-D7 | bash mode 使用埋点 — logEvent('tengu_input_bash') | TASK074 | 低 | 待排期 |
| 43 | DSG042-P3 | RawStdinPump 切默认退役 — 双终端实测矩阵跑齐后切默认 | DSG042 | 中 | 待排期 |
| 44 | TASK067-截断 | 输入超 10000 截断接线 — TruncateIfTooLong 已实现无调用点 | TASK067 | 中 | 已完成（PR #506） |

## 四、TASK071 整域缺失（需评估是否适用 C# 产品）

| # | 编号 | 描述 | 价值 | 状态 |
|---|------|------|------|------|
| 45 | TASK071-A1 | upstreamproxy（CONNECT→WS 隧道中继+CA bundle） | 待评估 | 待决策 |
| 46 | TASK071-A2 | remote 会话订阅端（CCR viewer-only 模式） | 待评估 | 待决策 |
| 47 | TASK071-A3 | 直连服务器客户端（server 模式） | 待评估 | 待决策 |
| 48 | TASK071-A6 | 团队记忆密钥扫描（gitleaks 规则） | 中 | 待决策 |
| 49 | TASK071-A7 | LocalAgentTask/主会话后台化任务类型 | 中 | 待决策 |

## 五、TASK069 基准有 C# 缺失（8 项）

| # | 功能 | 状态 |
|---|------|------|
| 50 | 成本 USD 计账 | ✅ 已实现 |
| 51 | 自定义 statusline | ✅ 已实现 |
| 52 | reducedMotion/yank-bug 禁用流式预览 | 待决策 |
| 53 | spinner 连接警告（Reconnecting/Disconnected 行） | 待决策 |
| 54 | ultrathink 彩虹文字 | 待决策 |
| 55 | 工具执行中耗时显示（elapsedTimeSeconds 进度行） | 待决策 |
| 56 | 用户中断专用消息 | 待决策 |
| 57 | redacted_thinking 消息 | 待决策 |

---

## 推进规则

1. **渐进式**：每次只完成一个功能，编译，单元测试，git 提交
2. **TDD**：红测试 → 实现 → 绿测试 → 回归 → 提交
3. **差异项**：功能相同实现不同 → 落差异文档一问一答用户决策
4. **bug 项**：确认 bug → 直接 TDD 修复
5. **跨域项**：涉及引擎域 modules/ 的 → 落跨域 spec
6. **每项完成后**：更新本清单状态 + 提交

---

<!-- 🤖 Auto Decision: 2026-10-26 -->
<!-- 决策: 用户选"全部做"，创建工作清单文档逐项推进 -->
<!-- 原因: 57 项高价值待做，需统一跟踪 -->
<!-- 验证: 文档已落地，从第 1 项开始推进 -->
