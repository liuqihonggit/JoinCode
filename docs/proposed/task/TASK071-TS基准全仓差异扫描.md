# TASK071 — TS 基准全仓差异扫描（六域并行调研）

> 状态：调研中（6 个并行只读子代理，2026-10-25 启动）
> 基准：`D:\project\claude-code-rust\claude-code-rev-main\src\`（1989 个 TS/TSX）
> C# 侧：w1 = origin/main（TASK070 #491 已合并，30daa0099）
> 前置排除：TASK065-070 已覆盖域（TUI 渲染/键位/终端/配置体系/压缩/重试/权限/outputStyle/hooks 5 源）不重复深挖

## 分域调研矩阵

| 代理 | TS 域 | C# 对照域 |
|------|-------|----------|
| A | Tool.ts / Task.ts / tools/ | modules/tools + cognition/hands（工具处理器） |
| B | commands/ + skills/ + outputStyles/ | composition/slash + cognition/hands/skills + prompts |
| C | services/ + jobs/ + tasks/ + memdir/ + history.ts + migrations/ | modules/services + storage + persistence + brain/context |
| D | remote/ + ssh/ + server/ + bridge/ + upstreamproxy/ + coordinator/ | apps/engine/bridge + llm/Coordinator + services/bridge（语义映射判定） |
| E | hooks/ + plugins/ + context/ + state/ + schemas/ + types/ | guard（hooks 管道）+ plugins + brain/context + abstractions |
| F | query/ + QueryEngine.ts + assistant/ + proactive/ + buddy/ + cost-tracker + voice/ | cognition/brain/query + llm + native_tui(vim) |

## 差异清单（回填区）

### 远程/桥接域（已完成调研 + 批次修复 2026-10-25）

**已修复（bug 直接修 + 行为差异判定为"目标一致→丰富实现"）**：

| # | 判定 | 修复 |
|---|------|------|
| 1 | bug | RegisterWorker 响应字段名 epoch→worker_epoch（TS workSecret.ts:115 protojson int64 字符串/number 双形态）+ 解析失败 throw（禁止静默 0 致 409 风暴）— TryParseWorkerEpoch 纯函数 7 用例 |
| 2 | bug | 重建传输携带旧 seq（InitialSequenceNum）+ fresh epoch — 便捷工厂删除（无后向兼容），AuthRecovery 改 options 版（防 SSE 全量回放/双重 bump） |
| 3 | bug | BridgeTokenRefreshScheduler：Schedule/ScheduleFromExpiresIn 30s 下限钳制（防紧循环）；失败计数不再被重试调度清零（MaxRefreshFailures=3 首次可达）；null token 60s 重试 ×2 后放弃（旧实现静默中断）— 行为锁 4 用例（RecordingTimeProvider 零真实等待） |
| 4 | bug | 初始 flush 完成回调补三重守卫（tornDown/authRecoveryInFlight/transport!==flushTransport 快照比对）— state.Transport 追踪字段 |
| 5 | bug（接线错误） | 心跳间隔接 config.HeartbeatIntervalMs（原误接 ConnectTimeoutMs 致 JCC_BRIDGE_HEARTBEAT_MS 死配置）— 两调用点 options 化 |
| 10 | 行为差异→丰富 | CapacityWakeSignal 重写为 TCS 轮换（对齐 TS controller 换新：wake 只影响在途等待者、无残留令牌陈旧唤醒、多等待者全醒）— 语义变更锁 8 用例 |
| 11 | 行为差异→丰富 | V2BridgeConfig.Validate 补 TS zod schema 上下限全集（heartbeat 5-30s/jitter≤0.5/buffer 30s-30min/teardown 500-2000/connect 5-60s 等 — violation 整体回退）— 24 用例 |
| 9 | 缺失→修复 | V2BridgeHandle 补 keep_alive 帧定时器（session_keepalive_interval_v2_ms 默认 120s，JCC_BRIDGE_KEEPALIVE_MS，0=禁用；v1 已有 v2 缺失致空闲会话被代理 GC） |

**登记待决策（整域缺失，价值待评估）**：upstreamproxy（CONNECT→WS 隧道中继，Windows 产品可能 N/A）、remote 订阅端（CCR viewer 模式）、直连服务器客户端、coordinator mode 恢复翻转。
**架构差异（非缺失）**：engine/bridge（ADR 0141 本地 UI 协议）与 TS bridge（claude.ai 远控）正交；JwtUtils/WorkSecret 同名不同义（服务端 vs 客户端）。

### 服务/后台任务/记忆域（已完成调研，待修复/决策）

**确认 bug（待修）**：① DefaultSessionScanner 只扫顶层 *.json 而会话存于 `<id>/transcript.jsonl`（autoDream 门控恒 0）；② KillDreamTaskAsync 锁回滚空操作；③ 会话扫描不排除当前会话。
**行为差异**：dream 时间门不读真实上次合并时间（重复合并风险）；记忆子系统为重设计（JSON 结构化+衰减归档 vs TS Markdown 永不过期）——**架构分歧待决策**（C# 模型会静默删除 TS 语义下应保留的记忆）。
**缺失块**：设置迁移机制（11 脚本）、交互输入历史 history.jsonl（CLI 核心体验）、团队记忆密钥扫描（安全面）、LocalAgentTask/主会话后台化、查询期记忆召回接线。

### 工具域（已完成调研，task_stop 双注册 bug 待修）

**确认 bug**：task_stop 同名双注册（vault TaskToolHandlers vs mcp TaskStopToolHandlers 争抢，后注册者静默覆盖 — 全库唯一重名）。
**高频参数分叉（待决策/修复）**：Bash `background` vs TS `run_in_background`（模型按基准传参被丢弃）、Grep 短旗标参数（-B/-A/-C/-n/-i/type 全部落空）、dangerouslyDisableSandbox 大小写、Glob 大小写敏感/无条数上限、TaskOutput 无 block/timeout、AskUserQuestion/TaskCreate schema 分叉。
**域级决策项**：工具命名体系 PascalCase vs snake_case（影响全工具面 API 兼容）；统一 Config/EnterWorktree/SendMessage 工具缺失。

### 命令/技能/输出风格域（已完成调研，3 bug 待修）

**确认 bug**：① /rate-limit 别名双注册静默冲突（MapRegistry 先注册者赢无告警）；② /clear 调 SessionStart 而非 SessionEnd hooks（注释与实现错配，SessionEnd:clear 永不执行）；③ disable-model-invocation 语义变为纯本地回显（TS 仍发模型）。
**结构性分歧（域级决策）**：技能生态——TS SKILL.md/prompt 扩展模型（frontmatter 全键/项目目录发现/$ARGUMENTS 插值/内置技能 prompt 化）vs C# 步骤机+单目录模型；/loop 同名不同义（cron 调度 vs 内联循环）；/init、/memory、/branch/fork、/skills 目标分叉。
**缺失**：项目级技能目录、命令参数插值索引形态、reload-plugins/terminal-setup 等命令、availability 门控。

### hooks/插件/状态域（已完成调研，4 死链待修）

**确认死链/bug**：① 插件钩子注入链路断链（GetInjectedHooks 无生产调用方，插件钩子永不执行）；② matcher 精确字典查找（`*`/管道/正则 matcher 全部失效）；③ UserPromptSubmit 钩子 sessionId 硬编码 unknown + AdditionalContext 未注入；④ 权限钩子 Deny 的 interrupt 语义丢失 + 注册接口 no-op；⑤ AsyncHookRegistry 注入后从未调用（死代码）。
**协议层系统性不对齐（高优先级修复面）**：stdin 输入 JSON 不写、字段 snake_case vs camelCase 双契约、基础字段（cwd/transcript_path/session_id）缺失、CLAUDE_* 环境变量族未注入、HTTP wire format 分叉、输出解析只认单行 JSON、approve/systemMessage/suppressOutput 丢弃、ask/allow/updatedInput 不进权限流、trust 门禁缺失（安全）。
**域级架构差异**：文件式插件生态（manifest/marketplace/组件目录）整体缺失；AppState 缺 ~20 组切片。

### 查询/助手/成本域（已完成调研 — query2 三条控制链"有壳无芯"）

**TASK069 判定更新**：成本 USD 记账已实现（CostTracker + ChatUsageProcessor 接线），不再报缺失。
**确认 bug（接线断裂 — 待修最高优先）**：① UsdBudgetManager.RecordCostAsync 全仓零调用（USD 预算熔断完全失效）；② ITokenCostTracker 无 DI 注册 → query2 Complete 的 CostUsd 恒 0（两套成本口径不联通）；③ StopHookMiddleware 无 isMeta 消息回灌（无法阻止过早结束）+ sessionId 硬编码 "session-id"；④ TokenBudgetMiddleware.AllocateBudgetAsync 零调用 → 历史裁剪死代码；⑤ DiminishingReturnsDetector Singleton 跨查询泄漏。
**结构化输出**：TS 有 MAX_RETRIES=5 熔断 + result.structured_output 回传，C# 无限重试风险。
**行为差异/缺失（登记）**：Complete chunk 缺 duration_api_ms/num_turns/stop_reason/modelUsage 协议字段；query2 无 transcript 预持久化；buddy stats/soul 残缺（GetBuddyPrompt 零调用）；proactive nextTickAt 不可观测；远端会话历史分页缺失。

## 修复进度（2026-10-25 累计 12 commit）

| 批次 | 内容 | commit |
|------|------|--------|
| 桥接域 | epoch/重建seq/刷新调度器/flush守卫/心跳/CapacityWake/配置钳制/v2 keepalive | 5d6f9b712 |
| 工具域 | task_stop 双注册裁决 | 82b236980 |
| query2-①②③ | ICostTracker 真实计价 + RecordCost 增量 + StopHook 回灌 | 218a1a734 |
| 服务域-① | SessionScanner transcript.jsonl 布局 | 0e2d0104a |
| hooks-② | matcher 模式匹配 | 431f07ed6 |
| 命令技能+deny.interrupt | rate-limit 别名 /clear 双钩子 interrupt 透传 | 0987a935b |
| hooks-③ | UserPromptSubmit sessionId + additionalContext | 4737f3e76 |
| 服务域-②③ | ConsolidationLock 时间门/kill回滚/当前会话排除 | 5cf887dff |
| 工具参数兼容 | 生成器三键查找链(别名→snake→camel)+Grep短旗标+run_in_background | (本批) |
| query2-④⑤ | TokenBudget 注入链 + DiminishingReturns 每查询Reset | (本批) |

设计偏差记录（已按目标一致性裁决）：
- ConsolidationLock 用文件内容存时间戳（TS 用 mtime）— C# IFileSystem 无文件级 mtime setter，内容式更抗备份 touch
- matcher 不做 normalizeLegacyToolName 旧名映射 — C# 无旧工具名历史
- 工具参数兼容走"三键查找链"而非重命名 C# 参数体系 — 命名体系整体 PascalCase↔snake 决策仍开放（登记待决策 #5）

| 查询域协议 | QueryStreamChunk.StopReason 终态标记（end_turn/error_max_turns/error_max_retries/error_max_budget_usd） | 17b4c424b |

暴露的既有问题（顺手修复）：IModelCatalog 接口演进漏 ModelPickerHistoryTests fake（全仓构建纪律捕获）。

## 待决策清单（终版 — 全部需要用户裁决，未动）

**A. 整域缺失（做/不做/何时）**：
1. upstreamproxy（CONNECT→WS 隧道中继+CA bundle — Windows 产品可能 N/A）
2. remote 会话订阅端（CCR viewer-only 模式）
3. 直连服务器客户端（server 模式）
4. 设置迁移机制（11 个 TS 迁移脚本 — 老版本升级用户配置静默失效）
5. 交互输入历史 history.jsonl（跨会话 Up/Ctrl+R 召回 — CLI 核心体验）
6. 团队记忆密钥扫描（gitleaks 规则 — 安全面）
7. LocalAgentTask/主会话后台化任务类型

**B. 架构分歧（保留 C# 设计还是对齐 TS）**：
1. 工具命名体系：C# snake_case（生成器统一）vs TS PascalCase — 已用三键查找链兼容传参，schema 呈现仍是 snake
2. 记忆子系统：C# JSON 结构化+衰减归档/删除 vs TS Markdown 永不过期 — C# 模型会静默删除 TS 语义下应保留的记忆
3. 技能生态：C# 步骤机+单目录 vs TS SKILL.md prompt 扩展（frontmatter 全键/项目目录发现/参数插值/内置技能 prompt 化）
4. 插件钩子链：C# 进程内插件无 manifest 模型，PluginHookInjector 注入后无消费方（死链根因是架构而非漏接线）
5. dream 时间门 ConsolidationLock 已落地，但 C# dream 独立进程/命令触发（TS 是 stop hook 内联自动触发）— 自动触发路径仍缺失

**C. 行为差异待逐项裁决（高价值节选）**：
1. DiminishingReturns 判据：C# 增长率<0.1 连续3次截断 vs TS delta<500 附续跑 nudge（已修 Reset 泄漏，判据差异未动）
2. 召回算法：关键词打分取10 vs LLM sideQuery 选≤5（含 recentTools/已曝光过滤）
3. /init 输出物（.jcc/project_rules.md vs CLAUDE.md）、/memory（DB 管理 vs 编辑 CLAUDE.md）、/loop（内联循环 vs cron）、/branch+/fork 双命令 vs 单命令
4. TaskCreate 参数体系（title vs subject+activeForm）、AskUserQuestion schema（string 内嵌 JSON vs 结构化数组+answers 回传）
5. Glob 大小写敏感/条数上限、Bash 超时环境变量覆盖、TaskOutput block/timeout 语义
6. 结构化输出体系（synthetic tool + 重试上限5 + result.structured_output — 需整体设计）
7. Complete 协议其余字段（duration_api_ms/num_turns/modelUsage/permission_denials — StopReason 已做，其余需消费方决策）

**D. hooks 协议层系统性不对齐（大工程，需专项排期）**：stdin 输入 JSON、snake_case 字段、CLAUDE_* 环境变量族、HTTP wire format、多行 JSON 输出解析、approve/systemMessage/suppressOutput、ask/allow/updatedInput 进权限流、trust 门禁。

## 处置原则（用户指令 2026-10-25）

## 处置原则（用户指令 2026-10-25）

## 处置原则（用户指令 2026-10-25）

- **bug**：双侧证据确凿 → 直接 TDD 修复，不请示
- **行为差异**：先检查两侧行为目标是否一致 → 目标一致实现不同 = 记录差异文档（保留或后续丰富）；目标不一致 = 决策保留还是丰富实现，记录决策依据
- **缺失功能**：按价值排序，登记后分批实施

## 验收表（四列 — 实施后回填）

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
